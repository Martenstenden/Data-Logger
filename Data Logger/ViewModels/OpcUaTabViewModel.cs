using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Data_Logger;
using Data_Logger.Core;
using Data_Logger.Enums;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
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
        #endregion

        #region Properties
        public OpcUaConnectionConfig OpcUaConfig =>
            ConnectionConfiguration as OpcUaConnectionConfig;
        public bool IsConnected => _opcUaService?.IsConnected ?? false;

        public ObservableCollection<OpcUaNodeViewModel> RootNodes { get; }
        public ObservableCollection<NodeAttributeViewModel> SelectedNodeAttributes { get; }
        public ObservableCollection<ReferenceDescriptionViewModel> SelectedNodeReferences { get; }

        public bool IsBrowseAddressSpace
        {
            get => _isBrowseAddressSpace;
            set
            {
                if (SetProperty(ref _isBrowseAddressSpace, value))
                    UpdateCommandStates();
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
                    _logger.Debug(
                        "Geselecteerde OPC UA Node in TreeView: {DisplayName}",
                        _selectedOpcUaNodeInTree?.DisplayName ?? "null"
                    );
                    UpdateCommandStates();
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

        public ObservableCollection<PlotTabViewModel> ActivePlotTabs { get; }
        public PlotTabViewModel SelectedPlotTab
        {
            get => _selectedPlotTab;
            set => SetProperty(ref _selectedPlotTab, value);
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
        #endregion

        #region Constructor
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
            _logger =
                logger
                    ?.ForContext<OpcUaTabViewModel>()
                    .ForContext(
                        "ConnectionName",
                        config?.ConnectionName ?? "UnknownOpcUaConnection"
                    ) ?? throw new ArgumentNullException(nameof(logger));
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

            if (OpcUaConfig != null && OpcUaConfig.TagsToMonitor == null)
            {
                OpcUaConfig.TagsToMonitor = new ObservableCollection<OpcUaTagConfig>();
            }

            ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => !IsConnected);
            DisconnectCommand = new RelayCommand(
                async _ => await DisconnectAsync(),
                _ => IsConnected
            );
            ReadAllConfiguredTagsCommand = new RelayCommand(
                async _ => await ReadAllConfiguredTagsAsync(),
                _ => IsConnected
            );
            LoadAddressSpaceCommand = new RelayCommand(
                async _ => await LoadInitialAddressSpaceAsync(),
                _ => IsConnected && !IsBrowseAddressSpace
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
                    await ReadNodeValueAsync(
                        param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree
                    ),
                param => CanReadNodeValue(param as OpcUaNodeViewModel ?? SelectedOpcUaNodeInTree)
            );
            UnmonitorTagFromListCommand = new RelayCommand(
                param => UnmonitorTag(param as OpcUaTagConfig),
                param => param is OpcUaTagConfig
            );

            AddTagToPlotCommand = new RelayCommand(
                ExecuteAddTagToPlot,
                CanExecuteAddTagToPlotParameter
            );
            OpenNewPlotTabCommand = new RelayCommand(
                ExecuteOpenNewPlotTabForSelected,
                CanExecuteOpenNewPlotTabForSelected
            );

            AddSelectedTagToCurrentPlotCommand = new RelayCommand(
                ExecuteAddSelectedTagToCurrentPlot,
                CanExecuteAddSelectedTagToCurrentPlot
            );

            _opcUaService.ConnectionStatusChanged += OnOpcUaConnectionStatusChanged;
            _opcUaService.TagsDataReceived += OnOpcUaTagsDataReceived;

            _logger.Debug("OpcUaTabViewModel geïnitialiseerd voor {ConnectionName}", DisplayName);
        }
        #endregion

        #region Connection and Data Handling
        private async Task ConnectAsync()
        {
            _statusService.SetStatus(
                ApplicationStatus.Connecting,
                $"Verbinden met OPC UA: {OpcUaConfig?.ConnectionName}..."
            );
            _logger.Information("Verbindingspoging gestart voor {ConnectionName}...", DisplayName);
            ResetAllTagBaselinesAndAlarms();
            bool success = await _opcUaService.ConnectAsync();
            if (success)
            {
                _statusService.SetStatus(
                    ApplicationStatus.Logging,
                    $"Verbonden met OPC UA: {OpcUaConfig?.ConnectionName}."
                );
                _logger.Information("Verbinding succesvol voor {ConnectionName}.", DisplayName);
                await LoadInitialAddressSpaceAsync();
                await _opcUaService.StartMonitoringTagsAsync();
            }
            else
            {
                _statusService.SetStatus(
                    ApplicationStatus.Error,
                    $"Kon niet verbinden met OPC UA: {OpcUaConfig?.ConnectionName}."
                );
                _logger.Warning("Verbinding mislukt voor {ConnectionName}.", DisplayName);
            }
            UpdateCommandStates();
        }

        private async Task DisconnectAsync()
        {
            _logger.Information("Verbinding verbreken voor {ConnectionName}...", DisplayName);
            await _opcUaService.StopMonitoringTagsAsync();
            await _opcUaService.DisconnectAsync();
            _statusService.SetStatus(
                ApplicationStatus.Idle,
                $"OPC UA verbinding verbroken: {OpcUaConfig?.ConnectionName}."
            );
            _logger.Information("Verbinding verbroken voor {ConnectionName}.", DisplayName);
            Application.Current?.Dispatcher.Invoke(() =>
            {
                RootNodes.Clear();
                SelectedNodeAttributes.Clear();
                SelectedNodeReferences.Clear();
                ActivePlotTabs.Clear();
                SelectedPlotTab = null;
            });
            UpdateCommandStates();
        }

        private void OnOpcUaConnectionStatusChanged(object sender, EventArgs e)
        {
            _logger.Debug(
                "OpcUaConnectionStatusChanged. IsConnected: {IsConnected} voor {ConnectionName}",
                _opcUaService.IsConnected,
                DisplayName
            );
            OnPropertyChanged(nameof(IsConnected));
            UpdateCommandStates();

            if (!_opcUaService.IsConnected)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    RootNodes.Clear();
                    SelectedNodeAttributes.Clear();
                    SelectedNodeReferences.Clear();
                    ActivePlotTabs.Clear();
                    SelectedPlotTab = null;
                });
            }
            else
            {
                ResetAllTagBaselinesAndAlarms();
                if (!RootNodes.Any() && !IsBrowseAddressSpace)
                {
                    Task.Run(async () => await LoadInitialAddressSpaceAsync());
                }
            }
        }

        private void OnOpcUaTagsDataReceived(
            object sender,
            IEnumerable<LoggedTagValue> receivedTagValues
        )
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
            if (!IsConnected || _opcUaService == null || IsBrowseAddressSpace)
                return;
            IsBrowseAddressSpace = true;
            _logger.Information(
                "Laden van initiële OPC UA address space voor {ConnectionName}",
                DisplayName
            );
            Application.Current?.Dispatcher.Invoke(() => RootNodes.Clear());
            try
            {
                ReferenceDescriptionCollection rootItems = await _opcUaService.BrowseRootAsync();
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
                            _logger.Error(
                                ex,
                                "Fout bij converteren root ExpandedNodeId {ExpNodeId} voor {ItemDisplayName}",
                                item.NodeId,
                                item.DisplayName?.Text
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
                                        _logger,
                                        hasChildren
                                    )
                                )
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout bij het laden van de initiële address space voor {ConnectionName}",
                    DisplayName
                );
                _statusService.SetStatus(
                    ApplicationStatus.Error,
                    $"Fout bij laden address space: {ex.Message}"
                );
            }
            finally
            {
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
                    _logger.Warning("ExecuteAddSelectedTagToCurrentPlot: Geselecteerde node '{SelectedNode}' is geen actieve, gemonitorde tag.", SelectedOpcUaNodeInTree.DisplayName);
                    MessageBox.Show($"Node '{SelectedOpcUaNodeInTree.DisplayName}' wordt niet actief gemonitord. Voeg het eerst toe aan monitoring.", "Tag niet gemonitord", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
                
            

            if (SelectedPlotTab != null && tagConfigToAdd != null && tagConfigToAdd.IsActive)
            {
                if (SelectedPlotTab.PlotModel.Series.Any(s => s.Title == tagConfigToAdd.TagName))
                {
                    _logger.Information("Tag '{TagName}' is al aanwezig in de huidige plot tab '{PlotTabTitle}'.", tagConfigToAdd.TagName, SelectedPlotTab.Header);
                    MessageBox.Show($"Tag '{tagConfigToAdd.TagName}' is al aanwezig in deze grafiek.", "Tag al geplot", MessageBoxButton.OK, MessageBoxImage.Information);
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
                    _logger.Warning("ExecuteAddSelectedTagToCurrentPlot: Geen actieve plot tab geselecteerd.");
                else if (tagConfigToAdd == null)
                    _logger.Warning("ExecuteAddSelectedTagToCurrentPlot: Geen tag geselecteerd om toe te voegen.");
                else if (!tagConfigToAdd.IsActive)
                    _logger.Warning("ExecuteAddSelectedTagToCurrentPlot: Geselecteerde tag '{TagName}' is niet actief.",
                        tagConfigToAdd.TagName);
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
                    _logger.Debug(
                        "Dispose(true) aangeroepen voor OpcUaTabViewModel: {ConnectionName}",
                        DisplayName
                    );
                    if (_opcUaService != null)
                    {
                        _opcUaService.ConnectionStatusChanged -= OnOpcUaConnectionStatusChanged;
                        _opcUaService.TagsDataReceived -= OnOpcUaTagsDataReceived;
                        _opcUaService.Dispose();
                    }
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        RootNodes?.Clear();
                        SelectedNodeAttributes?.Clear();
                        SelectedNodeReferences?.Clear();
                        ActivePlotTabs?.Clear();
                    });
                }
                _disposedValue = true;
            }
        }
        #endregion
    }
}
