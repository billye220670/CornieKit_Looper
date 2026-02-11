using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.ComponentModel;
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
    private DateTime _lastWheelTime = DateTime.MinValue;
    private const int FastScrollThresholdMs = 150; // 快速滚动阈值
    private DispatcherTimer? _volumeHudTimer;
    private string? _originalSegmentName;
    private Point _dragStartPoint;
    private bool _isDraggingSegment;
    private bool _isDraggingStartMarker;
    private bool _isDraggingEndMarker;

    // 帧步进相关
    private int _accumulatedFrameSteps = 0;
    private DateTime _lastFrameStepTime = DateTime.MinValue;
    private DispatcherTimer? _frameStepTimer;
    private const double DefaultFrameStepSeconds = 0.5;    // 默认步进0.5秒
    private const double FineFrameStepSeconds = 0.1;       // Ctrl: 精细步进0.1秒
    private const double CoarseFrameStepSeconds = 1.5;     // Shift: 粗调步进1.5秒
    private const int FrameStepThrottleMs = 50;            // 节流：50ms最多执行一次seek

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

        // 音量HUD自动隐藏计时器
        _volumeHudTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _volumeHudTimer.Tick += VolumeHudTimer_Tick;

        // 帧步进延迟执行定时器
        _frameStepTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(FrameStepThrottleMs)
        };
        _frameStepTimer.Tick += FrameStepTimer_Tick;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Focus();
        // 初始显示菜单按钮和底部控制栏
        ShowMenuButton();
        ShowBottomPanel();
        ResetHideTimer();
        MainWindow_Loaded_Markers();
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 在关闭前保存当前播放位置
        await _viewModel.SavePlaybackStateAsync();
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
        else if (e.Key == Key.D1 && !e.IsRepeat)
        {
            // 数字键1：标记起始点
            _viewModel.OnKey1Pressed();
            e.Handled = true;
        }
        else if (e.Key == Key.D2 && !e.IsRepeat)
        {
            // 数字键2：标记结束点并创建segment
            _viewModel.OnKey2Pressed();
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

    private void VideoOverlayGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 获取鼠标位置下的元素
        var element = e.OriginalSource as DependencyObject;

        // 检查是否在进度条区域（优先级最高）
        if (IsMouseOverProgressBar(element))
        {
            // 进度条区域 → 帧步进（类似滚动胶卷）
            HandleFrameStepping(e.Delta);
            e.Handled = true;
            return;
        }

        // 检查是否在其他UI控件上（右侧面板、底部控制栏等）
        var isOverUI = IsMouseOverUIElement(element);

        if (isOverUI)
        {
            return; // 在UI控件上，不处理
        }

        // 视频区域 → 音量调节
        HandleVolumeAdjustment(e.Delta);
        e.Handled = true;
    }

    /// <summary>
    /// 检查鼠标是否在进度条区域（包括Slider、MarkerCanvas、SliderOverlay）
    /// </summary>
    private bool IsMouseOverProgressBar(DependencyObject? element)
    {
        if (element == null)
            return false;

        while (element != null)
        {
            if (element is FrameworkElement fe)
            {
                // 进度条本体或MarkerCanvas或透明覆盖层
                if (fe.Name == "ProgressSlider" || fe.Name == "MarkerCanvas" ||
                    (fe is Border && fe.Parent is Grid grid && grid.Children.Contains(ProgressSlider)))
                {
                    return true;
                }
            }

            // 只对Visual/Visual3D使用VisualTreeHelper
            if (element is Visual or Visual3D)
            {
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            else if (element is FrameworkContentElement fce)
            {
                // 对于非Visual元素（如TextBlock中的Run），使用逻辑树
                element = fce.Parent;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// 处理帧步进（滚轮在进度条区域）
    /// </summary>
    private void HandleFrameStepping(int delta)
    {
        if (string.IsNullOrEmpty(_viewModel.CurrentVideoPath))
            return;

        // 累积步进方向（向上=前进，向下=后退）
        _accumulatedFrameSteps += (delta > 0 ? 1 : -1);

        // 节流检查：距离上次执行是否超过阈值
        var now = DateTime.Now;
        var timeSinceLastStep = (now - _lastFrameStepTime).TotalMilliseconds;

        if (timeSinceLastStep < FrameStepThrottleMs)
        {
            // 还在节流期内，重启延迟定时器，等待用户停止滚动
            _frameStepTimer?.Stop();
            _frameStepTimer?.Start();
            return;
        }

        // 立即执行步进
        ApplyFrameStep();
    }

    /// <summary>
    /// 应用累积的帧步进
    /// </summary>
    private void ApplyFrameStep()
    {
        if (_accumulatedFrameSteps == 0)
            return;

        // 根据修饰键选择步进大小
        double stepSize;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            // Ctrl: 精细步进 0.1秒
            stepSize = FineFrameStepSeconds;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            // Shift: 粗调步进 1.5秒
            stepSize = CoarseFrameStepSeconds;
        }
        else
        {
            // 默认步进 0.5秒
            stepSize = DefaultFrameStepSeconds;
        }

        // 使用ViewModel的SeekRelative方法
        var stepDelta = stepSize * _accumulatedFrameSteps;
        _viewModel.SeekRelative(stepDelta);

        // 重置累积
        _accumulatedFrameSteps = 0;
        _lastFrameStepTime = DateTime.Now;
    }

    /// <summary>
    /// 帧步进定时器：延迟执行，避免快速滚动时频繁seek
    /// </summary>
    private void FrameStepTimer_Tick(object? sender, EventArgs e)
    {
        _frameStepTimer?.Stop();
        ApplyFrameStep();
    }

    /// <summary>
    /// 处理音量调节（滚轮在视频区域）
    /// </summary>
    private void HandleVolumeAdjustment(int delta)
    {
        // 检测快速向下滚动
        var now = DateTime.Now;
        var timeSinceLastScroll = (now - _lastWheelTime).TotalMilliseconds;

        if (delta < 0 && timeSinceLastScroll < FastScrollThresholdMs)
        {
            // 快速向下滚动 → 静音
            _viewModel.SetVolume(0);
        }
        else
        {
            // 普通滚动 → ±5% 调节音量
            var volumeDelta = delta > 0 ? 5 : -5;
            _viewModel.AdjustVolume(volumeDelta);
        }

        _lastWheelTime = now;

        // 显示音量HUD
        ShowVolumeHUD(_viewModel.CurrentVolume);
    }

    /// <summary>
    /// 显示音量HUD
    /// </summary>
    private void ShowVolumeHUD(int volume)
    {
        // 更新音量文本
        VolumeText.Text = $"{volume}%";

        // 两态图标：静音 / 正常音量
        VolumeIcon.Text = volume == 0 ? "\uE74F" : "\uE995";

        // 显示HUD
        VolumeHUD.Visibility = Visibility.Visible;
        AnimateOpacity(VolumeHUD, VolumeHUD.Opacity, 1, 200);

        // 重置隐藏计时器
        _volumeHudTimer?.Stop();
        _volumeHudTimer?.Start();
    }

    /// <summary>
    /// 音量HUD自动隐藏
    /// </summary>
    private void VolumeHudTimer_Tick(object? sender, EventArgs e)
    {
        _volumeHudTimer?.Stop();
        AnimateOpacity(VolumeHUD, 1, 0, 300, () =>
        {
            VolumeHUD.Visibility = Visibility.Collapsed;
        });
    }

    /// <summary>
    /// 检查鼠标是否在UI控件上（右侧面板、底部控制栏等，不包括进度条）
    /// </summary>
    private bool IsMouseOverUIElement(DependencyObject? element)
    {
        if (element == null)
            return false;

        // 检查是否在UI控件上
        while (element != null)
        {
            if (element is FrameworkElement fe)
            {
                // 检查是否是右侧面板
                if (fe.Name == "SidePanel" && SidePanel.Visibility == Visibility.Visible)
                {
                    return true;
                }

                // 检查是否是底部控制面板
                if (fe.Name == "BottomPanel")
                {
                    return true;
                }

                // 检查是否是菜单按钮
                if (fe.Name == "MenuButton")
                {
                    return true;
                }
            }

            // 只对Visual/Visual3D使用VisualTreeHelper
            if (element is Visual or Visual3D)
            {
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            }
            else if (element is FrameworkContentElement fce)
            {
                // 对于非Visual元素（如TextBlock中的Run），使用逻辑树
                element = fce.Parent;
            }
            else
            {
                break;
            }
        }

        return false;
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
        if (!_isMouseOverMenuButton && !_isMouseOverBottomPanel && !_isDraggingSlider && !_isDraggingStartMarker && !_isDraggingEndMarker)
        {
            _hideControlsTimer?.Start();
        }
    }

    private void HideControlsTimer_Tick(object? sender, EventArgs e)
    {
        _hideControlsTimer?.Stop();

        // 再次检查条件
        if (!_isMouseOverMenuButton && !_isMouseOverBottomPanel && !_isDraggingSlider && !_isDraggingStartMarker && !_isDraggingEndMarker)
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
                var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                var videoExtensions = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" };

                if (videoExtensions.Any(x => x == ext))
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

    private void VideoArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 先检查是否有正在编辑的segment，如果有则应用重命名
        ApplyRenameIfEditing();

        // 获取鼠标位置下的元素
        var element = e.OriginalSource as DependencyObject;

        // 检查是否点击在UI控件上（右侧面板、底部控制栏、按钮等）
        if (IsMouseOverUIElement(element))
        {
            return; // 点击在UI控件上，不处理播放/暂停
        }

        // 点击在视频区域，触发播放/暂停
        _viewModel.TogglePlayPauseCommand.Execute(null);
        e.Handled = true;
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

    #region Segment Drag and Drop Reordering

    private void SegmentListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDraggingSegment = false;
    }

    private void SegmentListView_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && !_isDraggingSegment)
        {
            Point currentPosition = e.GetPosition(null);
            Vector diff = _dragStartPoint - currentPosition;

            // 检测是否超过拖拽阈值
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                // 先应用任何正在进行的重命名
                ApplyRenameIfEditing();

                // 获取被拖拽的项
                var listView = sender as ListView;
                if (listView == null) return;

                var listViewItem = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
                if (listViewItem == null) return;

                var segmentVm = listViewItem.DataContext as LoopSegmentViewModel;
                if (segmentVm == null) return;

                _isDraggingSegment = true;

                // 开始拖拽操作
                var dragData = new DataObject("LoopSegmentViewModel", segmentVm);
                DragDrop.DoDragDrop(listViewItem, dragData, DragDropEffects.Move);

                _isDraggingSegment = false;
            }
        }
    }

    private void SegmentListView_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("LoopSegmentViewModel"))
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void SegmentListView_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("LoopSegmentViewModel"))
        {
            var draggedSegment = e.Data.GetData("LoopSegmentViewModel") as LoopSegmentViewModel;
            if (draggedSegment == null) return;

            // 找到drop目标
            var targetItem = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
            if (targetItem == null) return;

            var targetSegment = targetItem.DataContext as LoopSegmentViewModel;
            if (targetSegment == null || targetSegment == draggedSegment) return;

            // 获取当前顺序
            var segments = _viewModel.Segments;
            int draggedIndex = segments.IndexOf(draggedSegment);
            int targetIndex = segments.IndexOf(targetSegment);

            if (draggedIndex < 0 || targetIndex < 0) return;

            // 移动项
            segments.Move(draggedIndex, targetIndex);

            // 更新Order并保存
            var newOrder = segments.Select(s => s.Id).ToList();
            _viewModel.ReorderSegments(newOrder);

            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        do
        {
            if (current is T ancestor)
            {
                return ancestor;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        while (current != null);
        return null;
    }

    #endregion

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
            // 保存原始名称，以便Escape时恢复
            _originalSegmentName = _pendingRenameSegment.Name;

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

    /// <summary>
    /// 检查是否有正在编辑的segment，如果有则应用重命名并返回true
    /// </summary>
    private bool ApplyRenameIfEditing()
    {
        var editingSegment = SegmentsEditingFirstOrDefault();
        if (editingSegment != null)
        {
            editingSegment.IsEditing = false;
            _viewModel.OnSegmentRenamed();
            return true;
        }
        return false;
    }

    private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is LoopSegmentViewModel segmentVm)
        {
            if (segmentVm.IsEditing)
            {
                segmentVm.IsEditing = false;
                _viewModel.OnSegmentRenamed();
            }
        }
    }

    private void MainWindow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (FindParent<TextBox>(source) != null)
        {
            return;
        }

        // 应用重命名（如果正在编辑）
        ApplyRenameIfEditing();
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
                // 恢复原始名称
                if (_originalSegmentName != null)
                {
                    segmentVm.Name = _originalSegmentName;
                    _originalSegmentName = null;
                }
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

    private LoopSegmentViewModel? SegmentsEditingFirstOrDefault()
    {
        return _viewModel.Segments.FirstOrDefault(segment => segment.IsEditing);
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        var current = child;
        while (current != null)
        {
            if (current is T parent)
            {
                return parent;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        return child switch
        {
            System.Windows.Media.Visual => System.Windows.Media.VisualTreeHelper.GetParent(child),
            System.Windows.Media.Media3D.Visual3D => System.Windows.Media.VisualTreeHelper.GetParent(child),
            FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent,
            _ => null
        };
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        // 先检查是否有正在编辑的segment，如果有则应用重命名
        ApplyRenameIfEditing();

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
            "Version 1.3.0\n\n" +
            "Controls:\n" +
            "• R - Hold to record segment\n" +
            "• 1 - Mark segment start point\n" +
            "• 2 - Mark segment end point (creates segment)\n" +
            "• Space - Play/Pause\n" +
            "• Tab - Toggle segment panel\n" +
            "• Left/Right Arrow - Seek backward/forward 5 seconds\n" +
            "• Up/Down Arrow - Select previous/next segment (cycle)\n" +
            "• Mouse Wheel (video) - Adjust volume (±5%)\n" +
            "• Mouse Wheel (progress bar) - Frame step (0.5s)\n" +
            "  • + Ctrl - Fine step (0.1s)\n" +
            "  • + Shift - Coarse step (1.5s)\n" +
            "• Drag marker - Adjust segment boundaries (real-time preview)\n" +
            "• Click video - Play/Pause\n" +
            "• Right-click - Menu\n\n" +
            "Created with LibVLCSharp.",
            "About",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private bool _isDraggingSlider;

    private void SliderOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // 先检查是否有正在编辑的segment，如果有则应用重命名
        ApplyRenameIfEditing();

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

    #region Marker Rendering and Dragging

    private const double MarkerLineWidth = 2;
    private const double MarkerDotRadius = 6;
    private const double SliderThumbWidth = 14;  // 与XAML中Slider Thumb宽度保持一致
    private const double SliderThumbRadius = SliderThumbWidth / 2;

    /// <summary>
    /// 初始化marker canvas和绑定
    /// </summary>
    private void MainWindow_Loaded_Markers()
    {
        // 延迟初始化，确保Canvas已加载并测量
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            // 订阅marker属性变化
            if (_viewModel is INotifyPropertyChanged notifyViewModel)
            {
                notifyViewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName is "IsPendingStartMarkerVisible" or
                        "AreSegmentMarkersVisible" or
                        "SelectedSegmentStartPosition" or
                        "SelectedSegmentEndPosition" or
                        "PendingStartMarkerPosition")
                    {
                        RedrawMarkers();
                    }
                };
            }

            // 订阅Canvas加载事件和大小变化
            MarkerCanvas.Loaded += (s, e) => RedrawMarkers();
            MarkerCanvas.SizeChanged += (s, e) => RedrawMarkers();

            // 初始绘制，并延迟再次绘制以确保视频加载后的标记也能显示
            RedrawMarkers();

            // 延迟100ms再次检查并绘制，确保视频加载完成后的状态也能正确显示
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                RedrawMarkers();
            });
        });
    }

    /// <summary>
    /// 重新绘制所有标记
    /// </summary>
    private void RedrawMarkers()
    {
        // 确保Canvas已加载并有有效的尺寸
        if (MarkerCanvas == null || MarkerCanvas.ActualWidth <= 0 || MarkerCanvas.ActualHeight <= 0)
        {
            return;
        }

        MarkerCanvas.Children.Clear();

        // 绘制待确认标记（蓝色）
        if (_viewModel.IsPendingStartMarkerVisible)
        {
            DrawPendingMarker(_viewModel.PendingStartMarkerPosition);
        }

        // 绘制segment边界标记（绿色和红色）
        if (_viewModel.AreSegmentMarkersVisible)
        {
            DrawSegmentMarkers(_viewModel.SelectedSegmentStartPosition, _viewModel.SelectedSegmentEndPosition);
        }
    }

    /// <summary>
    /// 绘制待确认标记（蓝色圆点 + 垂直线）
    /// </summary>
    private void DrawPendingMarker(double position)
    {
        // 计算位置时考虑Slider Thumb宽度，使标记线对齐到圆点中心
        var xPos = (position / 100) * (MarkerCanvas.ActualWidth - SliderThumbWidth) + SliderThumbRadius;

        // 垂直线
        var line = new System.Windows.Shapes.Line
        {
            X1 = xPos,
            Y1 = 0,
            X2 = xPos,
            Y2 = MarkerCanvas.ActualHeight,
            Stroke = new SolidColorBrush(Color.FromArgb(200, 74, 144, 226)),
            StrokeThickness = MarkerLineWidth,
            IsHitTestVisible = false
        };

        // 顶部圆点
        var dot = new Ellipse
        {
            Width = MarkerDotRadius * 2,
            Height = MarkerDotRadius * 2,
            Fill = new SolidColorBrush(Color.FromArgb(200, 74, 144, 226)),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(dot, xPos - MarkerDotRadius);
        Canvas.SetTop(dot, -MarkerDotRadius);

        MarkerCanvas.Children.Add(line);
        MarkerCanvas.Children.Add(dot);
    }

    /// <summary>
    /// 绘制segment边界标记（绿色和红色，可拖拽）
    /// </summary>
    private void DrawSegmentMarkers(double startPos, double endPos)
    {
        // 计算位置时考虑Slider Thumb宽度，使标记线对齐到圆点中心
        var startXPos = (startPos / 100) * (MarkerCanvas.ActualWidth - SliderThumbWidth) + SliderThumbRadius;
        var endXPos = (endPos / 100) * (MarkerCanvas.ActualWidth - SliderThumbWidth) + SliderThumbRadius;

        // 起始标记（绿色）
        DrawSegmentMarker(startXPos, true);

        // 结束标记（红色）
        DrawSegmentMarker(endXPos, false);
    }

    /// <summary>
    /// 绘制单个segment标记（起始或结束）
    /// </summary>
    private void DrawSegmentMarker(double xPos, bool isStartMarker)
    {
        var color = isStartMarker
            ? Color.FromArgb(255, 95, 184, 120)   // 柔和绿色（降低饱和度）
            : Color.FromArgb(255, 232, 122, 112);  // 柔和红色（降低饱和度）

        // 垂直线
        var line = new System.Windows.Shapes.Line
        {
            X1 = xPos,
            Y1 = 0,
            X2 = xPos,
            Y2 = MarkerCanvas.ActualHeight,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = MarkerLineWidth,
            IsHitTestVisible = false
        };

        // 顶部圆点（可拖拽，无白边）
        var dot = new Ellipse
        {
            Width = MarkerDotRadius * 2,
            Height = MarkerDotRadius * 2,
            Fill = new SolidColorBrush(color),
            Cursor = Cursors.SizeWE,
            IsHitTestVisible = true
        };

        Canvas.SetLeft(dot, xPos - MarkerDotRadius);
        Canvas.SetTop(dot, -MarkerDotRadius);

        // 绑定拖拽事件
        if (isStartMarker)
        {
            dot.MouseLeftButtonDown += StartMarker_MouseDown;
            dot.MouseMove += StartMarker_MouseMove;
            dot.MouseLeftButtonUp += StartMarker_MouseUp;
        }
        else
        {
            dot.MouseLeftButtonDown += EndMarker_MouseDown;
            dot.MouseMove += EndMarker_MouseMove;
            dot.MouseLeftButtonUp += EndMarker_MouseUp;
        }

        MarkerCanvas.Children.Add(line);
        MarkerCanvas.Children.Add(dot);
    }

    #region Start Marker Dragging

    private void StartMarker_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingStartMarker = true;
        _hideControlsTimer?.Stop();
        _viewModel.OnScrubStart();

        if (sender is UIElement element)
        {
            element.CaptureMouse();
        }

        e.Handled = true;
    }

    private void StartMarker_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingStartMarker)
            return;

        // 防护：确保Canvas有有效尺寸
        if (MarkerCanvas.ActualWidth <= 0)
            return;

        var position = e.GetPosition(MarkerCanvas);

        // 反向计算：Canvas坐标 → 百分比
        // xPos = (percentage / 100) * (Canvas.Width - ThumbWidth) + ThumbRadius
        // 反向推导：percentage = ((position.X - ThumbRadius) / (Canvas.Width - ThumbWidth)) * 100
        var ratio = (position.X - SliderThumbRadius) / (MarkerCanvas.ActualWidth - SliderThumbWidth);
        ratio = Math.Clamp(ratio, 0, 1);
        var newPosition = ratio * 100;

        _viewModel.UpdateSelectedSegmentStartPosition(newPosition);
        _viewModel.SeekToMarkerPosition(newPosition);
        RedrawMarkers();

        e.Handled = true;
    }

    private void StartMarker_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingStartMarker)
        {
            _isDraggingStartMarker = false;

            if (sender is UIElement element)
            {
                element.ReleaseMouseCapture();
            }

            _viewModel.CommitSegmentMarkerChange();
            _viewModel.OnScrubEnd();
            ResetHideTimer();
            RedrawMarkers();
        }

        e.Handled = true;
    }

    #endregion

    #region End Marker Dragging

    private void EndMarker_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingEndMarker = true;
        _hideControlsTimer?.Stop();
        _viewModel.OnScrubStart();

        if (sender is UIElement element)
        {
            element.CaptureMouse();
        }

        e.Handled = true;
    }

    private void EndMarker_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingEndMarker)
            return;

        // 防护：确保Canvas有有效尺寸
        if (MarkerCanvas.ActualWidth <= 0)
            return;

        var position = e.GetPosition(MarkerCanvas);

        // 反向计算：Canvas坐标 → 百分比
        // xPos = (percentage / 100) * (Canvas.Width - ThumbWidth) + ThumbRadius
        // 反向推导：percentage = ((position.X - ThumbRadius) / (Canvas.Width - ThumbWidth)) * 100
        var ratio = (position.X - SliderThumbRadius) / (MarkerCanvas.ActualWidth - SliderThumbWidth);
        ratio = Math.Clamp(ratio, 0, 1);
        var newPosition = ratio * 100;

        _viewModel.UpdateSelectedSegmentEndPosition(newPosition);
        _viewModel.SeekToMarkerPosition(newPosition);
        RedrawMarkers();

        e.Handled = true;
    }

    private void EndMarker_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingEndMarker)
        {
            _isDraggingEndMarker = false;

            if (sender is UIElement element)
            {
                element.ReleaseMouseCapture();
            }

            _viewModel.CommitSegmentMarkerChange();
            _viewModel.OnScrubEnd();
            ResetHideTimer();
            RedrawMarkers();
        }

        e.Handled = true;
    }

    #endregion

    #endregion
}
