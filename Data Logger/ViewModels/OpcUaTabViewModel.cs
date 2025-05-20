using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Data_Logger.Core;
using Data_Logger.Enums;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Opc.Ua;
using Serilog;

namespace Data_Logger.ViewModels
{
    /// <summary>
    /// ViewModel voor een tabblad dat een OPC UA connectie representeert.
    /// Beheert de connectiestatus, browsen van de OPC UA adresruimte, tag-configuratie,
    /// live data monitoring, alarmering, outlier detectie, en plotting.
    /// Implementeert <see cref="IDisposable"/> voor het correct vrijgeven van resources.
    /// </summary>
    public sealed class OpcUaTabViewModel : TabViewModelBase, IDisposable
    {
        #region Readonly Fields
        private readonly ILogger _specificLogger; // Logger specifiek voor deze ViewModel instantie
        private readonly IOpcUaService _opcUaService;
        private readonly IStatusService _statusService;
        private readonly IDataLoggingService _dataLoggingService;
        private readonly ISettingsService _settingsService;
        #endregion

        #region Fields
        private bool _isBrowseAddressSpaceProcessing; // Vlag om aan te geven of het browsen bezig is
        private OpcUaNodeViewModel _selectedOpcUaNodeInTree;
        private string _lastReadNodeValueMessage = "Nog geen waarde gelezen.";
        private bool _isLoadingNodeDetails;
        private bool _disposedValue; // Voor IDisposable patroon
        private PlotTabViewModel _selectedPlotTab;
        #endregion

        #region Properties
        /// <summary>
        /// Haalt de sterk getypeerde OPC UA connectieconfiguratie op.
        /// </summary>
        public OpcUaConnectionConfig OpcUaConfig =>
            ConnectionConfiguration as OpcUaConnectionConfig;

        /// <summary>
        /// Haalt een waarde op die aangeeft of de OPC UA service momenteel verbonden is.
        /// </summary>
        public bool IsConnected => _opcUaService?.IsConnected ?? false;

        /// <summary>
        /// Haalt de root nodes op voor de OPC UA adresruimte TreeView.
        /// </summary>
        public ObservableCollection<OpcUaNodeViewModel> RootNodes { get; }

        /// <summary>
        /// Haalt een collectie op van attributen van de <see cref="SelectedOpcUaNodeInTree"/>.
        /// </summary>
        public ObservableCollection<NodeAttributeViewModel> SelectedNodeAttributes { get; }

        /// <summary>
        /// Haalt een collectie op van referenties van de <see cref="SelectedOpcUaNodeInTree"/>.
        /// </summary>
        public ObservableCollection<ReferenceDescriptionViewModel> SelectedNodeReferences { get; }

        /// <summary>
        /// Haalt een waarde die aangeeft of de applicatie momenteel bezig is met het browsen van de OPC UA adresruimte, op of stelt deze in.
        /// Wordt gebruikt om bijvoorbeeld UI-elementen te disablen tijdens het browsen.
        /// </summary>
        public bool IsBrowseAddressSpaceProcessing
        {
            get => _isBrowseAddressSpaceProcessing;
            set
            {
                if (SetProperty(ref _isBrowseAddressSpaceProcessing, value))
                {
                    UpdateCommandStates(); // Commando's die afhankelijk zijn van deze status bijwerken
                }
            }
        }

        /// <summary>
        /// Haalt het bericht op dat het resultaat van de laatst uitgevoerde handmatige leesactie van een node-waarde weergeeft, of stelt deze in.
        /// </summary>
        public string LastReadNodeValueMessage
        {
            get => _lastReadNodeValueMessage;
            set => SetProperty(ref _lastReadNodeValueMessage, value);
        }

        /// <summary>
        /// Haalt een waarde die aangeeft of de details (attributen en referenties) van de geselecteerde node worden geladen, op of stelt deze in.
        /// </summary>
        public bool IsLoadingNodeDetails
        {
            get => _isLoadingNodeDetails;
            set => SetProperty(ref _isLoadingNodeDetails, value);
        }

        /// <summary>
        /// Haalt de momenteel geselecteerde OPC UA node in de TreeView op of stelt deze in.
        /// Bij het instellen worden de details van de node asynchroon geladen.
        /// </summary>
        public OpcUaNodeViewModel SelectedOpcUaNodeInTree
        {
            get => _selectedOpcUaNodeInTree;
            set
            {
                if (SetProperty(ref _selectedOpcUaNodeInTree, value))
                {
                    _specificLogger.Debug(
                        "Geselecteerde OPC UA Node in TreeView: {DisplayName} ({NodeId}) voor {ConnectionName}",
                        _selectedOpcUaNodeInTree?.DisplayName ?? "null",
                        _selectedOpcUaNodeInTree?.NodeId?.ToString() ?? "null",
                        DisplayName
                    );
                    UpdateCommandStates();
                    if (_selectedOpcUaNodeInTree != null && IsConnected)
                    {
                        // Start asynchroon het laden van node details
                        Task.Run(async () =>
                            await LoadSelectedNodeDetailsAsync().ConfigureAwait(false)
                        );
                    }
                    else
                    {
                        // Maak attributen en referenties leeg als er geen selectie is of geen verbinding
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            SelectedNodeAttributes.Clear();
                            SelectedNodeReferences.Clear();
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Haalt een observeerbare collectie op van actieve plot-tabbladen die geassocieerd zijn met deze OPC UA-verbinding.
        /// </summary>
        public ObservableCollection<PlotTabViewModel> ActivePlotTabs { get; }

        /// <summary>
        /// Haalt de momenteel geselecteerde plot-tab op of stelt deze in.
        /// </summary>
        public PlotTabViewModel SelectedPlotTab
        {
            get => _selectedPlotTab;
            set => SetProperty(ref _selectedPlotTab, value);
        }
        #endregion

        #region Commands
        /// <summary> Commando om verbinding te maken met de OPC UA server. </summary>
        public ICommand ConnectCommand { get; }

        /// <summary> Commando om de verbinding met de OPC UA server te verbreken. </summary>
        public ICommand DisconnectCommand { get; }

        /// <summary> Commando om de huidige waarden van alle geconfigureerde tags te lezen. </summary>
        public ICommand ReadAllConfiguredTagsCommand { get; }

        /// <summary> Commando om de OPC UA adresruimte (opnieuw) te laden. </summary>
        public ICommand LoadAddressSpaceCommand { get; }

        /// <summary> Commando om de geselecteerde node uit de adresruimte-browser toe te voegen aan de monitoringlijst. </summary>
        public ICommand AddSelectedNodeToMonitoringCommand { get; }

        /// <summary> Commando om de geselecteerde node uit de adresruimte-browser te verwijderen uit de monitoringlijst. </summary>
        public ICommand RemoveSelectedNodeFromMonitoringCommand { get; }

        /// <summary> Commando om de huidige waarde van de geselecteerde node in de adresruimte-browser te lezen. </summary>
        public ICommand ReadSelectedNodeValueCommand { get; }

        /// <summary> Commando om een tag uit de lijst van gemonitorde tags te verwijderen. </summary>
        public ICommand UnmonitorTagFromListCommand { get; }

        /// <summary> Commando om een nieuwe plot-tab te openen voor de geselecteerde (gemonitorde) tag. </summary>
        public ICommand OpenNewPlotTabCommand { get; }

        /// <summary> Commando om een tag (vanuit de monitoringlijst) toe te voegen aan een nieuwe of bestaande plot-tab. </summary>
        public ICommand AddTagToPlotCommand { get; }

        /// <summary> Commando om de geselecteerde node/tag toe te voegen aan de momenteel actieve plot-tab. </summary>
        public ICommand AddSelectedTagToCurrentPlotCommand { get; }
        #endregion

        #region Constructor
        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="OpcUaTabViewModel"/> klasse.
        /// </summary>
        /// <param name="config">De OPC UA connectieconfiguratie.</param>
        /// <param name="logger">De globale Serilog logger instantie.</param>
        /// <param name="opcUaService">De OPC UA service instantie.</param>
        /// <param name="statusService">De service voor applicatiestatus.</param>
        /// <param name="dataLoggingService">De service voor datalogging.</param>
        /// <param name="settingsService">De service voor instellingenbeheer.</param>
        public OpcUaTabViewModel(
            OpcUaConnectionConfig config,
            ILogger logger,
            IOpcUaService opcUaService,
            IStatusService statusService,
            IDataLoggingService dataLoggingService,
            ISettingsService settingsService
        )
            : base(config)
        {
            _specificLogger =
                logger
                    ?.ForContext<OpcUaTabViewModel>()
                    .ForContext("ConnectionName", config?.ConnectionName ?? "UnknownOpcUa")
                ?? throw new ArgumentNullException(nameof(logger));
            _opcUaService = opcUaService ?? throw new ArgumentNullException(nameof(opcUaService));
            _statusService =
                statusService ?? throw new ArgumentNullException(nameof(statusService));
            _dataLoggingService =
                dataLoggingService ?? throw new ArgumentNullException(nameof(dataLoggingService));
            _settingsService =
                settingsService ?? throw new ArgumentNullException(nameof(settingsService));

            RootNodes = new ObservableCollection<OpcUaNodeViewModel>();
            SelectedNodeAttributes = new ObservableCollection<NodeAttributeViewModel>();
            SelectedNodeReferences = new ObservableCollection<ReferenceDescriptionViewModel>();
            ActivePlotTabs = new ObservableCollection<PlotTabViewModel>();

            if (OpcUaConfig != null && OpcUaConfig.TagsToMonitor == null) // Zorg dat de lijst altijd bestaat
            {
                OpcUaConfig.TagsToMonitor = new ObservableCollection<OpcUaTagConfig>();
            }

            // Initialiseer Commando's
            ConnectCommand = new RelayCommand(
                async _ => await ConnectAsync().ConfigureAwait(false),
                _ => !IsConnected
            );
            DisconnectCommand = new RelayCommand(
                async _ => await DisconnectAsync().ConfigureAwait(false),
                _ => IsConnected
            );
            ReadAllConfiguredTagsCommand = new RelayCommand(
                async _ => await ReadAllConfiguredTagsAsync().ConfigureAwait(false),
                _ => IsConnected && (OpcUaConfig?.TagsToMonitor?.Any(t => t.IsActive) ?? false)
            );
            LoadAddressSpaceCommand = new RelayCommand(
                async _ => await LoadInitialAddressSpaceAsync().ConfigureAwait(false),
                _ => IsConnected && !IsBrowseAddressSpaceProcessing
            );
            AddSelectedNodeToMonitoringCommand = new RelayCommand(
                param =>
                    AddNodeToMonitoring(param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree),
                param =>
                    CanAddNodeToMonitoring(param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree)
            );
            RemoveSelectedNodeFromMonitoringCommand = new RelayCommand(
                param =>
                    RemoveNodeFromMonitoring(
                        param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree
                    ),
                param =>
                    CanRemoveNodeFromMonitoring(
                        param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree
                    )
            );
            ReadSelectedNodeValueCommand = new RelayCommand(
                async param =>
                    await ReadNodeValueAsync(param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree)
                        .ConfigureAwait(false),
                param => CanReadNodeValue(param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree)
            );
            UnmonitorTagFromListCommand = new RelayCommand(
                param => UnmonitorTag(param as OpcUaTagConfig),
                param => param is OpcUaTagConfig
            );
            OpenNewPlotTabCommand = new RelayCommand(
                ExecuteOpenNewPlotTabForSelected,
                CanExecuteOpenNewPlotTabForSelected
            );
            AddTagToPlotCommand = new RelayCommand(
                ExecuteAddTagToPlot,
                CanExecuteAddTagToPlotParameter
            );
            AddSelectedTagToCurrentPlotCommand = new RelayCommand(
                ExecuteAddSelectedTagToCurrentPlot,
                CanExecuteAddSelectedTagToCurrentPlot
            );

            // Abonneer op service events
            _opcUaService.ConnectionStatusChanged += OnOpcUaConnectionStatusChanged;
            _opcUaService.TagsDataReceived += OnOpcUaTagsDataReceived;

            ResetAllTagBaselinesAndAlarms(); // Zorg voor een schone start van alarm statussen

            _specificLogger.Debug(
                "OpcUaTabViewModel geïnitialiseerd voor {ConnectionName}",
                DisplayName
            );
        }
        #endregion

        #region Connection and Data Handling
        /// <summary>
        /// Probeert verbinding te maken met de OPC UA server en start monitoring bij succes.
        /// </summary>
        private async Task ConnectAsync()
        {
            _statusService.SetStatus(
                ApplicationStatus.Connecting,
                $"Verbinden met OPC UA: {DisplayName}..."
            );
            _specificLogger.Information(
                "Verbindingspoging gestart voor {ConnectionName}...",
                DisplayName
            );
            ResetAllTagBaselinesAndAlarms(); // Reset alarmen en baselines voor een schone staat

            bool success = await _opcUaService.ConnectAsync().ConfigureAwait(false);
            if (success)
            {
                _statusService.SetStatus(
                    ApplicationStatus.Logging,
                    $"Verbonden met OPC UA: {DisplayName}."
                );
                _specificLogger.Information(
                    "Verbinding succesvol voor {ConnectionName}.",
                    DisplayName
                );
                await LoadInitialAddressSpaceAsync().ConfigureAwait(false); // Laad de adresruimte
                await _opcUaService.StartMonitoringTagsAsync().ConfigureAwait(false); // Start tag monitoring
            }
            else
            {
                _statusService.SetStatus(
                    ApplicationStatus.Error,
                    $"Kon niet verbinden met OPC UA: {DisplayName}."
                );
                _specificLogger.Warning("Verbinding mislukt voor {ConnectionName}.", DisplayName);
            }
            UpdateCommandStates();
        }

        /// <summary>
        /// Verbreekt de verbinding met de OPC UA server en stopt monitoring.
        /// </summary>
        private async Task DisconnectAsync()
        {
            _specificLogger.Information(
                "Verbinding verbreken voor {ConnectionName}...",
                DisplayName
            );
            await _opcUaService.StopMonitoringTagsAsync().ConfigureAwait(false);
            await _opcUaService.DisconnectAsync().ConfigureAwait(false);
            _statusService.SetStatus(
                ApplicationStatus.Idle,
                $"OPC UA verbinding verbroken: {DisplayName}."
            );
            _specificLogger.Information("Verbinding verbroken voor {ConnectionName}.", DisplayName);

            // Ruim UI elementen op die afhankelijk zijn van de verbinding
            Application.Current?.Dispatcher.Invoke(() =>
            {
                RootNodes.Clear();
                SelectedNodeAttributes.Clear();
                SelectedNodeReferences.Clear();
                foreach (var plotTab in ActivePlotTabs.ToList())
                    RemovePlotTab(plotTab); // Sluit en dispose plot tabs
                ActivePlotTabs.Clear();
                SelectedPlotTab = null;
            });
            UpdateCommandStates();
        }

        /// <summary>
        /// Event handler voor wijzigingen in de OPC UA connectiestatus.
        /// Werkt UI-elementen en commando statussen bij.
        /// </summary>
        private void OnOpcUaConnectionStatusChanged(object sender, EventArgs e)
        {
            _specificLogger.Debug(
                "OpcUaConnectionStatusChanged. IsConnected: {IsConnected} voor {ConnectionName}",
                _opcUaService.IsConnected,
                DisplayName
            );
            OnPropertyChanged(nameof(IsConnected)); // Notificeer UI
            UpdateCommandStates();

            if (!_opcUaService.IsConnected)
            {
                // Ruim UI op die afhankelijk is van een actieve verbinding
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    RootNodes.Clear();
                    SelectedNodeAttributes.Clear();
                    SelectedNodeReferences.Clear();
                    foreach (var plotTab in ActivePlotTabs.ToList())
                        RemovePlotTab(plotTab);
                    ActivePlotTabs.Clear();
                    SelectedPlotTab = null;
                    if (OpcUaConfig?.TagsToMonitor != null)
                    {
                        foreach (var tag in OpcUaConfig.TagsToMonitor) // Reset live data in config
                        {
                            tag.CurrentValue = null;
                            tag.IsGoodQuality = false;
                            tag.ErrorMessage = "Niet verbonden";
                            tag.CurrentAlarmState = TagAlarmState.Error;
                        }
                    }
                });
            }
            else // Indien succesvol (her)verbonden
            {
                ResetAllTagBaselinesAndAlarms(); // Reset alarmen voor een schone start
                if (!RootNodes.Any() && !IsBrowseAddressSpaceProcessing) // Als adresruimte nog niet geladen is
                {
                    Task.Run(async () => await LoadInitialAddressSpaceAsync().ConfigureAwait(false)
                    );
                }
            }
        }

        /// <summary>
        /// Event handler voor ontvangen OPC UA tag data.
        /// Werkt de overeenkomstige <see cref="OpcUaTagConfig"/> objecten bij met live data,
        /// past alarm/outlier logica toe, en stuurt data door naar actieve plots.
        /// </summary>
        private void OnOpcUaTagsDataReceived(
            object sender,
            IEnumerable<LoggedTagValue> receivedTagValues
        )
        {
            var tagValuesList = receivedTagValues?.ToList() ?? new List<LoggedTagValue>();
            if (!tagValuesList.Any())
                return;

            _specificLogger.Verbose(
                "OnOpcUaTagsDataReceived: {Count} tag(s) ontvangen voor {ConnectionName}",
                tagValuesList.Count,
                DisplayName
            );

            Application.Current?.Dispatcher.Invoke(() => // Zorg voor UI thread access
            {
                foreach (var liveValue in tagValuesList)
                {
                    _specificLogger.Verbose(
                        "Ontvangen LiveValue voor {ConnectionName}: Tag='{Tag}', Waarde='{Val}', Kwaliteit={Qual}, ErrMsg='{Err}', Tijdstempel='{Ts:O}'",
                        DisplayName,
                        liveValue.TagName,
                        liveValue.Value,
                        liveValue.IsGoodQuality,
                        liveValue.ErrorMessage,
                        liveValue.Timestamp
                    );

                    // Vind de corresponderende tag configuratie. TagName in LoggedTagValue komt van MonitoredItem.DisplayName, wat we instellen op OpcUaTagConfig.TagName
                    var configuredTag = OpcUaConfig?.TagsToMonitor.FirstOrDefault(t =>
                        t.TagName == liveValue.TagName
                    );

                    if (configuredTag != null)
                    {
                        // Update live data in de configuratie (voor UI binding)
                        configuredTag.CurrentValue = liveValue.Value;
                        configuredTag.Timestamp = liveValue.Timestamp;
                        configuredTag.IsGoodQuality = liveValue.IsGoodQuality;
                        configuredTag.ErrorMessage = liveValue.ErrorMessage;

                        // Pas alarm en outlier logica toe
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
                            _specificLogger.Warning(
                                "Waarde '{RawValue}' voor tag '{TagName}' ({ConnectionName}) kon niet naar double geconverteerd worden voor alarm/outlier check.",
                                liveValue.Value,
                                configuredTag.TagName,
                                DisplayName
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

                        // Stuur data door naar actieve plots
                        if (liveValue.IsGoodQuality && numericValueForAlarmCheck.HasValue) // Gebruik de al geconverteerde waarde
                        {
                            foreach (var plotTab in ActivePlotTabs)
                            {
                                if (
                                    plotTab.PlotModel.Series.Any(s =>
                                        s.Title == configuredTag.TagName
                                    )
                                )
                                {
                                    _specificLogger.Verbose(
                                        "Plot Data Routing (OPC UA): TagName='{PlotSeriesKey}', Timestamp='{Ts}', Value={Val} naar plotTab '{PlotHeader}' voor {ConnectionName}",
                                        configuredTag.TagName,
                                        liveValue.Timestamp,
                                        numericValueForAlarmCheck.Value,
                                        plotTab.Header,
                                        DisplayName
                                    );
                                    plotTab.AddDataPoint(
                                        liveValue.Timestamp,
                                        numericValueForAlarmCheck.Value,
                                        configuredTag.TagName
                                    );
                                }
                            }
                        }
                        else if (!liveValue.IsGoodQuality)
                        {
                            _specificLogger.Warning(
                                "Plotting (OPC UA): Slechte kwaliteit data voor tag '{ConfiguredTagName}' ({ConnectionName}). NodeId: {NodeId}. Kwaliteit: {Quality}, Error: '{Error}'. Geen punt toegevoegd aan grafiek.",
                                configuredTag.TagName,
                                DisplayName,
                                configuredTag.NodeId,
                                liveValue.IsGoodQuality,
                                liveValue.ErrorMessage
                            );
                        }
                    }
                    else
                    {
                        _specificLogger.Warning(
                            "OnOpcUaTagsDataReceived: Geen geconfigureerde tag gevonden voor ontvangen TagName '{TagName}' voor {ConnectionName}. Data niet verwerkt voor alarmen/plots.",
                            liveValue.TagName,
                            DisplayName
                        );
                    }
                }
            });

            // Log data naar persistentie
            if (OpcUaConfig != null)
            {
                _dataLoggingService.LogTagValues(OpcUaConfig.ConnectionName, tagValuesList);
            }
        }

        /// <summary>
        /// Reset de baseline en alarmstatus voor alle geconfigureerde OPC UA tags.
        /// </summary>
        private void ResetAllTagBaselinesAndAlarms()
        {
            if (OpcUaConfig?.TagsToMonitor != null)
            {
                _specificLogger.Information(
                    "Resetten van baselines en alarmen voor alle relevante tags in {ConnectionName}.",
                    DisplayName
                );
                foreach (var tag in OpcUaConfig.TagsToMonitor)
                {
                    if (tag.IsOutlierDetectionEnabled)
                    {
                        tag.ResetBaselineState(); // De ResetBaselineState methode logt zelf ook al.
                    }
                    tag.CurrentAlarmState = TagAlarmState.Normal;
                    tag.AlarmTimestamp = null;
                }
            }
        }
        #endregion

        #region Alarm and Outlier Logic
        /// <summary>
        /// Bepaalt de alarmstatus van een tag op basis van geconfigureerde drempelwaarden.
        /// </summary>
        /// <param name="tagConfig">De configuratie van de tag.</param>
        /// <param name="numericValue">De huidige numerieke waarde van de tag.</param>
        /// <param name="limitDetails">Output parameter; de specifieke limiet die is overschreden, indien van toepassing.</param>
        /// <returns>De berekende <see cref="TagAlarmState"/>.</returns>
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

        /// <summary>
        /// Bepaalt of de huidige waarde van een tag een statistische uitschieter (outlier) is,
        /// gebaseerd op een expanding window baseline berekening (Welford's algoritme stijl).
        /// Werkt de baseline statistieken (<see cref="OpcUaTagConfig.BaselineMean"/>, <see cref="OpcUaTagConfig.BaselineStandardDeviation"/>, etc.) bij.
        /// </summary>
        /// <param name="tagConfig">De configuratie van de tag, inclusief baseline parameters en staat.</param>
        /// <param name="numericValue">De huidige numerieke waarde van de tag.</param>
        /// <returns>True als de waarde als een outlier wordt beschouwd; anders false.</returns>
        private bool IsCurrentValueOutlier(OpcUaTagConfig tagConfig, double numericValue)
        {
            if (!tagConfig.IsOutlierDetectionEnabled)
                return false;

            // Welford's algorithm for online variance/std deviation
            tagConfig.CurrentBaselineCount++;
            double delta = numericValue - tagConfig.BaselineMean; // Gebruik *vorige* gemiddelde voor delta
            tagConfig.BaselineMean += delta / tagConfig.CurrentBaselineCount; // Update gemiddelde
            double delta2 = numericValue - tagConfig.BaselineMean; // Gebruik *nieuwe* gemiddelde voor delta2
            tagConfig.SumOfSquaresForBaseline += delta * delta2; // Update som van kwadratische verschillen

            bool baselineJustEstablished = false;
            if (
                !tagConfig.IsBaselineEstablished
                && tagConfig.CurrentBaselineCount >= tagConfig.BaselineSampleSize
            )
            {
                tagConfig.IsBaselineEstablished = true;
                baselineJustEstablished = true; // Markeer dat baseline net is vastgesteld
                if (tagConfig.CurrentBaselineCount > 1)
                {
                    double variance =
                        tagConfig.SumOfSquaresForBaseline / (tagConfig.CurrentBaselineCount - 1);
                    tagConfig.BaselineStandardDeviation = Math.Sqrt(Math.Max(0, variance)); // Voorkom negatieve wortel door floating point onnauwkeurigheden
                }
                else
                {
                    tagConfig.BaselineStandardDeviation = 0; // StdDev van 1 punt is 0
                }
                _specificLogger.Information(
                    "Expanding baseline VASTGESTELD voor tag {TagName} ({ConnectionName}) na {Samples} samples: Mean={Mean:F2}, StdDev={StdDev:F2}",
                    tagConfig.TagName,
                    DisplayName,
                    tagConfig.CurrentBaselineCount,
                    tagConfig.BaselineMean,
                    tagConfig.BaselineStandardDeviation
                );
            }
            else if (tagConfig.IsBaselineEstablished && tagConfig.CurrentBaselineCount > 1) // Blijf StdDev updaten als baseline is vastgesteld
            {
                double variance =
                    tagConfig.SumOfSquaresForBaseline / (tagConfig.CurrentBaselineCount - 1);
                tagConfig.BaselineStandardDeviation = Math.Sqrt(Math.Max(0, variance));
                _specificLogger.Verbose(
                    "Expanding baseline BIJGEWERKT voor {TagName} ({ConnectionName}) (N={N}): Mean={Mean:F2}, StdDev={StdDev:F2}",
                    tagConfig.TagName,
                    DisplayName,
                    tagConfig.CurrentBaselineCount,
                    tagConfig.BaselineMean,
                    tagConfig.BaselineStandardDeviation
                );
            }
            else if (tagConfig.CurrentBaselineCount == 1) // Eerste datapunt voor baseline
            {
                tagConfig.BaselineStandardDeviation = 0;
                _specificLogger.Debug(
                    "Expanding Baseline voor {TagName} ({ConnectionName}): Eerste datapunt (N=1) ontvangen: {Value}. Mean={Mean}, StdDev=0.",
                    tagConfig.TagName,
                    DisplayName,
                    numericValue,
                    tagConfig.BaselineMean
                );
            }

            if (!tagConfig.IsBaselineEstablished || baselineJustEstablished)
                return false; // Geen outlier als baseline nog niet (volledig) is opgebouwd, of net opgebouwd

            // Als standaarddeviatie (bijna) nul is, is elke afwijking van het gemiddelde een outlier.
            if (tagConfig.BaselineStandardDeviation < 1e-9) // Kleine tolerantie voor floating point issues
            {
                bool isDifferent = Math.Abs(numericValue - tagConfig.BaselineMean) > 1e-9; // Vergelijk met kleine epsilon
                if (isDifferent)
                    _specificLogger.Verbose(
                        "Outlier (zero StdDev) gedetecteerd voor {TagName} ({ConnectionName}): Waarde {NumericValue} != BaselineMean {BaselineMean}",
                        tagConfig.TagName,
                        DisplayName,
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
                _specificLogger.Information(
                    "Outlier gedetecteerd voor {TagName} ({ConnectionName}): Waarde {NumericValue}. Afwijking: {Deviation:F2} > (Factor {Factor} * StdDev {StdDev:F2} = Drempel {Threshold:F2})",
                    tagConfig.TagName,
                    DisplayName,
                    numericValue,
                    deviation,
                    tagConfig.OutlierStandardDeviationFactor,
                    tagConfig.BaselineStandardDeviation,
                    (tagConfig.OutlierStandardDeviationFactor * tagConfig.BaselineStandardDeviation)
                );
            }
            return isAnOutlier;
        }

        /// <summary>
        /// Werkt de <see cref="OpcUaTagConfig.CurrentAlarmState"/> bij en logt een bericht als de status verandert.
        /// Stelt ook de <see cref="OpcUaTagConfig.AlarmTimestamp"/> in.
        /// </summary>
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
                tagConfig.CurrentAlarmState = newFinalState; // Update de status in de tag configuratie
                string valueString = numericValueForLog.HasValue
                    ? numericValueForLog.Value.ToString("G", CultureInfo.InvariantCulture)
                    : liveValue.Value?.ToString() ?? "N/A";

                if (newFinalState != TagAlarmState.Normal && newFinalState != TagAlarmState.Error) // Een daadwerkelijk alarm of outlier
                {
                    tagConfig.AlarmTimestamp = liveValue.Timestamp;
                    string alarmDetail = "";
                    if (newFinalState == TagAlarmState.Outlier)
                        alarmDetail =
                            $"Afwijking van baseline (Mean: {tagConfig.BaselineMean:F2}, StdDev: {tagConfig.BaselineStandardDeviation:F2}, Factor: {tagConfig.OutlierStandardDeviationFactor})";
                    else if (limitDetailsForLog.HasValue)
                        alarmDetail =
                            $"Limiet ({limitDetailsForLog.Value.ToString(CultureInfo.InvariantCulture)}) overschreden";
                    else
                        alarmDetail = "Limiet overschreden (details niet gespecificeerd)";

                    string formattedMessage = tagConfig
                        .AlarmMessageFormat.Replace("{TagName}", tagConfig.TagName)
                        .Replace("{AlarmState}", newFinalState.ToString())
                        .Replace("{Value}", valueString)
                        .Replace(
                            "{Limit}",
                            limitDetailsForLog?.ToString(CultureInfo.InvariantCulture) ?? "N/A"
                        );

                    _specificLogger.Warning(
                        "ALARMSTAAT GEWIJZIGD ({ConnectionName}): Tag {TagName} van {PreviousState} naar {NewState}. Waarde: {LiveValue}. Details: {AlarmDetail}. Bericht: {FormattedMessage}",
                        DisplayName,
                        tagConfig.TagName,
                        previousState,
                        newFinalState,
                        valueString,
                        alarmDetail,
                        formattedMessage
                    );
                    _statusService.SetStatus(
                        ApplicationStatus.Warning,
                        $"Alarm: {tagConfig.TagName} ({DisplayName}) is {newFinalState}"
                    );
                }
                else if (
                    newFinalState == TagAlarmState.Normal
                    && (
                        previousState != TagAlarmState.Normal
                        && previousState != TagAlarmState.Error
                    )
                ) // Hersteld van alarm/outlier
                {
                    tagConfig.AlarmTimestamp = null; // Reset timestamp
                    _specificLogger.Information(
                        "ALARM HERSTELD ({ConnectionName}): Tag {TagName} van {PreviousState} naar Normaal. Waarde: {LiveValue}",
                        DisplayName,
                        tagConfig.TagName,
                        previousState,
                        valueString
                    );
                }
                else if (
                    newFinalState == TagAlarmState.Error
                    && previousState != TagAlarmState.Error
                ) // Nieuwe Error status
                {
                    tagConfig.AlarmTimestamp = liveValue.Timestamp; // Zet timestamp voor error
                    _specificLogger.Error(
                        "FOUTSTATUS ({ConnectionName}): Tag {TagName} van {PreviousState} naar status Error. Waarde: {LiveValue}. Oorspronkelijke Fout: {OriginalError}",
                        DisplayName,
                        tagConfig.TagName,
                        previousState,
                        valueString,
                        liveValue.ErrorMessage ?? "N/A"
                    );
                    _statusService.SetStatus(
                        ApplicationStatus.Error,
                        $"Foutstatus voor tag: {tagConfig.TagName} ({DisplayName})"
                    );
                }
            }
        }

        /// <summary>
        /// Probeert de gegeven object waarde te converteren naar een double.
        /// </summary>
        protected bool TryConvertToDouble(object value, out double result)
        {
            result = 0;
            if (value == null)
                return false;
            Type valueType = value.GetType();

            if (valueType == typeof(double))
            {
                result = (double)value;
                return true;
            }
            if (valueType == typeof(float))
            {
                result = (double)(float)value;
                return true;
            }
            if (valueType == typeof(int))
            {
                result = (double)(int)value;
                return true;
            }
            if (valueType == typeof(uint))
            {
                result = (double)(uint)value;
                return true;
            }
            if (valueType == typeof(long))
            {
                result = (double)(long)value;
                return true;
            }
            if (valueType == typeof(ulong))
            {
                result = (double)(ulong)value;
                return true;
            }
            if (valueType == typeof(short))
            {
                result = (double)(short)value;
                return true;
            }
            if (valueType == typeof(ushort))
            {
                result = (double)(ushort)value;
                return true;
            }
            if (valueType == typeof(byte))
            {
                result = (double)(byte)value;
                return true;
            }
            if (valueType == typeof(sbyte))
            {
                result = (double)(sbyte)value;
                return true;
            }
            if (valueType == typeof(decimal))
            {
                result = (double)(decimal)value;
                return true;
            }
            if (valueType == typeof(bool))
            {
                result = (bool)value ? 1.0 : 0.0;
                return true;
            }
            if (value is string sValue)
            {
                if (
                    double.TryParse(
                        sValue,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out result
                    )
                )
                    return true;
                if (
                    double.TryParse(
                        sValue,
                        NumberStyles.Any,
                        CultureInfo.CurrentCulture,
                        out result
                    )
                )
                    return true; // Fallback
            }
            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch { }
            return false;
        }
        #endregion

        #region Configuration Handling
        /// <inheritdoc/>
        public void UpdateConfiguration(OpcUaConnectionConfig newConfig)
        {
            _specificLogger.Information(
                "Warme configuratie update (via Settings) voor OPC UA verbinding {ConnectionName}",
                newConfig.ConnectionName
            );
            ConnectionConfiguration = newConfig; // Update base property
            OnPropertyChanged(nameof(OpcUaConfig)); // Notificeer UI over de getypte property
            if (DisplayName != newConfig.ConnectionName)
                DisplayName = newConfig.ConnectionName;
            _opcUaService.Reconfigure(newConfig); // Geef door aan de service
        }

        /// <summary>
        /// Slaat wijzigingen voor een specifieke OPC UA tag configuratie op.
        /// </summary>
        public void SaveChangesForTagConfig(OpcUaTagConfig modifiedTagConfig)
        {
            if (modifiedTagConfig == null)
                return;
            _specificLogger.Information(
                "Wijzigingen voor tag '{TagName}' ({ConnectionName}) worden opgeslagen. IsActive: {IsActive}, Interval: {Interval}, Alarming: {IsAlarmingEnabled}, Outlier: {IsOutlierEnabled}",
                modifiedTagConfig.TagName,
                DisplayName,
                modifiedTagConfig.IsActive,
                modifiedTagConfig.SamplingInterval,
                modifiedTagConfig.IsAlarmingEnabled,
                modifiedTagConfig.IsOutlierDetectionEnabled
            );
            if (modifiedTagConfig.IsOutlierDetectionEnabled)
            {
                modifiedTagConfig.ResetBaselineState(); // Reset baseline als outlier settings veranderen
            }
            _statusService.SetStatus(
                ApplicationStatus.Saving,
                $"Wijziging voor tag '{modifiedTagConfig.TagName}' opslaan..."
            );
            SaveSettingsAndUpdateService();
        }

        /// <summary>
        /// Slaat alle instellingen op en herconfigureert de OPC UA service.
        /// </summary>
        private void SaveSettingsAndUpdateService()
        {
            _settingsService.SaveSettings();
            _specificLogger.Information(
                "Instellingen opgeslagen na tag configuratie wijziging voor {ConnectionName}.",
                DisplayName
            );
            if (IsConnected && OpcUaConfig != null)
            {
                _specificLogger.Information(
                    "OPC UA Service herconfigureren na tag wijziging voor {ConnectionName}",
                    DisplayName
                );
                _opcUaService.Reconfigure(OpcUaConfig); // Dit triggert mogelijk een herstart van monitoring
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
        /// <summary>
        /// Laadt asynchroon de initiële OPC UA adresruimte (root nodes).
        /// </summary>
        private async Task LoadInitialAddressSpaceAsync()
        {
            if (!IsConnected || _opcUaService == null || IsBrowseAddressSpaceProcessing)
                return;
            IsBrowseAddressSpaceProcessing = true;
            _specificLogger.Information(
                "Laden van initiële OPC UA address space voor {ConnectionName}",
                DisplayName
            );
            Application.Current?.Dispatcher.Invoke(() => RootNodes.Clear());
            try
            {
                ReferenceDescriptionCollection rootItems = await _opcUaService
                    .BrowseRootAsync()
                    .ConfigureAwait(false);
                if (rootItems != null)
                {
                    var namespaceUris = _opcUaService.NamespaceUris;
                    foreach (var item in rootItems)
                    {
                        bool hasChildren =
                            item.NodeClass == NodeClass.Object || item.NodeClass == NodeClass.View;
                        NodeId nodeId = null;
                        try
                        {
                            nodeId = ExpandedNodeId.ToNodeId(item.NodeId, namespaceUris);
                        }
                        catch (Exception ex)
                        {
                            _specificLogger.Error(
                                ex,
                                "Fout bij converteren root ExpandedNodeId {ExpNodeId}",
                                item.NodeId
                            );
                            continue;
                        }

                        if (nodeId != null)
                        {
                            Application.Current?.Dispatcher.Invoke(() =>
                                RootNodes.Add(
                                    new OpcUaNodeViewModel(
                                        nodeId,
                                        item.DisplayName?.Text ?? "Unknown",
                                        item.NodeClass,
                                        _opcUaService,
                                        _specificLogger,
                                        hasChildren,
                                        true
                                    )
                                )
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _specificLogger.Error(
                    ex,
                    "Fout bij laden initiële address space voor {ConnectionName}",
                    DisplayName
                );
                _statusService.SetStatus(
                    ApplicationStatus.Error,
                    $"Fout laden address space: {ex.Message}"
                );
            }
            finally
            {
                IsBrowseAddressSpaceProcessing = false;
            }
        }
        #endregion

        #region Node Details Logic
        /// <summary>
        /// Laadt asynchroon de details (attributen en referenties) van de <see cref="SelectedOpcUaNodeInTree"/>.
        /// </summary>
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
                    IsLoadingNodeDetails = false;
                });
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() => IsLoadingNodeDetails = true);

            _specificLogger.Information(
                "Laden van details voor geselecteerde node: {NodeId} ({ConnectionName})",
                SelectedOpcUaNodeInTree.NodeId,
                DisplayName
            );

            List<NodeAttributeViewModel> newAttributes = null; // Tijdelijke collectie voor attributen
            List<ReferenceDescriptionViewModel> newReferencesVMs =
                new List<ReferenceDescriptionViewModel>(); // Tijdelijke collectie voor referenties

            try
            {
                var attributesTask = _opcUaService.ReadNodeAttributesAsync(
                    SelectedOpcUaNodeInTree.NodeId
                );
                var referencesTask = _opcUaService.BrowseAllReferencesAsync(
                    SelectedOpcUaNodeInTree.NodeId
                );
                await Task.WhenAll(attributesTask, referencesTask).ConfigureAwait(false);

                newAttributes = await attributesTask.ConfigureAwait(false);
                var references = await referencesTask.ConfigureAwait(false);

                if (references != null)
                {
                    var nsUris = _opcUaService.NamespaceUris; // Eenmalig ophalen
                    foreach (var rd in references)
                    {
                        NodeId targetId = null;
                        NodeId refTypeIdNode = null;

                        try
                        {
                            targetId = ExpandedNodeId.ToNodeId(rd.NodeId, nsUris);
                        }
                        catch (Exception ex)
                        {
                            _specificLogger.Warning(
                                ex,
                                "Kon target NodeId {Node} niet parsen voor referentie. Referentie wordt overgeslagen.",
                                rd.NodeId
                            );
                            continue; // Ga naar de volgende referentie
                        }

                        try
                        {
                            refTypeIdNode = ExpandedNodeId.ToNodeId(rd.ReferenceTypeId, nsUris);
                        }
                        catch (Exception ex)
                        {
                            _specificLogger.Warning(
                                ex,
                                "Kon referentie type NodeId {Node} niet parsen. Referentie wordt overgeslagen.",
                                rd.ReferenceTypeId
                            );
                            continue; // Ga naar de volgende referentie
                        }

                        string refTypeDisp = refTypeIdNode.ToString(); // Fallback waarde
                        if (refTypeIdNode != null && !refTypeIdNode.IsNullNodeId) // Controleer of het een valide NodeId is
                        {
                            // Roep ReadNodeDisplayNameAsync asynchroon aan en wacht erop
                            var localizedText = await _opcUaService
                                .ReadNodeDisplayNameAsync(refTypeIdNode)
                                .ConfigureAwait(false);
                            if (localizedText != null && !string.IsNullOrEmpty(localizedText.Text))
                            {
                                refTypeDisp = localizedText.Text;
                            }
                        }
                        newReferencesVMs.Add(
                            new ReferenceDescriptionViewModel(
                                rd,
                                refTypeDisp,
                                rd.IsForward,
                                targetId
                            )
                        );
                    }
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SelectedNodeAttributes.Clear();
                    if (newAttributes != null)
                    {
                        foreach (var attr in newAttributes)
                            SelectedNodeAttributes.Add(attr);
                    }

                    SelectedNodeReferences.Clear();
                    foreach (var refVM in newReferencesVMs)
                        SelectedNodeReferences.Add(refVM);
                });
            }
            catch (Exception ex)
            {
                _specificLogger.Error(
                    ex,
                    "Fout bij laden node details voor {NodeId} ({ConnectionName})",
                    SelectedOpcUaNodeInTree.NodeId,
                    DisplayName
                );
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SelectedNodeAttributes.Clear();
                    SelectedNodeReferences.Clear();
                });
            }
            finally
            {
                await Application.Current.Dispatcher.InvokeAsync(() => IsLoadingNodeDetails = false
                );
            }
        }
        #endregion

        #region Monitoring Configuration from TreeView / List
        /// <summary> Bepaalt of de opgegeven node aan monitoring toegevoegd kan worden. </summary>
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

        /// <summary> Bepaalt of de opgegeven node uit monitoring verwijderd kan worden. </summary>
        private bool CanRemoveNodeFromMonitoring(OpcUaNodeViewModel node)
        {
            return node != null && IsNodeCurrentlyMonitored(node.NodeId);
        }

        /// <summary> Controleert of een NodeId momenteel gemonitord wordt. </summary>
        private bool IsNodeCurrentlyMonitored(NodeId nodeId)
        {
            if (nodeId == null || OpcUaConfig?.TagsToMonitor == null)
                return false;
            return OpcUaConfig.TagsToMonitor.Any(monitoredTag =>
                monitoredTag.NodeId == nodeId.ToString()
            );
        }

        /// <summary> Voegt de opgegeven node toe aan de monitoring lijst. </summary>
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

            if (OpcUaConfig != null)
                if (OpcUaConfig.TagsToMonitor != null)
                    OpcUaConfig.TagsToMonitor.Add(newTagConfig);
            _specificLogger.Information(
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

        /// <summary> Verwijdert de opgegeven node uit de monitoring lijst. </summary>
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
                _specificLogger.Information(
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

        /// <summary> Verwijdert de opgegeven tagconfiguratie uit de monitoring lijst. </summary>
        private void UnmonitorTag(OpcUaTagConfig tagConfig)
        {
            if (
                tagConfig == null
                || OpcUaConfig?.TagsToMonitor == null
                || !OpcUaConfig.TagsToMonitor.Contains(tagConfig)
            )
                return;
            _specificLogger.Information(
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
        /// <summary> Bepaalt of de waarde van de opgegeven node gelezen kan worden. </summary>
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

        /// <summary> Leest asynchroon de waarde van de opgegeven node. </summary>
        private async Task ReadNodeValueAsync(OpcUaNodeViewModel nodeToRead)
        {
            if (nodeToRead == null || !CanReadNodeValue(nodeToRead))
            {
                LastReadNodeValueMessage =
                    "Kan waarde niet lezen (geen node/verbinding/verkeerde klasse).";
                return;
            }
            _specificLogger.Information(
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
                    _specificLogger.Information(
                        "Waarde gelezen voor {NodeId}: {Value}, Status: {StatusCode}",
                        nodeToRead.NodeId,
                        dataValue.Value,
                        dataValue.StatusCode
                    );
                }
                else
                {
                    LastReadNodeValueMessage = "Geen waarde object (null) ontvangen.";
                    _specificLogger.Warning(
                        "Geen DataValue object ontvangen bij lezen van {NodeId}",
                        nodeToRead.NodeId
                    );
                }
            }
            catch (Exception ex)
            {
                LastReadNodeValueMessage = $"Exception: {ex.Message}";
                _specificLogger.Error(
                    ex,
                    "Exception bij lezen van geselecteerde node {NodeId}",
                    nodeToRead.NodeId
                );
            }
            _statusService.SetStatus(ApplicationStatus.Idle, "Klaar met lezen.");
        }

        /// <summary> Leest asynchroon de huidige waarden van alle geconfigureerde tags. </summary>
        private async Task ReadAllConfiguredTagsAsync()
        {
            if (!IsConnected || OpcUaConfig == null)
            {
                _specificLogger.Warning(
                    "Kan geconfigureerde tags niet lezen, niet verbonden: {DisplayName}",
                    DisplayName
                );
                return;
            }
            _specificLogger.Information(
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
        /// <summary> Bepaalt of een nieuwe plot-tab geopend kan worden voor de geselecteerde/gegeven tag. </summary>
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

        /// <summary> Opent een nieuwe plot-tab voor de geselecteerde/gegeven tag. </summary>
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
                    _specificLogger.Warning(
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
                _specificLogger.Debug(
                    "ExecuteOpenNewPlotTabForSelected: Roept ExecuteAddTagToPlot aan voor tag '{TagName}'",
                    tagToPlot.TagName
                );
                ExecuteAddTagToPlot(tagToPlot);
            }
            else
            {
                _specificLogger.Warning(
                    "Kan geen plot tab openen: geen geschikte actieve tag geselecteerd of gevonden."
                );
            }
        }

        /// <summary> Bepaalt of een tag aan een plot toegevoegd kan worden. </summary>
        private bool CanExecuteAddTagToPlotParameter(object parameter)
        {
            return parameter is OpcUaTagConfig tagConfig && tagConfig.IsActive;
        }

        /// <summary> Voegt een tag (vanuit lijst) toe aan een nieuwe of bestaande plot. </summary>
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
                        RemovePlotTab
                    );
                    newPlotTab.EnsureSeriesExists(tagConfig.TagName, tagConfig.TagName);
                    ActivePlotTabs.Add(newPlotTab);
                    SelectedPlotTab = newPlotTab;
                    _specificLogger.Information(
                        "Nieuwe plot tab geopend voor OPC UA Tag: {TagName} ({NodeId})",
                        tagConfig.TagName,
                        tagConfig.NodeId
                    );
                }
                else
                {
                    _specificLogger.Information(
                        "ExecuteAddTagToPlot: Bestaande plot tab geselecteerd voor primaire NodeId: {NodeId}. Zorgen dat series '{TagName}' bestaat.",
                        tagConfig.NodeId,
                        tagConfig.TagName
                    );
                    SelectedPlotTab = existingPlotTab;
                    SelectedPlotTab.EnsureSeriesExists(tagConfig.TagName, tagConfig.TagName);
                    _specificLogger.Information(
                        "Bestaande plot tab geselecteerd voor OPC UA Tag: {TagName} ({NodeId})",
                        tagConfig.TagName,
                        tagConfig.NodeId
                    );
                }
            }
        }

        /// <summary> Bepaalt of de geselecteerde node/tag aan de huidige plot toegevoegd kan worden. </summary>
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

        /// <summary> Voegt de geselecteerde node/tag toe aan de huidige plot. </summary>
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
                    _specificLogger.Warning(
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
                    _specificLogger.Information(
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

                _specificLogger.Information(
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
                    _specificLogger.Warning(
                        "ExecuteAddSelectedTagToCurrentPlot: Geen actieve plot tab geselecteerd."
                    );
                else if (tagConfigToAdd == null)
                    _specificLogger.Warning(
                        "ExecuteAddSelectedTagToCurrentPlot: Geen tag geselecteerd om toe te voegen."
                    );
                else if (!tagConfigToAdd.IsActive)
                    _specificLogger.Warning(
                        "ExecuteAddSelectedTagToCurrentPlot: Geselecteerde tag '{TagName}' is niet actief.",
                        tagConfigToAdd.TagName
                    );
            }
        }

        /// <summary> Verwijdert de opgegeven plot-tab. </summary>
        private void RemovePlotTab(PlotTabViewModel plotTabToRemove)
        {
            if (plotTabToRemove != null && ActivePlotTabs.Contains(plotTabToRemove))
            {
                (plotTabToRemove as IDisposable).Dispose();
                ActivePlotTabs.Remove(plotTabToRemove);
                _specificLogger.Information(
                    "Plot tab gesloten voor: {Header}",
                    plotTabToRemove.Header
                );
                if (SelectedPlotTab == plotTabToRemove)
                {
                    SelectedPlotTab = ActivePlotTabs.FirstOrDefault();
                }
            }
        }
        #endregion

        #region UI Update and Dispose
        /// <summary>
        /// Werkt de CanExecute status van alle commando's bij.
        /// Moet aangeroepen worden wanneer condities veranderen die de uitvoerbaarheid beïnvloeden.
        /// </summary>
        private void UpdateCommandStates()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ReadAllConfiguredTagsCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (LoadAddressSpaceCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (AddSelectedNodeToMonitoringCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RemoveSelectedNodeFromMonitoringCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (ReadSelectedNodeValueCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (UnmonitorTagFromListCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (OpenNewPlotTabCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (AddTagToPlotCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (AddSelectedTagToCurrentPlotCommand as RelayCommand)?.RaiseCanExecuteChanged();
            });
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Geeft beheerde en onbeheerde resources vrij die door de <see cref="OpcUaTabViewModel"/> worden gebruikt.
        /// </summary>
        /// <param name="disposing">True om zowel beheerde als onbeheerde resources vrij te geven; false om alleen onbeheerde resources vrij te geven.</param>
        private void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _specificLogger.Debug(
                        "Dispose(true) aangeroepen voor OpcUaTabViewModel: {ConnectionName}",
                        DisplayName
                    );

                    // Unsubscribe van service events
                    if (_opcUaService != null)
                    {
                        _opcUaService.ConnectionStatusChanged -= OnOpcUaConnectionStatusChanged;
                        _opcUaService.TagsDataReceived -= OnOpcUaTagsDataReceived;
                        _opcUaService.Dispose(); // Dispose de service
                    }

                    // Ruim plot tabs op (elke PlotTabViewModel is IDisposable)
                    if (
                        Application.Current?.Dispatcher != null
                        && !Application.Current.Dispatcher.CheckAccess()
                    )
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            foreach (var plotTab in ActivePlotTabs.ToList())
                                RemovePlotTab(plotTab);
                            ActivePlotTabs.Clear();
                        });
                    }
                    else
                    {
                        foreach (var plotTab in ActivePlotTabs.ToList())
                            RemovePlotTab(plotTab);
                        ActivePlotTabs.Clear();
                    }
                }
                _disposedValue = true;
            }
        }
        #endregion
    }
}
