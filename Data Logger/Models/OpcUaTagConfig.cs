using System;
using System.Collections.Generic;
using System.Linq;
using Data_Logger.Core;
using Data_Logger.Enums;
using Newtonsoft.Json;
using Serilog;

namespace Data_Logger.Models
{
    /// <summary>
    /// Representeert de configuratie voor een individuele OPC UA-tag die gemonitord moet worden.
    /// Bevat details zoals NodeId, gewenst datatype, sampling interval, en alarm/outlier instellingen.
    /// </summary>
    public class OpcUaTagConfig : ObservableObject
    {
        private string _tagName = "Nieuwe OPC UA Tag";

        /// <summary>
        /// Haalt de gebruiksvriendelijke naam voor deze OPC UA-tag op of stelt deze in.
        /// </summary>
        public string TagName
        {
            get => _tagName;
            set => SetProperty(ref _tagName, value);
        }

        private string _nodeId = "ns=2;s=MyVariable"; // Voorbeeld NodeId

        /// <summary>
        /// Haalt de NodeId (string representatie) van de OPC UA-tag op de server op of stelt deze in.
        /// Bijvoorbeeld: "ns=2;i=1234" of "ns=3;s=MijnVariabele".
        /// </summary>
        public string NodeId
        {
            get => _nodeId;
            set => SetProperty(ref _nodeId, value);
        }

        /// <summary>
        /// Haalt een <see cref="IEnumerable{T}"/> op die alle waarden van de <see cref="OpcUaDataType"/> enum bevat.
        /// Handig voor ComboBox binding in de UI.
        /// </summary>
        public static IEnumerable<OpcUaDataType> Instance =>
            Enum.GetValues(typeof(OpcUaDataType)).Cast<OpcUaDataType>();

        private OpcUaDataType _dataType = OpcUaDataType.Variant;

        /// <summary>
        /// Haalt het gewenste datatype voor het lezen van de OPC UA-tag op of stelt deze in.
        /// <see cref="OpcUaDataType.Variant"/> laat de server het type bepalen.
        /// </summary>
        public OpcUaDataType DataType
        {
            get => _dataType;
            set => SetProperty(ref _dataType, value);
        }

        private int _samplingInterval = 1000; // in milliseconden

        /// <summary>
        /// Haalt het door de client gewenste sampling interval in milliseconden op of stelt deze in.
        /// De OPC UA server kan een ander (meestal langzamer) interval toewijzen.
        /// </summary>
        public int SamplingInterval
        {
            get => _samplingInterval;
            set => SetProperty(ref _samplingInterval, value >= -1 ? value : 1000); // Basis validatie
        }

        private bool _isActive = true;

        /// <summary>
        /// Haalt een waarde die aangeeft of deze tag actief gemonitord moet worden, op of stelt deze in.
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        // Live data eigenschappen (worden bijgewerkt door de OpcUaService)
        private object _currentValue;

        /// <summary>
        /// Haalt de laatst ontvangen waarde van deze tag op of stelt deze in.
        /// Deze property wordt bijgewerkt door de OPC UA service.
        /// </summary>
        [JsonIgnore] // Wordt niet geserialiseerd in settings
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

        /// <summary>
        /// Haalt het tijdstip (meestal source timestamp) van de <see cref="CurrentValue"/> op of stelt deze in.
        /// </summary>
        [JsonIgnore]
        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        private bool _isGoodQuality = true;

        /// <summary>
        /// Haalt een waarde die aangeeft of de kwaliteit van de <see cref="CurrentValue"/> goed is op of stelt deze in.
        /// </summary>
        [JsonIgnore]
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

        /// <summary>
        /// Haalt een eventuele foutmelding gerelateerd aan de <see cref="CurrentValue"/> op of stelt deze in.
        /// </summary>
        [JsonIgnore]
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

        /// <summary>
        /// Haalt een geformatteerde stringrepresentatie van de <see cref="CurrentValue"/> op,
        /// rekening houdend met kwaliteit en foutmeldingen.
        /// </summary>
        [JsonIgnore]
        public string FormattedLiveValue
        {
            get
            {
                if (!IsGoodQuality && !string.IsNullOrEmpty(ErrorMessage))
                {
                    return ErrorMessage;
                }
                if (!IsGoodQuality)
                {
                    return "Bad Quality";
                }
                return CurrentValue?.ToString() ?? "N/A";
            }
        }

        // Alarmeringseigenschappen
        private bool _isAlarmingEnabled;

        /// <summary>
        /// Haalt een waarde die aangeeft of alarmering op basis van grenswaarden voor deze tag is ingeschakeld, op of stelt deze in.
        /// </summary>
        public bool IsAlarmingEnabled
        {
            get => _isAlarmingEnabled;
            set => SetProperty(ref _isAlarmingEnabled, value);
        }

        private double? _highHighLimit;

        /// <summary>
        /// Haalt de HighHigh alarmgrens voor deze tag op of stelt deze in. Null als niet ingesteld.
        /// </summary>
        public double? HighHighLimit
        {
            get => _highHighLimit;
            set => SetProperty(ref _highHighLimit, value);
        }

        private double? _highLimit;

        /// <summary>
        /// Haalt de High alarmgrens voor deze tag op of stelt deze in. Null als niet ingesteld.
        /// </summary>
        public double? HighLimit
        {
            get => _highLimit;
            set => SetProperty(ref _highLimit, value);
        }

        private double? _lowLimit;

        /// <summary>
        /// Haalt de Low alarmgrens voor deze tag op of stelt deze in. Null als niet ingesteld.
        /// </summary>
        public double? LowLimit
        {
            get => _lowLimit;
            set => SetProperty(ref _lowLimit, value);
        }

        private double? _lowLowLimit;

        /// <summary>
        /// Haalt de LowLow alarmgrens voor deze tag op of stelt deze in. Null als niet ingesteld.
        /// </summary>
        public double? LowLowLimit
        {
            get => _lowLowLimit;
            set => SetProperty(ref _lowLowLimit, value);
        }

        private string _alarmMessageFormat =
            "{TagName} is in alarm ({AlarmState}) met waarde {Value}";

        /// <summary>
        /// Haalt het format voor alarmberichten voor deze tag op of stelt deze in.
        /// Beschikbare placeholders: {TagName}, {AlarmState}, {Value}, {Limit}.
        /// </summary>
        public string AlarmMessageFormat
        {
            get => _alarmMessageFormat;
            set => SetProperty(ref _alarmMessageFormat, value);
        }

        private TagAlarmState _currentAlarmState = TagAlarmState.Normal;

        /// <summary>
        /// Haalt de berekende actuele alarmstatus van de tag op of stelt deze in.
        /// Deze wordt intern bijgewerkt.
        /// </summary>
        [JsonIgnore]
        public TagAlarmState CurrentAlarmState
        {
            get => _currentAlarmState;
            set => SetProperty(ref _currentAlarmState, value);
        }

        private DateTime? _alarmTimestamp;

        /// <summary>
        /// Haalt het tijdstip waarop de tag voor het laatst in een alarmstatus (anders dan Normal of Error) is gekomen, op of stelt deze in.
        /// </summary>
        [JsonIgnore]
        public DateTime? AlarmTimestamp
        {
            get => _alarmTimestamp;
            set => SetProperty(ref _alarmTimestamp, value);
        }

        // Outlier detectie eigenschappen
        private bool _isOutlierDetectionEnabled;

        /// <summary>
        /// Haalt een waarde die aangeeft of outlier (uitschieter) detectie voor deze tag is ingeschakeld, op of stelt deze in.
        /// Bij het wijzigen van deze waarde wordt de baseline data gereset.
        /// </summary>
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

        /// <summary>
        /// Haalt het aantal datapunten dat gebruikt wordt om de initiële baseline voor outlier detectie te bepalen, op of stelt deze in.
        /// </summary>
        public int BaselineSampleSize
        {
            get => _baselineSampleSize;
            set => SetProperty(ref _baselineSampleSize, value > 0 ? value : 1);
        }

        private double _outlierStandardDeviationFactor = 3.0;

        /// <summary>
        /// Haalt de factor voor de standaarddeviatie die gebruikt wordt om outliers te detecteren, op of stelt deze in.
        /// </summary>
        public double OutlierStandardDeviationFactor
        {
            get => _outlierStandardDeviationFactor;
            set => SetProperty(ref _outlierStandardDeviationFactor, value > 0 ? value : 0.1);
        }

        /// <summary>
        /// Bevat de datapunten die gebruikt worden voor het opbouwen van de (expanding) baseline.
        /// Wordt niet geserialiseerd.
        /// </summary>
        [JsonIgnore]
        public List<double> BaselineDataPoints { get; private set; } = new List<double>();

        /// <summary>
        /// Geeft aan of de baseline voor outlier detectie is vastgesteld (d.w.z. voldoende samples zijn verzameld).
        /// Wordt niet geserialiseerd.
        /// </summary>
        [JsonIgnore]
        public bool IsBaselineEstablished { get; set; } = false;

        /// <summary>
        /// Het berekende gemiddelde van de baseline datapunten.
        /// Wordt niet geserialiseerd.
        /// </summary>
        [JsonIgnore]
        public double BaselineMean { get; set; } = 0;

        /// <summary>
        /// De berekende standaarddeviatie van de baseline datapunten.
        /// Wordt niet geserialiseerd.
        /// </summary>
        [JsonIgnore]
        public double BaselineStandardDeviation { get; set; } = 0;

        /// <summary>
        /// Telt het aantal datapunten dat is gebruikt voor de huidige (expanding) baseline.
        /// Wordt niet geserialiseerd.
        /// </summary>
        [JsonIgnore]
        public int CurrentBaselineCount { get; set; } = 0;

        /// <summary>
        /// De som van de waarden die gebruikt zijn voor de (expanding) baseline. Nodig voor efficiënte berekening.
        /// Wordt niet geserialiseerd.
        /// </summary>
        [JsonIgnore]
        public double SumOfValuesForBaseline { get; set; } = 0;

        /// <summary>
        /// De som van de kwadraten van de waarden die gebruikt zijn voor de (expanding) baseline. Nodig voor efficiënte berekening.
        /// Wordt niet geserialiseerd.
        /// </summary>
        [JsonIgnore]
        public double SumOfSquaresForBaseline { get; set; } = 0;

        [JsonIgnore]
        private readonly ILogger _logger;

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="OpcUaTagConfig"/> klasse.
        /// </summary>
        public OpcUaTagConfig()
        {
            // Initialiseer logger voor deze instantie.
            _logger = Log.Logger.ForContext<OpcUaTagConfig>().ForContext("TagName", _tagName);
        }

        /// <summary>
        /// Reset de baseline data en status voor outlier detectie.
        /// Wordt aangeroepen wanneer outlier detectie wordt (de)geactiveerd of handmatig.
        /// </summary>
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
    }
}
