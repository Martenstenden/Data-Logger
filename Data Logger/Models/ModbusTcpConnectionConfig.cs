using System.Collections.ObjectModel;
using Data_Logger.Enums;

namespace Data_Logger.Models
{
    /// <summary>
    /// Representeert de configuratie voor een Modbus TCP/IP dataverbinding.
    /// Erft gemeenschappelijke eigenschappen van <see cref="ConnectionConfigBase"/>.
    /// </summary>
    public class ModbusTcpConnectionConfig : ConnectionConfigBase
    {
        private string _ipAddress = "127.0.0.1";

        /// <summary>
        /// Haalt het IP-adres van de Modbus TCP server op of stelt deze in.
        /// </summary>
        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        private int _port = 502; // Standaard Modbus TCP poort

        /// <summary>
        /// Haalt de TCP-poort van de Modbus TCP server op of stelt deze in.
        /// De standaardpoort voor Modbus TCP is 502.
        /// </summary>
        public int Port
        {
            get => _port;
            set => SetProperty(ref _port, value > 0 && value <= 65535 ? value : 502); // Basis validatie
        }

        private byte _unitId = 1; // Ook wel Slave ID genoemd

        /// <summary>
        /// Haalt de Unit Identifier (ook wel Slave ID) van het Modbus-apparaat op of stelt deze in.
        /// Typisch een waarde tussen 1 en 247.
        /// </summary>
        public byte UnitId
        {
            get => _unitId;
            set => SetProperty(ref _unitId, value);
        }

        private ObservableCollection<ModbusTagConfig> _tagsToMonitor;

        /// <summary>
        /// Haalt een observeerbare collectie van Modbus-tags die voor deze verbinding gemonitord moeten worden op, of stelt deze in.
        /// </summary>
        public ObservableCollection<ModbusTagConfig> TagsToMonitor
        {
            get => _tagsToMonitor;
            set => SetProperty(ref _tagsToMonitor, value);
        }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="ModbusTcpConnectionConfig"/> klasse
        /// met default waarden.
        /// </summary>
        public ModbusTcpConnectionConfig()
            : base(ConnectionType.ModbusTcp) // Stelt het type in via de base constructor
        {
            ConnectionName = "Nieuwe Modbus TCP Verbinding";
            TagsToMonitor = new ObservableCollection<ModbusTagConfig>();
        }
    }
}
