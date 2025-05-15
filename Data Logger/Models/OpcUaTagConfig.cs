using System;
using System.Collections.Generic;
using Data_Logger.Core;
using Data_Logger.Enums;
using Newtonsoft.Json;

namespace Data_Logger.Models
{
    public class OpcUaTagConfig : ObservableObject
    {
        private string _tagName = "Nieuwe OPC UA Tag";
        public string TagName
        {
            get => _tagName;
            set => SetProperty(ref _tagName, value);
        }

        private string _nodeId = "ns=2;s=MyVariable";
        public string NodeId
        {
            get => _nodeId;
            set => SetProperty(ref _nodeId, value);
        }

        private OpcUaDataType _dataType = OpcUaDataType.Variant;
        public OpcUaDataType DataType
        {
            get => _dataType;
            set => SetProperty(ref _dataType, value);
        }

        private int _samplingInterval = 1000;
        public int SamplingInterval
        {
            get => _samplingInterval;
            set => SetProperty(ref _samplingInterval, value);
        }

        private bool _isActive = true;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        private object _currentValue;
        public object CurrentValue
        {
            get => _currentValue;
            set
            {
                if (SetProperty(ref _currentValue, value))
                {
                    OnPropertyChanged(nameof(FormattedLiveValue));
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
                    OnPropertyChanged(nameof(FormattedLiveValue));
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
                    OnPropertyChanged(nameof(FormattedLiveValue));
                }
            }
        }

        public string FormattedLiveValue
        {
            get
            {
                if (!IsGoodQuality && !string.IsNullOrEmpty(ErrorMessage))
                    return ErrorMessage;
                if (!IsGoodQuality)
                    return "Bad Quality";
                return CurrentValue?.ToString() ?? "N/A";
            }
        }

        private bool _isAlarmingEnabled;
        public bool IsAlarmingEnabled
        {
            get => _isAlarmingEnabled;
            set => SetProperty(ref _isAlarmingEnabled, value);
        }

        private double? _highHighLimit;
        public double? HighHighLimit
        {
            get => _highHighLimit;
            set => SetProperty(ref _highHighLimit, value);
        }

        private double? _highLimit;
        public double? HighLimit
        {
            get => _highLimit;
            set => SetProperty(ref _highLimit, value);
        }

        private double? _lowLimit;
        public double? LowLimit
        {
            get => _lowLimit;
            set => SetProperty(ref _lowLimit, value);
        }

        private double? _lowLowLimit;
        public double? LowLowLimit
        {
            get => _lowLowLimit;
            set => SetProperty(ref _lowLowLimit, value);
        }

        private string _alarmMessageFormat =
            "{TagName} is in alarm ({AlarmState}) met waarde {Value}";
        public string AlarmMessageFormat
        {
            get => _alarmMessageFormat;
            set => SetProperty(ref _alarmMessageFormat, value);
        }

        private TagAlarmState _currentAlarmState = TagAlarmState.Normal;
        public TagAlarmState CurrentAlarmState
        {
            get => _currentAlarmState;
            set => SetProperty(ref _currentAlarmState, value);
        }

        private DateTime? _alarmTimestamp;
        public DateTime? AlarmTimestamp
        {
            get => _alarmTimestamp;
            set => SetProperty(ref _alarmTimestamp, value);
        }

        private bool _isOutlierDetectionEnabled;
        public bool IsOutlierDetectionEnabled
        {
            get => _isOutlierDetectionEnabled;
            set
            {
                if (SetProperty(ref _isOutlierDetectionEnabled, value))
                {
                    ResetBaselineState();
                }
            }
        }

        private int _baselineSampleSize = 20;
        public int BaselineSampleSize
        {
            get => _baselineSampleSize;
            set => SetProperty(ref _baselineSampleSize, value);
        }

        private double _outlierStandardDeviationFactor = 3.0;
        public double OutlierStandardDeviationFactor
        {
            get => _outlierStandardDeviationFactor;
            set => SetProperty(ref _outlierStandardDeviationFactor, value);
        }

        [JsonIgnore]
        public List<double> BaselineDataPoints { get; private set; } = new List<double>();

        [JsonIgnore]
        public bool IsBaselineEstablished { get; set; } = false;

        [JsonIgnore]
        public double BaselineMean { get; set; } = 0;

        [JsonIgnore]
        public double BaselineStandardDeviation { get; set; } = 0;

        public void ResetBaselineState()
        {
            BaselineDataPoints.Clear();
            IsBaselineEstablished = false;
            BaselineMean = 0;
            BaselineStandardDeviation = 0;
            CurrentBaselineCount = 0;
            SumOfValuesForBaseline = 0;
            SumOfSquaresForBaseline = 0;
            _logger?.Debug("Expanding baseline state gereset voor tag {TagName}", TagName);
        }

        [JsonIgnore]
        private Serilog.ILogger _logger = Serilog.Log.Logger.ForContext<OpcUaTagConfig>();

        [JsonIgnore]
        public int CurrentBaselineCount { get; set; } = 0;

        [JsonIgnore]
        public double SumOfValuesForBaseline { get; set; } = 0;

        [JsonIgnore]
        public double SumOfSquaresForBaseline { get; set; } = 0;
    }
}
