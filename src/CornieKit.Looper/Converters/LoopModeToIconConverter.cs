using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CornieKit.Looper.Models;

namespace CornieKit.Looper.Converters;

public class LoopModeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LoopMode mode)
        {
            var iconName = mode switch
            {
                LoopMode.Single => "repeat-single",
                LoopMode.SequentialLoop => "repeat-playlist",
                LoopMode.Random => "shuffle-playlist",
                _ => "repeat-single"
            };

            var bitmap = new BitmapImage(new Uri($"pack://application:,,,/Assets/Icons/{iconName}.png"));
            return new ImageBrush(bitmap);
        }

        return Binding.DoNothing;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
