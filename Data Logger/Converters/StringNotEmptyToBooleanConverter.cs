using System;
using System.Globalization;
using System.Windows.Data;

namespace Data_Logger.Converters
{
    /// <summary>
    /// Converteert een string naar een boolean waarde.
    /// Retourneert true als de string niet null of leeg is, anders false.
    /// </summary>
    public class StringNotEmptyToBooleanConverter : IValueConverter
    {
        /// <summary>
        /// Converteert een string naar true als deze niet null of leeg is.
        /// </summary>
        /// <param name="value">De string die geëvalueerd moet worden.</param>
        /// <param name="targetType">Het type van de binding target property (niet gebruikt).</param>
        /// <param name="parameter">De converter parameter (niet gebruikt).</param>
        /// <param name="culture">De cultuur om te gebruiken in de converter (niet gebruikt).</param>
        /// <returns>
        /// True als <paramref name="value"/> een string is die niet null of leeg is.
        /// False in alle andere gevallen (inclusief als <paramref name="value"/> geen string is).
        /// </returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                return !string.IsNullOrEmpty(str);
            }
            return false;
        }

        /// <summary>
        /// Converteert een boolean terug naar een string.
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
                "Conversie van boolean terug naar string is niet ondersteund."
            );
        }
    }
}
