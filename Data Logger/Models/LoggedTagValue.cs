using System;
using Data_Logger.Core;
using Data_Logger.Enums;

namespace Data_Logger.Models
{
    /// <summary>
    /// Representeert een individuele gelogde waarde voor een specifieke tag.
    /// Bevat de tagnaam, de gemeten waarde, het tijdstip van logging, de datakwaliteit,
    /// een eventuele foutmelding, en de alarmstatus.
    /// </summary>
    public class LoggedTagValue : ObservableObject
    {
        private string _tagName;

        /// <summary>
        /// Haalt de naam van de tag waarvan deze waarde afkomstig is, op of stelt deze in.
        /// </summary>
        public string TagName
        {
            get => _tagName;
            set => SetProperty(ref _tagName, value);
        }

        private object _value;

        /// <summary>
        /// Haalt de gelogde waarde van de tag op of stelt deze in.
        /// Bij het instellen wordt ook <see cref="FormattedValue"/> bijgewerkt.
        /// </summary>
        public object Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    OnPropertyChanged(nameof(FormattedValue));
                }
            }
        }

        private DateTime _timestamp;

        /// <summary>
        /// Haalt het tijdstip waarop deze waarde is gelogd, op of stelt deze in.
        /// </summary>
        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        private bool _isGoodQuality = true;

        /// <summary>
        /// Haalt een waarde die aangeeft of de kwaliteit van de gelogde waarde goed is, op of stelt deze in.
        /// Default is true. Bij het instellen wordt ook <see cref="FormattedValue"/> bijgewerkt.
        /// </summary>
        public bool IsGoodQuality
        {
            get => _isGoodQuality;
            set
            {
                if (SetProperty(ref _isGoodQuality, value))
                {
                    OnPropertyChanged(nameof(FormattedValue));
                }
            }
        }

        private string _errorMessage;

        /// <summary>
        /// Haalt een eventuele foutmelding die is opgetreden bij het lezen van deze waarde, op of stelt deze in.
        /// Is typisch null of leeg als <see cref="IsGoodQuality"/> true is.
        /// Bij het instellen wordt ook <see cref="FormattedValue"/> bijgewerkt.
        /// </summary>
        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnPropertyChanged(nameof(FormattedValue));
                }
            }
        }

        /// <summary>
        /// Haalt een geformatteerde stringrepresentatie van de waarde op,
        /// rekening houdend met de datakwaliteit en eventuele foutmeldingen.
        /// </summary>
        public string FormattedValue
        {
            get
            {
                if (!IsGoodQuality)
                {
                    return ErrorMessage ?? "Error";
                }
                return Value?.ToString() ?? "N/A";
            }
        }

        private TagAlarmState _alarmState = TagAlarmState.Normal;

        /// <summary>
        /// Haalt de huidige alarmstatus van deze tagwaarde op of stelt deze in.
        /// Default is <see cref="TagAlarmState.Normal"/>.
        /// </summary>
        public TagAlarmState AlarmState
        {
            get => _alarmState;
            set => SetProperty(ref _alarmState, value);
        }
    }
}
