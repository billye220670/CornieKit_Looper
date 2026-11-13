namespace CornieKit.Looper.Interpolation;

/// <summary>
/// Pure decisions for the 2x loop interpolator: seam pairs, which output
/// frame matches a clock time, and when a finished cache may take over.
/// </summary>
public static class InterpolationMath
{
    public const string ModelId = "rife-4.25-lite";
    public const int Scale = 2;
    public const int PadMultiple = 64;
    public const int MaxLongSide = 1920;
    public static readonly TimeSpan MaxSegmentDuration = TimeSpan.FromSeconds(120);

    public static int Pad(int value)
    {
        if (value <= 0)
            return PadMultiple;
        var remainder = value % PadMultiple;
        return remainder == 0 ? value : value + (PadMultiple - remainder);
    }

    public static bool IsSupportedSize(int width, int height) =>
        width >= 2 && height >= 2 && Math.Max(width, height) <= MaxLongSide;

    public static bool IsSupportedDuration(TimeSpan duration) =>
        duration > TimeSpan.Zero && duration <= MaxSegmentDuration;

    /// <summary>
    /// 2x sequence: source, midpoint, source, midpoint, … and the last midpoint
    /// is between the last source frame and the first so the loop can wrap.
    /// </summary>
    public static int OutputFrameCount(int sourceFrameCount) =>
        sourceFrameCount < 2 ? 0 : sourceFrameCount * Scale;

    public static (int Left, int Right, float Timestep) SeamPair(int outputIndex, int sourceFrameCount)
    {
        if (sourceFrameCount < 2)
            throw new ArgumentOutOfRangeException(nameof(sourceFrameCount));
        var left = (outputIndex / Scale) % sourceFrameCount;
        var isMidpoint = outputIndex % Scale == 1;
        if (!isMidpoint)
            return (left, left, 0f);
        var right = (left + 1) % sourceFrameCount;
        return (left, right, 0.5f);
    }

    public static int FrameIndex(TimeSpan time, TimeSpan start, TimeSpan end, int frameCount)
    {
        if (frameCount <= 0)
            return 0;
        var duration = (end - start).TotalSeconds;
        if (duration <= 0)
            return 0;
        var t = (time - start).TotalSeconds;
        if (t < 0)
            t = 0;
        if (t >= duration)
            t = duration - 1e-9;
        var index = (int)(t / duration * frameCount);
        if (index < 0)
            return 0;
        if (index >= frameCount)
            return frameCount - 1;
        return index;
    }

    /// <summary>
    /// A cache that is already on disk may play at the start of a loop.
    /// A cache that finishes in the middle of a loop waits for the next wrap.
    /// </summary>
    public static bool PresentImmediately(bool cacheReadyAtStart) => cacheReadyAtStart;

    public static bool HoldUntilLoopBoundary(bool becameReadyMidLoop) => becameReadyMidLoop;
}
