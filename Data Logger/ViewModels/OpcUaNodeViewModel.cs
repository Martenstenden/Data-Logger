using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Data_Logger.Core;
using Data_Logger.Services.Abstractions;
using Opc.Ua;
using Serilog;

namespace Data_Logger.ViewModels
{
    /// <summary>
    /// ViewModel die een enkele node in de OPC UA adresruimte representeert,
    /// bedoeld voor weergave in een TreeView. Beheert het laden van onderliggende (child) nodes.
    /// </summary>
    public class OpcUaNodeViewModel : ObservableObject
    {
        private readonly NodeId _nodeId;
        private readonly IOpcUaService _opcUaService;
        private readonly ILogger _logger;

        private string _displayName;

        /// <summary>
        /// Haalt de weergavenaam van de OPC UA node op of stelt deze in.
        /// </summary>
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        private NodeClass _nodeClass;

        /// <summary>
        /// Haalt de <see cref="Opc.Ua.NodeClass"/> (bijv. Object, Variable, Method) van de node op of stelt deze in.
        /// </summary>
        public NodeClass NodeClass
        {
            get => _nodeClass;
            set => SetProperty(ref _nodeClass, value);
        }

        /// <summary>
        /// Haalt de <see cref="Opc.Ua.NodeId"/> van deze OPC UA node op.
        /// </summary>
        public NodeId NodeId => _nodeId;

        private bool _isExpanded;

        /// <summary>
        /// Haalt een waarde die aangeeft of deze node in de TreeView is uitgeklapt, op of stelt deze in.
        /// Als de node wordt uitgeklapt en de kinderen nog niet zijn geladen, wordt <see cref="LoadChildren"/> aangeroepen.
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value))
                {
                    // Laad kinderen alleen als de node wordt uitgeklapt, kinderen nog niet geladen zijn,
                    // en er een dummy child aanwezig is (indicatie dat er kinderen kunnen zijn).
                    if (_isExpanded && !_childrenLoaded && Children.Any(c => c == null))
                    {
                        var _ = LoadChildrenAsync();
                    }
                }
            }
        }

        /// <summary>
        /// Haalt de observeerbare collectie van kind-nodes van deze OPC UA node op.
        /// Bevat initieel een dummy null-item als <paramref name="hasChildren"/> true was in de constructor,
        /// om de expander in de TreeView te tonen.
        /// </summary>
        public ObservableCollection<OpcUaNodeViewModel> Children { get; }

        /// <summary>
        /// Haalt de zichtbaarheid van deze node op of stelt deze in.
        /// </summary>
        public bool IsVisible { get; }

        private bool _childrenLoaded = false;

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="OpcUaNodeViewModel"/> klasse.
        /// Deze constructor wordt gebruikt voor het aanmaken van de initiële (root) nodes of nodes waarvan de kinderen nog geladen moeten worden.
        /// </summary>
        /// <param name="nodeId">De <see cref="NodeId"/> van de OPC UA node.</param>
        /// <param name="displayName">De weergavenaam van de node.</param>
        /// <param name="nodeClass">De <see cref="NodeClass"/> van de node.</param>
        /// <param name="opcUaService">De OPC UA service voor het browsen van de adresruimte.</param>
        /// <param name="logger">De Serilog logger instantie.</param>
        /// <param name="hasChildren">True als deze node potentieel kinderen heeft (om een dummy child toe te voegen voor de TreeView expander).</param>
        /// <param name="isVisible">De initiële zichtbaarheid van de node.</param>
        public OpcUaNodeViewModel(
            NodeId nodeId,
            string displayName,
            NodeClass nodeClass,
            IOpcUaService opcUaService,
            ILogger logger,
            bool hasChildren,
            bool isVisible
        )
        {
            _nodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
            DisplayName = displayName;
            NodeClass = nodeClass;
            _opcUaService = opcUaService ?? throw new ArgumentNullException(nameof(opcUaService));
            _logger =
                logger?.ForContext<OpcUaNodeViewModel>()
                ?? throw new ArgumentNullException(nameof(logger));
            IsVisible = isVisible;

            Children = new ObservableCollection<OpcUaNodeViewModel>();
            if (hasChildren)
            {
                Children.Add(null); // Dummy item om de expander in de TreeView te tonen
            }
        }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="OpcUaNodeViewModel"/> klasse.
        /// </summary>
        /// <param name="nodeId">De <see cref="NodeId"/> van de OPC UA node.</param>
        /// <param name="displayName">De weergavenaam van de node.</param>
        /// <param name="nodeClass">De <see cref="NodeClass"/> van de node.</param>
        /// <param name="opcUaService">De OPC UA service voor het browsen van de adresruimte.</param>
        /// <param name="logger">De Serilog logger instantie.</param>
        /// <param name="hasGrandChildren">True als deze node potentieel kleinkinderen heeft.</param>
        public OpcUaNodeViewModel(
            NodeId nodeId,
            string displayName,
            NodeClass nodeClass,
            IOpcUaService opcUaService,
            ILogger logger,
            bool hasGrandChildren
        )
            : this(nodeId, displayName, nodeClass, opcUaService, logger, hasGrandChildren, true) // Delegeert, stelt IsVisible op true.
        { }

        /// <summary>
        /// Laadt asynchroon de kind-nodes van deze OPC UA node.
        /// Wordt aangeroepen wanneer de node wordt uitgeklapt in de TreeView.
        /// Opmerking: 'async void' wordt over het algemeen afgeraden behalve voor top-level event handlers.
        /// Overweeg 'async Task' en een commando-patroon als dit problemen oplevert.
        /// </summary>
        private async Task LoadChildrenAsync()
        {
            if (_childrenLoaded)
                return; // Voorkom dubbel laden

            _childrenLoaded = true;
            Application.Current?.Dispatcher.Invoke(() => Children.Clear()); // Verwijder dummy null-item

            _logger.Debug(
                "Laden van children voor OPC UA Node: {NodeIdString} - {DisplayName}",
                _nodeId.ToString(),
                DisplayName
            );

            if (_opcUaService == null || !_opcUaService.IsConnected)
            {
                _logger.Warning("Kan children niet laden: OpcUaService is null of niet verbonden.");
                _childrenLoaded = false;
                return;
            }

            try
            {
                // Browse voor hiërarchische referenties om kinderen te vinden
                ReferenceDescriptionCollection childReferences = await _opcUaService
                    .BrowseAsync(
                        _nodeId,
                        ReferenceTypeIds.HierarchicalReferences, // Alleen hiërarchische kinderen
                        true, // Inclusief subtypes van HierarchicalReferences
                        BrowseDirection.Forward,
                        NodeClass.Unspecified, // Alle node klasses
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);

                if (childReferences != null)
                {
                    var newChildrenViewModels = new List<OpcUaNodeViewModel>();
                    foreach (var childData in childReferences)
                    {
                        // Bepaal of de child-node zelf ook weer kinderen kan hebben (voor de TreeView expander)
                        bool hasGrandChildren =
                            childData.NodeClass == NodeClass.Object
                            || childData.NodeClass == NodeClass.View;
                        NodeId actualChildNodeId;

                        try
                        {
                            // Converteer de ExpandedNodeId (die namespace URI's kan bevatten) naar een absolute NodeId
                            actualChildNodeId = ExpandedNodeId.ToNodeId(
                                childData.NodeId,
                                _opcUaService.NamespaceUris
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(
                                ex,
                                "Fout bij het converteren van ExpandedNodeId {ExpandedNodeId} naar NodeId voor DisplayName {DisplayNameChild}",
                                childData.NodeId,
                                childData.DisplayName?.Text
                            );
                            continue; // Sla deze child over bij een fout
                        }
                        newChildrenViewModels.Add(
                            new OpcUaNodeViewModel(
                                actualChildNodeId,
                                childData.DisplayName?.Text ?? "Unknown",
                                childData.NodeClass,
                                _opcUaService,
                                _logger,
                                hasGrandChildren,
                                true
                            )
                        );
                    }

                    if (newChildrenViewModels.Any())
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            foreach (var vm in newChildrenViewModels)
                                Children.Add(vm);
                        });
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
                _childrenLoaded = false;
            }
            OnPropertyChanged(nameof(Children));
        }
    }
}
