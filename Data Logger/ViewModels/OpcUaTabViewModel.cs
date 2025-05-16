// Data Logger/Data Logger/ViewModels/OpcUaTabViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data; // Nodig voor BindingOperations en CollectionViewSource
using System.Windows.Input;
using Data_Logger.Core;
using Data_Logger.Enums;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions; // Voor IOpcUaService & NodeSearchResult
using Data_Logger.Services.Implementations; // Voor OpcUaService (als NodeSearchResult daar staat)
using Opc.Ua;
using Serilog;

namespace Data_Logger.ViewModels
{
    public class OpcUaTabViewModel : TabViewModelBase, IDisposable
    {
        #region Fields
        private readonly ILogger _logger;
        private readonly IOpcUaService _opcUaService;
        private readonly IStatusService _statusService;
        private readonly IDataLoggingService _dataLoggingService;
        private readonly ISettingsService _settingsService;

        private bool _isBrowseAddressSpace = false;
        private OpcUaNodeViewModel _selectedOpcUaNodeInTree;
        private string _lastReadNodeValueMessage = "Nog geen waarde gelezen.";
        private bool _isLoadingNodeDetails = false;
        private bool _disposedValue;

        private PlotTabViewModel _selectedPlotTab;

        private string _nodeFilterText;
        private bool _useNodeRegexFilter;
        private readonly object _rootNodesLock = new object(); // Lock voor RootNodes

        private NodeSearchResult _selectedServerSearchResult;
        private ObservableCollection<NodeSearchResult> _serverSearchResults;
        private bool _isSearchingOnServer;
        #endregion

        #region Properties
        public OpcUaConnectionConfig OpcUaConfig => ConnectionConfiguration as OpcUaConnectionConfig;
        public bool IsConnected => _opcUaService?.IsConnected ?? false;

        public ObservableCollection<OpcUaNodeViewModel> RootNodes { get; } // Broncollectie
        public ICollectionView FilteredRootNodesView { get; } // View voor TreeView

        public ObservableCollection<NodeAttributeViewModel> SelectedNodeAttributes { get; }
        public ObservableCollection<ReferenceDescriptionViewModel> SelectedNodeReferences { get; }

        public bool IsBrowseAddressSpace
        {
            get => _isBrowseAddressSpace;
            set
            {
                if (SetProperty(ref _isBrowseAddressSpace, value))
                    UpdateCommandStates(); // Afhankelijk van je logica voor LoadAddressSpaceCommand
            }
        }

        public string LastReadNodeValueMessage
        {
            get => _lastReadNodeValueMessage;
            set => SetProperty(ref _lastReadNodeValueMessage, value);
        }

        public bool IsLoadingNodeDetails
        {
            get => _isLoadingNodeDetails;
            set => SetProperty(ref _isLoadingNodeDetails, value);
        }

        public OpcUaNodeViewModel SelectedOpcUaNodeInTree
        {
            get => _selectedOpcUaNodeInTree;
            set
            {
                if (SetProperty(ref _selectedOpcUaNodeInTree, value))
                {
                    _logger.Debug("Geselecteerde OPC UA Node in TreeView: {DisplayName}", _selectedOpcUaNodeInTree?.DisplayName ?? "null");
                    UpdateCommandStates(); // Voor knoppen die afhangen van selectie
                    if (_selectedOpcUaNodeInTree != null && IsConnected)
                    {
                        Task.Run(async () => await LoadSelectedNodeDetailsAsync());
                    }
                    else
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            SelectedNodeAttributes.Clear();
                            SelectedNodeReferences.Clear();
                        });
                    }
                }
            }
        }

        public string NodeFilterText
        {
            get => _nodeFilterText;
            set
            {
                if (SetProperty(ref _nodeFilterText, value))
                {
                    RefreshTreeViewFilter();
                    ((RelayCommand)SearchOnServerCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool UseNodeRegexFilter
        {
            get => _useNodeRegexFilter;
            set
            {
                if (SetProperty(ref _useNodeRegexFilter, value))
                {
                    RefreshTreeViewFilter();
                    ((RelayCommand)SearchOnServerCommand).RaiseCanExecuteChanged(); // Ook hier CanExecute updaten
                }
            }
        }
        
        public ObservableCollection<PlotTabViewModel> ActivePlotTabs { get; }
        public PlotTabViewModel SelectedPlotTab
        {
            get => _selectedPlotTab;
            set => SetProperty(ref _selectedPlotTab, value);
        }

        public ObservableCollection<NodeSearchResult> ServerSearchResults
        {
            get => _serverSearchResults;
            set => SetProperty(ref _serverSearchResults, value);
        }

        public bool IsSearchingOnServer
        {
            get => _isSearchingOnServer;
            set
            {
                if(SetProperty(ref _isSearchingOnServer, value))
                {
                    ((RelayCommand)SearchOnServerCommand).RaiseCanExecuteChanged();
                }
            }
        }
        public NodeSearchResult SelectedServerSearchResult
        {
            get => _selectedServerSearchResult;
            set
            {
                if (SetProperty(ref _selectedServerSearchResult, value))
                {
                    ((RelayCommand)MonitorFoundNodeCommand).RaiseCanExecuteChanged();
                }
            }
        }
        #endregion

        #region Commands
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand ReadAllConfiguredTagsCommand { get; }
        public ICommand LoadAddressSpaceCommand { get; }
        public ICommand AddSelectedNodeToMonitoringCommand { get; }
        public ICommand RemoveSelectedNodeFromMonitoringCommand { get; }
        public ICommand ReadSelectedNodeValueCommand { get; }
        public ICommand UnmonitorTagFromListCommand { get; }
        public ICommand OpenNewPlotTabCommand { get; }
        public ICommand AddTagToPlotCommand { get; }
        public ICommand AddSelectedTagToCurrentPlotCommand { get; }
        public ICommand SearchOnServerCommand { get; }
        public ICommand MonitorFoundNodeCommand { get; }
        #endregion

        public OpcUaTabViewModel(
            OpcUaConnectionConfig config,
            ILogger logger,
            IOpcUaService opcUaService,
            IStatusService statusService,
            IDataLoggingService dataLoggingService,
            ISettingsService settingsService
        ) : base(config)
        {
            _logger = logger?.ForContext<OpcUaTabViewModel>().ForContext("ConnectionName", config?.ConnectionName ?? "UnknownOpcUa")
                ?? throw new ArgumentNullException(nameof(logger));
            _opcUaService = opcUaService ?? throw new ArgumentNullException(nameof(opcUaService));
            _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
            _dataLoggingService = dataLoggingService ?? throw new ArgumentNullException(nameof(dataLoggingService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

            RootNodes = new ObservableCollection<OpcUaNodeViewModel>();
            BindingOperations.EnableCollectionSynchronization(RootNodes, _rootNodesLock); // Synchronisatie activeren
            FilteredRootNodesView = CollectionViewSource.GetDefaultView(RootNodes);
            FilteredRootNodesView.Filter = ApplyNodeFilter;

            SelectedNodeAttributes = new ObservableCollection<NodeAttributeViewModel>();
            SelectedNodeReferences = new ObservableCollection<ReferenceDescriptionViewModel>();
            ActivePlotTabs = new ObservableCollection<PlotTabViewModel>();
            ServerSearchResults = new ObservableCollection<NodeSearchResult>();

            if (OpcUaConfig != null && OpcUaConfig.TagsToMonitor == null)
            {
                OpcUaConfig.TagsToMonitor = new ObservableCollection<OpcUaTagConfig>();
            }

            ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => !IsConnected);
            DisconnectCommand = new RelayCommand(async _ => await DisconnectAsync(), _ => IsConnected);
            ReadAllConfiguredTagsCommand = new RelayCommand(async _ => await ReadAllConfiguredTagsAsync(), _ => IsConnected && (OpcUaConfig?.TagsToMonitor.Any(t => t.IsActive) ?? false) );
            LoadAddressSpaceCommand = new RelayCommand(async _ => await LoadInitialAddressSpaceAsync(), _ => IsConnected && !IsBrowseAddressSpace);
            AddSelectedNodeToMonitoringCommand = new RelayCommand(param => AddNodeToMonitoring(param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree), param => CanAddNodeToMonitoring(param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree));
            RemoveSelectedNodeFromMonitoringCommand = new RelayCommand(param => RemoveNodeFromMonitoring(param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree), param => CanRemoveNodeFromMonitoring(param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree));
            ReadSelectedNodeValueCommand = new RelayCommand(async param => await ReadNodeValueAsync(param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree), param => CanReadNodeValue(param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree));
            UnmonitorTagFromListCommand = new RelayCommand(param => UnmonitorTag(param as OpcUaTagConfig), param => param is OpcUaTagConfig);
            AddTagToPlotCommand = new RelayCommand(ExecuteAddTagToPlot, CanExecuteAddTagToPlotParameter);
            OpenNewPlotTabCommand = new RelayCommand(ExecuteOpenNewPlotTabForSelected, CanExecuteOpenNewPlotTabForSelected);
            AddSelectedTagToCurrentPlotCommand = new RelayCommand(ExecuteAddSelectedTagToCurrentPlot, CanExecuteAddSelectedTagToCurrentPlot);
            SearchOnServerCommand = new RelayCommand(async _ => await ExecuteSearchOnServerAsync(), _ => IsConnected && !string.IsNullOrWhiteSpace(NodeFilterText) && !IsSearchingOnServer);
            MonitorFoundNodeCommand = new RelayCommand(ExecuteMonitorFoundNode, CanExecuteMonitorFoundNode);

            _opcUaService.ConnectionStatusChanged += OnOpcUaConnectionStatusChanged;
            _opcUaService.TagsDataReceived += OnOpcUaTagsDataReceived;

            _logger.Debug("OpcUaTabViewModel geïnitialiseerd voor {ConnectionName}", DisplayName);
        }

        public void RefreshTreeViewFilter()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _logger?.Debug("RefreshTreeViewFilter aangeroepen in OpcUaTabViewModel.");
                try
                {
                    FilteredRootNodesView?.Refresh();
                    _logger?.Debug("RefreshTreeViewFilter: FilteredRootNodesView.Refresh() voltooid.");
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "RefreshTreeViewFilter: Fout tijdens FilteredRootNodesView.Refresh().");
                }            });
        }

        private bool ApplyNodeFilter(object item)
        {
            if (item is OpcUaNodeViewModel node)
            {
                if (string.IsNullOrWhiteSpace(NodeFilterText))
                {
                    SetVisibilityRecursive(node, true); // Maak de hele sub-tree zichtbaar
                    return true; // De root node zelf is altijd zichtbaar als er geen filter is
                }
                return UpdateAndCheckVisibilityRecursive(node, NodeFilterText, UseNodeRegexFilter);
            }
            return false;
        }

        private void SetVisibilityRecursive(OpcUaNodeViewModel node, bool visible)
        {
            if (node == null) return;
            node.IsVisible = visible;
            if (node.Children != null) // Check Children, niet FilteredChildrenView
            {
                foreach (var child in node.Children.Where(c => c != null)) // Itereren over de *echte* kinderen
                {
                    SetVisibilityRecursive(child, visible);
                }
            }
            // Na het aanpassen van IsVisible van kinderen, moet hun view ook refreshen
            // Dit is belangrijk voor de HierarchicalDataTemplate binding aan FilteredChildrenView
            Application.Current?.Dispatcher.Invoke(() => node.FilteredChildrenView?.Refresh());
        }

        private bool UpdateAndCheckVisibilityRecursive(OpcUaNodeViewModel node, string filterText, bool useRegex)
        {
            if (node == null) return false;

            bool selfMatches = false;
            if (!string.IsNullOrWhiteSpace(filterText))
            {
                string nodeTextToMatch = node.DisplayName;
                if (useRegex)
                {
                    try { selfMatches = Regex.IsMatch(nodeTextToMatch, filterText, RegexOptions.IgnoreCase); }
                    catch (ArgumentException ex) { _logger.Debug(ex, "Ongeldige RegEx: {Pattern}", filterText); selfMatches = false; }
                }
                else
                {
                    selfMatches = nodeTextToMatch.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            else
            {
                selfMatches = true; // Geen filter, dus node zelf is 'zichtbaar' qua match
            }

            bool anyChildMatches = false;
            if (node.Children != null && node.Children.Any(c => c != null)) // Alleen als er kinderen zijn (en geen dummy null)
            {
                foreach (var child in node.Children.Where(c => c != null))
                {
                    if (UpdateAndCheckVisibilityRecursive(child, filterText, useRegex))
                    {
                        anyChildMatches = true;
                    }
                }
            }

            node.IsVisible = selfMatches || anyChildMatches;
            // Application.Current?.Dispatcher.Invoke(() => node.FilteredChildrenView?.Refresh());
            return node.IsVisible;
        }
        
        private async Task ExecuteSearchOnServerAsync()
        {
            if (!IsConnected || string.IsNullOrWhiteSpace(NodeFilterText) || _opcUaService == null)
            {
                _logger.Warning("Kan niet zoeken op server: niet verbonden, geen filtertekst, of service is null.");
                return;
            }

            IsSearchingOnServer = true;
            _logger.Information("Start zoekopdracht op server met RegEx: {Pattern}, CaseSensitive: {CaseSensitive}", NodeFilterText, !UseNodeRegexFilter); // Aangenomen dat UseNodeRegexFilter = true betekent case-insensitive
            
            await Application.Current.Dispatcher.InvokeAsync(() => ServerSearchResults.Clear() );
            _statusService.SetStatus(ApplicationStatus.Loading, $"Zoeken op OPC server met filter: {NodeFilterText}...");

            try
            {
                var results = await _opcUaService.SearchNodesRecursiveAsync(
                    ObjectIds.ObjectsFolder, 
                    NodeFilterText, 
                    caseSensitive: !UseNodeRegexFilter, // Als UseNodeRegexFilter true is, is het IgnoreCase. Dus caseSensitive is de inverse.
                    maxDepth: 5); // Max diepte, configureerbaar maken?
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    lock(_rootNodesLock) // Lock voor ServerSearchResults als deze ook een View heeft
                    {
                        foreach (var res in results) ServerSearchResults.Add(res);
                    }
                    _logger.Information("Zoekopdracht voltooid, {Count} resultaten gevonden.", results.Count);
                    _statusService.SetStatus(ApplicationStatus.Idle, $"{results.Count} nodes gevonden met filter '{NodeFilterText}'.");
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fout tijdens zoeken op server.");
                _statusService.SetStatus(ApplicationStatus.Error, $"Fout bij zoeken: {ex.Message}");
            }
            finally
            {
                IsSearchingOnServer = false;
            }
        }
        
        private bool CanExecuteMonitorFoundNode(object parameter)
        {
            var searchResult = parameter as NodeSearchResult ?? SelectedServerSearchResult;
            if (searchResult == null || OpcUaConfig?.TagsToMonitor == null) return false;
            bool isMonitorableClass = searchResult.NodeClass == NodeClass.Variable || searchResult.NodeClass == NodeClass.VariableType;
            bool isAlreadyMonitored = OpcUaConfig.TagsToMonitor.Any(t => t.NodeId == searchResult.NodeId.ToString());
            return isMonitorableClass && !isAlreadyMonitored;
        }

        private void ExecuteMonitorFoundNode(object parameter)
        {
            var searchResult = parameter as NodeSearchResult ?? SelectedServerSearchResult;
            if (searchResult == null || !CanExecuteMonitorFoundNode(searchResult)) return;

            var newTagConfig = new OpcUaTagConfig
            {
                TagName = searchResult.DisplayName,
                NodeId = searchResult.NodeId.ToString(),
                IsActive = true,
                SamplingInterval = OpcUaConfig?.TagsToMonitor?.FirstOrDefault()?.SamplingInterval ?? 1000,
                DataType = OpcUaDataType.Variant,
                IsAlarmingEnabled = false,
                IsOutlierDetectionEnabled = false,
                BaselineSampleSize = 20,
                OutlierStandardDeviationFactor = 3.0,
                AlarmMessageFormat = "{TagName} is in alarm ({AlarmState}) met waarde {Value}",
            };
            newTagConfig.ResetBaselineState();

            OpcUaConfig.TagsToMonitor.Add(newTagConfig);
            _logger.Information("Node '{NodeId}' ({DisplayName}) uit server zoekresultaten toegevoegd aan monitoring.", newTagConfig.NodeId, newTagConfig.TagName);
            _statusService.SetStatus(ApplicationStatus.Saving, $"Node '{newTagConfig.TagName}' toegevoegd & instellingen opslaan...");

            SaveChangesForTagConfig(newTagConfig);
            ((RelayCommand)MonitorFoundNodeCommand).RaiseCanExecuteChanged();
            // Update ook de CanExecute voor de treeview knoppen als de geselecteerde node nu gemonitord wordt
            ((RelayCommand)AddSelectedNodeToMonitoringCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RemoveSelectedNodeFromMonitoringCommand).RaiseCanExecuteChanged();
        }

        #region Connection and Data Handling
        private async Task ConnectAsync()
        {
            _statusService.SetStatus(ApplicationStatus.Connecting, $"Verbinden met OPC UA: {OpcUaConfig?.ConnectionName}...");
            _logger.Information("Verbindingspoging gestart voor {ConnectionName}...", DisplayName);
            ResetAllTagBaselinesAndAlarms();
            bool success = await _opcUaService.ConnectAsync();
            if (success)
            {
                _statusService.SetStatus(ApplicationStatus.Logging, $"Verbonden met OPC UA: {OpcUaConfig?.ConnectionName}.");
                _logger.Information("Verbinding succesvol voor {ConnectionName}.", DisplayName);
                await LoadInitialAddressSpaceAsync(); // Laad address space na succesvolle verbinding
                await _opcUaService.StartMonitoringTagsAsync(); // Start monitoring van geconfigureerde tags
            }
            else
            {
                _statusService.SetStatus(ApplicationStatus.Error, $"Kon niet verbinden met OPC UA: {OpcUaConfig?.ConnectionName}.");
                _logger.Warning("Verbinding mislukt voor {ConnectionName}.", DisplayName);
            }
            UpdateCommandStates();
        }

        private async Task DisconnectAsync()
        {
            _logger.Information("Verbinding verbreken voor {ConnectionName}...", DisplayName);
            await _opcUaService.StopMonitoringTagsAsync();
            await _opcUaService.DisconnectAsync();
            _statusService.SetStatus(ApplicationStatus.Idle, $"OPC UA verbinding verbroken: {OpcUaConfig?.ConnectionName}.");
            _logger.Information("Verbinding verbroken voor {ConnectionName}.", DisplayName);
            
            await Application.Current.Dispatcher.InvokeAsync(() => // UI updates op UI thread
            {
                lock (_rootNodesLock) { RootNodes.Clear(); }
                SelectedNodeAttributes.Clear();
                SelectedNodeReferences.Clear();
                ServerSearchResults.Clear(); // Wis ook zoekresultaten
                ActivePlotTabs.Clear();
                SelectedPlotTab = null;
                SelectedOpcUaNodeInTree = null; // Reset selectie
            });
            UpdateCommandStates();
        }

        private void OnOpcUaConnectionStatusChanged(object sender, EventArgs e)
        {
            _logger.Debug("OpcUaConnectionStatusChanged. IsConnected: {IsConnected} voor {ConnectionName}", _opcUaService.IsConnected, DisplayName);
            OnPropertyChanged(nameof(IsConnected)); // Voor UI bindings
            UpdateCommandStates();

            if (!_opcUaService.IsConnected)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    lock(_rootNodesLock) { RootNodes.Clear(); }
                    SelectedNodeAttributes.Clear();
                    SelectedNodeReferences.Clear();
                    ServerSearchResults.Clear();
                    ActivePlotTabs.Clear();
                    SelectedPlotTab = null;
                    SelectedOpcUaNodeInTree = null;
                    _logger.Information("UI elementen gewist vanwege disconnect voor {ConnectionName}", DisplayName);
                });
            }
            else // Zojuist verbonden
            {
                ResetAllTagBaselinesAndAlarms();
                // Laad address space alleen als het nog niet geladen is of als de browse modus niet actief is
                if (!RootNodes.Any() && !IsBrowseAddressSpace)
                {
                    Task.Run(async () => await LoadInitialAddressSpaceAsync());
                }
                // Herstart monitoring van tags indien nodig (gebeurt nu in ConnectAsync en Reconfigure)
            }
        }

        private void OnOpcUaTagsDataReceived(object sender, IEnumerable<LoggedTagValue> receivedTagValues)
        {
            var tagValuesList = receivedTagValues?.ToList() ?? new List<LoggedTagValue>();
            if (!tagValuesList.Any())
                return;

            _logger.Debug(
                "OnOpcUaTagsDataReceived: {Count} tag(s) ontvangen voor {ConnectionName}",
                tagValuesList.Count,
                DisplayName
            );

            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var liveValue in tagValuesList)
                {
                    _logger.Debug(
                        "Ontvangen LiveValue: Tag='{Tag}', Val='{Val}', QualityGood={Qual}, ErrMsg='{Err}'",
                        liveValue.TagName,
                        liveValue.Value,
                        liveValue.IsGoodQuality,
                        liveValue.ErrorMessage
                    );

                    var configuredTag = OpcUaConfig?.TagsToMonitor.FirstOrDefault(t =>
                        t.TagName == liveValue.TagName || t.NodeId == liveValue.TagName
                    );

                    if (configuredTag != null)
                    {
                        configuredTag.CurrentValue = liveValue.Value;
                        configuredTag.Timestamp = liveValue.Timestamp;
                        configuredTag.IsGoodQuality = liveValue.IsGoodQuality;
                        configuredTag.ErrorMessage = liveValue.ErrorMessage;

                        TagAlarmState finalAlarmState;
                        double? numericValueForAlarmCheck = null;
                        double? limitDetailsForThreshold = null;

                        if (!configuredTag.IsGoodQuality)
                        {
                            finalAlarmState = TagAlarmState.Error;
                            if (configuredTag.IsOutlierDetectionEnabled)
                                configuredTag.ResetBaselineState();
                        }
                        else if (!TryConvertToDouble(liveValue.Value, out double valForCheck))
                        {
                            _logger.Warning(
                                "Waarde '{RawValue}' voor tag '{TagName}' kon niet naar double voor alarm/outlier check.",
                                liveValue.Value,
                                configuredTag.TagName
                            );
                            finalAlarmState = TagAlarmState.Error;
                            if (configuredTag.IsOutlierDetectionEnabled)
                                configuredTag.ResetBaselineState();
                        }
                        else
                        {
                            numericValueForAlarmCheck = valForCheck;
                            TagAlarmState thresholdState = DetermineThresholdAlarmState(
                                configuredTag,
                                numericValueForAlarmCheck.Value,
                                out limitDetailsForThreshold
                            );
                            bool isOutlier = IsCurrentValueOutlier(
                                configuredTag,
                                numericValueForAlarmCheck.Value
                            );
                            finalAlarmState = isOutlier ? TagAlarmState.Outlier : thresholdState;
                        }
                        UpdateAndLogFinalAlarmState(
                            configuredTag,
                            liveValue,
                            finalAlarmState,
                            numericValueForAlarmCheck,
                            limitDetailsForThreshold
                        );

                        if (liveValue.IsGoodQuality)
                        {
                            if (TryConvertToDouble(liveValue.Value, out double numericValue))
                            {
                                foreach (var plotTab in ActivePlotTabs)
                                {
                                    if (
                                        plotTab.PlotModel.Series.Any(s =>
                                            s.Title == configuredTag.TagName
                                        )
                                    )
                                    {
                                        _logger.Debug(
                                            "Plot Data Routing: TagName='{PlotSeriesKey}', Timestamp='{Ts}', Value={Val} naar plotTab '{PlotHeader}'",
                                            configuredTag.TagName,
                                            liveValue.Timestamp,
                                            numericValue,
                                            plotTab.Header
                                        );
                                        plotTab.AddDataPoint(
                                            liveValue.Timestamp,
                                            numericValue,
                                            configuredTag.TagName
                                        );
                                    }
                                }
                            }
                        }
                        else
                        {
                            _logger.Warning(
                                "Plotting: Slechte kwaliteit data voor tag '{ConfiguredTagName}' (NodeId: {NodeId}). Kwaliteit: {Quality}, Error: '{Error}'. Geen punt toegevoegd aan grafiek.",
                                configuredTag.TagName,
                                configuredTag.NodeId,
                                liveValue.IsGoodQuality,
                                liveValue.ErrorMessage
                            );
                        }
                    }
                    else
                    {
                        _logger.Verbose(
                            "OnOpcUaTagsDataReceived: Geen geconfigureerde tag gevonden voor TagName/NodeId '{TagName}'",
                            liveValue.TagName
                        );
                    }
                }
            });

            if (OpcUaConfig != null)
            {
                _dataLoggingService.LogTagValues(OpcUaConfig.ConnectionName, tagValuesList);
            }
        }

        private void ResetAllTagBaselinesAndAlarms()
        {
            if (OpcUaConfig?.TagsToMonitor != null)
            {
                _logger.Information(
                    "Resetten van baselines en alarmen voor alle relevante tags in {ConnectionName}.",
                    DisplayName
                );
                foreach (var tag in OpcUaConfig.TagsToMonitor)
                {
                    if (tag.IsOutlierDetectionEnabled)
                    {
                        tag.ResetBaselineState();
                        _logger.Debug("Baseline gereset voor tag {TagName}", tag.TagName);
                    }
                    tag.CurrentAlarmState = TagAlarmState.Normal;
                    tag.AlarmTimestamp = null;
                }
            }
        }
        #endregion

        #region Alarm and Outlier Logic
        private TagAlarmState DetermineThresholdAlarmState(
            OpcUaTagConfig tagConfig,
            double numericValue,
            out double? limitDetails
        )
        {
            limitDetails = null;
            if (!tagConfig.IsAlarmingEnabled)
                return TagAlarmState.Normal;

            if (tagConfig.HighHighLimit.HasValue && numericValue >= tagConfig.HighHighLimit.Value)
            {
                limitDetails = tagConfig.HighHighLimit.Value;
                return TagAlarmState.HighHigh;
            }
            if (tagConfig.HighLimit.HasValue && numericValue >= tagConfig.HighLimit.Value)
            {
                limitDetails = tagConfig.HighLimit.Value;
                return TagAlarmState.High;
            }
            if (tagConfig.LowLowLimit.HasValue && numericValue <= tagConfig.LowLowLimit.Value)
            {
                limitDetails = tagConfig.LowLowLimit.Value;
                return TagAlarmState.LowLow;
            }
            if (tagConfig.LowLimit.HasValue && numericValue <= tagConfig.LowLimit.Value)
            {
                limitDetails = tagConfig.LowLimit.Value;
                return TagAlarmState.Low;
            }
            return TagAlarmState.Normal;
        }

        private bool IsCurrentValueOutlier(OpcUaTagConfig tagConfig, double numericValue)
        {
            if (!tagConfig.IsOutlierDetectionEnabled)
                return false;

            tagConfig.CurrentBaselineCount++;
            double delta = numericValue - tagConfig.BaselineMean;
            tagConfig.BaselineMean += delta / tagConfig.CurrentBaselineCount;
            double delta2 = numericValue - tagConfig.BaselineMean;
            tagConfig.SumOfSquaresForBaseline += delta * delta2;

            bool baselineJustEstablished = false;
            if (
                !tagConfig.IsBaselineEstablished
                && tagConfig.CurrentBaselineCount >= tagConfig.BaselineSampleSize
            )
            {
                tagConfig.IsBaselineEstablished = true;
                baselineJustEstablished = true;
                if (tagConfig.CurrentBaselineCount > 1)
                {
                    double variance =
                        tagConfig.SumOfSquaresForBaseline / (tagConfig.CurrentBaselineCount - 1);
                    tagConfig.BaselineStandardDeviation = Math.Sqrt(variance < 0 ? 0 : variance);
                }
                else
                {
                    tagConfig.BaselineStandardDeviation = 0;
                }
                _logger.Information(
                    "Expanding baseline VASTGESTELD voor tag {TagName} na {Samples} samples: Mean={Mean:F2}, StdDev={StdDev:F2}",
                    tagConfig.TagName,
                    tagConfig.CurrentBaselineCount,
                    tagConfig.BaselineMean,
                    tagConfig.BaselineStandardDeviation
                );
            }
            else if (tagConfig.IsBaselineEstablished && tagConfig.CurrentBaselineCount > 1)
            {
                double variance =
                    tagConfig.SumOfSquaresForBaseline / (tagConfig.CurrentBaselineCount - 1);
                tagConfig.BaselineStandardDeviation = Math.Sqrt(variance < 0 ? 0 : variance);
                _logger.Verbose(
                    "Expanding baseline BIJGEWERKT voor {TagName} (N={N}): Mean={Mean:F2}, StdDev={StdDev:F2}",
                    tagConfig.TagName,
                    tagConfig.CurrentBaselineCount,
                    tagConfig.BaselineMean,
                    tagConfig.BaselineStandardDeviation
                );
            }
            else if (tagConfig.CurrentBaselineCount == 1)
            {
                tagConfig.BaselineStandardDeviation = 0;
                _logger.Debug(
                    "Expanding Baseline voor {TagName}: Eerste datapunt (N=1) ontvangen: {Value}. Mean={Mean}, StdDev=0.",
                    tagConfig.TagName,
                    numericValue,
                    tagConfig.BaselineMean
                );
            }

            if (!tagConfig.IsBaselineEstablished)
                return false;
            if (baselineJustEstablished)
                return false;

            if (tagConfig.BaselineStandardDeviation == 0)
            {
                bool isDifferent = Math.Abs(numericValue - tagConfig.BaselineMean) > 1e-9;
                if (isDifferent)
                    _logger.Verbose(
                        "Outlier (zero StdDev) gedetecteerd voor {TagName}: Waarde {NumericValue} != BaselineMean {BaselineMean}",
                        tagConfig.TagName,
                        numericValue,
                        tagConfig.BaselineMean
                    );
                return isDifferent;
            }

            double deviation = Math.Abs(numericValue - tagConfig.BaselineMean);
            bool isAnOutlier =
                deviation
                > (tagConfig.OutlierStandardDeviationFactor * tagConfig.BaselineStandardDeviation);
            if (isAnOutlier)
            {
                _logger.Verbose(
                    "Outlier gedetecteerd voor {TagName}: Waarde {NumericValue}. Afwijking: {Deviation:F2} > ({Factor} * {StdDev:F2} = {Threshold:F2})",
                    tagConfig.TagName,
                    numericValue,
                    deviation,
                    tagConfig.OutlierStandardDeviationFactor,
                    tagConfig.BaselineStandardDeviation,
                    (tagConfig.OutlierStandardDeviationFactor * tagConfig.BaselineStandardDeviation)
                );
            }
            return isAnOutlier;
        }

        private void UpdateAndLogFinalAlarmState(
            OpcUaTagConfig tagConfig,
            LoggedTagValue liveValue,
            TagAlarmState newFinalState,
            double? numericValueForLog,
            double? limitDetailsForLog
        )
        {
            if (tagConfig.CurrentAlarmState != newFinalState)
            {
                var previousState = tagConfig.CurrentAlarmState;
                tagConfig.CurrentAlarmState = newFinalState;
                string valueString = numericValueForLog.HasValue
                    ? numericValueForLog.Value.ToString(CultureInfo.InvariantCulture)
                    : liveValue.Value?.ToString() ?? "N/A";

                if (newFinalState != TagAlarmState.Normal && newFinalState != TagAlarmState.Error)
                {
                    tagConfig.AlarmTimestamp = liveValue.Timestamp;
                    string alarmDetail = "";
                    if (newFinalState == TagAlarmState.Outlier)
                        alarmDetail =
                            $"Afwijking van baseline (Mean: {tagConfig.BaselineMean:F2}, StdDev: {tagConfig.BaselineStandardDeviation:F2}, Factor: {tagConfig.OutlierStandardDeviationFactor})";
                    else if (limitDetailsForLog.HasValue)
                        alarmDetail =
                            $"Limiet overschreden: {limitDetailsForLog.Value.ToString(CultureInfo.InvariantCulture)}";
                    string formattedMessage = tagConfig
                        .AlarmMessageFormat.Replace("{TagName}", tagConfig.TagName)
                        .Replace("{AlarmState}", newFinalState.ToString())
                        .Replace("{Value}", valueString)
                        .Replace(
                            "{Limit}",
                            limitDetailsForLog?.ToString(CultureInfo.InvariantCulture) ?? "N/A"
                        );

                    _logger.Warning(
                        "ALARMSTAAT GEWIJZIGD: Tag {TagName} van {PreviousState} naar {NewState}. Waarde: {LiveValue}. Details: {AlarmDetail}. Bericht: {FormattedMessage}",
                        tagConfig.TagName,
                        previousState,
                        newFinalState,
                        valueString,
                        alarmDetail,
                        formattedMessage
                    );
                    _statusService.SetStatus(
                        ApplicationStatus.Warning,
                        $"Alarm: {tagConfig.TagName} is {newFinalState}"
                    );
                }
                else if (
                    newFinalState == TagAlarmState.Normal
                    && (
                        previousState != TagAlarmState.Normal
                        && previousState != TagAlarmState.Error
                    )
                )
                {
                    tagConfig.AlarmTimestamp = null;
                    _logger.Information(
                        "ALARM HERSTELD: Tag {TagName} van {PreviousState} naar Normaal. Waarde: {LiveValue}",
                        tagConfig.TagName,
                        previousState,
                        valueString
                    );
                }
                else if (
                    newFinalState == TagAlarmState.Error
                    && previousState != TagAlarmState.Error
                )
                {
                    tagConfig.AlarmTimestamp = liveValue.Timestamp;
                    _logger.Error(
                        "FOUTSTATUS: Tag {TagName} naar status Error. Waarde: {LiveValue}",
                        tagConfig.TagName,
                        valueString
                    );
                }
            }
        }

        protected bool TryConvertToDouble(object value, out double result)
        {
            result = 0;
            if (value == null)
                return false;
            if (value is double d)
            {
                result = d;
                return true;
            }
            if (value is float f)
            {
                result = (double)f;
                return true;
            }
            if (value is int i1)
            {
                result = i1;
                return true;
            }
            if (value is uint ui)
            {
                result = ui;
                return true;
            }
            if (value is long l)
            {
                result = l;
                return true;
            }
            if (value is ulong ul)
            {
                result = ul;
                return true;
            }
            if (value is short s)
            {
                result = s;
                return true;
            }
            if (value is ushort us)
            {
                result = us;
                return true;
            }
            if (value is byte b)
            {
                result = b;
                return true;
            }
            if (value is sbyte sb)
            {
                result = sb;
                return true;
            }
            if (value is decimal dec)
            {
                result = (double)dec;
                return true;
            }
            if (value is string sValue)
            {
                if (
                    double.TryParse(
                        sValue,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out double parsedDouble
                    )
                )
                {
                    result = parsedDouble;
                    return true;
                }
                else if (
                    double.TryParse(
                        sValue,
                        NumberStyles.Any,
                        CultureInfo.CurrentCulture,
                        out parsedDouble
                    )
                )
                {
                    result = parsedDouble;
                    return true;
                }
            }
            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region Configuration Handling
public void UpdateConfiguration(OpcUaConnectionConfig newConfig)
        {
            _logger.Information(
                "Warme configuratie update (via Settings) voor OPC UA verbinding {ConnectionName}",
                newConfig.ConnectionName
            );
            ConnectionConfiguration = newConfig;
            OnPropertyChanged(nameof(OpcUaConfig));
            if (DisplayName != newConfig.ConnectionName)
                DisplayName = newConfig.ConnectionName;
            _opcUaService.Reconfigure(newConfig);
        }

        public void SaveChangesForTagConfig(OpcUaTagConfig modifiedTagConfig)
        {
            if (modifiedTagConfig == null)
                return;
            _logger.Information(
                "Wijzigingen voor tag '{TagName}' (IsActive: {IsActive}, Interval: {Interval}, Alarming: {IsAlarmingEnabled}, Outlier: {IsOutlierEnabled}) worden opgeslagen.",
                modifiedTagConfig.TagName,
                modifiedTagConfig.IsActive,
                modifiedTagConfig.SamplingInterval,
                modifiedTagConfig.IsAlarmingEnabled,
                modifiedTagConfig.IsOutlierDetectionEnabled
            );
            if (modifiedTagConfig.IsOutlierDetectionEnabled)
            {
                modifiedTagConfig.ResetBaselineState();
                _logger.Debug(
                    "Baseline state gereset voor tag {TagName} vanwege configuratiewijziging.",
                    modifiedTagConfig.TagName
                );
            }
            _statusService.SetStatus(
                ApplicationStatus.Saving,
                $"Wijziging voor '{modifiedTagConfig.TagName}' opslaan..."
            );
            SaveSettingsAndUpdateService();
        }

        private void SaveSettingsAndUpdateService()
        {
            _settingsService.SaveSettings();
            _logger.Information("Instellingen opgeslagen na tag configuratie wijziging.");
            if (IsConnected && OpcUaConfig != null)
            {
                _logger.Information(
                    "OPC UA Service herconfigureren na tag wijziging voor {ConnectionName}",
                    DisplayName
                );
                _opcUaService.Reconfigure(OpcUaConfig);
                _statusService.SetStatus(
                    ApplicationStatus.Logging,
                    $"Monitoring geüpdatet voor {DisplayName}."
                );
            }
            else
            {
                _statusService.SetStatus(
                    ApplicationStatus.Idle,
                    "Tag configuratie gewijzigd en opgeslagen."
                );
            }
        }
        #endregion

        #region Node Browser Logic
        private async Task LoadInitialAddressSpaceAsync()
        {
            if (!IsConnected || _opcUaService == null) return;
            if (IsBrowseAddressSpace) // Voorkom dubbel laden als al bezig
            {
                _logger.Debug("LoadInitialAddressSpaceAsync: Al bezig met browsen, aanroep genegeerd.");
                return;
            }

            IsBrowseAddressSpace = true;
            _logger.Information("Laden van initiële OPC UA address space voor {ConnectionName}", DisplayName);
            _statusService.SetStatus(ApplicationStatus.Loading, $"Address space laden voor {DisplayName}...");
            
            var tempRootNodes = new List<OpcUaNodeViewModel>();
            try
            {
                ReferenceDescriptionCollection rootItems = await _opcUaService.BrowseRootAsync();
                if (rootItems != null)
                {
                    var namespaceUris = _opcUaService.NamespaceUris;
                    foreach (var item in rootItems)
                    {
                        bool hasChildren = (item.NodeClass == NodeClass.Object || item.NodeClass == NodeClass.View);
                        NodeId nodeId = ExpandedNodeId.ToNodeId(item.NodeId, namespaceUris);
                        if (nodeId != null)
                        {
                            tempRootNodes.Add(new OpcUaNodeViewModel(nodeId, item.DisplayName?.Text ?? "Unknown", item.NodeClass, _opcUaService, _logger, hasChildren));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fout bij het laden van de initiële address space voor {ConnectionName}", DisplayName);
                _statusService.SetStatus(ApplicationStatus.Error, $"Fout bij laden address space: {ex.Message}");
            }
            finally
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    lock (_rootNodesLock)
                    {
                        RootNodes.Clear();
                        foreach (var node in tempRootNodes) RootNodes.Add(node);
                    }
                    RefreshTreeViewFilter(); // Past filter toe op de nieuwe root nodes
                    _logger.Information("RootNodes bijgewerkt ({Count} items) en filter toegepast.", RootNodes.Count);
                    _statusService.SetStatus(ApplicationStatus.Idle, $"Address space geladen voor {DisplayName}.");
                });
                IsBrowseAddressSpace = false;
            }
        }
        #endregion

        #region Node Details Logic
        private async Task LoadSelectedNodeDetailsAsync()
        {
            if (
                SelectedOpcUaNodeInTree == null
                || _opcUaService == null
                || !_opcUaService.IsConnected
            )
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    SelectedNodeAttributes.Clear();
                    SelectedNodeReferences.Clear();
                });
                return;
            }
            IsLoadingNodeDetails = true;
            _logger.Information(
                "Laden van details voor geselecteerde node: {NodeId}",
                SelectedOpcUaNodeInTree.NodeId
            );
            try
            {
                var attributes = await _opcUaService.ReadNodeAttributesAsync(
                    SelectedOpcUaNodeInTree.NodeId
                );
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    SelectedNodeAttributes.Clear();
                    foreach (var attr in attributes)
                        SelectedNodeAttributes.Add(attr);
                });
                var references = await _opcUaService.BrowseAllReferencesAsync(
                    SelectedOpcUaNodeInTree.NodeId
                );
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    SelectedNodeReferences.Clear();
                    if (references != null)
                    {
                        var nsUris = _opcUaService.NamespaceUris;
                        foreach (var rd in references)
                        {
                            NodeId targetId = null;
                            NodeId refTypeIdNode = null;
                            try
                            {
                                targetId = ExpandedNodeId.ToNodeId(rd.NodeId, nsUris);
                            }
                            catch
                            { /* ignore */
                            }
                            try
                            {
                                refTypeIdNode = ExpandedNodeId.ToNodeId(rd.ReferenceTypeId, nsUris);
                            }
                            catch
                            { /* ignore */
                            }

                            if (targetId != null && refTypeIdNode != null)
                            {
                                string refTypeDisp =
                                    (
                                        _opcUaService.NamespaceUris != null
                                            ? _opcUaService
                                                .ReadNodeDisplayNameAsync(refTypeIdNode)
                                                .Result?.Text
                                            : null
                                    ) ?? refTypeIdNode.ToString();
                                SelectedNodeReferences.Add(
                                    new ReferenceDescriptionViewModel(
                                        rd,
                                        refTypeIdNode,
                                        refTypeDisp,
                                        rd.IsForward,
                                        targetId
                                    )
                                );
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout bij het laden van node details voor {NodeId}",
                    SelectedOpcUaNodeInTree.NodeId
                );
                _statusService.SetStatus(
                    ApplicationStatus.Error,
                    $"Fout bij laden node details: {ex.Message}"
                );
            }
            finally
            {
                IsLoadingNodeDetails = false;
            }
        }
        #endregion

        #region Monitoring Configuration from TreeView / List
private bool CanAddNodeToMonitoring(OpcUaNodeViewModel node)
        {
            if (node == null)
                return false;
            bool isCorrectNodeClass = (
                node.NodeClass == NodeClass.Variable || node.NodeClass == NodeClass.VariableType
            );
            bool isAlreadyMonitored = IsNodeCurrentlyMonitored(node.NodeId);
            return isCorrectNodeClass && !isAlreadyMonitored;
        }

        private bool CanRemoveNodeFromMonitoring(OpcUaNodeViewModel node)
        {
            return node != null && IsNodeCurrentlyMonitored(node.NodeId);
        }

        private bool IsNodeCurrentlyMonitored(NodeId nodeId)
        {
            if (nodeId == null || OpcUaConfig?.TagsToMonitor == null)
                return false;
            return OpcUaConfig.TagsToMonitor.Any(monitoredTag =>
                monitoredTag.NodeId == nodeId.ToString()
            );
        }

        private void AddNodeToMonitoring(OpcUaNodeViewModel nodeToAdd)
        {
            if (nodeToAdd == null || !CanAddNodeToMonitoring(nodeToAdd))
                return;
            var newTagConfig = new OpcUaTagConfig
            {
                TagName = nodeToAdd.DisplayName,
                NodeId = nodeToAdd.NodeId.ToString(),
                IsActive = true,
                SamplingInterval =
                    OpcUaConfig?.TagsToMonitor?.FirstOrDefault()?.SamplingInterval ?? 1000,
                DataType = OpcUaDataType.Variant,
                IsAlarmingEnabled = false,
                IsOutlierDetectionEnabled = false,
                BaselineSampleSize = 20,
                OutlierStandardDeviationFactor = 3.0,
                AlarmMessageFormat = "{TagName} is {AlarmState}: {Value} (Limit: {Limit})",
            };
            newTagConfig.ResetBaselineState();

            OpcUaConfig.TagsToMonitor.Add(newTagConfig);
            _logger.Information(
                "Tag {NodeId} ({DisplayName}) toegevoegd aan monitoring.",
                newTagConfig.NodeId,
                newTagConfig.TagName
            );
            _statusService.SetStatus(
                ApplicationStatus.Saving,
                $"Tag '{newTagConfig.TagName}' toegevoegd & instellingen opslaan..."
            );
            SaveChangesForTagConfig(newTagConfig);
            UpdateCommandStates();
        }

        private void RemoveNodeFromMonitoring(OpcUaNodeViewModel nodeToRemove)
        {
            if (nodeToRemove == null || !CanRemoveNodeFromMonitoring(nodeToRemove))
                return;
            var tagToRemove = OpcUaConfig.TagsToMonitor.FirstOrDefault(t =>
                t.NodeId == nodeToRemove.NodeId.ToString()
            );
            if (tagToRemove != null)
            {
                OpcUaConfig.TagsToMonitor.Remove(tagToRemove);
                _logger.Information(
                    "Tag {NodeId} ({DisplayName}) verwijderd uit monitoring.",
                    nodeToRemove.NodeId,
                    nodeToRemove.DisplayName
                );
                _statusService.SetStatus(
                    ApplicationStatus.Saving,
                    $"Tag '{nodeToRemove.DisplayName}' verwijderd & instellingen opslaan..."
                );
                SaveSettingsAndUpdateService();
                UpdateCommandStates();
            }
        }

        private void UnmonitorTag(OpcUaTagConfig tagConfig)
        {
            if (
                tagConfig == null
                || OpcUaConfig?.TagsToMonitor == null
                || !OpcUaConfig.TagsToMonitor.Contains(tagConfig)
            )
                return;
            _logger.Information(
                "Stopt monitoring en verwijdert tag: {TagName} ({NodeId}) uit lijst.",
                tagConfig.TagName,
                tagConfig.NodeId
            );
            OpcUaConfig.TagsToMonitor.Remove(tagConfig);
            _statusService.SetStatus(
                ApplicationStatus.Saving,
                $"Tag '{tagConfig.TagName}' verwijderd & instellingen opslaan..."
            );
            SaveSettingsAndUpdateService();
        }
        #endregion

        #region Node Interaction Commands (Read Value)
private bool CanReadNodeValue(OpcUaNodeViewModel node)
        {
            if (node == null)
                node = SelectedOpcUaNodeInTree;
            return node != null
                && IsConnected
                && (
                    node.NodeClass == NodeClass.Variable || node.NodeClass == NodeClass.VariableType
                );
        }

        private async Task ReadNodeValueAsync(OpcUaNodeViewModel nodeToRead)
        {
            if (nodeToRead == null || !CanReadNodeValue(nodeToRead))
            {
                LastReadNodeValueMessage =
                    "Kan waarde niet lezen (geen node/verbinding/verkeerde klasse).";
                return;
            }
            _logger.Information(
                "Lezen van waarde voor geselecteerde node: {NodeId}",
                nodeToRead.NodeId
            );
            _statusService.SetStatus(
                ApplicationStatus.Logging,
                $"Leest waarde van {nodeToRead.DisplayName}..."
            );
            try
            {
                DataValue dataValue = await _opcUaService.ReadValueAsync(nodeToRead.NodeId);
                if (dataValue != null)
                {
                    LastReadNodeValueMessage = StatusCode.IsGood(dataValue.StatusCode)
                        ? $"Waarde: {dataValue.Value?.ToString() ?? "null"} @ {dataValue.SourceTimestamp.ToLocalTime():HH:mm:ss.fff} (Kwaliteit: Goed)"
                        : $"Fout bij lezen: {dataValue.StatusCode}";
                    _logger.Information(
                        "Waarde gelezen voor {NodeId}: {Value}, Status: {StatusCode}",
                        nodeToRead.NodeId,
                        dataValue.Value,
                        dataValue.StatusCode
                    );
                }
                else
                {
                    LastReadNodeValueMessage = "Geen waarde object (null) ontvangen.";
                    _logger.Warning(
                        "Geen DataValue object ontvangen bij lezen van {NodeId}",
                        nodeToRead.NodeId
                    );
                }
            }
            catch (Exception ex)
            {
                LastReadNodeValueMessage = $"Exception: {ex.Message}";
                _logger.Error(
                    ex,
                    "Exception bij lezen van geselecteerde node {NodeId}",
                    nodeToRead.NodeId
                );
            }
            _statusService.SetStatus(ApplicationStatus.Idle, "Klaar met lezen.");
        }

        private async Task ReadAllConfiguredTagsAsync()
        {
            if (!IsConnected || OpcUaConfig == null)
            {
                _logger.Warning(
                    "Kan geconfigureerde tags niet lezen, niet verbonden: {DisplayName}",
                    DisplayName
                );
                return;
            }
            _logger.Information(
                "Eenmalige leesactie voor geconfigureerde tags gestart voor {DisplayName}",
                DisplayName
            );
            _statusService.SetStatus(
                ApplicationStatus.Logging,
                $"Leest geconfigureerde tags van {OpcUaConfig.ConnectionName}..."
            );
            var values = await _opcUaService.ReadCurrentTagValuesAsync();
            OnOpcUaTagsDataReceived(this, values);
            _statusService.SetStatus(
                ApplicationStatus.Idle,
                $"Klaar met lezen van geconfigureerde tags van {OpcUaConfig.ConnectionName}."
            );
        }
        #endregion

        #region Plotting Commands & Logic
        private bool CanExecuteOpenNewPlotTabForSelected(object parameter)
        {
            if (parameter is OpcUaTagConfig tagParam)
            {
                return tagParam.IsActive;
            }
            if (SelectedOpcUaNodeInTree != null)
            {
                var tagConfig = OpcUaConfig?.TagsToMonitor.FirstOrDefault(t =>
                    t.NodeId == SelectedOpcUaNodeInTree.NodeId.ToString()
                );
                return tagConfig != null && tagConfig.IsActive;
            }
            return false;
        }

        private void ExecuteOpenNewPlotTabForSelected(object parameter)
        {
            OpcUaTagConfig tagToPlot = null;
            if (parameter is OpcUaTagConfig directTag)
            {
                tagToPlot = directTag;
            }
            else if (SelectedOpcUaNodeInTree != null)
            {
                tagToPlot = OpcUaConfig?.TagsToMonitor.FirstOrDefault(t =>
                    t.NodeId == SelectedOpcUaNodeInTree.NodeId.ToString()
                );
                if (tagToPlot == null)
                {
                    _logger.Warning(
                        "ExecuteOpenNewPlotTabForSelected: Geselecteerde node '{SelectedNode}' is geen actieve, gemonitorde tag.",
                        SelectedOpcUaNodeInTree.DisplayName
                    );
                    MessageBox.Show(
                        $"Node '{SelectedOpcUaNodeInTree.DisplayName}' wordt niet actief gemonitord. Voeg het eerst toe aan monitoring.",
                        "Tag niet gemonitord",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }
            }

            if (tagToPlot != null && tagToPlot.IsActive)
            {
                _logger.Debug(
                    "ExecuteOpenNewPlotTabForSelected: Roept ExecuteAddTagToPlot aan voor tag '{TagName}'",
                    tagToPlot.TagName
                );
                ExecuteAddTagToPlot(tagToPlot);
            }
            else
            {
                _logger.Warning(
                    "Kan geen plot tab openen: geen geschikte actieve tag geselecteerd of gevonden."
                );
            }
        }

        private bool CanExecuteAddTagToPlotParameter(object parameter)
        {
            return parameter is OpcUaTagConfig tagConfig && tagConfig.IsActive;
        }

        private void ExecuteAddTagToPlot(object parameter)
        {
            if (parameter is OpcUaTagConfig tagConfig)
            {
                var existingPlotTab = ActivePlotTabs.FirstOrDefault(pt =>
                    pt.TagIdentifier == tagConfig.NodeId
                );
                if (existingPlotTab == null)
                {
                    var newPlotTab = new PlotTabViewModel(
                        tagConfig.NodeId,
                        $"{tagConfig.TagName}",
                        RemovePlotTab /*, _logger.ForContext("PlotContext", tagConfig.TagName) */
                    );
                    newPlotTab.EnsureSeriesExists(tagConfig.TagName, tagConfig.TagName);
                    ActivePlotTabs.Add(newPlotTab);
                    SelectedPlotTab = newPlotTab;
                    _logger.Information(
                        "Nieuwe plot tab geopend voor OPC UA Tag: {TagName} ({NodeId})",
                        tagConfig.TagName,
                        tagConfig.NodeId
                    );
                }
                else
                {
                    _logger.Information(
                        "ExecuteAddTagToPlot: Bestaande plot tab geselecteerd voor primaire NodeId: {NodeId}. Zorgen dat series '{TagName}' bestaat.",
                        tagConfig.NodeId,
                        tagConfig.TagName
                    );
                    SelectedPlotTab = existingPlotTab;
                    SelectedPlotTab.EnsureSeriesExists(tagConfig.TagName, tagConfig.TagName);
                    _logger.Information(
                        "Bestaande plot tab geselecteerd voor OPC UA Tag: {TagName} ({NodeId})",
                        tagConfig.TagName,
                        tagConfig.NodeId
                    );
                }
            }
        }

        private bool CanExecuteAddSelectedTagToCurrentPlot(object parameter)
        {
            OpcUaTagConfig tagConfigToAdd = null;
            if (parameter is OpcUaTagConfig paramTag)
                tagConfigToAdd = paramTag;
            else if (SelectedOpcUaNodeInTree != null)
                tagConfigToAdd = OpcUaConfig?.TagsToMonitor.FirstOrDefault(t =>
                    t.NodeId == SelectedOpcUaNodeInTree.NodeId.ToString()
                );

            if (SelectedPlotTab != null && tagConfigToAdd != null && tagConfigToAdd.IsActive)
            {
                return !SelectedPlotTab.PlotModel.Series.Any(s =>
                    s.Title == tagConfigToAdd.TagName
                );
            }
            return false;
        }

        private void ExecuteAddSelectedTagToCurrentPlot(object parameter)
        {
            OpcUaTagConfig tagConfigToAdd = null;
            if (parameter is OpcUaTagConfig paramTag)
            {
                tagConfigToAdd = paramTag;
            }
            else if (SelectedOpcUaNodeInTree != null)
            {
                tagConfigToAdd = OpcUaConfig?.TagsToMonitor.FirstOrDefault(t =>
                    t.NodeId == SelectedOpcUaNodeInTree.NodeId.ToString()
                );

                if (tagConfigToAdd == null)
                {
                    _logger.Warning(
                        "ExecuteAddSelectedTagToCurrentPlot: Geselecteerde node '{SelectedNode}' is geen actieve, gemonitorde tag.",
                        SelectedOpcUaNodeInTree.DisplayName
                    );
                    MessageBox.Show(
                        $"Node '{SelectedOpcUaNodeInTree.DisplayName}' wordt niet actief gemonitord. Voeg het eerst toe aan monitoring.",
                        "Tag niet gemonitord",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }
            }

            if (SelectedPlotTab != null && tagConfigToAdd != null && tagConfigToAdd.IsActive)
            {
                if (SelectedPlotTab.PlotModel.Series.Any(s => s.Title == tagConfigToAdd.TagName))
                {
                    _logger.Information(
                        "Tag '{TagName}' is al aanwezig in de huidige plot tab '{PlotTabTitle}'.",
                        tagConfigToAdd.TagName,
                        SelectedPlotTab.Header
                    );
                    MessageBox.Show(
                        $"Tag '{tagConfigToAdd.TagName}' is al aanwezig in deze grafiek.",
                        "Tag al geplot",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return;
                }

                _logger.Information(
                    "Voegt tag '{TagName}' als series toe aan de huidige plot tab '{PlotTabTitle}'",
                    tagConfigToAdd.TagName,
                    SelectedPlotTab.Header
                );
                SelectedPlotTab.EnsureSeriesExists(tagConfigToAdd.TagName, tagConfigToAdd.TagName);

                SelectedPlotTab.EnsureSeriesExists(tagConfigToAdd.TagName, tagConfigToAdd.TagName);
                (AddSelectedTagToCurrentPlotCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
            else
            {
                if (SelectedPlotTab == null)
                    _logger.Warning(
                        "ExecuteAddSelectedTagToCurrentPlot: Geen actieve plot tab geselecteerd."
                    );
                else if (tagConfigToAdd == null)
                    _logger.Warning(
                        "ExecuteAddSelectedTagToCurrentPlot: Geen tag geselecteerd om toe te voegen."
                    );
                else if (!tagConfigToAdd.IsActive)
                    _logger.Warning(
                        "ExecuteAddSelectedTagToCurrentPlot: Geselecteerde tag '{TagName}' is niet actief.",
                        tagConfigToAdd.TagName
                    );
            }
        }

        private void RemovePlotTab(PlotTabViewModel plotTabToRemove)
        {
            if (plotTabToRemove != null && ActivePlotTabs.Contains(plotTabToRemove))
            {
                (plotTabToRemove as IDisposable)?.Dispose();
                ActivePlotTabs.Remove(plotTabToRemove);
                _logger.Information("Plot tab gesloten voor: {Header}", plotTabToRemove.Header);
                if (SelectedPlotTab == plotTabToRemove)
                {
                    SelectedPlotTab = ActivePlotTabs.FirstOrDefault();
                }
            }
        }
        #endregion

        #region UI Update and Dispose
        private void UpdateCommandStates()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ReadAllConfiguredTagsCommand).RaiseCanExecuteChanged();
                ((RelayCommand)LoadAddressSpaceCommand).RaiseCanExecuteChanged();
                ((RelayCommand)AddSelectedNodeToMonitoringCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RemoveSelectedNodeFromMonitoringCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ReadSelectedNodeValueCommand).RaiseCanExecuteChanged();
                ((RelayCommand)UnmonitorTagFromListCommand).RaiseCanExecuteChanged();
                ((RelayCommand)OpenNewPlotTabCommand).RaiseCanExecuteChanged();
                ((RelayCommand)AddTagToPlotCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SearchOnServerCommand).RaiseCanExecuteChanged();
                ((RelayCommand)MonitorFoundNodeCommand).RaiseCanExecuteChanged();
                ((RelayCommand)AddSelectedTagToCurrentPlotCommand).RaiseCanExecuteChanged();

            });
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _logger.Debug("Dispose(true) aangeroepen voor OpcUaTabViewModel: {ConnectionName}", DisplayName);
                    if (_opcUaService != null)
                    {
                        _opcUaService.ConnectionStatusChanged -= OnOpcUaConnectionStatusChanged;
                        _opcUaService.TagsDataReceived -= OnOpcUaTagsDataReceived;
                        _opcUaService.Dispose();
                    }
                    Application.Current?.Dispatcher.InvokeAsync(() => // Gebruik InvokeAsync hier ook
                    {
                        lock(_rootNodesLock) { RootNodes?.Clear(); }
                        SelectedNodeAttributes?.Clear();
                        SelectedNodeReferences?.Clear();
                        ServerSearchResults?.Clear();
                        ActivePlotTabs?.Clear(); // PlotTabs zullen hun eigen Dispose aanroepen
                    });
                }
                _disposedValue = true;
            }
        }
        #endregion
    }
}

