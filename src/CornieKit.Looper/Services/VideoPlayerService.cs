using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using CornieKit.Looper.Interpolation;
using CornieKit.Looper.Models;

namespace CornieKit.Looper.Services;

public class VideoPlayerService : IDisposable
{
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private int _abandoned;
    private int _startMuted;
    private System.Timers.Timer? _positionTimer;
    private LoopSegment? _activeLoopSegment;

    // WriteableBitmap offscreen rendering
    private IntPtr _videoBuffer;
    private volatile byte[]? _frameBytes;
    private GCHandle _videoBufferHandle;
    private uint _videoWidth, _videoHeight, _videoPitch;
    private WriteableBitmap? _videoSource;
    private readonly object _videoLock = new();
    private IInterpolatedFrameBridge? _frameBridge;
    private byte[]? _presentScratch;
    private GCHandle _presentPin;
    private int _lastPresentedIndex = -1;
    private bool _renderHooked;
    private System.Windows.Threading.DispatcherTimer? _presentTimer;
    private int _hiResTimer;
    private TimeSpan _syncMedia;
    private long _syncStamp = Stopwatch.GetTimestamp();
    private double _sourceFrameRate = 30;

    public IInterpolatedFrameBridge? FrameBridge
    {
        get => _frameBridge;
        set => _frameBridge = value;
    }

    public double SourceFrameRate => _sourceFrameRate;

    public TimeSpan SmoothedMediaTime
    {
        get
        {
            if (_mediaPlayer?.IsPlaying != true)
                return _syncMedia;
            return _syncMedia + Stopwatch.GetElapsedTime(_syncStamp);
        }
    }

    public event EventHandler<LoopSegment>? SegmentPlaybackStarted;
    public event EventHandler? SegmentLoopStopped;

    public MediaPlayer? MediaPlayer => _mediaPlayer;
    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;
    public int Volume => _mediaPlayer?.Volume ?? 100;
    public TimeSpan CurrentTime => _mediaPlayer != null
        ? TimeSpan.FromMilliseconds(_mediaPlayer.Time)
        : TimeSpan.Zero;
    public TimeSpan Duration => _mediaPlayer != null && _mediaPlayer.Length > 0
        ? TimeSpan.FromMilliseconds(_mediaPlayer.Length)
        : TimeSpan.Zero;

    /// <summary>
    /// WriteableBitmap for WPF Image binding (offscreen rendered video frames)
    /// </summary>
    public WriteableBitmap? VideoSource => _videoSource;

    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackPaused;
    public event EventHandler? PlaybackStopped;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? SegmentLoopCompleted;
    public event EventHandler? PlaybackEnded;
    public event EventHandler? FrameRendered;

    public void Initialize()
    {
        if (_libVLC != null)
            return;

        Core.Initialize();

        var options = new[]
        {
            "--aout=directsound",
            "--directx-audio-float32",
            "--file-caching=300",
            "--no-audio-time-stretch",
            "--no-lua",
            "--no-stats",
            "--no-sub-autodetect-file",
            "--quiet",
        };

        _libVLC = new LibVLC(options);
        _mediaPlayer = new MediaPlayer(_libVLC);

        // Set up video callbacks for offscreen rendering to WriteableBitmap
        _mediaPlayer.SetVideoFormatCallbacks(OnVideoFormatSetup, OnVideoFormatCleanup);
        _mediaPlayer.SetVideoCallbacks(OnVideoLock, null, OnVideoDisplay);

        _mediaPlayer.Playing += (s, e) =>
        {
            // Playing is raised on a LibVLC thread. Do not call libvlc from here.
            // If Play() ignored the pre-play volume, re-apply mute as soon as the
            // output exists, until the user picks a non-zero level for this video.
            if (Volatile.Read(ref _startMuted) != 0)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    if (Volatile.Read(ref _startMuted) != 0)
                        ApplyVolume(0);
                });
            }

            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        };
        _mediaPlayer.Paused += (s, e) => PlaybackPaused?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.Stopped += (s, e) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.EndReached += OnEndReached;

        _positionTimer = new System.Timers.Timer(50);
        _positionTimer.Elapsed += OnPositionTimerElapsed;
        // Don't start timer here - it will start when video plays
    }

    #region Video Format/Render Callbacks

    /// <summary>
    /// Called by LibVLC to negotiate video format. We request BGRA32 (RV32).
    /// </summary>
    private uint OnVideoFormatSetup(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        // Format setup runs on a LibVLC thread. Dispatcher.Invoke from here deadlocks
        // if the UI thread is inside MediaPlayer.Stop/Dispose. On quit, fail the
        // negotiation immediately instead of marshaling.
        if (IsAbandoned)
            return 0;

        // Request RV32 (BGRA32) format
        var chromaBytes = new byte[] { (byte)'R', (byte)'V', (byte)'3', (byte)'2' };
        Marshal.Copy(chromaBytes, 0, chroma, 4);

        pitches = width * 4;
        lines = height;

        // Allocate pinned buffer for VLC to decode into
        var bufferSize = pitches * lines;
        var buffer = new byte[bufferSize];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        lock (_videoLock)
        {
            // Do not free the previous pin here. A display callback may still be
            // reading it, and freeing it faults. The old pin is reclaimed on exit.
            _videoWidth = width;
            _videoHeight = height;
            _videoPitch = pitches;
            _frameBytes = buffer;
            _videoBufferHandle = handle;
            _videoBuffer = handle.AddrOfPinnedObject();
        }

        // Copy ref params to locals before capturing in lambda (CS1628)
        uint capturedWidth = width;
        uint capturedHeight = height;

        if (IsAbandoned)
            return 0;

        // Create WriteableBitmap on the UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (IsAbandoned)
                return;

            _videoSource = new WriteableBitmap(
                (int)capturedWidth, (int)capturedHeight,
                96, 96,
                System.Windows.Media.PixelFormats.Bgra32,
                null);
        });

        return 1;
    }

    /// <summary>
    /// Called by LibVLC before decoding a frame. Provides the buffer pointer.
    /// </summary>
    private IntPtr OnVideoLock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, _videoBuffer);
        return IntPtr.Zero;
    }

    /// <summary>
    /// Called by LibVLC after a frame is decoded. Copy buffer to WriteableBitmap.
    /// </summary>
    private void OnVideoDisplay(IntPtr opaque, IntPtr picture)
    {
        if (IsAbandoned || _videoSource == null)
            return;

        var bridge = _frameBridge;
        if (bridge?.IsPresenting == true)
            return;

        if (bridge?.WantsCapture == true && _videoBuffer != IntPtr.Zero && _videoWidth > 0 && _videoHeight > 0)
        {
            try
            {
                var bytes = (int)(_videoPitch * _videoHeight);
                var copy = new byte[bytes];
                Marshal.Copy(_videoBuffer, copy, 0, bytes);
                bridge.CaptureFrame(copy, (int)_videoWidth, (int)_videoHeight, (int)_videoPitch, CurrentTime);
            }
            catch
            {
                // A format change can retire the buffer between the check and the copy.
            }
        }

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (IsAbandoned)
                    return;

                byte[]? frame;
                IntPtr buffer;
                int width, height, pitch;
                lock (_videoLock)
                {
                    frame = _frameBytes;
                    buffer = _videoBuffer;
                    width = (int)_videoWidth;
                    height = (int)_videoHeight;
                    pitch = (int)_videoPitch;
                }

                if (_videoSource == null || frame == null || buffer == IntPtr.Zero || width == 0 || height == 0)
                    return;

                // RV32 leaves alpha at 0. Stamp 255 before the upload so the
                // bitmap the shader samples is an opaque frame, not a
                // transparent layer that composites as a second image.
                BgraFrameAlpha.ForceOpaque(frame);

                _videoSource.Lock();
                _videoSource.WritePixels(
                    new Int32Rect(0, 0, width, height),
                    buffer,
                    pitch * height,
                    pitch);
                _videoSource.Unlock();

                FrameRendered?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                // Ignore rendering errors during shutdown/format change
            }
        });
    }

    /// <summary>
    /// Called by LibVLC when video format changes or playback ends.
    /// </summary>
    private void OnVideoFormatCleanup(ref IntPtr opaque)
    {
        // Leave the pinned buffer alive on quit. Freeing it while a display
        // callback still reads it faults, and process exit reclaims it.
        if (IsAbandoned)
            return;

        lock (_videoLock)
        {
            if (_videoBufferHandle.IsAllocated)
            {
                _videoBufferHandle.Free();
            }
            _videoBuffer = IntPtr.Zero;
            _frameBytes = null;
            _videoWidth = 0;
            _videoHeight = 0;
            _videoPitch = 0;
        }
    }

    #endregion

    public async Task<bool> LoadVideoAsync(string filePath)
    {
        if (_mediaPlayer == null || _libVLC == null)
            return false;

        try
        {
            _currentMedia?.Dispose();
            _currentMedia = new Media(_libVLC, filePath, FromType.FromPath);
            _mediaPlayer.Media = _currentMedia;

            // The probe Play() below is the first moment this file can make sound.
            // Mute on this thread immediately before it; LibVLC's default volume is 100.
            PrepareMutedOpen();

            await Task.Run(async () =>
            {
                if (Volatile.Read(ref _startMuted) != 0)
                    ApplyVolume(0);

                _mediaPlayer.Play();
                var timeout = DateTime.Now.AddSeconds(10);
                while (_mediaPlayer.Length == 0)
                {
                    if (DateTime.Now > timeout)
                        throw new TimeoutException("Video metadata loading timeout");
                    await Task.Delay(10);
                }
                _mediaPlayer.Pause();
            });

            RememberFrameRate();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load video: {ex.Message}");
            return false;
        }
    }

    public void StartPresentationClock()
    {
        if (_renderHooked)
            return;
        _renderHooked = true;
        // Rendering alone follows whatever else dirties the window. During
        // playback that is mostly the 50 ms position text, so a cached loop
        // was only copied about 20 times a second. The timer asks for a frame
        // often enough to show every interpolated picture.
        System.Windows.Media.CompositionTarget.Rendering += (_, _) => BlitPresentedFrame();
        _presentTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(8)
        };
        _presentTimer.Tick += (_, _) => OnPresentTick();
        _presentTimer.Start();
    }

    private void OnPresentTick()
    {
        if (IsAbandoned)
            return;
        if (_frameBridge?.IsPresenting == true)
        {
            if (Interlocked.Exchange(ref _hiResTimer, 1) == 0)
                TimeBeginPeriod(1);
            BlitPresentedFrame();
            return;
        }
        if (Interlocked.Exchange(ref _hiResTimer, 0) == 1)
            TimeEndPeriod(1);
    }

    private void RememberFrameRate()
    {
        try
        {
            if (_currentMedia == null)
                return;
            foreach (var track in _currentMedia.Tracks)
            {
                if (track.TrackType != TrackType.Video)
                    continue;
                var video = track.Data.Video;
                if (video.FrameRateDen == 0 || video.FrameRateNum == 0)
                    continue;
                var fps = video.FrameRateNum / (double)video.FrameRateDen;
                if (fps is >= 1 and <= 240)
                    _sourceFrameRate = fps;
                return;
            }
        }
        catch
        {
            // Keep the previous estimate. Capture still works without an exact rate.
        }
    }

    private void BlitPresentedFrame()
    {
        var bridge = _frameBridge;
        if (IsAbandoned || bridge?.IsPresenting != true || _videoSource == null)
        {
            _lastPresentedIndex = -1;
            return;
        }
        var index = bridge.PresentedFrameIndex;
        if (index < 0 || index == _lastPresentedIndex)
            return;
        var width = _videoSource.PixelWidth;
        var height = _videoSource.PixelHeight;
        var needed = width * height * 4;
        if (needed <= 0)
            return;
        var pinned = PinPresentScratch(needed);
        if (!bridge.TryCopyPresentedFrame(_presentScratch!, out var frameWidth, out var frameHeight))
            return;
        if (frameWidth != width || frameHeight != height)
            return;
        _lastPresentedIndex = index;
        try
        {
            // Same upload the original frames use. The scratch buffer is already
            // decoded; this tick only copies it into the bitmap.
            _videoSource.Lock();
            _videoSource.WritePixels(new Int32Rect(0, 0, width, height), pinned, needed, width * 4);
            _videoSource.Unlock();
        }
        catch
        {
            // The bitmap can be replaced while a frame is being drawn.
        }
    }

    private IntPtr PinPresentScratch(int bytes)
    {
        if (_presentScratch == null || _presentScratch.Length < bytes)
        {
            if (_presentPin.IsAllocated)
                _presentPin.Free();
            _presentScratch = new byte[bytes];
            _presentPin = GCHandle.Alloc(_presentScratch, GCHandleType.Pinned);
        }
        return _presentPin.AddrOfPinnedObject();
    }

    public void Play()
    {
        _mediaPlayer?.Play();
        _positionTimer?.Start();
        _frameBridge?.NotePlaybackRunning(true);
    }

    public void Pause()
    {
        _mediaPlayer?.Pause();
        _positionTimer?.Stop();
        _frameBridge?.NotePlaybackRunning(false);
    }

    public void Stop()
    {
        _mediaPlayer?.Stop();
        _positionTimer?.Stop();
        _activeLoopSegment = null;
        _frameBridge?.NotePlaybackRunning(false);
    }

    public void TogglePlayPause()
    {
        if (IsPlaying)
            Pause();
        else
            Play();
    }

    public void Seek(TimeSpan time)
    {
        if (_mediaPlayer != null && _mediaPlayer.Length > 0)
        {
            var clampedTime = Math.Clamp((long)time.TotalMilliseconds, 0, _mediaPlayer.Length);
            _mediaPlayer.Time = clampedTime;
        }
    }

    public void SeekByPosition(float position)
    {
        if (_mediaPlayer != null && _mediaPlayer.Length > 0)
        {
            _mediaPlayer.Position = Math.Clamp(position, 0f, 1f);
        }
    }

    /// <summary>
    /// Silence the current media before playback. A later non-zero
    /// <see cref="SetVolume"/> clears this so in-session unmute keeps working.
    /// </summary>
    public void PrepareMutedOpen()
    {
        Volatile.Write(ref _startMuted, 1);
        ApplyVolume(0);
    }

    public void SetVolume(int volume)
    {
        var clampedVolume = Math.Clamp(volume, 0, 100);
        Console.WriteLine($"[VideoPlayerService] SetVolume called, volume={volume}, clamped={clampedVolume}");

        // A positive level is an explicit unmute for this video.
        if (clampedVolume > 0)
            Volatile.Write(ref _startMuted, 0);

        ApplyVolume(clampedVolume);
    }

    private void ApplyVolume(int clampedVolume)
    {
        if (_mediaPlayer == null)
        {
            Console.WriteLine("[VideoPlayerService] MediaPlayer is null!");
            return;
        }

        _mediaPlayer.Volume = clampedVolume;
        _mediaPlayer.Mute = clampedVolume == 0;
        Console.WriteLine($"[VideoPlayerService] MediaPlayer.Volume set to {_mediaPlayer.Volume}");
    }

    public void PlaySegment(LoopSegment segment)
    {
        _activeLoopSegment = segment;
        SegmentPlaybackStarted?.Invoke(this, segment);
        Seek(segment.StartTime);
        Play();
    }

    public void StopSegmentLoop()
    {
        _activeLoopSegment = null;
        SegmentLoopStopped?.Invoke(this, EventArgs.Empty);
    }

    private void OnPositionTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (IsAbandoned || _mediaPlayer == null || !IsPlaying)
            return;

        var currentTime = CurrentTime;
        _syncMedia = currentTime;
        _syncStamp = Stopwatch.GetTimestamp();
        PositionChanged?.Invoke(this, currentTime);

        if (_activeLoopSegment != null && currentTime >= _activeLoopSegment.EndTime)
        {
            Seek(_activeLoopSegment.StartTime);
            SegmentLoopCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        _positionTimer?.Stop();

        // IMPORTANT: LibVLC EndReached callback runs on internal thread
        // Calling Play/Stop/Seek directly here causes deadlock
        // Must use ThreadPool to avoid blocking LibVLC's thread
        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (IsAbandoned)
                return;

            _mediaPlayer?.Stop();
            if (IsAbandoned)
                return;

            Thread.Sleep(50); // Give LibVLC time to release resources
            if (IsAbandoned)
                return;

            // If there's an active loop segment, restart from its beginning
            if (_activeLoopSegment != null)
            {
                Seek(_activeLoopSegment.StartTime);
            }
            else
            {
                // No segment - just restart from the beginning
                SeekByPosition(0);
            }

            Play();
        });
    }

    /// <summary>
    /// Stops managed callbacks without calling LibVLC stop/release.
    /// Those native calls join threads that may be blocked on the UI dispatcher.
    /// </summary>
    public void Abandon()
    {
        if (Interlocked.Exchange(ref _abandoned, 1) != 0)
            return;

        try
        {
            _positionTimer?.Stop();
            _presentTimer?.Stop();
            if (Interlocked.Exchange(ref _hiResTimer, 0) == 1)
                TimeEndPeriod(1);
        }
        catch
        {
            // Timer shutdown must not block process exit.
        }

        _activeLoopSegment = null;
    }

    public void Dispose()
    {
        // Intentionally does not Stop/Dispose MediaPlayer or LibVLC.
        // libvlc_media_player_stop (and release, which stops) blocks until decoder
        // threads exit. OnVideoFormatSetup marshals with Dispatcher.Invoke, so that
        // join deadlocks when it runs on the UI thread. Process termination reclaims
        // the native player, the pinned frame buffer, and any in-flight decode.
        Abandon();
        GC.SuppressFinalize(this);
    }

    private bool IsAbandoned => Volatile.Read(ref _abandoned) != 0;

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);
}
