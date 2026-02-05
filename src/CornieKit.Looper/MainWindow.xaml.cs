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
    private bool _isMenuButtonVisible;
    private bool _isBottomPanelVisible;
    private bool _isMouseOverMenuButton;
    private bool _isMouseOverBottomPanel;
    private const int HideDelayMs = 1000; // 1秒后自动隐藏

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
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Focus();
        // 初始显示菜单按钮和底部控制栏
        ShowMenuButton();
        ShowBottomPanel();
        ResetHideTimer();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.Dispose();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Tab 键切换右侧面板
        if (e.Key == Key.Tab)
        {
            if (Keyboard.FocusedElement is TextBox)
            {
                return;
            }

            ToggleSidePanel();

            // 清除焦点，避免显示虚线框
            Keyboard.ClearFocus();
            Focus();

            e.Handled = true;
        }
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // 任何按键活动都显示控制栏
        ShowMenuButton();
        ShowBottomPanel();
        ResetHideTimer();

        // 如果焦点在TextBox上（重命名模式），不处理快捷键
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (e.Key == Key.R && !e.IsRepeat)
        {
            _viewModel.OnRecordKeyDown();
            e.Handled = true;
        }
        else if (e.Key == Key.Space && !e.IsRepeat)
        {
            _viewModel.TogglePlayPauseCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Left && !e.IsRepeat)
        {
            // 左方向键：后退 5 秒
            _viewModel.SeekRelative(-5);
            e.Handled = true;
        }
        else if (e.Key == Key.Right && !e.IsRepeat)
        {
            // 右方向键：快进 5 秒
            _viewModel.SeekRelative(5);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && !e.IsRepeat)
        {
            // 上方向键：选择上一个 segment（循环）
            _viewModel.SelectPreviousSegment();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && !e.IsRepeat)
        {
            // 下方向键：选择下一个 segment（循环）
            _viewModel.SelectNextSegment();
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
        ShowMenuButton();
        ShowBottomPanel();
        ResetHideTimer();
    }

    private void VideoOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        // 鼠标在视频区域内移动 → 显示控制栏
        ShowMenuButton();
        ShowBottomPanel();
        ResetHideTimer();
    }

    private void VideoOverlay_MouseLeave(object sender, MouseEventArgs e)
    {
        // 鼠标离开视频区域 → 开始隐藏计时
        ResetHideTimer();
    }

    private void TopHoverZone_MouseEnter(object sender, MouseEventArgs e)
    {
        ShowMenuButton();
        ShowBottomPanel();
        ResetHideTimer();
    }

    private void TopHoverZone_MouseMove(object sender, MouseEventArgs e)
    {
        ShowMenuButton();
        ShowBottomPanel();
        ResetHideTimer();
    }

    private void TopPanel_MouseEnter(object sender, MouseEventArgs e)
    {
        // 鼠标在菜单按钮上 → 停止隐藏计时，保持显示
        _isMouseOverMenuButton = true;
        _hideControlsTimer?.Stop();
        ShowMenuButton();
        ShowBottomPanel();
    }

    private void TopPanel_MouseLeave(object sender, MouseEventArgs e)
    {
        // 鼠标离开菜单按钮 → 重新开始计时
        _isMouseOverMenuButton = false;
        ResetHideTimer();
    }

    private void BottomHoverZone_MouseEnter(object sender, MouseEventArgs e)
    {
        ShowMenuButton();
        ShowBottomPanel();
        ResetHideTimer();
    }

    private void BottomHoverZone_MouseMove(object sender, MouseEventArgs e)
    {
        ShowMenuButton();
        ShowBottomPanel();
        ResetHideTimer();
    }

    private void BottomPanel_MouseEnter(object sender, MouseEventArgs e)
    {
        // 鼠标在控制栏上 → 停止隐藏计时，保持显示
        _isMouseOverBottomPanel = true;
        _hideControlsTimer?.Stop();
        ShowMenuButton();
        ShowBottomPanel();
    }

    private void BottomPanel_MouseLeave(object sender, MouseEventArgs e)
    {
        // 鼠标离开控制栏 → 重新开始计时
        _isMouseOverBottomPanel = false;
        ResetHideTimer();
    }

    private void ResetHideTimer()
    {
        _hideControlsTimer?.Stop();

        // 鼠标不在控制栏上且未拖动时启动隐藏计时
        if (!_isMouseOverMenuButton && !_isMouseOverBottomPanel && !_isDraggingSlider)
        {
            _hideControlsTimer?.Start();
        }
    }

    private void HideControlsTimer_Tick(object? sender, EventArgs e)
    {
        _hideControlsTimer?.Stop();

        // 再次检查条件
        if (!_isMouseOverMenuButton && !_isMouseOverBottomPanel && !_isDraggingSlider)
        {
            HideMenuButton();
            HideBottomPanel();
        }
    }

    private void ShowMenuButton()
    {
        if (!_isMenuButtonVisible)
        {
            _isMenuButtonVisible = true;
            MenuButton.IsHitTestVisible = true;
            AnimateOpacity(MenuButton, MenuButton.Opacity, 1, 150);
        }
    }

    private void HideMenuButton()
    {
        if (_isMenuButtonVisible)
        {
            _isMenuButtonVisible = false;
            MenuButton.IsHitTestVisible = false;
            AnimateOpacity(MenuButton, MenuButton.Opacity, 0, 300);
        }
    }

    private void ShowBottomPanel()
    {
        if (!_isBottomPanelVisible)
        {
            _isBottomPanelVisible = true;
            BottomPanel.IsHitTestVisible = true;
            AnimateOpacity(BottomPanel, BottomPanel.Opacity, 1, 150);
        }
    }

    private void HideBottomPanel()
    {
        if (_isBottomPanelVisible)
        {
            _isBottomPanelVisible = false;
            BottomPanel.IsHitTestVisible = false;  // 立即禁用鼠标事件，让下层的 BottomHoverZone 能接收事件
            AnimateOpacity(BottomPanel, BottomPanel.Opacity, 0, 300);
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

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = !MenuPopup.IsOpen;
    }

    private void MenuPopup_ItemClick(object sender, RoutedEventArgs e)
    {
        MenuPopup.IsOpen = false;
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "CornieKit Looper - Video Segment Loop Player\n\n" +
            "Version 1.1.0\n\n" +
            "Controls:\n" +
            "• R - Hold to record segment\n" +
            "• Space - Play/Pause\n" +
            "• Tab - Toggle segment panel\n" +
            "• Left/Right Arrow - Seek backward/forward 5 seconds\n" +
            "• Up/Down Arrow - Select previous/next segment (cycle)\n" +
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
