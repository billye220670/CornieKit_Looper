using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using CornieKit.Looper.Models;
using CornieKit.Looper.Services;
using CornieKit.Looper.Helpers;

namespace CornieKit.Looper.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly VideoPlayerService _videoPlayer;
    private readonly SegmentManager _segmentManager;
    private readonly DataPersistenceService _dataPersistence;
    private readonly RecentFilesService _recentFiles;

    [ObservableProperty]
    private MediaPlayer? _mediaPlayer;

    [ObservableProperty]
    private string _currentVideoPath = string.Empty;

    [ObservableProperty]
    private string _currentTimeText = "00:00";

    [ObservableProperty]
    private string _durationText = "00:00";

    [ObservableProperty]
    private double _playbackPosition;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private LoopMode _selectedLoopMode = LoopMode.Single;

    private TimeSpan? _recordingStartTime;
    private DateTime? _recordKeyDownTime;
    private bool _updatingFromVideo;
    private bool _isScrubbing;
    private bool _wasPlayingBeforeScrub;
    private DateTime _lastSeekTime;
    private const int SeekThrottleMs = 50;
    private bool _playingSingleSegment;

    public ObservableCollection<LoopSegmentViewModel> Segments { get; } = new();
    public ObservableCollection<RecentFileItem> RecentFiles { get; } = new();

    [ObservableProperty]
    private bool _hasRecentFiles;

    public MainViewModel(
        VideoPlayerService videoPlayer,
        SegmentManager segmentManager,
        DataPersistenceService dataPersistence,
        RecentFilesService recentFiles)
    {
        _videoPlayer = videoPlayer;
        _segmentManager = segmentManager;
        _dataPersistence = dataPersistence;
        _recentFiles = recentFiles;

        _videoPlayer.Initialize();
        MediaPlayer = _videoPlayer.MediaPlayer;

        _videoPlayer.PositionChanged += OnPositionChanged;
        _videoPlayer.PlaybackStarted += (s, e) => IsPlaying = true;
        _videoPlayer.PlaybackPaused += (s, e) => IsPlaying = false;
        _videoPlayer.PlaybackStopped += (s, e) => IsPlaying = false;
        _videoPlayer.SegmentLoopCompleted += OnSegmentLoopCompleted;

        _segmentManager.SegmentsChanged += OnSegmentsChanged;
        _segmentManager.CurrentSegmentChanged += OnCurrentSegmentChanged;

        _recentFiles.RecentFilesChanged += (s, e) => RefreshRecentFiles();
        RefreshRecentFiles();
    }

    [RelayCommand]
    private async Task OpenVideoAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Video Files|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.flv;*.webm|All Files|*.*",
            Title = "Select a video file"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadVideoAsync(dialog.FileName);
        }
    }

    public async Task LoadVideoAsync(string filePath)
    {
        StatusMessage = "Loading video...";

        var success = await _videoPlayer.LoadVideoAsync(filePath);
        if (!success)
        {
            StatusMessage = "Failed to load video";
            MessageBox.Show("Failed to load video file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        CurrentVideoPath = filePath;
        DurationText = TimeFormatHelper.FormatTime(_videoPlayer.Duration);

        // 添加到最近文件
        _recentFiles.AddRecentFile(filePath);

        var metadata = await _dataPersistence.LoadMetadataAsync(filePath);
        if (metadata != null)
        {
            _segmentManager.LoadSegments(metadata.Segments);
            SelectedLoopMode = metadata.DefaultLoopMode;
            StatusMessage = $"Loaded {metadata.Segments.Count} segments";
        }
        else
        {
            _segmentManager.Clear();
            StatusMessage = "Video loaded. Press R to mark segments.";
        }

        // 自动开始播放
        _videoPlayer.Play();
    }

    private void RefreshRecentFiles()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            RecentFiles.Clear();
            foreach (var filePath in _recentFiles.RecentFiles)
            {
                RecentFiles.Add(new RecentFileItem(filePath));
            }
            HasRecentFiles = RecentFiles.Count > 0;
        });
    }

    [RelayCommand]
    private async Task OpenRecentFileAsync(string filePath)
    {
        if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
        {
            await LoadVideoAsync(filePath);
        }
        else
        {
            _recentFiles.RemoveRecentFile(filePath);
            MessageBox.Show("File not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void ClearRecentFiles()
    {
        _recentFiles.ClearRecentFiles();
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (string.IsNullOrEmpty(CurrentVideoPath))
            return;

        if (!IsRecording)
        {
            _videoPlayer.TogglePlayPause();
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _videoPlayer.Stop();
        PlaybackPosition = 0;
    }

    public void OnRecordKeyDown()
    {
        if (string.IsNullOrEmpty(CurrentVideoPath))
            return;

        // 暂停状态下忽略R键
        if (!IsPlaying)
            return;

        if (!IsRecording)
        {
            _recordKeyDownTime = DateTime.Now;
            _recordingStartTime = _videoPlayer.CurrentTime;
            IsRecording = true;
            StatusMessage = "Recording segment...";
        }
    }

    public void OnRecordKeyUp()
    {
        if (!IsRecording || !_recordingStartTime.HasValue || !_recordKeyDownTime.HasValue)
            return;

        var duration = DateTime.Now - _recordKeyDownTime.Value;

        if (duration.TotalMilliseconds > 200)
        {
            var endTime = _videoPlayer.CurrentTime;
            var segment = _segmentManager.AddSegment(_recordingStartTime.Value, endTime);

            StatusMessage = $"Segment created: {segment.Name}";
            SaveMetadataAsync().ConfigureAwait(false);

            // 自动开始循环播放新录制的片段
            _playingSingleSegment = true;
            _segmentManager.SetCurrentSegment(segment);
            _videoPlayer.PlaySegment(segment);
            UpdateSegmentPlayingState();
        }

        IsRecording = false;
        _recordingStartTime = null;
        _recordKeyDownTime = null;
    }

    [RelayCommand]
    private void PlaySegment(LoopSegmentViewModel segmentViewModel)
    {
        _playingSingleSegment = true;
        _segmentManager.SetCurrentSegment(segmentViewModel.Segment);
        _videoPlayer.PlaySegment(segmentViewModel.Segment);
        UpdateSegmentPlayingState();
    }

    [RelayCommand]
    private void DeleteSegment(LoopSegmentViewModel segmentViewModel)
    {
        var result = MessageBox.Show(
            $"Delete segment '{segmentViewModel.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _segmentManager.RemoveSegment(segmentViewModel.Id);
            SaveMetadataAsync().ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private void PlayAllSegments()
    {
        if (Segments.Count == 0)
            return;

        _playingSingleSegment = false;
        _segmentManager.SetCurrentSegment(Segments.First().Segment);
        _videoPlayer.PlaySegment(Segments.First().Segment);
        UpdateSegmentPlayingState();
    }

    partial void OnSelectedLoopModeChanged(LoopMode value)
    {
        _segmentManager.LoopMode = value;
        SaveMetadataAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void CycleLoopMode()
    {
        // 循环切换：Single → SequentialLoop → Random → Single
        SelectedLoopMode = SelectedLoopMode switch
        {
            LoopMode.Single => LoopMode.SequentialLoop,
            LoopMode.SequentialLoop => LoopMode.Random,
            LoopMode.Random => LoopMode.Single,
            _ => LoopMode.Single
        };
    }

    partial void OnPlaybackPositionChanged(double value)
    {
        if (_updatingFromVideo)
            return;

        if (string.IsNullOrEmpty(CurrentVideoPath) || _videoPlayer.Duration.TotalSeconds == 0)
            return;

        var targetTime = TimeSpan.FromSeconds(value / 100.0 * _videoPlayer.Duration.TotalSeconds);
        CurrentTimeText = TimeFormatHelper.FormatTime(targetTime);

        if (_isScrubbing)
        {
            var now = DateTime.Now;
            if ((now - _lastSeekTime).TotalMilliseconds < SeekThrottleMs)
                return;
            _lastSeekTime = now;
        }

        // 使用 Position (0-1) 而非 Time 来 seek，更稳定
        _videoPlayer.SeekByPosition((float)(value / 100.0));
    }

    public void OnScrubStart()
    {
        if (string.IsNullOrEmpty(CurrentVideoPath))
            return;

        _isScrubbing = true;
        _wasPlayingBeforeScrub = IsPlaying;

        // 不暂停播放，避免 LibVLC 在暂停状态下 seek 卡死
        // 清除活跃的循环片段，避免 scrub 时被自动跳回
        _videoPlayer.StopSegmentLoop();
    }

    public void OnScrubEnd()
    {
        if (!_isScrubbing)
            return;

        _isScrubbing = false;

        _videoPlayer.SeekByPosition((float)(PlaybackPosition / 100.0));

        // 恢复到拖拽前的状态
        if (_wasPlayingBeforeScrub && !_videoPlayer.IsPlaying)
        {
            _videoPlayer.Play();
        }
        else if (!_wasPlayingBeforeScrub && _videoPlayer.IsPlaying)
        {
            _videoPlayer.Pause();
        }
    }

    private void OnPositionChanged(object? sender, TimeSpan time)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_isScrubbing)
                return;

            CurrentTimeText = TimeFormatHelper.FormatTime(time);

            if (_videoPlayer.Duration.TotalSeconds > 0)
            {
                _updatingFromVideo = true;
                PlaybackPosition = time.TotalSeconds / _videoPlayer.Duration.TotalSeconds * 100;
                _updatingFromVideo = false;
            }
        });
    }

    private void OnSegmentLoopCompleted(object? sender, EventArgs e)
    {
        // 单片段循环模式：VideoPlayerService 已自动 seek 回起点，不切换片段
        if (_playingSingleSegment)
            return;

        var nextSegment = _segmentManager.GetNextSegment();
        if (nextSegment != null && nextSegment != _segmentManager.CurrentSegment)
        {
            _segmentManager.SetCurrentSegment(nextSegment);
            _videoPlayer.PlaySegment(nextSegment);
            UpdateSegmentPlayingState();
        }
    }

    private void OnSegmentsChanged(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Segments.Clear();
            foreach (var segment in _segmentManager.Segments)
            {
                Segments.Add(new LoopSegmentViewModel(segment));
            }
        });
    }

    private void OnCurrentSegmentChanged(object? sender, LoopSegment segment)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateSegmentPlayingState();
        });
    }

    private void UpdateSegmentPlayingState()
    {
        foreach (var segmentVm in Segments)
        {
            segmentVm.IsPlaying = segmentVm.Segment == _segmentManager.CurrentSegment;
        }
    }

    public void OnSegmentRenamed()
    {
        SaveMetadataAsync().ConfigureAwait(false);
    }

    private async Task SaveMetadataAsync()
    {
        if (string.IsNullOrEmpty(CurrentVideoPath))
            return;

        try
        {
            var metadata = new VideoMetadata
            {
                VideoFilePath = CurrentVideoPath,
                Segments = _segmentManager.Segments.ToList(),
                DefaultLoopMode = SelectedLoopMode
            };

            await _dataPersistence.SaveMetadataAsync(metadata);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save metadata: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _videoPlayer?.Dispose();
    }
}
