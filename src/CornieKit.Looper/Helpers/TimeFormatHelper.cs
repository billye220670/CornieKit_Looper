namespace CornieKit.Looper.Helpers;

public static class TimeFormatHelper
{
    public static string FormatTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
        }
        return $"{time.Minutes:D2}:{time.Seconds:D2}";
    }

    public static string FormatTimeWithMilliseconds(TimeSpan time)
    {
        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
        }
        return $"{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
    }
}
