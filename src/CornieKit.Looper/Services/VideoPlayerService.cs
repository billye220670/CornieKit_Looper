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

    public MediaPlayer? MediaPlayer => _mediaPlayer;
    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;
    public int Volume => _mediaPlayer?.Volume ?? 100;
    public TimeSpan CurrentTime => _mediaPlayer != null
        ? TimeSpan.FromMilliseconds(_mediaPlayer.Time)
        : TimeSpan.Zero;
    public TimeSpan Duration => _mediaPlayer != null && _mediaPlayer.Length > 0
        ? TimeSpan.FromMilliseconds(_mediaPlayer.Length)
        : TimeSpan.Zero;

    public event EventHandler? PlaybackStarted;
    public event EventHandler? PlaybackPaused;
    public event EventHandler? PlaybackStopped;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler? SegmentLoopCompleted;
    public event EventHandler? PlaybackEnded;

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

        _mediaPlayer.Playing += (s, e) => PlaybackStarted?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.Paused += (s, e) => PlaybackPaused?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.Stopped += (s, e) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.EndReached += OnEndReached;

        _positionTimer = new System.Timers.Timer(50);
        _positionTimer.Elapsed += OnPositionTimerElapsed;
        // Don't start timer here - it will start when video plays
    }

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

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}
