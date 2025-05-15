using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Data_Logger.Core;
using Data_Logger.Services.Abstractions;
using Opc.Ua;

namespace Data_Logger.ViewModels
{
    public class OpcUaNodeViewModel : ObservableObject
    {
        private readonly NodeId _nodeId;
        private readonly IOpcUaService _opcUaService;
        private readonly Serilog.ILogger _logger;

        private string _displayName;
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        private NodeClass _nodeClass;
        public NodeClass NodeClass
        {
            get => _nodeClass;
            set => SetProperty(ref _nodeClass, value);
        }

        public NodeId NodeId => _nodeId;

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value))
                {
                    if (_isExpanded && !_childrenLoaded && Children.Any(c => c == null))
                    {
                        LoadChildren();
                    }
                }
            }
        }

        public ObservableCollection<OpcUaNodeViewModel> Children { get; }

        private bool _childrenLoaded = false;

        public OpcUaNodeViewModel(
            NodeId nodeId,
            string displayName,
            NodeClass nodeClass,
            IOpcUaService opcUaService,
            Serilog.ILogger logger,
            bool hasChildren
        )
        {
            _nodeId = nodeId;
            DisplayName = displayName;
            NodeClass = nodeClass;
            _opcUaService = opcUaService;
            _logger = logger;
            Children = new ObservableCollection<OpcUaNodeViewModel>();
            if (hasChildren)
            {
                Children.Add(null);
            }
        }

        private async void LoadChildren()
        {
            _childrenLoaded = true;
            Children.Clear();

            _logger.Debug(
                "Laden van children voor OPC UA Node: {NodeIdString} - {DisplayName}",
                _nodeId.ToString(),
                DisplayName
            );

            if (_opcUaService == null || !_opcUaService.IsConnected)
            {
                _logger.Warning("Kan children niet laden: OpcUaService is null of niet verbonden.");

                return;
            }

            try
            {
                ReferenceDescriptionCollection childReferences = await _opcUaService.BrowseAsync(
                    _nodeId,
                    ct: CancellationToken.None
                );

                if (childReferences != null)
                {
                    foreach (var childData in childReferences)
                    {
                        bool hasGrandChildren =
                            childData.NodeClass == Opc.Ua.NodeClass.Object
                            || childData.NodeClass == Opc.Ua.NodeClass.View;

                        NodeId actualNodeId = null;
                        if (_opcUaService.NamespaceUris != null)
                        {
                            try
                            {
                                actualNodeId = ExpandedNodeId.ToNodeId(
                                    childData.NodeId,
                                    _opcUaService.NamespaceUris
                                );
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(
                                    ex,
                                    "Fout bij het converteren van ExpandedNodeId {ExpandedNodeId} naar NodeId voor DisplayName {DisplayName}",
                                    childData.NodeId,
                                    childData.DisplayName?.Text
                                );
                                continue;
                            }
                        }
                        else
                        {
                            _logger.Warning(
                                "NamespaceUris is null in OpcUaService, kan ExpandedNodeId niet correct converteren voor {DisplayName}",
                                childData.DisplayName?.Text
                            );

                            try
                            {
                                actualNodeId = ExpandedNodeId.ToNodeId(childData.NodeId, null);
                            }
                            catch
                            {
                                continue;
                            }
                        }

                        if (actualNodeId == null)
                            continue;

                        System.Windows.Application.Current.Dispatcher.Invoke(
                            (Action)(
                                () =>
                                {
                                    Children.Add(
                                        new OpcUaNodeViewModel(
                                            actualNodeId,
                                            childData.DisplayName?.Text ?? "Unknown",
                                            childData.NodeClass,
                                            _opcUaService,
                                            _logger,
                                            hasGrandChildren
                                        )
                                    );
                                }
                            )
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout bij het laden van children voor node {NodeIdString}",
                    _nodeId.ToString()
                );
            }
            OnPropertyChanged(nameof(Children));
        }
    }
}
