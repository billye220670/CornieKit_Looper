using System.Diagnostics;

namespace CornieKit.Looper.Interpolation;

/// <summary>
/// Free-running clock for a cached loop. It does not follow LibVLC's 50ms
/// position reports, which step and sometimes move backward.
/// </summary>
public sealed class LoopPlaybackClock
{
    private long _originStamp;
    private long _originTicks;
    private long _frozenTicks;
    private int _running;
    private long _durationTicks = 1;

    public void Restart(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            duration = TimeSpan.FromMilliseconds(1);
        _durationTicks = duration.Ticks;
        _originTicks = 0;
        _frozenTicks = 0;
        _originStamp = Stopwatch.GetTimestamp();
        Volatile.Write(ref _running, 1);
    }

    public void SetRunning(bool running)
    {
        if (running)
        {
            if (Volatile.Read(ref _running) == 1)
                return;
            _originTicks = _frozenTicks;
            _originStamp = Stopwatch.GetTimestamp();
            Volatile.Write(ref _running, 1);
            return;
        }

        if (Volatile.Read(ref _running) == 0)
            return;
        _frozenTicks = ElapsedTicks();
        Volatile.Write(ref _running, 0);
    }

    public TimeSpan PositionInSegment() => TimeSpan.FromTicks(ElapsedTicks());

    public static TimeSpan Wrap(TimeSpan elapsed, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return TimeSpan.Zero;
        var ticks = elapsed.Ticks % duration.Ticks;
        if (ticks < 0)
            ticks += duration.Ticks;
        return TimeSpan.FromTicks(ticks);
    }

    private long ElapsedTicks()
    {
        long raw;
        if (Volatile.Read(ref _running) == 0)
            raw = _frozenTicks;
        else
        {
            var elapsed = Stopwatch.GetElapsedTime(_originStamp);
            raw = _originTicks + elapsed.Ticks;
        }
        var duration = _durationTicks;
        if (duration <= 0)
            return 0;
        raw %= duration;
        if (raw < 0)
            raw += duration;
        return raw;
    }
}
