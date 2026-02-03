using System.Globalization;
using System.Windows.Data;
using CornieKit.Looper.Models;

namespace CornieKit.Looper.Converters;

public class LoopModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LoopMode loopMode)
        {
            return loopMode switch
            {
                LoopMode.Single => "Single (Loop current)",
                LoopMode.Sequential => "Sequential",
                LoopMode.Random => "Random",
                LoopMode.SequentialLoop => "Sequential Loop",
                _ => loopMode.ToString()
            };
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return str switch
            {
                "Single (Loop current)" => LoopMode.Single,
                "Sequential" => LoopMode.Sequential,
                "Random" => LoopMode.Random,
                "Sequential Loop" => LoopMode.SequentialLoop,
                _ => LoopMode.Single
            };
        }
        return LoopMode.Single;
    }
}
