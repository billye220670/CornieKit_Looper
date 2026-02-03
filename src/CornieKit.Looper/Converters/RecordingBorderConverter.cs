using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CornieKit.Looper.Converters;

public class RecordingBorderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isRecording && isRecording)
        {
            return new SolidColorBrush(Color.FromRgb(255, 0, 0));
        }
        return new SolidColorBrush(Color.FromRgb(64, 64, 64));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
