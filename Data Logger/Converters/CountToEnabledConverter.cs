using System;
using System.Globalization;
using System.Windows.Data;

namespace Data_Logger.Converters
{
    /// <summary>
    /// Converteert een integer (aantal) naar een boolean waarde.
    /// Typisch gebruikt om UI-elementen te enablen (true) als het aantal groter dan nul is.
    /// </summary>
    public class CountToEnabledConverter : IValueConverter
    {
        /// <summary>
        /// Converteert een integer aantal naar een boolean.
        /// </summary>
        /// <param name="value">Het integer aantal dat geconverteerd moet worden.</param>
        /// <param name="targetType">Het type van de binding target property (niet gebruikt).</param>
        /// <param name="parameter">De converter parameter (niet gebruikt).</param>
        /// <param name="culture">De cultuur om te gebruiken in de converter (niet gebruikt).</param>
        /// <returns>
        /// True als <paramref name="value"/> een integer is en groter dan 0.
        /// False in alle andere gevallen.
        /// </returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count > 0;
            }
            return false;
        }

        /// <summary>
        /// Converteert een boolean terug naar een integer aantal.
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
                "Conversie van boolean terug naar count is niet ondersteund."
            );
        }
    }
}
