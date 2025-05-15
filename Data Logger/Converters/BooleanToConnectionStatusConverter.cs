using System;
using System.Globalization;
using System.Windows.Data;

namespace Data_Logger.Converters
{
    public class BooleanToConnectionStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isConnected)
            {
                return isConnected ? "Verbonden" : "Niet Verbonden";
            }
            return "Onbekend";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}