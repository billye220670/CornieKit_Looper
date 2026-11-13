namespace CornieKit.Looper.Interpolation;

/// <summary>
/// A few decoded frames kept ahead of the playhead. Playback copies one of
/// these slots and never reads the disk cache or allocates a new frame.
/// </summary>
public sealed class DecodedFrameRing
{
    private readonly object _gate = new();
    private readonly int[] _stored;
    private byte[][] _slots;
    private int _bytes;

    public DecodedFrameRing(int capacity = 8)
    {
        if (capacity < 2)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        _stored = new int[capacity];
        _slots = new byte[capacity][];
        Array.Fill(_stored, -1);
    }

    public int Capacity { get; }

    public void Reset(int width, int height)
    {
        var bytes = checked(width * height * 4);
        lock (_gate)
        {
            if (bytes != _bytes)
            {
                _bytes = bytes;
                _slots = new byte[Capacity][];
            }
            Array.Fill(_stored, -1);
        }
    }

    public void Clear()
    {
        lock (_gate)
            Array.Fill(_stored, -1);
    }

    public bool Contains(int frameIndex)
    {
        lock (_gate)
            return SlotHas(frameIndex);
    }

    public void Write(int frameIndex, ReadOnlySpan<byte> pixels)
    {
        lock (_gate)
        {
            var slot = frameIndex % Capacity;
            if ((uint)slot >= (uint)Capacity)
                slot += Capacity;
            if (_slots[slot] == null || _slots[slot].Length != _bytes)
                _slots[slot] = new byte[_bytes];
            pixels.Slice(0, Math.Min(pixels.Length, _bytes)).CopyTo(_slots[slot]);
            _stored[slot] = frameIndex;
        }
    }

    public bool TryCopy(int frameIndex, Span<byte> destination)
    {
        lock (_gate)
        {
            if (!SlotHas(frameIndex) || destination.Length < _bytes)
                return false;
            var slot = frameIndex % Capacity;
            if (slot < 0)
                slot += Capacity;
            _slots[slot].AsSpan(0, _bytes).CopyTo(destination);
            return true;
        }
    }

    /// <summary>
    /// Copies <paramref name="frameIndex"/> when it is ready, otherwise the newest
    /// decoded frame at or before it. Playback must not skip the present just
    /// because the exact index is one decode behind.
    /// </summary>
    public bool TryCopyLatest(int frameIndex, int frameCount, Span<byte> destination)
    {
        if (frameCount <= 0 || destination.Length < _bytes)
            return false;
        lock (_gate)
        {
            for (var back = 0; back < Capacity; back++)
            {
                var index = frameIndex - back;
                if (index < 0)
                    index += frameCount;
                if ((uint)index >= (uint)frameCount || !SlotHas(index))
                    continue;
                var slot = index % Capacity;
                if (slot < 0)
                    slot += Capacity;
                _slots[slot].AsSpan(0, _bytes).CopyTo(destination);
                return true;
            }
            return false;
        }
    }

    /// <summary>The next frame index the decoder should produce, or -1 when the window is full.</summary>
    public int NextToDecode(int playhead, int frameCount, int skipBelow = 0)
    {
        if (frameCount <= 0)
            return -1;
        lock (_gate)
        {
            for (var ahead = 0; ahead < Capacity && ahead < frameCount; ahead++)
            {
                var index = playhead + ahead;
                if (index >= frameCount)
                    index -= frameCount;
                if (index < skipBelow)
                    continue;
                if (!SlotHas(index))
                    return index;
            }
            return -1;
        }
    }

    private bool SlotHas(int frameIndex)
    {
        var slot = frameIndex % Capacity;
        if (slot < 0)
            slot += Capacity;
        return _stored[slot] == frameIndex && _slots[slot] != null;
    }
}
