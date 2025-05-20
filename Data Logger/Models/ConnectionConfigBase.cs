using Data_Logger.Core;
using Data_Logger.Enums;

namespace Data_Logger.Models
{
    /// <summary>
    /// Een abstracte basisklasse voor configuraties van dataverbindingen.
    /// Het definieert gemeenschappelijke eigenschappen die alle specifieke verbindingstypes delen,
    /// zoals de naam van de verbinding, of deze actief is, en het scaninterval.
    /// </summary>
    public abstract class ConnectionConfigBase : ObservableObject
    {
        private string _connectionName;

        /// <summary>
        /// Haalt de door de gebruiker gedefinieerde naam voor deze verbinding op of stelt deze in.
        /// </summary>
        public string ConnectionName
        {
            get => _connectionName;
            set => SetProperty(ref _connectionName, value);
        }

        private ConnectionType _type;

        /// <summary>
        /// Haalt het type van de verbinding op (bijv. OpcUa, ModbusTcp).
        /// Dit wordt typisch ingesteld door de constructor van een afgeleide klasse.
        /// </summary>
        public ConnectionType Type
        {
            get => _type;
            protected set => SetProperty(ref _type, value); // Protected set: type wordt bepaald door de afgeleide klasse
        }

        private bool _isEnabled = true;

        /// <summary>
        /// Haalt een waarde die aangeeft of deze verbinding actief is en gebruikt moet worden, op of stelt deze in.
        /// Default is true.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        private int _scanIntervalSeconds = 5;

        /// <summary>
        /// Haalt het interval in seconden waarmee data van deze verbinding gepolld of gescand moet worden, op of stelt deze in.
        /// Default is 5 seconden.
        /// </summary>
        public int ScanIntervalSeconds
        {
            get => _scanIntervalSeconds;
            set
            {
                // Zorg ervoor dat het interval een positieve waarde heeft
                if (value <= 0)
                {
                    SetProperty(ref _scanIntervalSeconds, 1); // Stel in op een minimum van 1 seconde
                }
                else
                {
                    SetProperty(ref _scanIntervalSeconds, value);
                }
            }
        }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="ConnectionConfigBase"/> klasse met het opgegeven type.
        /// </summary>
        /// <param name="type">Het type van de verbinding.</param>
        protected ConnectionConfigBase(ConnectionType type)
        {
            Type = type;
        }
    }
}
