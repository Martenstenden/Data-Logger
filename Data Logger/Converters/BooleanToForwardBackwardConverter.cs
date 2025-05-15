using System;
using System.Globalization;
using System.Windows.Data;

namespace Data_Logger.Converters;

public class BooleanToForwardBackwardConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isForward)
        {
            return isForward ? "Forward" : "Inverse";
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}