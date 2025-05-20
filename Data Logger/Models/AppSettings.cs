using System.Collections.ObjectModel;
using Data_Logger.Core;

namespace Data_Logger.Models
{
    /// <summary>
    /// Representeert de algemene applicatie-instellingen.
    /// Deze klasse dient als de hoofdcontainer voor alle configureerbare
    /// aspecten van de applicatie, zoals de lijst van dataverbindingen.
    /// </summary>
    public class AppSettings : ObservableObject
    {
        private ObservableCollection<ConnectionConfigBase> _connections;

        /// <summary>
        /// Haalt een observeerbare collectie van alle geconfigureerde dataverbindingen op of stelt deze in.
        /// Wijzigingen in deze collectie of in de eigenschappen van de items erin
        /// kunnen UI-updates triggeren indien correct gebonden.
        /// </summary>
        public ObservableCollection<ConnectionConfigBase> Connections
        {
            get => _connections;
            set => SetProperty(ref _connections, value);
        }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="AppSettings"/> klasse.
        /// De <see cref="Connections"/> collectie wordt geïnitialiseerd als een lege <see cref="ObservableCollection{T}"/>.
        /// </summary>
        public AppSettings()
        {
            Connections = new ObservableCollection<ConnectionConfigBase>();
        }
    }
}
