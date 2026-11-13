namespace CornieKit.Looper.Interpolation;

/// <summary>
/// Keeps the encoded frames seen while a segment plays from its start to its end.
/// Duplicate callbacks for the same source frame are ignored.
/// </summary>
public sealed class SegmentFrameCollector
{
    private readonly List<byte[]> _frames = new();
    private TimeSpan _lastTime = TimeSpan.MinValue;
    private bool _sawStart;

    public TimeSpan Start { get; private set; }
    public TimeSpan End { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Count { get { lock (_frames) return _frames.Count; } }
    public bool IsComplete { get; private set; }

    public void Reset(TimeSpan start, TimeSpan end)
    {
        lock (_frames)
        {
            Start = start;
            End = end;
            _frames.Clear();
            _lastTime = TimeSpan.MinValue;
            _sawStart = false;
            IsComplete = false;
            Width = 0;
            Height = 0;
        }
    }

    public bool Add(TimeSpan time, byte[] encoded, int width, int height)
    {
        lock (_frames)
        {
            if (IsComplete || encoded.Length == 0 || width < 2 || height < 2)
                return IsComplete;
            var duration = End - Start;
            if (duration <= TimeSpan.Zero)
                return false;
            var slack = TimeSpan.FromMilliseconds(80);
            if (time < Start - slack || time > End + slack)
                return false;
            if (Width == 0)
            {
                Width = width;
                Height = height;
            }
            else if (width != Width || height != Height)
            {
                return false;
            }

            // A seek back to the start begins another pass. Keep the fuller one.
            if (_lastTime != TimeSpan.MinValue && time + slack < _lastTime)
            {
                if (ReachedEnd(_lastTime) && _sawStart && _frames.Count >= 2)
                {
                    IsComplete = true;
                    return true;
                }
                _frames.Clear();
                _sawStart = false;
            }

            // LibVLC's clock often repeats a timestamp for several real frames.
            // Dropping those made the pass look short and the loop was discarded.
            if (time <= Start + slack)
                _sawStart = true;
            _frames.Add(encoded);
            _lastTime = time;
            if (_sawStart && ReachedEnd(time) && _frames.Count >= 2)
                IsComplete = true;
            return IsComplete;
        }
    }

    /// <summary>
    /// The playhead wrapped. Accept the pass when it covered the segment,
    /// otherwise keep collecting on the next repeat.
    /// </summary>
    public bool TryFreeze()
    {
        lock (_frames)
        {
            if (IsComplete)
                return _frames.Count >= 2;
            if (_sawStart && ReachedEnd(_lastTime) && _frames.Count >= 2)
            {
                IsComplete = true;
                return true;
            }
            return false;
        }
    }

    public byte[][] Snapshot()
    {
        lock (_frames)
        {
            if (_frames.Count < 2)
                throw new InvalidOperationException("Frame collection is not complete.");
            return _frames.ToArray();
        }
    }

    private bool ReachedEnd(TimeSpan time) =>
        time >= End - TimeSpan.FromMilliseconds(Math.Min(200, Math.Max(40, (End - Start).TotalMilliseconds * 0.05)));
}
