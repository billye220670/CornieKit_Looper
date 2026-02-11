using System.Collections.ObjectModel;
using System.Linq;
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
    private CancellationTokenSource? _loadCancellationToken;
    private TimeSpan? _pendingStartTime;
    private DateTime _lastMarkerSeekTime;

    public ObservableCollection<LoopSegmentViewModel> Segments { get; } = new();
    public ObservableCollection<RecentFileItem> RecentFiles { get; } = new();

    [ObservableProperty]
    private bool _hasRecentFiles;

    [ObservableProperty]
    private LoopSegmentViewModel? _selectedSegment;

    partial void OnSelectedSegmentChanged(LoopSegmentViewModel? value)
    {
        UpdateSegmentMarkers();
    }

    [ObservableProperty]
    private int _currentVolume = 100;

    [ObservableProperty]
    private double _pendingStartMarkerPosition;

    [ObservableProperty]
    private bool _isPendingStartMarkerVisible;

    [ObservableProperty]
    private double _selectedSegmentStartPosition;

    [ObservableProperty]
    private double _selectedSegmentEndPosition;

    [ObservableProperty]
    private bool _areSegmentMarkersVisible;

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

        // 初始化音量
        _videoPlayer.SetVolume(100);
        CurrentVolume = 100;

        _videoPlayer.PositionChanged += OnPositionChanged;
        _videoPlayer.PlaybackStarted += (s, e) => IsPlaying = true;
        _videoPlayer.PlaybackPaused += (s, e) => IsPlaying = false;
        _videoPlayer.PlaybackStopped += (s, e) => IsPlaying = false;
        _videoPlayer.SegmentLoopCompleted += OnSegmentLoopCompleted;
        _videoPlayer.PlaybackEnded += OnPlaybackEnded;

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
        // Cancel any previous loading operation
        _loadCancellationToken?.Cancel();
        _loadCancellationToken = new CancellationTokenSource();
        var token = _loadCancellationToken.Token;

        StatusMessage = "Loading video...";

        try
        {
            var success = await _videoPlayer.LoadVideoAsync(filePath);

            // Check if cancelled before proceeding
            token.ThrowIfCancellationRequested();

            if (!success)
            {
                StatusMessage = "Failed to load video";
                MessageBox.Show("Failed to load video file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            CurrentVideoPath = filePath;
            DurationText = TimeFormatHelper.FormatTime(_videoPlayer.Duration);

            // Reset playback state for new video
            _playingSingleSegment = false;
            PlaybackPosition = 0;

            // 添加到最近文件
            _recentFiles.AddRecentFile(filePath);

            var metadata = await _dataPersistence.LoadMetadataAsync(filePath);

            // Check if cancelled after async operation
            token.ThrowIfCancellationRequested();

            if (metadata != null)
            {
                _segmentManager.LoadSegments(metadata.Segments);
                SelectedLoopMode = metadata.DefaultLoopMode;
                StatusMessage = $"Loaded {metadata.Segments.Count} segments";

                // 延迟确保UI加载完成
                await Task.Delay(100);

                // 恢复播放状态
                if (metadata.WasPlayingSegments && Segments.Count > 0)
                {
                    if (metadata.WasPlayingSingleSegment && metadata.LastPlayingSegmentId.HasValue)
                    {
                        // 恢复单个segment循环
                        var targetSegment = Segments.FirstOrDefault(s => s.Id == metadata.LastPlayingSegmentId.Value);
                        if (targetSegment != null)
                        {
                            SelectedSegment = targetSegment;
                            PlaySegmentCommand.Execute(targetSegment);
                            StatusMessage = $"Resumed playing segment: {targetSegment.Name}";
                        }
                        else
                        {
                            // 找不到目标segment，播放第一个
                            SelectedSegment = Segments.First();
                            PlaySegmentCommand.Execute(Segments.First());
                        }
                    }
                    else
                    {
                        // 恢复segment列表播放
                        PlayAllSegments();
                        StatusMessage = "Resumed playing segment list";
                    }
                }
                else
                {
                    // 不在播放segments状态，只恢复播放位置
                    if (metadata.LastPlaybackPosition > TimeSpan.Zero && metadata.LastPlaybackPosition < _videoPlayer.Duration)
                    {
                        _videoPlayer.SeekByPosition((float)(metadata.LastPlaybackPosition.TotalSeconds / _videoPlayer.Duration.TotalSeconds));
                    }
                    _videoPlayer.Play();
                }
            }
            else
            {
                _segmentManager.Clear();
                StatusMessage = "Video loaded. Press R to mark segments.";
                // 自动开始播放
                _videoPlayer.Play();
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Video loading cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to load video";
            MessageBox.Show($"Failed to load video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        _playingSingleSegment = false;
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

            // 设置新创建的segment为SelectedSegment以显示标记
            var newSegmentVm = Segments.FirstOrDefault(s => s.Id == segment.Id);
            if (newSegmentVm != null)
            {
                SelectedSegment = newSegmentVm;
            }
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

        // 设置SelectedSegment以显示标记
        SelectedSegment = segmentViewModel;
    }

    [RelayCommand]
    private async Task DeleteSegment(LoopSegmentViewModel segmentViewModel)
    {
        // 检查被删除的segment是否是当前正在播放的segment
        var isPlayingDeletedSegment = _segmentManager.CurrentSegment?.Id == segmentViewModel.Id;

        _segmentManager.RemoveSegment(segmentViewModel.Id);

        // 如果删除的是正在播放的segment，需要停止循环
        if (isPlayingDeletedSegment)
        {
            _playingSingleSegment = false;
            _videoPlayer.StopSegmentLoop();
            _segmentManager.SetCurrentSegment(null);
            UpdateSegmentPlayingState();

            // 如果还有其他segment，可以选择播放第一个
            if (Segments.Count > 0)
            {
                StatusMessage = "Segment deleted. Playback continues.";
            }
            else
            {
                StatusMessage = "Last segment deleted.";
            }
        }

        // 如果删除后没有segment了，删除.cornieloop文件
        if (Segments.Count == 0)
        {
            await _dataPersistence.DeleteMetadataAsync(CurrentVideoPath);
            StatusMessage = "All segments deleted. Metadata file removed.";
        }
        else
        {
            await SaveMetadataAsync();
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

        // 设置SelectedSegment以显示标记
        SelectedSegment = Segments.First();
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

    /// <summary>
    /// 相对时间跳转（快进/快退）
    /// </summary>
    public void SeekRelative(int seconds)
    {
        if (string.IsNullOrEmpty(CurrentVideoPath) || _videoPlayer.Duration.TotalSeconds == 0)
            return;

        var currentTime = _videoPlayer.CurrentTime;
        var newTime = currentTime.Add(TimeSpan.FromSeconds(seconds));

        // 边界检查
        if (newTime < TimeSpan.Zero)
            newTime = TimeSpan.Zero;
        else if (newTime > _videoPlayer.Duration)
            newTime = _videoPlayer.Duration;

        // 使用 Position 进行 seek
        var position = (float)(newTime.TotalSeconds / _videoPlayer.Duration.TotalSeconds);
        _videoPlayer.SeekByPosition(position);
    }

    /// <summary>
    /// 选择上一个 segment（循环），并自动播放
    /// </summary>
    public void SelectPreviousSegment()
    {
        if (Segments.Count == 0)
            return;

        int currentIndex;

        if (SelectedSegment == null)
        {
            // 如果没有选中项，选择最后一个
            currentIndex = Segments.Count;
        }
        else
        {
            currentIndex = Segments.IndexOf(SelectedSegment);
            if (currentIndex == -1)
            {
                currentIndex = Segments.Count;
            }
        }

        // 循环到上一个（到顶部时循环到底部）
        var previousIndex = (currentIndex - 1 + Segments.Count) % Segments.Count;
        SelectedSegment = Segments[previousIndex];

        // 自动播放选中的 segment（相当于双击）
        if (SelectedSegment != null)
        {
            PlaySegmentCommand.Execute(SelectedSegment);
        }
    }

    /// <summary>
    /// 选择下一个 segment（循环），并自动播放
    /// </summary>
    public void SelectNextSegment()
    {
        if (Segments.Count == 0)
            return;

        int currentIndex;

        if (SelectedSegment == null)
        {
            // 如果没有选中项，选择第一个
            currentIndex = -1;
        }
        else
        {
            currentIndex = Segments.IndexOf(SelectedSegment);
            if (currentIndex == -1)
            {
                currentIndex = -1;
            }
        }

        // 循环到下一个（到底部时循环到顶部）
        var nextIndex = (currentIndex + 1) % Segments.Count;
        SelectedSegment = Segments[nextIndex];

        // 自动播放选中的 segment（相当于双击）
        if (SelectedSegment != null)
        {
            PlaySegmentCommand.Execute(SelectedSegment);
        }
    }

    /// <summary>
    /// 调节音量
    /// </summary>
    public void AdjustVolume(int delta)
    {
        Console.WriteLine($"[MainViewModel] AdjustVolume called, delta={delta}, current={CurrentVolume}");
        var newVolume = CurrentVolume + delta;
        newVolume = Math.Clamp(newVolume, 0, 100);
        CurrentVolume = newVolume;
        Console.WriteLine($"[MainViewModel] Setting volume to {newVolume}");
        _videoPlayer.SetVolume(newVolume);
        Console.WriteLine($"[MainViewModel] Volume set complete, VideoPlayer.Volume={_videoPlayer.Volume}");
    }

    /// <summary>
    /// 设置音量
    /// </summary>
    public void SetVolume(int volume)
    {
        Console.WriteLine($"[MainViewModel] SetVolume called, volume={volume}");
        var newVolume = Math.Clamp(volume, 0, 100);
        CurrentVolume = newVolume;
        Console.WriteLine($"[MainViewModel] Setting volume to {newVolume}");
        _videoPlayer.SetVolume(newVolume);
        Console.WriteLine($"[MainViewModel] Volume set complete, VideoPlayer.Volume={_videoPlayer.Volume}");
    }

    /// <summary>
    /// 按数字键1标记起始点
    /// </summary>
    public void OnKey1Pressed()
    {
        if (string.IsNullOrEmpty(CurrentVideoPath))
            return;

        _pendingStartTime = _videoPlayer.CurrentTime;
        PendingStartMarkerPosition = TimeToPosition(_pendingStartTime.Value);
        IsPendingStartMarkerVisible = true;
        StatusMessage = "Start point marked. Press 2 to set end point.";
    }

    /// <summary>
    /// 按数字键2标记结束点并创建segment
    /// </summary>
    public void OnKey2Pressed()
    {
        if (!_pendingStartTime.HasValue)
        {
            StatusMessage = "Press 1 to mark start point first.";
            return;
        }

        var endTime = _videoPlayer.CurrentTime;

        if (endTime <= _pendingStartTime.Value)
        {
            StatusMessage = "End point must be after start point.";
            return;
        }

        // 创建segment
        var segment = _segmentManager.AddSegment(_pendingStartTime.Value, endTime);
        StatusMessage = $"Segment created: {segment.Name}";
        SaveMetadataAsync().ConfigureAwait(false);

        // 自动播放新segment
        _playingSingleSegment = true;
        _segmentManager.SetCurrentSegment(segment);
        _videoPlayer.PlaySegment(segment);
        UpdateSegmentPlayingState();

        // 设置新创建的segment为SelectedSegment以显示标记
        var newSegmentVm = Segments.FirstOrDefault(s => s.Id == segment.Id);
        if (newSegmentVm != null)
        {
            SelectedSegment = newSegmentVm;
        }

        // 清除待确认标记
        _pendingStartTime = null;
        IsPendingStartMarkerVisible = false;
    }

    /// <summary>
    /// TimeSpan转进度条位置 (0-100)
    /// </summary>
    public double TimeToPosition(TimeSpan time)
    {
        if (_videoPlayer.Duration.TotalSeconds == 0)
            return 0;
        return (time.TotalSeconds / _videoPlayer.Duration.TotalSeconds) * 100;
    }

    /// <summary>
    /// 进度条位置转TimeSpan
    /// </summary>
    public TimeSpan PositionToTime(double position)
    {
        return TimeSpan.FromSeconds((position / 100) * _videoPlayer.Duration.TotalSeconds);
    }

    /// <summary>
    /// 更新segment边界标记位置
    /// </summary>
    private void UpdateSegmentMarkers()
    {
        if (SelectedSegment != null)
        {
            SelectedSegmentStartPosition = TimeToPosition(SelectedSegment.Segment.StartTime);
            SelectedSegmentEndPosition = TimeToPosition(SelectedSegment.Segment.EndTime);
            AreSegmentMarkersVisible = true;
        }
        else
        {
            AreSegmentMarkersVisible = false;
        }
    }

    /// <summary>
    /// 更新选中segment的起始标记位置（拖拽用）
    /// </summary>
    public void UpdateSelectedSegmentStartPosition(double newPosition)
    {
        if (SelectedSegment == null)
            return;

        // 限制：起始点不能超过结束点
        if (newPosition >= SelectedSegmentEndPosition)
        {
            newPosition = Math.Max(0, SelectedSegmentEndPosition - 1);
        }

        SelectedSegmentStartPosition = newPosition;
    }

    /// <summary>
    /// 更新选中segment的结束标记位置（拖拽用）
    /// </summary>
    public void UpdateSelectedSegmentEndPosition(double newPosition)
    {
        if (SelectedSegment == null)
            return;

        // 限制：结束点不能小于起始点
        if (newPosition <= SelectedSegmentStartPosition)
        {
            newPosition = Math.Min(100, SelectedSegmentStartPosition + 1);
        }

        SelectedSegmentEndPosition = newPosition;
    }

    /// <summary>
    /// 实时拖拽预览（更新视频播放位置但不保存）
    /// </summary>
    public void SeekToMarkerPosition(double position)
    {
        if (string.IsNullOrEmpty(CurrentVideoPath) || _videoPlayer.Duration.TotalSeconds == 0)
            return;

        // 限制拖拽频率 (50ms)
        var now = DateTime.Now;
        if ((now - _lastMarkerSeekTime).TotalMilliseconds < SeekThrottleMs)
            return;
        _lastMarkerSeekTime = now;

        _videoPlayer.SeekByPosition((float)(position / 100.0));
    }

    /// <summary>
    /// 提交segment标记变更（拖拽结束时调用）
    /// </summary>
    public void CommitSegmentMarkerChange()
    {
        if (SelectedSegment == null)
            return;

        // 更新Segment的实际时间
        SelectedSegment.Segment.StartTime = PositionToTime(SelectedSegmentStartPosition);
        SelectedSegment.Segment.EndTime = PositionToTime(SelectedSegmentEndPosition);

        // 保存到文件
        SaveMetadataAsync().ConfigureAwait(false);

        StatusMessage = $"Segment '{SelectedSegment.Name}' updated.";
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

    private void OnPlaybackEnded(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _updatingFromVideo = true;
            PlaybackPosition = 0;
            CurrentTimeText = "00:00";
            _updatingFromVideo = false;
            IsPlaying = false;
            _playingSingleSegment = false;
            StatusMessage = "Playback ended";
        });
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

    public void ReorderSegments(List<Guid> newOrder)
    {
        _segmentManager.ReorderSegments(newOrder);
        SaveMetadataAsync().ConfigureAwait(false);
    }

    public async Task SavePlaybackStateAsync()
    {
        await SaveMetadataAsync();
    }

    private async Task SaveMetadataAsync()
    {
        if (string.IsNullOrEmpty(CurrentVideoPath))
            return;

        try
        {
            // 判断是否在播放segments
            bool wasPlayingSegments = _segmentManager.CurrentSegment != null && IsPlaying;

            var metadata = new VideoMetadata
            {
                VideoFilePath = CurrentVideoPath,
                Segments = _segmentManager.Segments.ToList(),
                DefaultLoopMode = SelectedLoopMode,
                LastPlaybackPosition = _videoPlayer.CurrentTime,
                WasPlayingSegments = wasPlayingSegments,
                WasPlayingSingleSegment = _playingSingleSegment,
                LastPlayingSegmentId = _segmentManager.CurrentSegment?.Id
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
        try
        {
            _loadCancellationToken?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // CancellationTokenSource already disposed, ignore
        }

        _loadCancellationToken?.Dispose();
        _videoPlayer?.Dispose();
    }
}
