using System.Collections.ObjectModel;
using Data_Logger.Enums;
using Opc.Ua;

namespace Data_Logger.Models
{
    /// <summary>
    /// Representeert de configuratie voor een OPC UA dataverbinding.
    /// Erft gemeenschappelijke eigenschappen van <see cref="ConnectionConfigBase"/>.
    /// </summary>
    public class OpcUaConnectionConfig : ConnectionConfigBase
    {
        private string _endpointUrl = "opc.tcp://localhost:4840";

        /// <summary>
        /// Haalt de OPC UA server endpoint URL op of stelt deze in (bijv. "opc.tcp://server:poort/path").
        /// </summary>
        public string EndpointUrl
        {
            get => _endpointUrl;
            set => SetProperty(ref _endpointUrl, value);
        }

        private MessageSecurityMode _securityMode = MessageSecurityMode.None;

        /// <summary>
        /// Haalt de OPC UA message security mode op of stelt deze in (bijv. None, Sign, SignAndEncrypt).
        /// </summary>
        public MessageSecurityMode SecurityMode
        {
            get => _securityMode;
            set => SetProperty(ref _securityMode, value);
        }

        private string _securityPolicyUri = SecurityPolicies.None;

        /// <summary>
        /// Haalt de OPC UA security policy URI op of stelt deze in (bijv. <see cref="SecurityPolicies.None"/>, <see cref="SecurityPolicies.Basic256Sha256"/>).
        /// </summary>
        public string SecurityPolicyUri
        {
            get => _securityPolicyUri;
            set => SetProperty(ref _securityPolicyUri, value);
        }

        private string _userName;

        /// <summary>
        /// Haalt de gebruikersnaam voor authenticatie met de OPC UA server op of stelt deze in.
        /// Kan null of leeg zijn als anonieme toegang is toegestaan.
        /// </summary>
        public string UserName
        {
            get => _userName;
            set => SetProperty(ref _userName, value);
        }

        private string _password;

        /// <summary>
        /// Haalt het wachtwoord voor authenticatie met de OPC UA server op of stelt deze in.
        /// </summary>
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        private ObservableCollection<OpcUaTagConfig> _tagsToMonitor;

        /// <summary>
        /// Haalt een observeerbare collectie van OPC UA-tags die voor deze verbinding gemonitord moeten worden op of stelt deze in.
        /// </summary>
        public ObservableCollection<OpcUaTagConfig> TagsToMonitor
        {
            get => _tagsToMonitor;
            set => SetProperty(ref _tagsToMonitor, value);
        }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="OpcUaConnectionConfig"/> klasse
        /// met default waarden.
        /// </summary>
        public OpcUaConnectionConfig()
            : base(ConnectionType.OpcUa) // Stelt het type in via de base constructor
        {
            ConnectionName = "Nieuwe OPC UA Verbinding";
            TagsToMonitor = new ObservableCollection<OpcUaTagConfig>();
        }
    }
}
