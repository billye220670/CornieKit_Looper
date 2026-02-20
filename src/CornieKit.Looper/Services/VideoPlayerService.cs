using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using CornieKit.Looper.Models;

namespace CornieKit.Looper.Services;

public class VideoPlayerService : IDisposable
{
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private bool _isDisposed;
    private System.Timers.Timer? _positionTimer;
    private LoopSegment? _activeLoopSegment;

    // WriteableBitmap offscreen rendering
    private IntPtr _videoBuffer;
    private GCHandle _videoBufferHandle;
    private uint _videoWidth, _videoHeight, _videoPitch;
    private WriteableBitmap? _videoSource;
    private readonly object _videoLock = new();

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

        _mediaPlayer.Playing += (s, e) => PlaybackStarted?.Invoke(this, EventArgs.Empty);
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
        // Request RV32 (BGRA32) format
        var chromaBytes = new byte[] { (byte)'R', (byte)'V', (byte)'3', (byte)'2' };
        Marshal.Copy(chromaBytes, 0, chroma, 4);

        pitches = width * 4;
        lines = height;

        _videoWidth = width;
        _videoHeight = height;
        _videoPitch = pitches;

        // Allocate pinned buffer for VLC to decode into
        var bufferSize = pitches * lines;
        var buffer = new byte[bufferSize];
        _videoBufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        _videoBuffer = _videoBufferHandle.AddrOfPinnedObject();

        // Copy ref params to locals before capturing in lambda (CS1628)
        uint capturedWidth = width;
        uint capturedHeight = height;

        // Create WriteableBitmap on the UI thread
        Application.Current.Dispatcher.Invoke(() =>
        {
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
        if (_videoSource == null)
            return;

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_videoSource == null || _videoWidth == 0 || _videoHeight == 0)
                    return;

                _videoSource.Lock();
                _videoSource.WritePixels(
                    new Int32Rect(0, 0, (int)_videoWidth, (int)_videoHeight),
                    _videoBuffer,
                    (int)(_videoPitch * _videoHeight),
                    (int)_videoPitch);
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
        lock (_videoLock)
        {
            if (_videoBufferHandle.IsAllocated)
            {
                _videoBufferHandle.Free();
            }
            _videoBuffer = IntPtr.Zero;
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

            await Task.Run(async () =>
            {
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

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load video: {ex.Message}");
            return false;
        }
    }

    public void Play()
    {
        _mediaPlayer?.Play();
        _positionTimer?.Start();
    }

    public void Pause()
    {
        _mediaPlayer?.Pause();
        _positionTimer?.Stop();
    }

    public void Stop()
    {
        _mediaPlayer?.Stop();
        _positionTimer?.Stop();
        _activeLoopSegment = null;
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

    public void SetVolume(int volume)
    {
        var clampedVolume = Math.Clamp(volume, 0, 100);
        Console.WriteLine($"[VideoPlayerService] SetVolume called, volume={volume}, clamped={clampedVolume}");

        if (_mediaPlayer != null)
        {
            _mediaPlayer.Volume = clampedVolume;
            Console.WriteLine($"[VideoPlayerService] MediaPlayer.Volume set to {_mediaPlayer.Volume}");
        }
        else
        {
            Console.WriteLine("[VideoPlayerService] MediaPlayer is null!");
        }
    }

    public void PlaySegment(LoopSegment segment)
    {
        _activeLoopSegment = segment;
        Seek(segment.StartTime);
        Play();
    }

    public void StopSegmentLoop()
    {
        _activeLoopSegment = null;
    }

    private void OnPositionTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_mediaPlayer == null || !IsPlaying)
            return;

        var currentTime = CurrentTime;
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
            _mediaPlayer?.Stop();
            Thread.Sleep(50); // Give LibVLC time to release resources

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

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _positionTimer?.Stop();
        _positionTimer?.Dispose();

        _mediaPlayer?.Stop();
        _currentMedia?.Dispose();
        _mediaPlayer?.Dispose();
        _libVLC?.Dispose();

        // Clean up video buffer
        lock (_videoLock)
        {
            if (_videoBufferHandle.IsAllocated)
            {
                _videoBufferHandle.Free();
            }
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
