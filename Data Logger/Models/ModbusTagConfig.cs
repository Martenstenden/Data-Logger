using Data_Logger.Core;
using Data_Logger.Enums;

namespace Data_Logger.Models
{
    /// <summary>
    /// Representeert de configuratie voor een individuele Modbus-tag die gemonitord moet worden.
    /// </summary>
    public class ModbusTagConfig : ObservableObject
    {
        private string _tagName = "Nieuwe Modbus Tag";

        /// <summary>
        /// Haalt de gebruiksvriendelijke naam voor deze Modbus-tag op of stelt deze in.
        /// </summary>
        public string TagName
        {
            get => _tagName;
            set => SetProperty(ref _tagName, value);
        }

        private ushort _address;

        /// <summary>
        /// Haalt het Modbus-adres (0-based) van de tag op of stelt deze in.
        /// </summary>
        public ushort Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        private ModbusRegisterType _registerType = ModbusRegisterType.HoldingRegister;

        /// <summary>
        /// Haalt het type Modbus-register (bijv. HoldingRegister, Coil) op of stelt deze in.
        /// Als het registertype wordt ingesteld op Coil of DiscreteInput, wordt het <see cref="DataType"/>
        /// automatisch ingesteld op <see cref="ModbusDataType.Boolean"/>.
        /// </summary>
        public ModbusRegisterType RegisterType
        {
            get => _registerType;
            set
            {
                if (SetProperty(ref _registerType, value))
                {
                    // Als het type Coil of DiscreteInput is, is het datatype altijd Boolean.
                    if (
                        _registerType == ModbusRegisterType.Coil
                        || _registerType == ModbusRegisterType.DiscreteInput
                    )
                    {
                        if (DataType != ModbusDataType.Boolean)
                        {
                            DataType = ModbusDataType.Boolean;
                        }
                    }
                    OnPropertyChanged(nameof(IsDataTypeSelectionEnabled));
                }
            }
        }

        private ModbusDataType _dataType = ModbusDataType.UInt16;

        /// <summary>
        /// Haalt het datatype van de Modbus-tag op of stelt deze in (bijv. Int16, Float32).
        /// Voor <see cref="ModbusRegisterType.Coil"/> en <see cref="ModbusRegisterType.DiscreteInput"/>
        /// wordt dit altijd geforceerd naar <see cref="ModbusDataType.Boolean"/>.
        /// </summary>
        public ModbusDataType DataType
        {
            get => _dataType;
            set
            {
                if (
                    RegisterType == ModbusRegisterType.Coil
                    || RegisterType == ModbusRegisterType.DiscreteInput
                )
                {
                    // Forceren naar Boolean als het registertype een coil of discrete input is.
                    if (value != ModbusDataType.Boolean)
                    {
                        SetProperty(ref _dataType, ModbusDataType.Boolean);
                        return;
                    }
                }
                SetProperty(ref _dataType, value);
            }
        }

        /// <summary>
        /// Haalt een waarde op die aangeeft of het datatype selecteerbaar is in de UI.
        /// Dit is false voor Coils en DiscreteInputs, omdat hun datatype altijd Boolean is.
        /// </summary>
        public bool IsDataTypeSelectionEnabled =>
            RegisterType != ModbusRegisterType.Coil
            && RegisterType != ModbusRegisterType.DiscreteInput;

        private bool _isActive = true;

        /// <summary>
        /// Haalt een waarde die aangeeft of deze tag actief gemonitord moet worden op, of stelt deze in.
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        // Alarmeringseigenschappen
        private bool _isAlarmingEnabled;

        /// <summary>
        /// Haalt een waarde die aangeeft of alarmering voor deze tag is ingeschakeld op, of stelt deze in.
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

        // Outlier detectie eigenschappen
        private bool _isOutlierDetectionEnabled;

        /// <summary>
        /// Haalt een waarde die aangeeft of outlier (uitschieter) detectie voor deze tag is ingeschakeld op, of stelt deze in.
        /// </summary>
        public bool IsOutlierDetectionEnabled
        {
            get => _isOutlierDetectionEnabled;
            set => SetProperty(ref _isOutlierDetectionEnabled, value);
        }

        private int _baselineSampleSize = 20;

        /// <summary>
        /// Haalt het aantal datapunten dat gebruikt wordt om de initiële baseline voor outlier detectie te bepalen op, of stelt deze in.
        /// </summary>
        public int BaselineSampleSize
        {
            get => _baselineSampleSize;
            set => SetProperty(ref _baselineSampleSize, value > 0 ? value : 1);
        }

        private double _outlierStandardDeviationFactor = 3.0;

        /// <summary>
        /// Haalt de factor voor de standaarddeviatie die gebruikt wordt om outliers te detecteren op, of stelt deze in.
        /// Een waarde wordt als outlier beschouwd als deze meer dan (factor * standaarddeviatie) afwijkt van het gemiddelde.
        /// </summary>
        public double OutlierStandardDeviationFactor
        {
            get => _outlierStandardDeviationFactor;
            set => SetProperty(ref _outlierStandardDeviationFactor, value > 0 ? value : 0.1);
        }
    }
}
