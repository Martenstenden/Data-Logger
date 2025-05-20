using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Data_Logger.Converters
{
    /// <summary>
    /// Converteert een integer (aantal) naar een <see cref="Visibility"/> waarde.
    /// Typisch gebruikt om UI-elementen zichtbaar te maken als het aantal groter dan nul is.
    /// </summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// Converteert een integer aantal naar <see cref="Visibility.Visible"/> of <see cref="Visibility.Collapsed"/>.
        /// </summary>
        /// <param name="value">Het integer aantal dat geconverteerd moet worden.</param>
        /// <param name="targetType">Het type van de binding target property (niet gebruikt).</param>
        /// <param name="parameter">De converter parameter (niet gebruikt).</param>
        /// <param name="culture">De cultuur om te gebruiken in de converter (niet gebruikt).</param>
        /// <returns>
        /// <see cref="Visibility.Visible"/> als <paramref name="value"/> een integer is en groter dan 0.
        /// <see cref="Visibility.Collapsed"/> in alle andere gevallen.
        /// </returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        /// <summary>
        /// Converteert een <see cref="Visibility"/> waarde terug naar een integer aantal.
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
                "Conversie van Visibility terug naar count is niet ondersteund."
            );
        }
    }
}
