using System.IO;
using System.Threading.Channels;
using CornieKit.Looper.Interpolation;
using CornieKit.Looper.Models;

namespace CornieKit.Looper.Services;

/// <summary>
/// Builds a 2x RIFE loop for the segment that is playing and keeps it on disk.
/// Playback stays on the original frames until the cache is ready, and a cache
/// that finishes mid-loop waits for the next wrap before it is shown.
/// </summary>
public sealed class LoopInterpolationService : IInterpolatedFrameBridge, IDisposable
{
    private readonly InterpolationCacheStore _store;
    private readonly IFrameCodec _codec;
    private readonly RifeFrameInterpolator _rife;
    private readonly SegmentFrameCollector _collector = new();
    private readonly Channel<CapturedRaw> _incoming = Channel.CreateBounded<CapturedRaw>(new BoundedChannelOptions(32)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly object _gate = new();
    private CancellationTokenSource _lifetime = new();
    private Task? _worker;
    private Task? _pump;
    private TaskCompletionSource _captureDone = NewTcs();
    private InterpolationCacheReader? _reader;
    private readonly DecodedFrameRing _ring = new(12);
    private readonly LoopPlaybackClock _clock = new();
    private byte[]? _decodeScratch;
    private byte[]? _jpegScratch;
    private int _thinPasses;
    private int _frameWidth;
    private int _frameHeight;
    private int _frameCount;
    private string _videoPath = string.Empty;
    private string _videoHash = string.Empty;
    private Guid _segmentId;
    private TimeSpan _start;
    private TimeSpan _end;
    private double _fps = 30;
    private Phase _phase = Phase.Idle;
    private bool _presenting;
    private bool _wantsCapture;
    private bool _reportedGpu;
    private int _generation;

    private enum Phase { Idle, Capturing, Interpolating, PendingSwitch, Failed }

    public LoopInterpolationService(InterpolationCacheStore store, IFrameCodec codec, RifeFrameInterpolator rife)
    {
        _store = store;
        _codec = codec;
        _rife = rife;
        var token = _lifetime.Token;
        _worker = Task.Run(() => EncodeLoop(token), token);
        _pump = Task.Run(() => WorkerLoop(token), token);
        _ = Task.Run(() => PresentLoop(token), token);
    }

    public Func<TimeSpan>? MediaTime { get; set; }
    public event Action<string>? Status;
    public event Action<Guid, bool>? SegmentReady;

    public bool WantsCapture { get { lock (_gate) return _wantsCapture; } }
    public bool IsPresenting { get { lock (_gate) return _presenting; } }

    public int PresentedFrameIndex
    {
        get
        {
            if (!IsPresenting || _frameCount <= 0)
                return -1;
            var time = _start + _clock.PositionInSegment();
            return InterpolationMath.FrameIndex(time, _start, _end, _frameCount);
        }
    }

    public void NotePlaybackRunning(bool running) => _clock.SetRunning(running);

    public void Watch(LoopSegment segment, string videoPath, double fps)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return;
        if (!InterpolationMath.IsSupportedDuration(segment.Duration))
        {
            StopPresenting();
            Status?.Invoke("This loop is too long for high frame rate smoothing.");
            return;
        }

        var hash = HashFor(videoPath);
        if (TryPresentExisting(segment, videoPath, hash))
            return;

        lock (_gate)
        {
            if (_phase == Phase.Capturing && SameSegment(segment, videoPath))
            {
                _wantsCapture = true;
                return;
            }
            if (_phase == Phase.Interpolating && SameSegment(segment, videoPath))
                return;
            if (_phase == Phase.PendingSwitch && SameSegment(segment, videoPath))
            {
                _presenting = true;
                _phase = Phase.Idle;
                _wantsCapture = false;
                _clock.Restart(_end - _start);
                return;
            }
        }

        StartCapture(segment, videoPath, hash, fps);
    }

    public void OnLoopBoundary()
    {
        lock (_gate)
        {
            if (_phase == Phase.PendingSwitch)
            {
                _presenting = true;
                _phase = Phase.Idle;
                _wantsCapture = false;
                _clock.Restart(_end - _start);
                return;
            }
        }
        TryFinishCapture();
    }

    public void StopPresenting()
    {
        lock (_gate)
            _presenting = false;
    }

    public void Reset()
    {
        TaskCompletionSource previous;
        lock (_gate)
        {
            _generation++;
            _phase = Phase.Idle;
            _presenting = false;
            _wantsCapture = false;
            _segmentId = Guid.Empty;
            _reader?.Dispose();
            _reader = null;
            _frameCount = 0;
            _ring.Clear();
            previous = _captureDone;
            _captureDone = NewTcs();
        }
        previous.TrySetCanceled();
    }

    public bool HasReadyCache(string videoPath, TimeSpan start, TimeSpan end)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return false;
        var hash = HashFor(videoPath);
        return _store.TryOpen(videoPath, hash, start, end, _codec.Id, out var reader)
            && Close(reader);
    }

    public int ClearMissingCaches() => _store.DeleteCachesForMissingVideos();

    public void CaptureFrame(byte[] pixels, int width, int height, int stride, TimeSpan mediaTime)
    {
        if (!WantsCapture || !InterpolationMath.IsSupportedSize(width, height) || pixels.Length == 0)
            return;
        var row = width * 4;
        byte[] tight;
        if (stride == row && pixels.Length >= row * height)
            tight = pixels;
        else
        {
            tight = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                pixels.AsSpan(y * stride, row).CopyTo(tight.AsSpan(y * row, row));
        }
        _incoming.Writer.TryWrite(new CapturedRaw(tight, width, height, mediaTime));
    }

    public bool TryCopyPresentedFrame(Span<byte> destination, out int width, out int height)
    {
        width = _frameWidth;
        height = _frameHeight;
        var index = PresentedFrameIndex;
        if (index < 0)
            return false;
        return _ring.TryCopyLatest(index, _frameCount, destination);
    }

    public void Dispose() => Reset();

    private bool TryPresentExisting(LoopSegment segment, string videoPath, string hash)
    {
        if (!_store.TryOpen(videoPath, hash, segment.StartTime, segment.EndTime, _codec.Id, out var reader) || reader == null)
            return false;
        var duration = segment.EndTime - segment.StartTime;
        var sourceFrames = reader.Manifest.FrameCount / Math.Max(1, reader.Manifest.Scale);
        if (!InterpolationMath.CaptureCoversSourceRate(sourceFrames, duration, reader.Manifest.SourceFps))
        {
            reader.Dispose();
            return false;
        }
        lock (_gate)
        {
            _reader?.Dispose();
            _reader = reader;
            _videoPath = videoPath;
            _videoHash = hash;
            _segmentId = segment.Id;
            _start = segment.StartTime;
            _end = segment.EndTime;
            _phase = Phase.Idle;
            _wantsCapture = false;
            _presenting = InterpolationMath.PresentImmediately(cacheReadyAtStart: true);
            _frameWidth = reader.Manifest.Width;
            _frameHeight = reader.Manifest.Height;
            _frameCount = reader.Manifest.FrameCount;
            _ring.Reset(_frameWidth, _frameHeight);
            EnsureDecodeScratch();
            _clock.Restart(segment.EndTime - segment.StartTime);
        }
        SegmentReady?.Invoke(segment.Id, true);
        return true;
    }

    private void StartCapture(LoopSegment segment, string videoPath, string hash, double fps)
    {
        lock (_gate)
        {
            _generation++;
            _presenting = false;
            _reader?.Dispose();
            _reader = null;
            _videoPath = videoPath;
            _videoHash = hash;
            _segmentId = segment.Id;
            _start = segment.StartTime;
            _end = segment.EndTime;
            _fps = fps is >= 1 and <= 240 ? fps : 30;
            _thinPasses = 0;
            _phase = Phase.Capturing;
            _wantsCapture = true;
            _collector.Reset(_start, _end);
            var previous = _captureDone;
            _captureDone = NewTcs();
            previous.TrySetCanceled();
        }
        SegmentReady?.Invoke(segment.Id, false);
        Status?.Invoke("Smoothing this loop in the background.");
    }

    private async Task WorkerLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TaskCompletionSource done;
            int generation;
            lock (_gate)
            {
                done = _captureDone;
                generation = _generation;
            }
            try
            {
                await done.Task.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                if (token.IsCancellationRequested)
                    return;
                continue;
            }

            lock (_gate)
            {
                // The signal has been consumed. Leave a fresh task so this loop
                // waits for the next pass instead of spinning on the completed one.
                if (ReferenceEquals(_captureDone, done))
                    _captureDone = NewTcs();
            }

            byte[][] sources;
            int width;
            int height;
            Guid segmentId;
            string path;
            string hash;
            TimeSpan start;
            TimeSpan end;
            double fps;
            lock (_gate)
            {
                if (generation != _generation || _phase != Phase.Capturing)
                    continue;
                if (!_collector.IsComplete && !_collector.TryFreeze())
                    continue;
                sources = _collector.Snapshot();
                width = _collector.Width;
                height = _collector.Height;
                segmentId = _segmentId;
                path = _videoPath;
                hash = _videoHash;
                start = _start;
                end = _end;
                fps = _fps;
            }

            if (!InterpolationMath.CaptureCoversSourceRate(sources.Length, end - start, fps))
            {
                var giveUp = false;
                lock (_gate)
                {
                    if (generation != _generation)
                        continue;
                    _thinPasses++;
                    giveUp = _thinPasses >= 2;
                    if (giveUp)
                    {
                        _phase = Phase.Failed;
                        _wantsCapture = false;
                    }
                    else
                    {
                        _phase = Phase.Capturing;
                        _wantsCapture = true;
                        _collector.Reset(start, end);
                        _captureDone = NewTcs();
                    }
                }
                if (giveUp)
                    Status?.Invoke("This loop stays at the original frame rate.");
                continue;
            }

            lock (_gate)
            {
                if (generation != _generation || _phase != Phase.Capturing)
                    continue;
                _phase = Phase.Interpolating;
                _wantsCapture = false;
            }

        try
        {
            if (!_rife.IsAvailable)
            {
                if (!_reportedGpu)
                {
                    _reportedGpu = true;
                    Fail(segmentId, "High frame rate smoothing needs a DirectX 12 GPU, so this loop stays at the original frame rate.");
                }
                else
                {
                    lock (_gate)
                    {
                        _phase = Phase.Failed;
                        _wantsCapture = false;
                    }
                }
                continue;
            }
            var outputCount = InterpolationMath.OutputFrameCount(sources.Length);
            var decoded = new byte[sources.Length][];
            for (var i = 0; i < sources.Length; i++)
                decoded[i] = _codec.Decode(sources[i], width, height);
            var produced = new byte[outputCount][];
            for (var i = 0; i < outputCount; i++)
            {
                token.ThrowIfCancellationRequested();
                if (generation != VolatileGeneration())
                    break;
                var (left, right, timestep) = InterpolationMath.SeamPair(i, sources.Length);
                produced[i] = timestep <= 0
                    ? sources[left]
                    : _codec.Encode(_rife.Interpolate(decoded[left], decoded[right], width, height, timestep), width, height, width * 4);
            }
            if (generation != VolatileGeneration())
                continue;
            _store.Write(new InterpolationCacheManifest
            {
                VideoPath = path,
                VideoHash = hash,
                StartTicks = start.Ticks,
                EndTicks = end.Ticks,
                ModelId = InterpolationMath.ModelId,
                Scale = InterpolationMath.Scale,
                Width = width,
                Height = height,
                FrameCount = produced.Length,
                SourceFps = fps,
                Codec = _codec.Id
            }, produced);

            if (!_store.TryOpen(path, hash, start, end, _codec.Id, out var reader) || reader == null)
            {
                Fail(segmentId, "The smoothed loop could not be saved.");
                continue;
            }
            lock (_gate)
            {
                if (generation != _generation)
                {
                    reader.Dispose();
                    continue;
                }
                _reader?.Dispose();
                _reader = reader;
                _phase = Phase.PendingSwitch;
                _frameWidth = width;
                _frameHeight = height;
                _frameCount = reader.Manifest.FrameCount;
                _ring.Reset(width, height);
                EnsureDecodeScratch();
                _presenting = false;
            }
            SegmentReady?.Invoke(segmentId, true);
            Status?.Invoke("High frame rate loop ready. It will start on the next repeat.");
        }
        catch (OperationCanceledException)
        {
            // A newer segment replaced this job, or the app is closing.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Interpolation] {ex.Message}");
            Fail(segmentId, "High frame rate smoothing failed. Playback stays on the original frames.");
        }
        }
    }

    private async Task EncodeLoop(CancellationToken token)
    {
        try
        {
            await foreach (var frame in _incoming.Reader.ReadAllAsync(token))
            {
                var encoded = _codec.Encode(frame.Bgra, frame.Width, frame.Height, frame.Width * 4);
                var complete = _collector.Add(frame.Time, encoded, frame.Width, frame.Height);
                if (complete)
                    TryFinishCapture();
            }
        }
        catch (OperationCanceledException)
        {
            // Reset closed the channel reader via the lifetime token.
        }
    }

    private void TryFinishCapture()
    {
        lock (_gate)
        {
            if (_phase != Phase.Capturing)
                return;
            if (!_collector.IsComplete && !_collector.TryFreeze())
                return;
        }
        _captureDone.TrySetResult();
    }

    private void Fail(Guid segmentId, string message)
    {
        lock (_gate)
        {
            _phase = Phase.Failed;
            _wantsCapture = false;
            _presenting = false;
        }
        SegmentReady?.Invoke(segmentId, false);
        Status?.Invoke(message);
    }

    private void EnsureDecodeScratch()
    {
        var bytes = _frameWidth * _frameHeight * 4;
        if (bytes > 0 && (_decodeScratch == null || _decodeScratch.Length != bytes))
            _decodeScratch = new byte[bytes];
    }

    private byte[] EnsureJpegScratch(int length)
    {
        if (_jpegScratch == null || _jpegScratch.Length < length)
            _jpegScratch = new byte[Math.Max(length, 256 * 1024)];
        return _jpegScratch;
    }

    private async Task PresentLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var reader = _reader;
                if (reader == null || _decodeScratch == null || _frameCount <= 0)
                {
                    await Task.Delay(15, token);
                    continue;
                }
                // Fill the ring before the loop is on screen, then stay ahead of
                // the playhead. Waiting until the first present left the clock
                // stepping through frames that were not decoded yet.
                var playhead = IsPresenting ? PresentedFrameIndex : 0;
                if (playhead < 0)
                    playhead = 0;
                var next = _ring.NextToDecode(playhead, _frameCount);
                if (next < 0)
                {
                    await Task.Delay(1, token);
                    continue;
                }
                var encodedLength = reader.EncodedLength(next);
                var jpeg = EnsureJpegScratch(encodedLength);
                encodedLength = reader.ReadTo(next, jpeg);
                _codec.DecodeTo(jpeg.AsSpan(0, encodedLength), _frameWidth, _frameHeight, _decodeScratch);
                _ring.Write(next, _decodeScratch);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                await Task.Delay(15, token);
            }
            catch
            {
                await Task.Delay(15, token);
            }
        }
    }

    private bool SameSegment(LoopSegment segment, string videoPath) =>
        _segmentId == segment.Id
        && _start == segment.StartTime
        && _end == segment.EndTime
        && string.Equals(_videoPath, videoPath, StringComparison.OrdinalIgnoreCase);

    private string HashFor(string path)
    {
        lock (_gate)
        {
            if (string.Equals(_videoPath, path, StringComparison.OrdinalIgnoreCase) && _videoHash.Length > 0)
                return _videoHash;
        }
        var hash = VideoFingerprint.Compute(path);
        lock (_gate)
        {
            _videoPath = path;
            _videoHash = hash;
        }
        return hash;
    }

    private int VolatileGeneration()
    {
        lock (_gate)
            return _generation;
    }

    private static bool Close(InterpolationCacheReader? reader)
    {
        reader?.Dispose();
        return reader != null;
    }

    private static TaskCompletionSource NewTcs() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly record struct CapturedRaw(byte[] Bgra, int Width, int Height, TimeSpan Time);
}
