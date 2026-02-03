using CornieKit.Looper.Models;

namespace CornieKit.Looper.Services;

public class SegmentManager
{
    private readonly List<LoopSegment> _segments = new();
    private LoopSegment? _currentSegment;
    private LoopMode _loopMode = LoopMode.Single;
    private readonly Random _random = new();

    public IReadOnlyList<LoopSegment> Segments => _segments.AsReadOnly();
    public LoopSegment? CurrentSegment => _currentSegment;
    public LoopMode LoopMode
    {
        get => _loopMode;
        set => _loopMode = value;
    }

    public event EventHandler? SegmentsChanged;
    public event EventHandler<LoopSegment>? CurrentSegmentChanged;

    public void LoadSegments(IEnumerable<LoopSegment> segments)
    {
        _segments.Clear();
        _segments.AddRange(segments.OrderBy(s => s.Order));
        SegmentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public LoopSegment AddSegment(TimeSpan startTime, TimeSpan endTime)
    {
        var segment = new LoopSegment
        {
            Id = Guid.NewGuid(),
            Name = $"Segment {_segments.Count + 1}",
            StartTime = startTime,
            EndTime = endTime,
            Order = _segments.Count,
            CreatedAt = DateTime.Now
        };

        _segments.Add(segment);
        SegmentsChanged?.Invoke(this, EventArgs.Empty);

        return segment;
    }

    public void RemoveSegment(Guid segmentId)
    {
        var segment = _segments.FirstOrDefault(s => s.Id == segmentId);
        if (segment != null)
        {
            _segments.Remove(segment);
            ReorderSegments();
            SegmentsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RenameSegment(Guid segmentId, string newName)
    {
        var segment = _segments.FirstOrDefault(s => s.Id == segmentId);
        if (segment != null)
        {
            segment.Name = newName;
            SegmentsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ReorderSegments(IEnumerable<Guid>? newOrder = null)
    {
        if (newOrder != null)
        {
            var orderDict = newOrder.Select((id, index) => new { id, index })
                                    .ToDictionary(x => x.id, x => x.index);

            foreach (var segment in _segments)
            {
                if (orderDict.TryGetValue(segment.Id, out var newIndex))
                {
                    segment.Order = newIndex;
                }
            }
        }
        else
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                _segments[i].Order = i;
            }
        }

        _segments.Sort((a, b) => a.Order.CompareTo(b.Order));
        SegmentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCurrentSegment(LoopSegment? segment)
    {
        _currentSegment = segment;
        if (segment != null)
        {
            CurrentSegmentChanged?.Invoke(this, segment);
        }
    }

    public LoopSegment? GetNextSegment()
    {
        if (_segments.Count == 0)
            return null;

        if (_currentSegment == null)
            return _segments.FirstOrDefault();

        return _loopMode switch
        {
            LoopMode.Single => _currentSegment,
            LoopMode.Sequential => GetNextInOrder(false),
            LoopMode.Random => GetRandomSegment(),
            LoopMode.SequentialLoop => GetNextInOrder(true),
            _ => _currentSegment
        };
    }

    private LoopSegment? GetNextInOrder(bool loop)
    {
        if (_currentSegment == null)
            return _segments.FirstOrDefault();

        var currentIndex = _segments.IndexOf(_currentSegment);
        if (currentIndex < 0)
            return _segments.FirstOrDefault();

        var nextIndex = currentIndex + 1;
        if (nextIndex >= _segments.Count)
        {
            return loop ? _segments.FirstOrDefault() : null;
        }

        return _segments[nextIndex];
    }

    private LoopSegment? GetRandomSegment()
    {
        if (_segments.Count == 0)
            return null;

        if (_segments.Count == 1)
            return _segments[0];

        var availableSegments = _segments.Where(s => s.Id != _currentSegment?.Id).ToList();
        if (availableSegments.Count == 0)
            return _currentSegment;

        var randomIndex = _random.Next(availableSegments.Count);
        return availableSegments[randomIndex];
    }

    public void Clear()
    {
        _segments.Clear();
        _currentSegment = null;
        SegmentsChanged?.Invoke(this, EventArgs.Empty);
    }
}
