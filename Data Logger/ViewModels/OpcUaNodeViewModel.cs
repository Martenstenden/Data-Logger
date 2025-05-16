// Data Logger/Data Logger/ViewModels/OpcUaNodeViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows; // Nodig voor Application.Current
using System.Windows.Data; // Nodig voor BindingOperations en CollectionViewSource
using Data_Logger.Core;
using Data_Logger.Services.Abstractions;
using Opc.Ua;
using Serilog;

namespace Data_Logger.ViewModels
{
    public class OpcUaNodeViewModel : ObservableObject
    {
        private readonly NodeId _nodeId;
        private readonly IOpcUaService _opcUaService;
        private readonly Serilog.ILogger _logger;
        private readonly object _childrenLock = new object(); // Lock object voor Children collectie

        private string _displayName;
        public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }

        private NodeClass _nodeClass;
        public NodeClass NodeClass { get => _nodeClass; set => SetProperty(ref _nodeClass, value); }

        public NodeId NodeId => _nodeId;

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _logger?.Verbose("IsExpanded setter: {DisplayName} - Oude: {_isExpanded}, Nieuwe: {value}", DisplayName, _isExpanded, value);
                if (SetProperty(ref _isExpanded, value))
                {
                    _logger?.Information("IsExpanded daadwerkelijk gewijzigd naar {IsExpandedValue} voor {DisplayName}", _isExpanded, DisplayName);
                    if (_isExpanded && HasDummyChild && !_childrenActuallyLoaded) // _childrenActuallyLoaded ipv _childrenLoadedSuccessfully
                    {
                        _logger?.Debug("IsExpanded: Triggering LoadChildrenAsync for {DisplayName} (Heeft dummy, nog niet succesvol geladen)", DisplayName);
                        LoadChildrenAsync();
                    }
                    else if (_isExpanded && _childrenActuallyLoaded)
                    {
                        _logger?.Debug("IsExpanded: Children al geladen voor {DisplayName}, refresh van FilteredChildrenView en globale filter (indien nodig).", DisplayName);
                        // Kinderen zijn al geladen, maar de node wordt (opnieuw) uitgeklapt.
                        // De globale filter kan veranderd zijn.
                        Application.Current?.Dispatcher.Invoke(() => FilteredChildrenView?.Refresh());
                        FindParentTabViewModel()?.RefreshTreeViewFilter(); // Kan IsVisible van kinderen updaten
                    }
                }
            }
        }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                SetProperty(ref _isVisible, value);
            }
        }

        public ObservableCollection<OpcUaNodeViewModel> Children { get; }
        public ICollectionView FilteredChildrenView { get; }

        private bool _childrenActuallyLoaded = false; // Vervangt _childrenLoadedSuccessfully
        private bool _serverIndicatedChildrenInitially;

        public OpcUaNodeViewModel(
            NodeId nodeId,
            string displayName,
            NodeClass nodeClass,
            IOpcUaService opcUaService,
            Serilog.ILogger parentLogger,
            bool serverIndicatedChildrenByParent
        )
        {
            _nodeId = nodeId;
            DisplayName = displayName;
            NodeClass = nodeClass;
            _opcUaService = opcUaService;
            _logger = parentLogger?.ForContext("OpcUaNode", $"{displayName} ({nodeId})");

            _serverIndicatedChildrenInitially = serverIndicatedChildrenByParent;

            Children = new ObservableCollection<OpcUaNodeViewModel>();
            // BELANGRIJK: Activeer synchronisatie voor cross-thread toegang tot Children
            BindingOperations.EnableCollectionSynchronization(Children, _childrenLock);

            FilteredChildrenView = CollectionViewSource.GetDefaultView(Children);
            FilteredChildrenView.Filter = ChildFilter;

            if (_serverIndicatedChildrenInitially)
            {
                // Voeg dummy toe (binnen lock als je paranoia bent, maar constructor is meestal safe)
                lock (_childrenLock)
                {
                    if (!Children.Any()) // Alleen als echt leeg
                    {
                        Children.Add(null);
                    }
                }
                _logger?.Verbose("Constructor: Dummy child potentieel toegevoegd voor {DisplayName}. Children.Count: {Count}", DisplayName, Children.Count);
            }
            else
            {
                _logger?.Verbose("Constructor: Geen dummy child initieel nodig voor {DisplayName}.", DisplayName);
            }
            // Properties voor UI bijwerken
            OnPropertyChanged(nameof(HasDummyChild));
            OnPropertyChanged(nameof(ShowExpander));
        }

        private bool ChildFilter(object item)
        {
            if (item is OpcUaNodeViewModel childNode)
            {
                return childNode.IsVisible;
            }
            return false; // Dummy null en andere types niet tonen
        }

        public bool HasDummyChild => Children.Count == 1 && Children[0] == null;

        // WPF TreeView toont expander als er een dummy is, of als FilteredChildrenView items heeft.
        // Deze property is nu meer een indicatie voor debuggen.
        public bool ShowExpander
        {
            get
            {
                bool hasVisibleChildren = FilteredChildrenView?.Cast<object>().Any(i => i != null) ?? false;
                bool shouldShow = HasDummyChild || hasVisibleChildren;
                // _logger?.Verbose("ShowExpander voor {DisplayName}: HasDummy={hd}, HasVisibleChildren={hvc}, Result={s}", DisplayName, HasDummyChild, hasVisibleChildren, shouldShow);
                return shouldShow;
            }
        }


        public async Task LoadChildrenAsync()
        {
            if (!HasDummyChild)
            {
                _logger?.Debug("LoadChildrenAsync voor {DisplayName}: Afgebroken (geen dummy child). _childrenActuallyLoaded={Loaded}", DisplayName, _childrenActuallyLoaded);
                if (!_childrenActuallyLoaded) _childrenActuallyLoaded = true; // Markeer poging als gedaan
                return;
            }
            if (!_isExpanded)
            {
                 _logger?.Debug("LoadChildrenAsync voor {DisplayName}: Afgebroken (niet IsExpanded).", DisplayName);
                 return;
            }
            // Als _childrenActuallyLoaded al true is EN er is nog steeds een dummy, dan is er iets raars, reset.
            if (_childrenActuallyLoaded && HasDummyChild) {
                _logger?.Warning("LoadChildrenAsync voor {DisplayName}: Inconsistente state (_childrenActuallyLoaded=true, maar HasDummyChild=true). Resetting _childrenActuallyLoaded.", DisplayName);
                _childrenActuallyLoaded = false;
            }
            if (_childrenActuallyLoaded) { // Voorkom dubbel laden als de setter van IsExpanded snel getriggerd wordt
                _logger?.Debug("LoadChildrenAsync voor {DisplayName}: Afgebroken (kinderen al succesvol geladen).", DisplayName);
                return;
            }

            _logger?.Information("LoadChildrenAsync: Start daadwerkelijk laden voor {DisplayName}", DisplayName);

            // Zet _childrenActuallyLoaded vroeg om recursieve/dubbele aanroepen te voorkomen terwijl deze bezig is.
            // Wordt false gezet bij fout.
            _childrenActuallyLoaded = true;

            // Verwijder de dummy op de UI thread
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (_childrenLock)
                {
                    if (HasDummyChild) Children.Clear();
                }
                _logger?.Debug("LoadChildrenAsync: Dummy child verwijderd (indien aanwezig) voor {DisplayName}. Children.Count: {Count}", DisplayName, Children.Count);
                OnPropertyChanged(nameof(HasDummyChild));
            });

            var tempChildren = new List<OpcUaNodeViewModel>();
            bool successDuringBrowse = false;
            try
            {
                if (_opcUaService == null || !_opcUaService.IsConnected)
                {
                    _logger?.Warning("LoadChildrenAsync: Kan kinderen niet laden voor {DisplayName}: Service null/niet verbonden.", DisplayName);
                    _childrenActuallyLoaded = false; // Mislukt
                    return;
                }

                ReferenceDescriptionCollection childReferences = await _opcUaService.BrowseAsync(_nodeId, ReferenceTypeIds.HierarchicalReferences, true, BrowseDirection.Forward, NodeClass.Unspecified, CancellationToken.None);
                successDuringBrowse = true; // Browse zelf gaf geen exceptie

                _serverIndicatedChildrenInitially = childReferences != null && childReferences.Any();

                if (_serverIndicatedChildrenInitially)
                {
                    var namespaceUris = _opcUaService.NamespaceUris;
                    foreach (var rd in childReferences)
                    {
                        NodeId childNodeId = ExpandedNodeId.ToNodeId(rd.NodeId, namespaceUris);
                        if (childNodeId == null) continue;
                        bool grandChildrenPotentiallyExist = (rd.NodeClass == Opc.Ua.NodeClass.Object || rd.NodeClass == Opc.Ua.NodeClass.View);
                        tempChildren.Add(new OpcUaNodeViewModel(childNodeId, rd.DisplayName?.Text ?? "Unknown", rd.NodeClass, _opcUaService, _logger, grandChildrenPotentiallyExist));
                    }
                    _logger?.Debug("LoadChildrenAsync: {NumChildren} kinderen gevonden voor {DisplayName}", tempChildren.Count, DisplayName);
                }
                else
                {
                     _logger?.Debug("LoadChildrenAsync: Geen kinderen gevonden na browse voor {DisplayName}", DisplayName);
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "LoadChildrenAsync: Fout TIJDENS laden kinderen voor {DisplayName}", DisplayName);
                successDuringBrowse = false; // Markeer als mislukt
            }
            finally // Dit blok wordt altijd uitgevoerd, ook na return in try of catch.
            {
                _childrenActuallyLoaded = successDuringBrowse; // Definitieve status van deze laadpoging

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (successDuringBrowse && tempChildren.Any())
                    {
                        lock (_childrenLock)
                        {
                            foreach (var child in tempChildren) Children.Add(child);
                        }
                        _logger?.Debug("LoadChildrenAsync finally: {NumTemp} toegevoegd aan Children. Children.Count: {ChildrenCount}", tempChildren.Count, DisplayName, Children.Count);
                    }
                    else if (successDuringBrowse && !tempChildren.Any())
                    {
                        _logger?.Debug("LoadChildrenAsync finally: Laden was succesvol, geen kinderen om toe te voegen. Children.Count: {ChildrenCount}", DisplayName, Children.Count);
                    }
                    else if (!successDuringBrowse)
                    {
                         _logger?.Warning("LoadChildrenAsync finally: Laden was NIET succesvol. Children.Count: {ChildrenCount}", DisplayName, Children.Count);
                         // Als server initieel kinderen aangaf, maar laden mislukte, plaats dummy terug
                         if (_serverIndicatedChildrenInitially && !Children.Any(c => c != null)) { // _serverIndicatedChildrenInitially van *deze* laadpoging
                             lock(_childrenLock) { if (!HasDummyChild) Children.Add(null); } // Voorkom dubbele dummy
                             _logger?.Information("LoadChildrenAsync finally: Dummy teruggeplaatst na mislukte laadpoging voor {DisplayName}", DisplayName);
                         }
                    }

                    OpcUaTabViewModel parentTabViewModel = FindParentTabViewModel();
                    if (parentTabViewModel != null) {
                        _logger?.Debug("LoadChildrenAsync finally: Aanroepen RefreshTreeViewFilter voor {DisplayName}", DisplayName);
                        parentTabViewModel.RefreshTreeViewFilter();
                    } else {
                        _logger?.Warning("LoadChildrenAsync finally: Kon parentTabViewModel niet vinden voor {DisplayName}. Lokale kinderfilter refresh.", DisplayName);
                        FilteredChildrenView.Refresh(); // Fallback
                    }
                    
                    // Properties die de UI beïnvloeden als laatste bijwerken
                    OnPropertyChanged(nameof(HasDummyChild));
                    OnPropertyChanged(nameof(ShowExpander));
                    _logger?.Debug("LoadChildrenAsync finally: UI updates voor {DisplayName}. FilteredChildren.Count: {FilteredCount}, ShowExpander: {ShowExp}", DisplayName, FilteredChildrenView.Cast<object>().Count(), ShowExpander);
                });
            }
        }

        private OpcUaTabViewModel FindParentTabViewModel()
        {
            if (Application.Current?.MainWindow?.DataContext is MainViewModel mainVm)
            {
                foreach (var tab in mainVm.ActiveTabs.OfType<OpcUaTabViewModel>())
                {
                    if (tab.RootNodes.Any(rn => IsNodeOrDescendant(rn, this)))
                    {
                        return tab;
                    }
                }
            }
            return null;
        }

        private bool IsNodeOrDescendant(OpcUaNodeViewModel parent, OpcUaNodeViewModel target)
        {
            if (parent == null || target == null) return false;
            if (parent == target) return true;
            // Moet itereren over de *originele* Children collectie
            return parent.Children.Where(c => c != null).Any(child => IsNodeOrDescendant(child, target));
        }
    }
}