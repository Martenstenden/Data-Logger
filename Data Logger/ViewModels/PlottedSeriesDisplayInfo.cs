using System;
using Data_Logger.Core;

namespace Data_Logger.ViewModels
{
    /// <summary>
    /// ViewModel die weergave-informatie en opties beheert voor een enkele geplotte serie in een grafiek.
    /// Stelt de gebruiker in staat om bijvoorbeeld statistische lijnen (gemiddelde, min, max) aan/uit te zetten.
    /// </summary>
    public class PlottedSeriesDisplayInfo : ObservableObject
    {
        private string _seriesKey;

        /// <summary>
        /// Haalt de unieke sleutel (identifier) van de geplotte serie op.
        /// Dit is meestal de TagName of NodeId.
        /// </summary>
        public string SeriesKey
        {
            get => _seriesKey;
            private set => SetProperty(ref _seriesKey, value); // Private set, ingesteld via constructor
        }

        private bool _showMeanLine;

        /// <summary>
        /// Haalt een waarde die aangeeft of de gemiddelde-lijn voor deze serie getoond moet worden in de grafiek, op of stelt deze in.
        /// Het wijzigen van deze waarde triggert het <see cref="OnStatLineVisibilityChanged"/> event.
        /// </summary>
        public bool ShowMeanLine
        {
            get => _showMeanLine;
            set
            {
                if (SetProperty(ref _showMeanLine, value))
                {
                    OnStatLineVisibilityChanged?.Invoke(this, "mean", value);
                }
            }
        }

        private bool _showMaxLine;

        /// <summary>
        /// Haalt een waarde die aangeeft of de maximum-lijn voor deze serie getoond moet worden in de grafiek, op of stelt deze in.
        /// Het wijzigen van deze waarde triggert het <see cref="OnStatLineVisibilityChanged"/> event.
        /// </summary>
        public bool ShowMaxLine
        {
            get => _showMaxLine;
            set
            {
                if (SetProperty(ref _showMaxLine, value))
                {
                    OnStatLineVisibilityChanged?.Invoke(this, "max", value);
                }
            }
        }

        private bool _showMinLine;

        /// <summary>
        /// Haalt een waarde die aangeeft of de minimum-lijn voor deze serie getoond moet worden in de grafiek, op of stelt deze in.
        /// Het wijzigen van deze waarde triggert het <see cref="OnStatLineVisibilityChanged"/> event.
        /// </summary>
        public bool ShowMinLine
        {
            get => _showMinLine;
            set
            {
                if (SetProperty(ref _showMinLine, value))
                {
                    OnStatLineVisibilityChanged?.Invoke(this, "min", value);
                }
            }
        }

        /// <summary>
        /// Delegate voor het event dat wordt getriggerd wanneer de zichtbaarheid van een statistische lijn verandert.
        /// </summary>
        /// <param name="seriesInfo">De <see cref="PlottedSeriesDisplayInfo"/> instantie waarvan de lijnzichtbaarheid is gewijzigd.</param>
        /// <param name="lineType">Het type statistische lijn (bijv. "mean", "max", "min").</param>
        /// <param name="visibility">De nieuwe zichtbaarheidsstatus (true voor zichtbaar, false voor verborgen).</param>
        public delegate void StatLineVisibilityChangedHandler(
            PlottedSeriesDisplayInfo seriesInfo,
            string lineType,
            bool visibility
        );

        /// <summary>
        /// Event dat wordt getriggerd wanneer de gebruiker de zichtbaarheid van een statistische lijn
        /// (gemiddelde, maximum, minimum) voor deze serie wijzigt.
        /// </summary>
        public event StatLineVisibilityChangedHandler OnStatLineVisibilityChanged;

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="PlottedSeriesDisplayInfo"/> klasse.
        /// </summary>
        /// <param name="seriesKey">De unieke sleutel die deze geplotte serie identificeert.</param>
        /// <exception cref="ArgumentNullException">Als <paramref name="seriesKey"/> null of leeg is.</exception>
        public PlottedSeriesDisplayInfo(string seriesKey)
        {
            if (string.IsNullOrEmpty(seriesKey))
            {
                throw new ArgumentNullException(nameof(seriesKey));
            }
            SeriesKey = seriesKey;
            // Standaard geen statistische lijnen tonen
            _showMeanLine = false;
            _showMaxLine = false;
            _showMinLine = false;
        }
    }
}
