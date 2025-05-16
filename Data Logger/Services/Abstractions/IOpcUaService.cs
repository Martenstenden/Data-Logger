using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Data_Logger.Models;
using Data_Logger.ViewModels;
using Opc.Ua;

namespace Data_Logger.Services.Abstractions
{
    public class NodeSearchResult
    {
        public NodeId NodeId { get; set; }
        public string DisplayName { get; set; }
        public NodeClass NodeClass { get; set; }
        public string Path { get; set; }
    }
    
    public interface IOpcUaService : IDisposable
    {
        bool IsConnected { get; }

        NamespaceTable NamespaceUris { get; }

        event EventHandler ConnectionStatusChanged;

        event EventHandler<IEnumerable<LoggedTagValue>> TagsDataReceived;

        Task<bool> ConnectAsync();

        Task DisconnectAsync();

        void Reconfigure(OpcUaConnectionConfig newConfig);

        Task StartMonitoringTagsAsync();

        Task StopMonitoringTagsAsync();

        Task<IEnumerable<LoggedTagValue>> ReadCurrentTagValuesAsync();

        Task<ReferenceDescriptionCollection> BrowseAsync(
            NodeId nodeIdToBrowse,
            NodeId referenceTypeId = null,
            bool includeSubtypes = true,
            BrowseDirection direction = BrowseDirection.Forward,
            NodeClass nodeClassMask = NodeClass.Unspecified,
            CancellationToken ct = default
        );

        Task<ReferenceDescriptionCollection> BrowseRootAsync();

        Task<DataValue> ReadValueAsync(NodeId nodeId);

        Task<List<NodeAttributeViewModel>> ReadNodeAttributesAsync(NodeId nodeId);

        Task<ReferenceDescriptionCollection> BrowseAllReferencesAsync(
            NodeId nodeIdToBrowse,
            BrowseDirection direction = BrowseDirection.Both
        );

        Task<LocalizedText> ReadNodeDisplayNameAsync(NodeId nodeId);
        
        NodeId ParseNodeId(string nodeIdString);
        
        Task<List<NodeSearchResult>> SearchNodesRecursiveAsync(NodeId startNodeId, string regexPattern, bool caseSensitive, int maxDepth = 5, CancellationToken ct = default);
    }
}
