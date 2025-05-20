using System;
using System.Globalization;
using System.Windows.Data;

namespace Data_Logger.Converters
{
    /// <summary>
    /// Converteert een boolean waarde naar een string die "Forward" of "Inverse" representeert.
    /// </summary>
    public class BooleanToForwardBackwardConverter : IValueConverter
    {
        /// <summary>
        /// Converteert een boolean naar "Forward" of "Inverse".
        /// </summary>
        /// <param name="value">De boolean waarde die geconverteerd moet worden.</param>
        /// <param name="targetType">Het type van de binding target property (niet gebruikt).</param>
        /// <param name="parameter">De converter parameter (niet gebruikt).</param>
        /// <param name="culture">De cultuur om te gebruiken in de converter (niet gebruikt).</param>
        /// <returns>
        /// "Forward" als <paramref name="value"/> true is.
        /// "Inverse" als <paramref name="value"/> false is.
        /// <see cref="string.Empty"/> als <paramref name="value"/> geen boolean is of null.
        /// </returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isForward)
            {
                return isForward ? "Forward" : "Inverse";
            }
            return string.Empty; // Fallback voor onverwachte types
        }

        /// <summary>
        /// Converteert "Forward" of "Inverse" terug naar een boolean.
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
                "Conversie van 'Forward'/'Inverse' terug naar boolean is niet ondersteund."
            );
        }
    }
}
