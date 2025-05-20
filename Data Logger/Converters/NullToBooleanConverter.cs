using System;
using System.Globalization;
using System.Windows.Data;

namespace Data_Logger.Converters
{
    /// <summary>
    /// Converteert een object naar een boolean waarde.
    /// Retourneert true als de waarde niet null is, anders false.
    /// </summary>
    public class NullToBooleanConverter : IValueConverter
    {
        /// <summary>
        /// Converteert een object naar true als het niet null is, anders false.
        /// </summary>
        /// <param name="value">Het object dat geëvalueerd moet worden.</param>
        /// <param name="targetType">Het type van de binding target property (niet gebruikt).</param>
        /// <param name="parameter">De converter parameter (niet gebruikt).</param>
        /// <param name="culture">De cultuur om te gebruiken in de converter (niet gebruikt).</param>
        /// <returns>True als <paramref name="value"/> niet null is; anders false.</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        /// <summary>
        /// Converteert een boolean terug naar een object.
        /// Deze methode is niet geïmplementeerd omdat de conversie typisch eenrichtingsverkeer is.
        /// </summary>
        /// <param name="value">De waarde die geconverteerd moet worden (niet gebruikt).</param>
        /// <param name="targetType">Het type om naar te converteren (niet gebruikt).</param>
        /// <param name="parameter">De converter parameter (niet gebruikt).</param>
        /// <param name="culture">De cultuur om te gebruiken in de converter (niet gebruikt).</param>
        /// <returns>Gooit altijd <see cref="NotImplementedException"/>.</returns>
        /// <exception cref="NotImplementedException">
        /// Deze methode is niet geïmplementeerd. Conversie van boolean naar (potentieel null) object is context-specifiek.
        /// </exception>
        public object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture
        )
        {
            throw new NotImplementedException(
                "NullToBooleanConverter.ConvertBack is niet geïmplementeerd."
            );
        }
    }
}
