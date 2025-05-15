using System;
using Data_Logger.Core;
using Data_Logger.Enums;

namespace Data_Logger.Models
{
    public class LoggedTagValue : ObservableObject
    {
        private string _tagName;
        public string TagName
        {
            get => _tagName;
            set => SetProperty(ref _tagName, value);
        }

        private object _value;
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
        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        private bool _isGoodQuality = true;
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

        public string FormattedValue
        {
            get
            {
                if (!IsGoodQuality)
                    return ErrorMessage ?? "Error";
                return Value?.ToString() ?? "N/A";
            }
        }

        private TagAlarmState _alarmState = TagAlarmState.Normal;
        public TagAlarmState AlarmState
        {
            get => _alarmState;
            set => SetProperty(ref _alarmState, value);
        }
    }
}
