using System;
using System.Globalization;
using System.Windows.Data;

namespace a;

public class StringNotEmptyToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return !string.IsNullOrEmpty(str); 
        }
        return false; 
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        
        throw new NotImplementedException();
    }
}