using Data_Logger.Core;
using Data_Logger.Models;

namespace Data_Logger.ViewModels
{
    public abstract class TabViewModelBase : ObservableObject
    {
        private string _displayName;

        private ConnectionConfigBase _connectionConfiguration;

        public ConnectionConfigBase ConnectionConfiguration
        {
            get => _connectionConfiguration;
            protected set => SetProperty(ref _connectionConfiguration, value);
        }

        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        protected TabViewModelBase(ConnectionConfigBase connectionConfig)
        {
            ConnectionConfiguration = connectionConfig;
            DisplayName = connectionConfig.ConnectionName;
        }
    }
}
