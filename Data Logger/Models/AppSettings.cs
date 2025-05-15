using System.Collections.ObjectModel;
using Data_Logger.Core;

namespace Data_Logger.Models
{
    public class AppSettings : ObservableObject
    {
        private ObservableCollection<ConnectionConfigBase> _connections;

        public ObservableCollection<ConnectionConfigBase> Connections
        {
            get => _connections;
            set => SetProperty(ref _connections, value);
        }

        public AppSettings()
        {
            Connections = new ObservableCollection<ConnectionConfigBase>();
        }
    }
}
