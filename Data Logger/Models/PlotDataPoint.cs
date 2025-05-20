using System;
using Data_Logger.Core;

namespace Data_Logger.Models
{
    /// <summary>
    /// Representeert een enkel datapunt voor weergave in een grafiek.
    /// Bevat een tijdstempel en een numerieke waarde.
    /// </summary>
    public class PlotDataPoint : ObservableObject
    {
        private DateTime _timestamp;

        /// <summary>
        /// Haalt het tijdstempel van het datapunt op of stelt deze in.
        /// </summary>
        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        private double _value;

        /// <summary>
        /// Haalt de numerieke waarde van het datapunt op of stelt deze in.
        /// </summary>
        public double Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="PlotDataPoint"/> klasse.
        /// </summary>
        public PlotDataPoint() { }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="PlotDataPoint"/> klasse met opgegeven waarden.
        /// </summary>
        /// <param name="timestamp">Het tijdstempel van het datapunt.</param>
        /// <param name="value">De numerieke waarde van het datapunt.</param>
        public PlotDataPoint(DateTime timestamp, double value)
        {
            _timestamp = timestamp;
            _value = value;
        }
    }
}
