using Data_Logger.Core;
using Data_Logger.Enums;

namespace Data_Logger.Models
{
    public abstract class ConnectionConfigBase : ObservableObject
    {
        private string _connectionName;
        private ConnectionType _type;
        private bool _isEnabled = true;
        private int _scanIntervalSeconds = 5;

        public string ConnectionName
        {
            get => _connectionName;
            set => SetProperty(ref _connectionName, value);
        }

        public ConnectionType Type
        {
            get => _type;
            protected set => SetProperty(ref _type, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public int ScanIntervalSeconds
        {
            get => _scanIntervalSeconds;
            set => SetProperty(ref _scanIntervalSeconds, value);
        }

        protected ConnectionConfigBase(ConnectionType type)
        {
            Type = type;
        }
    }
}
