namespace CornieKit.Looper.Interpolation;

/// <summary>
/// The first frames of a cached loop, decoded once and never overwritten.
/// The sliding ring would otherwise recycle those slots while prefetching
/// the wrap, and the picture stalls until disk decode catches up.
/// </summary>
public sealed class SeamFrames
{
    public const int Count = 16;

    private byte[]?[] _frames = Array.Empty<byte[]?>();
    private int _bytes;
    private int _filled;

    public int Filled => _filled;

    public void Reset(int width, int height)
    {
        _bytes = checked(width * height * 4);
        _frames = new byte[]?[Count];
        _filled = 0;
    }

    public void Clear()
    {
        _frames = Array.Empty<byte[]?>();
        _bytes = 0;
        _filled = 0;
    }

    public int NextMissing(int frameCount)
    {
        var limit = Math.Min(Count, frameCount);
        for (var i = 0; i < limit; i++)
        {
            if (_frames[i] == null)
                return i;
        }
        return -1;
    }

    public void Write(int index, ReadOnlySpan<byte> pixels)
    {
        if ((uint)index >= (uint)_frames.Length || pixels.Length < _bytes || _frames[index] != null)
            return;
        var slot = new byte[_bytes];
        pixels.Slice(0, _bytes).CopyTo(slot);
        _frames[index] = slot;
        _filled++;
    }

    public bool TryCopy(int index, Span<byte> destination)
    {
        if ((uint)index >= (uint)_frames.Length || destination.Length < _bytes)
            return false;
        var slot = _frames[index];
        if (slot == null || slot.Length < _bytes)
            return false;
        slot.AsSpan(0, _bytes).CopyTo(destination);
        return true;
    }
}
