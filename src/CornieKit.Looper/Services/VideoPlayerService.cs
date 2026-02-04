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

    public void Initialize()
    {
        if (_libVLC != null)
            return;

        Core.Initialize();

        var options = new[]
        {
            "--aout=mmdevice",
            "--audio-resampler=speex_resampler",
            "--file-caching=300",
            "--no-audio-time-stretch"
        };

        _libVLC = new LibVLC(options);
        _mediaPlayer = new MediaPlayer(_libVLC);

        _mediaPlayer.Playing += (s, e) => PlaybackStarted?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.Paused += (s, e) => PlaybackPaused?.Invoke(this, EventArgs.Empty);
        _mediaPlayer.Stopped += (s, e) => PlaybackStopped?.Invoke(this, EventArgs.Empty);

        _positionTimer = new System.Timers.Timer(50);
        _positionTimer.Elapsed += OnPositionTimerElapsed;
        _positionTimer.Start();
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

            await Task.Run(() =>
            {
                _mediaPlayer.Play();
                while (_mediaPlayer.Length == 0)
                {
                    Task.Delay(10).Wait();
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
    }

    public void Pause()
    {
        _mediaPlayer?.Pause();
    }

    public void Stop()
    {
        _mediaPlayer?.Stop();
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
            _mediaPlayer.Time = (long)time.TotalMilliseconds;
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
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Volume = Math.Clamp(volume, 0, 100);
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
