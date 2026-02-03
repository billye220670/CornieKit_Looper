using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CornieKit.Looper.ViewModels;

namespace CornieKit.Looper;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private DispatcherTimer? _renameTimer;
    private LoopSegmentViewModel? _pendingRenameSegment;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        KeyDown += MainWindow_KeyDown;
        KeyUp += MainWindow_KeyUp;

        _renameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _renameTimer.Tick += RenameTimer_Tick;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Focus();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.Dispose();
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
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
    }

    private void MainWindow_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.R)
        {
            _viewModel.OnRecordKeyUp();
            e.Handled = true;
        }
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

    private void SegmentListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 取消重命名计时器
        CancelRenameTimer();

        if (SegmentListView.SelectedItem is LoopSegmentViewModel segmentVm)
        {
            // 确保不在编辑模式
            segmentVm.IsEditing = false;
            _viewModel.PlaySegmentCommand.Execute(segmentVm);
        }
    }

    private void SegmentName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is LoopSegmentViewModel segmentVm)
        {
            // 只有已选中的项目再次点击名称才会启动重命名计时器
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

            // 聚焦到 TextBox
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
                // 移除焦点
                SegmentListView.Focus();
            }
            else if (e.Key == Key.Escape)
            {
                segmentVm.IsEditing = false;
                e.Handled = true;
                SegmentListView.Focus();
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
            "Press and hold R to mark video segments.\n" +
            "Created with LibVLCSharp.",
            "About",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private bool _isDraggingSlider;

    private void SliderOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSlider = true;
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