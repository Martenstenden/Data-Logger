using System.Collections.ObjectModel;
using Data_Logger.Enums;

namespace Data_Logger.Models
{
    public class ModbusTcpConnectionConfig : ConnectionConfigBase
    {
        private string _ipAddress = "127.0.0.1";
        private int _port = 502;
        private byte _unitId = 1;

        private ObservableCollection<ModbusTagConfig> _tagsToMonitor;
        public ObservableCollection<ModbusTagConfig> TagsToMonitor
        {
            get => _tagsToMonitor;
            set => SetProperty(ref _tagsToMonitor, value);
        }

        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        public byte UnitId
        {
            get => _unitId;
            set => SetProperty(ref _unitId, value);
        }

        public ModbusTcpConnectionConfig()
            : base(ConnectionType.ModbusTcp)
        {
            ConnectionName = "Nieuwe Modbus TCP Verbinding";
            TagsToMonitor = new ObservableCollection<ModbusTagConfig>();
        }
    }
}
