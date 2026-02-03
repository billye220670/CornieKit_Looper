using CommunityToolkit.Mvvm.ComponentModel;
using CornieKit.Looper.Models;
using CornieKit.Looper.Helpers;

namespace CornieKit.Looper.ViewModels;

public partial class LoopSegmentViewModel : ObservableObject
{
    private readonly LoopSegment _segment;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isEditing;

    public Guid Id => _segment.Id;
    public string Name
    {
        get => _segment.Name;
        set
        {
            if (_segment.Name != value)
            {
                _segment.Name = value;
                OnPropertyChanged();
            }
        }
    }

    public string TimeRange => $"{TimeFormatHelper.FormatTime(_segment.StartTime)} - {TimeFormatHelper.FormatTime(_segment.EndTime)}";
    public string DurationText => TimeFormatHelper.FormatTime(_segment.Duration);
    public TimeSpan StartTime => _segment.StartTime;
    public TimeSpan EndTime => _segment.EndTime;

    public LoopSegment Segment => _segment;

    public LoopSegmentViewModel(LoopSegment segment)
    {
        _segment = segment;
    }
}
