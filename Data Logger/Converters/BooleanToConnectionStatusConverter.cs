using System;
using System.Globalization;
using System.Windows.Data;

namespace Data_Logger.Converters
{
    /// <summary>
    /// Converteert een boolean waarde die de connectiestatus representeert
    /// naar een gebruiksvriendelijke string.
    /// </summary>
    public class BooleanToConnectionStatusConverter : IValueConverter
    {
        /// <summary>
        /// Converteert een boolean naar een connectiestatus string.
        /// </summary>
        /// <param name="value">De boolean waarde die geconverteerd moet worden. True voor verbonden, False voor niet verbonden.</param>
        /// <param name="targetType">Het type van de binding target property (niet gebruikt).</param>
        /// <param name="parameter">De converter parameter (niet gebruikt).</param>
        /// <param name="culture">De cultuur om te gebruiken in de converter (niet gebruikt).</param>
        /// <returns>
        /// "Verbonden" als <paramref name="value"/> true is.
        /// "Niet Verbonden" als <paramref name="value"/> false is.
        /// "Onbekend" als <paramref name="value"/> geen boolean is of null.
        /// </returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isConnected)
            {
                return isConnected ? "Verbonden" : "Niet Verbonden";
            }
            return "Onbekend"; // Fallback voor onverwachte types
        }

        /// <summary>
        /// Converteert een connectiestatus string terug naar een boolean.
        /// Deze methode is niet geïmplementeerd omdat de conversie typisch eenrichtingsverkeer is.
        /// </summary>
        /// <param name="value">De waarde die geconverteerd moet worden (niet gebruikt).</param>
        /// <param name="targetType">Het type om naar te converteren (niet gebruikt).</param>
        /// <param name="parameter">De converter parameter (niet gebruikt).</param>
        /// <param name="culture">De cultuur om te gebruiken in de converter (niet gebruikt).</param>
        /// <returns>Gooit altijd <see cref="NotImplementedException"/>.</returns>
        /// <exception cref="NotImplementedException">Deze methode is niet geïmplementeerd.</exception>
        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException(
                "Conversie van status string terug naar boolean is niet ondersteund."
            );
        }
    }
}
