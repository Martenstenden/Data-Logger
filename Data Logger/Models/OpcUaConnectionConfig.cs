using System.Collections.ObjectModel;
using Data_Logger.Core;
using Data_Logger.Enums;
using Opc.Ua;

namespace Data_Logger.Models
{
    public class OpcUaConnectionConfig : ConnectionConfigBase
    {
        private string _endpointUrl = "opc.tcp://localhost:4840";
        public string EndpointUrl
        {
            get => _endpointUrl;
            set => SetProperty(ref _endpointUrl, value);
        }

        private MessageSecurityMode _securityMode = MessageSecurityMode.None;
        public MessageSecurityMode SecurityMode
        {
            get => _securityMode;
            set => SetProperty(ref _securityMode, value);
        }

        private string _securityPolicyUri = SecurityPolicies.None;
        public string SecurityPolicyUri
        {
            get => _securityPolicyUri;
            set => SetProperty(ref _securityPolicyUri, value);
        }

        private string _userName;
        public string UserName
        {
            get => _userName;
            set => SetProperty(ref _userName, value);
        }

        private string _password;
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        private ObservableCollection<OpcUaTagConfig> _tagsToMonitor;
        public ObservableCollection<OpcUaTagConfig> TagsToMonitor
        {
            get => _tagsToMonitor;
            set => SetProperty(ref _tagsToMonitor, value);
        }

        public OpcUaConnectionConfig()
            : base(ConnectionType.OpcUa)
        {
            ConnectionName = "Nieuwe OPC UA Verbinding";
            TagsToMonitor = new ObservableCollection<OpcUaTagConfig>();
        }
    }
}
