


using Data_Logger.Core;
using Data_Logger.Enums;


namespace Data_Logger.Models
{
    public class ModbusTagConfig : ObservableObject
    {
        private string _tagName = "Nieuwe Modbus Tag"; 
        public string TagName
        {
            get => _tagName;
            set => SetProperty(ref _tagName, value);
        }

        private ushort _address;
        public ushort Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        private ModbusRegisterType _registerType = ModbusRegisterType.HoldingRegister;
        public ModbusRegisterType RegisterType
        {
            get => _registerType;
            set
            {
                if (SetProperty(ref _registerType, value))
                {
                    if (
                        _registerType == ModbusRegisterType.Coil ||
                        _registerType == ModbusRegisterType.DiscreteInput
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
        public ModbusDataType DataType
        {
            get => _dataType;
            set
            {
                
                if (RegisterType == ModbusRegisterType.Coil || RegisterType == ModbusRegisterType.DiscreteInput)
                {
                    if (value != ModbusDataType.Boolean) 
                    {
                        SetProperty(ref _dataType, ModbusDataType.Boolean, nameof(DataType));
                        return;
                    }
                }
                SetProperty(ref _dataType, value);
            }
        }

        public bool IsDataTypeSelectionEnabled =>
            RegisterType != ModbusRegisterType.Coil &&
            RegisterType != ModbusRegisterType.DiscreteInput;

        private bool _isActive = true;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
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

        private string _alarmMessageFormat = "{TagName} is in alarm ({AlarmState}) met waarde {Value}";
        public string AlarmMessageFormat
        {
            get => _alarmMessageFormat;
            set => SetProperty(ref _alarmMessageFormat, value);
        }

        
        private bool _isOutlierDetectionEnabled;
        public bool IsOutlierDetectionEnabled
        {
            get => _isOutlierDetectionEnabled;
            set => SetProperty(ref _isOutlierDetectionEnabled, value);
            
            
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

        
        
        
        
        
    }
}