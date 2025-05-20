using System;
using System.Globalization;
using System.Windows.Data;

namespace Data_Logger.Converters
{
    /// <summary>
    /// Converteert een boolean waarde naar zijn inverse (NOT operator).
    /// </summary>
    public class InverseBooleanConverter : IValueConverter
    {
        /// <summary>
        /// Inverteert de gegeven boolean waarde.
        /// </summary>
        /// <param name="value">De boolean waarde die geïnverteerd moet worden.</param>
        /// <param name="targetType">Het type van de binding target property (niet gebruikt).</param>
        /// <param name="parameter">De converter parameter (niet gebruikt).</param>
        /// <param name="culture">De cultuur om te gebruiken in de converter (niet gebruikt).</param>
        /// <returns>
        /// De geïnverteerde boolean waarde als <paramref name="value"/> een boolean is.
        /// Anders wordt de originele <paramref name="value"/> teruggegeven.
        /// </returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return value;
        }

        /// <summary>
        /// Inverteert de gegeven boolean waarde terug.
        /// </summary>
        /// <param name="value">De boolean waarde die geïnverteerd moet worden.</param>
        /// <param name="targetType">Het type om naar te converteren (niet gebruikt).</param>
        /// <param name="parameter">De converter parameter (niet gebruikt).</param>
        /// <param name="culture">De cultuur om te gebruiken in de converter (niet gebruikt).</param>
        /// <returns>
        /// De geïnverteerde boolean waarde als <paramref name="value"/> een boolean is.
        /// Anders wordt de originele <paramref name="value"/> teruggegeven.
        /// </returns>
        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return value;
        }
    }
}
