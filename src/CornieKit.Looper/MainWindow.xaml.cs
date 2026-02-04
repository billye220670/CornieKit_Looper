using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CornieKit.Looper.ViewModels;

namespace CornieKit.Looper;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private DispatcherTimer? _renameTimer;
    private LoopSegmentViewModel? _pendingRenameSegment;
    private DispatcherTimer? _hideControlsTimer;
    private bool _isSidePanelVisible;
    private bool _isBottomPanelVisible;
    private bool _isMouseOverBottomPanel;
    private const int HideDelayMs = 3000; // YouTube 风格：3秒后隐藏

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        KeyDown += MainWindow_KeyDown;
        KeyUp += MainWindow_KeyUp;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        MouseMove += MainWindow_MouseMove;

        _renameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _renameTimer.Tick += RenameTimer_Tick;

        // YouTube 风格：鼠标停止移动后延迟隐藏
        _hideControlsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(HideDelayMs)
        };
        _hideControlsTimer.Tick += HideControlsTimer_Tick;

        // 监听播放状态变化，暂停时保持控制栏显示
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPlaying))
        {
            if (!_viewModel.IsPlaying)
            {
                // 暂停时停止隐藏计时器，保持控制栏显示
                _hideControlsTimer?.Stop();
                ShowBottomPanel();
            }
            else
            {
                // 恢复播放时重新开始计时
                ResetHideTimer();
            }
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Focus();
        // 初始显示控制栏
        ShowBottomPanel();
        ResetHideTimer();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.Dispose();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Tab 键切换右侧面板
        if (e.Key == Key.Tab)
        {
            System.Diagnostics.Debug.WriteLine($"[PreviewKeyDown] Tab pressed, Focused: {Keyboard.FocusedElement?.GetType().Name ?? "null"}");

            if (Keyboard.FocusedElement is TextBox)
            {
                System.Diagnostics.Debug.WriteLine("[PreviewKeyDown] Tab ignored - TextBox has focus");
                return;
            }

            System.Diagnostics.Debug.WriteLine("[PreviewKeyDown] Calling ToggleSidePanel");
            ToggleSidePanel();

            // 清除焦点，避免显示虚线框
            Keyboard.ClearFocus();
            Focus();

            e.Handled = true;
        }
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // 记录焦点元素
        var focusedElement = Keyboard.FocusedElement;
        System.Diagnostics.Debug.WriteLine($"[KeyDown] Key: {e.Key}, Focused: {focusedElement?.GetType().Name ?? "null"}");

        // 任何按键活动都显示控制栏
        ShowBottomPanel();
        ResetHideTimer();

        if (e.Key == Key.R && !e.IsRepeat)
        {
            _viewModel.OnRecordKeyDown();
            e.Handled = true;
        }
        else if (e.Key == Key.Space && !e.IsRepeat)
        {
            // 如果焦点在TextBox上，不处理空格键
            if (focusedElement is TextBox)
            {
                System.Diagnostics.Debug.WriteLine("[KeyDown] Space ignored - TextBox has focus");
                return;
            }

            System.Diagnostics.Debug.WriteLine("[KeyDown] Space handled - toggling play/pause");
            _viewModel.TogglePlayPauseCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void MainWindow_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.R)
        {
            _viewModel.OnRecordKeyUp();
            e.Handled = true;
        }
    }

    private void ToggleSidePanel()
    {
        _isSidePanelVisible = !_isSidePanelVisible;

        if (_isSidePanelVisible)
        {
            SidePanel.Visibility = Visibility.Visible;
            AnimateOpacity(SidePanel, 0, 1, 200);
        }
        else
        {
            AnimateOpacity(SidePanel, 1, 0, 200, () =>
            {
                SidePanel.Visibility = Visibility.Collapsed;
            });
        }
    }

    #region YouTube-style Controls Show/Hide

    private void MainWindow_MouseMove(object sender, MouseEventArgs e)
    {
        // Window 级别的鼠标移动 → 显示控制栏
        System.Diagnostics.Debug.WriteLine($"[Window] MouseMove - BottomVisible: {_isBottomPanelVisible}");
        ShowBottomPanel();
        ResetHideTimer();
    }

    private void VideoOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        // 鼠标在视频区域内移动 → 显示控制栏
        System.Diagnostics.Debug.WriteLine($"[VideoOverlay] MouseMove - BottomVisible: {_isBottomPanelVisible}");
        ShowBottomPanel();
        ResetHideTimer();
    }

    private void VideoOverlay_MouseLeave(object sender, MouseEventArgs e)
    {
        // 鼠标离开视频区域 → 开始隐藏计时
        ResetHideTimer();
    }

    private void BottomHoverZone_MouseEnter(object sender, MouseEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[BottomHoverZone] MouseEnter");
        ShowBottomPanel();
        ResetHideTimer();
    }

    private void BottomHoverZone_MouseMove(object sender, MouseEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[BottomHoverZone] MouseMove - BottomVisible: {_isBottomPanelVisible}");
        ShowBottomPanel();
        ResetHideTimer();
    }

    private void BottomPanel_MouseEnter(object sender, MouseEventArgs e)
    {
        // 鼠标在控制栏上 → 停止隐藏计时，保持显示
        System.Diagnostics.Debug.WriteLine("[BottomPanel] MouseEnter");
        _isMouseOverBottomPanel = true;
        _hideControlsTimer?.Stop();
        ShowBottomPanel();
    }

    private void BottomPanel_MouseLeave(object sender, MouseEventArgs e)
    {
        // 鼠标离开控制栏 → 重新开始计时
        System.Diagnostics.Debug.WriteLine("[BottomPanel] MouseLeave");
        _isMouseOverBottomPanel = false;
        ResetHideTimer();
    }

    private void ResetHideTimer()
    {
        _hideControlsTimer?.Stop();

        // 只有在播放状态且鼠标不在控制栏上时才启动隐藏计时
        if (_viewModel.IsPlaying && !_isMouseOverBottomPanel && !_isDraggingSlider)
        {
            System.Diagnostics.Debug.WriteLine($"[ResetHideTimer] Starting timer - Playing: {_viewModel.IsPlaying}, MouseOver: {_isMouseOverBottomPanel}, Dragging: {_isDraggingSlider}");
            _hideControlsTimer?.Start();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[ResetHideTimer] NOT starting timer - Playing: {_viewModel.IsPlaying}, MouseOver: {_isMouseOverBottomPanel}, Dragging: {_isDraggingSlider}");
        }
    }

    private void HideControlsTimer_Tick(object? sender, EventArgs e)
    {
        _hideControlsTimer?.Stop();

        // 再次检查条件
        if (_viewModel.IsPlaying && !_isMouseOverBottomPanel && !_isDraggingSlider)
        {
            HideBottomPanel();
        }
    }

    private void ShowBottomPanel()
    {
        if (!_isBottomPanelVisible)
        {
            System.Diagnostics.Debug.WriteLine("[ShowBottomPanel] Showing panel");
            _isBottomPanelVisible = true;
            BottomPanel.IsHitTestVisible = true;
            AnimateOpacity(BottomPanel, BottomPanel.Opacity, 1, 150);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[ShowBottomPanel] Already visible, skipping");
        }
    }

    private void HideBottomPanel()
    {
        if (_isBottomPanelVisible)
        {
            System.Diagnostics.Debug.WriteLine("[HideBottomPanel] Hiding panel");
            _isBottomPanelVisible = false;
            BottomPanel.IsHitTestVisible = false;  // 立即禁用鼠标事件，让下层的 BottomHoverZone 能接收事件
            AnimateOpacity(BottomPanel, BottomPanel.Opacity, 0, 300);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[HideBottomPanel] Already hidden, skipping");
        }
    }

    #endregion

    private void AnimateOpacity(UIElement element, double from, double to, int durationMs, Action? onCompleted = null)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        if (onCompleted != null)
        {
            animation.Completed += (s, e) => onCompleted();
        }

        element.BeginAnimation(OpacityProperty, animation);
    }

    private async void VideoArea_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length > 0)
            {
                var file = files[0];
                var ext = Path.GetExtension(file).ToLowerInvariant();
                var videoExtensions = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" };

                if (videoExtensions.Contains(ext))
                {
                    await _viewModel.LoadVideoAsync(file);
                }
            }
        }
    }

    private void VideoArea_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void VideoArea_DragLeave(object sender, DragEventArgs e)
    {
    }

    /// <summary>
    /// 在覆盖层上的任何鼠标操作结束后，重新激活 WPF 窗口焦点。
    /// LibVLC 的原生 HWND 会抢夺 Win32 焦点，导致键盘事件无法到达 WPF。
    /// </summary>
    private void VideoOverlayGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 延迟执行以确保其他鼠标事件处理完成
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            // 如果没有 TextBox 在编辑中，重新激活窗口
            if (Keyboard.FocusedElement is not TextBox)
            {
                Activate();
                Focus();
            }
        });
    }

    private void SegmentListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        CancelRenameTimer();

        if (SegmentListView.SelectedItem is LoopSegmentViewModel segmentVm)
        {
            segmentVm.IsEditing = false;
            _viewModel.PlaySegmentCommand.Execute(segmentVm);
        }
    }

    private void SegmentName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is LoopSegmentViewModel segmentVm)
        {
            if (SegmentListView.SelectedItem == segmentVm && !segmentVm.IsEditing)
            {
                _pendingRenameSegment = segmentVm;
                _renameTimer?.Start();
            }
        }
    }

    private void RenameTimer_Tick(object? sender, EventArgs e)
    {
        _renameTimer?.Stop();

        if (_pendingRenameSegment != null)
        {
            _pendingRenameSegment.IsEditing = true;
            _pendingRenameSegment = null;

            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                var textBox = FindVisualChild<TextBox>(SegmentListView);
                if (textBox != null && textBox.Visibility == Visibility.Visible)
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }
            });
        }
    }

    private void CancelRenameTimer()
    {
        _renameTimer?.Stop();
        _pendingRenameSegment = null;
    }

    private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is LoopSegmentViewModel segmentVm)
        {
            segmentVm.IsEditing = false;
            _viewModel.OnSegmentRenamed();
        }
    }

    private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is LoopSegmentViewModel segmentVm)
        {
            if (e.Key == Key.Enter)
            {
                segmentVm.IsEditing = false;
                _viewModel.OnSegmentRenamed();
                e.Handled = true;
                Focus();
            }
            else if (e.Key == Key.Escape)
            {
                segmentVm.IsEditing = false;
                e.Handled = true;
                Focus();
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild && typedChild is TextBox tb && tb.Visibility == Visibility.Visible)
            {
                return typedChild;
            }
            var result = FindVisualChild<T>(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "CornieKit Looper - Video Segment Loop Player\n\n" +
            "Version 1.0\n\n" +
            "Controls:\n" +
            "• R - Hold to record segment\n" +
            "• Space - Play/Pause\n" +
            "• Tab - Toggle segment panel\n" +
            "• Right-click - Menu\n\n" +
            "Created with LibVLCSharp.",
            "About",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private bool _isDraggingSlider;

    private void SliderOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = true;
        _hideControlsTimer?.Stop(); // 拖动时停止隐藏计时
        _viewModel.OnScrubStart();

        if (sender is FrameworkElement element)
        {
            element.CaptureMouse();
            UpdateSliderFromMouse(element, e);
        }
    }

    private void SliderOverlay_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.ReleaseMouseCapture();
        }

        _isDraggingSlider = false;
        _viewModel.OnScrubEnd();
        ResetHideTimer(); // 拖动结束后重新开始计时
    }

    private void SliderOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingSlider || e.LeftButton != MouseButtonState.Pressed)
            return;

        if (sender is FrameworkElement element)
        {
            UpdateSliderFromMouse(element, e);
        }
    }

    private void UpdateSliderFromMouse(FrameworkElement element, MouseEventArgs e)
    {
        var position = e.GetPosition(element);
        var ratio = position.X / element.ActualWidth;
        ratio = Math.Clamp(ratio, 0, 1);
        ProgressSlider.Value = ratio * 100;
    }
}
