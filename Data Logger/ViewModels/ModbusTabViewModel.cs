using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Data_Logger.Core;
using Data_Logger.Enums;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Serilog;

namespace Data_Logger.ViewModels
{
    public class ModbusTabViewModel : TabViewModelBase, IDisposable
    {
        #region Fields
        private readonly ILogger _specificLogger;
        private readonly IModbusService _modbusService;
        private readonly IStatusService _statusService;
        private readonly IDataLoggingService _dataLoggingService;
        private readonly ISettingsService _settingsService;
        private DispatcherTimer _scanTimer;
        private readonly Dictionary<string, TagBaselineState> _tagBaselineStates =
            new Dictionary<string, TagBaselineState>();

        private PlotTabViewModel _selectedPlotTab;
        
        private DispatcherTimer _saveChangesDebounceTimer;
        private const int DebounceTimeMs = 750; 
        #endregion

        #region Properties
        public ModbusTcpConnectionConfig ModbusConfig =>
            ConnectionConfiguration as ModbusTcpConnectionConfig;
        public bool IsConnected => _modbusService?.IsConnected ?? false;
        public ObservableCollection<LoggedTagValue> DataValues { get; } =
            new ObservableCollection<LoggedTagValue>();

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

        public ICommand OpenPlotForTagCommand { get; }
        public ICommand AddSelectedTagToCurrentPlotCommand { get; }
        #endregion

        #region Constructor
        public ModbusTabViewModel(
            ModbusTcpConnectionConfig config,
            ILogger logger,
            IModbusService modbusService,
            IStatusService statusService,
            IDataLoggingService dataLoggingService,
            ISettingsService settingsService 
        )
            : base(config)
        {
            _specificLogger =
                logger
                    ?.ForContext<ModbusTabViewModel>()
                    .ForContext(
                        "ConnectionName",
                        config?.ConnectionName ?? "UnknownModbusConnection"
                    ) ?? throw new ArgumentNullException(nameof(logger));
            _modbusService =
                modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            _statusService =
                statusService ?? throw new ArgumentNullException(nameof(statusService));
            _dataLoggingService =
                dataLoggingService ?? throw new ArgumentNullException(nameof(dataLoggingService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService)); // <<< TOEWIJZEN

            ActivePlotTabs = new ObservableCollection<PlotTabViewModel>();

            ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => !IsConnected);
            DisconnectCommand = new RelayCommand(
                async _ => await DisconnectAsync(),
                _ => IsConnected
            );
            OpenPlotForTagCommand = new RelayCommand(
                ExecuteOpenPlotForTag,
                CanExecuteOpenPlotForTag
            );
            AddSelectedTagToCurrentPlotCommand = new RelayCommand(
                ExecuteAddSelectedTagToCurrentPlot,
                CanExecuteAddSelectedTagToCurrentPlot
            );
            
            _saveChangesDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DebounceTimeMs)
            };
            _saveChangesDebounceTimer.Tick += SaveChangesDebounceTimer_Tick;

            _modbusService.ConnectionStatusChanged += OnModbusConnectionStatusChanged;
            _modbusService.TagsDataReceived += OnModbusTagsDataReceived;

            InitializeScanTimer();
            InitializeBaselineStates();
            _specificLogger.Debug(
                "ModbusTabViewModel geïnitialiseerd voor {ConnectionName}",
                DisplayName
            );
        }
        #endregion
        
        private void SaveChangesDebounceTimer_Tick(object sender, EventArgs e)
        {
            _saveChangesDebounceTimer.Stop();
            _specificLogger.Debug("Debounce timer Tick: daadwerkelijk opslaan en herconfigureren.");
            PersistAndReconfigureModbusService();
        }
        
        public void SaveChangesForModbusConfigAndService()
        {
            _specificLogger.Debug("SaveChangesForModbusConfigAndService aangeroepen. Debounce timer wordt (her)start.");
            _saveChangesDebounceTimer.Stop(); // Herstart de timer bij elke aanroep
            _saveChangesDebounceTimer.Start();
        }
        
        private void PersistAndReconfigureModbusService()
        {
            _specificLogger.Information("ModbusTabViewModel '{DisplayName}': Wijzigingen in ModbusConfig worden opgeslagen en service wordt geherconfigureerd.", DisplayName);

            // De ModbusConfig property verwijst naar het object in SettingsService.CurrentSettings.
            // De wijzigingen in de DataGrid zijn direct in dat object gedaan dankzij TwoWay binding.
            // We hoeven hier dus alleen SettingsService.SaveSettings() aan te roepen.
            _settingsService.SaveSettings(); 
            _specificLogger.Information("Instellingen (incl. Modbus tag wijzigingen) opgeslagen.");

            // Herconfigureer de Modbus service met de (mogelijk gemuteerde) ModbusConfig
            if (_modbusService != null && this.ModbusConfig != null)
            {
                _modbusService.Reconfigure(this.ModbusConfig);
                _specificLogger.Information("ModbusService geherconfigureerd.");
            }

            // Herinitialiseer baseline states (belangrijk als alarm/outlier instellingen zijn gewijzigd)
            InitializeBaselineStates();
            _specificLogger.Debug("Baseline states hergeïnitialiseerd.");
            
            SynchronizeDataValuesWithConfiguration();

            UpdateCommandStates(); // Update CanExecute van commando's
            _specificLogger.Debug("ModbusTabViewModel '{DisplayName}': PersistAndReconfigureModbusService voltooid.");
        }
        
        private void SynchronizeDataValuesWithConfiguration()
        {
            if (ModbusConfig == null || ModbusConfig.TagsToMonitor == null)
            {
                // Als er geen configuratie is, maak DataValues leeg
                Application.Current?.Dispatcher.Invoke(() => DataValues.Clear());
                _specificLogger.Debug("SynchronizeDataValues: ModbusConfig of TagsToMonitor is null, DataValues gewist.");
                return;
            }

            // Haal de namen op van alle actieve, geconfigureerde tags
            var activeConfiguredTagNames = ModbusConfig.TagsToMonitor
                                               .Where(t => t.IsActive)
                                               .Select(t => t.TagName)
                                               .Distinct() // Voor het geval er dubbele namen zouden zijn (idealiter niet)
                                               .ToList();

            _specificLogger.Debug("SynchronizeDataValues: Actieve geconfigureerde TagNames: [{ActiveTags}]", string.Join(", ", activeConfiguredTagNames));

            Application.Current?.Dispatcher.Invoke(() =>
            {
                // 1. Verwijder items uit DataValues waarvan de TagName niet (meer)
                //    voorkomt in de lijst van actieve, geconfigureerde tags.
                var tagsToRemoveFromDataValues = DataValues
                                                   .Where(dv => !activeConfiguredTagNames.Contains(dv.TagName))
                                                   .ToList(); // Maak een kopie om te itereren en te verwijderen

                foreach (var tagToRemove in tagsToRemoveFromDataValues)
                {
                    DataValues.Remove(tagToRemove);
                    _specificLogger.Debug("SynchronizeDataValues: Verwijderd '{TagName}' uit live DataValues (niet meer actief/geconfigureerd).", tagToRemove.TagName);
                }

                // 2. Optioneel: Voeg placeholders toe voor nieuwe actieve tags die nog niet in DataValues staan.
                //    Dit zorgt ervoor dat ze direct zichtbaar zijn in de DataGrid, zelfs voordat de eerste data binnenkomt.
                //    De OnModbusTagsDataReceived methode zal deze placeholders dan updaten.
                foreach (var tagName in activeConfiguredTagNames)
                {
                    if (!DataValues.Any(dv => dv.TagName == tagName))
                    {
                        var placeholder = new LoggedTagValue
                        {
                            TagName = tagName,
                            Timestamp = DateTime.MinValue, // Of DateTime.Now, maar MinValue geeft aan dat het oud is
                            Value = "---",                 // Placeholder
                            IsGoodQuality = false,         // Initieel geen data = geen goede kwaliteit
                            ErrorMessage = "Wacht op data..."
                        };
                        DataValues.Add(placeholder);
                        _specificLogger.Debug("SynchronizeDataValues: Placeholder toegevoegd voor nieuwe actieve tag '{TagName}' in DataValues.", tagName);
                    }
                }
            });
        }

        #region Connection, Timer, Data Handling
        private void InitializeScanTimer()
        {
            _scanTimer = new DispatcherTimer { IsEnabled = false };
            _scanTimer.Interval = TimeSpan.FromSeconds(
                ModbusConfig?.ScanIntervalSeconds > 0 ? ModbusConfig.ScanIntervalSeconds : 5
            );
            _scanTimer.Tick += async (s, e) => await ScanTimer_TickAsync();
        }

        private async Task ScanTimer_TickAsync()
        {
            if (IsConnected && (_modbusService != null))
            {
                await ReadConfiguredTagsAsync();
            }
        }

        private async Task ConnectAsync()
        {
            _statusService.SetStatus(
                ApplicationStatus.Connecting,
                $"Verbinden met Modbus: {ModbusConfig?.ConnectionName}..."
            );
            _specificLogger.Information(
                "Verbindingspoging gestart voor {ConnectionName}...",
                DisplayName
            );
            InitializeBaselineStates();

            bool success = await _modbusService.ConnectAsync();
            if (success)
            {
                _statusService.SetStatus(
                    ApplicationStatus.Logging,
                    $"Verbonden met Modbus: {ModbusConfig?.ConnectionName}."
                );
                _specificLogger.Information(
                    "Verbinding succesvol voor {ConnectionName}.",
                    DisplayName
                );
                SynchronizeDataValuesWithConfiguration(); 
                _scanTimer.Start();
            }
            else
            {
                _statusService.SetStatus(
                    ApplicationStatus.Error,
                    $"Kon niet verbinden met Modbus: {ModbusConfig?.ConnectionName}."
                );
                _specificLogger.Warning("Verbinding mislukt voor {ConnectionName}.", DisplayName);
            }
            UpdateCommandStates();
        }

        private async Task DisconnectAsync()
        {
            _scanTimer.Stop();
            _specificLogger.Information(
                "Verbinding verbreken voor {ConnectionName}...",
                DisplayName
            );
            await _modbusService.DisconnectAsync();
            _statusService.SetStatus(
                ApplicationStatus.Idle,
                $"Modbus verbinding verbroken: {ModbusConfig?.ConnectionName}."
            );
            _specificLogger.Information("Verbinding verbroken voor {ConnectionName}.", DisplayName);
            UpdateCommandStates();
            ClearUIData();

            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var plotTab in ActivePlotTabs.ToList())
                {
                    RemovePlotTab(plotTab);
                }
            });
        }

        private async Task ReadConfiguredTagsAsync()
        {
            if (!IsConnected || _modbusService == null)
                return;
            _specificLogger.Debug(
                "Bezig met pollen van geconfigureerde Modbus tags voor {ConnectionName}",
                ModbusConfig?.ConnectionName
            );
            await _modbusService.PollConfiguredTagsAsync();
        }

        private void OnModbusConnectionStatusChanged(object sender, EventArgs e)
        {
            _specificLogger.Debug(
                "ModbusConnectionStatusChanged. IsConnected: {IsConnected} voor {ConnectionName}",
                _modbusService.IsConnected,
                DisplayName
            );
            OnPropertyChanged(nameof(IsConnected));
            UpdateCommandStates();
            if (!_modbusService.IsConnected)
            {
                _scanTimer.Stop();
                ClearUIData();
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    foreach (var plotTab in ActivePlotTabs.ToList())
                        RemovePlotTab(plotTab);
                });
            }
            else
            {
                if (
                    !_scanTimer.IsEnabled
                    && (ModbusConfig?.TagsToMonitor?.Any(t => t.IsActive) ?? false)
                )
                {
                    ClearUIData();
                    _scanTimer.Start();
                }
            }
        }

        private void OnModbusTagsDataReceived(
            object sender,
            IEnumerable<LoggedTagValue> receivedTagValues
        )
        {
            var tagValuesList = receivedTagValues?.ToList() ?? new List<LoggedTagValue>();
            if (!tagValuesList.Any())
                return;

            _specificLogger.Debug(
                "OnModbusTagsDataReceived: {Count} tag(s) ontvangen voor {ConnectionName}",
                tagValuesList.Count,
                DisplayName
            );

            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var liveValue in tagValuesList)
                {
                    var existingTagInUI = DataValues.FirstOrDefault(t =>
                        t.TagName == liveValue.TagName
                    );
                    if (existingTagInUI != null)
                    {
                        existingTagInUI.Value = liveValue.Value;
                        existingTagInUI.Timestamp = liveValue.Timestamp;
                        existingTagInUI.IsGoodQuality = liveValue.IsGoodQuality;
                        existingTagInUI.ErrorMessage = liveValue.ErrorMessage;
                    }
                    else
                    {
                        DataValues.Add(liveValue);
                        existingTagInUI = liveValue;
                    }

                    var configuredTag = ModbusConfig?.TagsToMonitor.FirstOrDefault(t =>
                        t.TagName == liveValue.TagName
                    );
                    if (configuredTag != null)
                    {
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
                                        _specificLogger.Debug(
                                            "Plot Data Routing (Modbus): TagName='{PlotSeriesKey}', Timestamp='{Ts}', Value={Val} naar plotTab '{PlotHeader}'",
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
                            else
                            {
                                _specificLogger.Warning(
                                    "Plotting (Modbus): Kon waarde '{RawValue}' voor tag '{TagName}' niet naar double converteren.",
                                    liveValue.Value,
                                    configuredTag.TagName
                                );
                            }
                        }
                    }
                }
            });
            _dataLoggingService.LogTagValues(ModbusConfig.ConnectionName, tagValuesList);
        }

        private void ClearUIData()
        {
            Application.Current?.Dispatcher.Invoke(() => DataValues.Clear());
            _specificLogger.Debug("DataValues (live waarden) gewist (bijv. na disconnect).");
        }

        private void UpdateCommandStates()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)OpenPlotForTagCommand)?.RaiseCanExecuteChanged();
                ((RelayCommand)AddSelectedTagToCurrentPlotCommand)?.RaiseCanExecuteChanged();
            });
        }
        #endregion

        #region Plotting Command Implementations
        private bool CanExecuteOpenPlotForTag(object parameter)
        {
            return parameter is ModbusTagConfig tagConfig && tagConfig.IsActive;
        }

        private void ExecuteOpenPlotForTag(object parameter)
        {
            if (parameter is ModbusTagConfig tagConfig && tagConfig.IsActive)
            {
                string plotTabIdentifier = tagConfig.TagName;
                var existingPlotTab = ActivePlotTabs.FirstOrDefault(pt =>
                    pt.TagIdentifier == plotTabIdentifier
                );

                if (existingPlotTab == null)
                {
                    _specificLogger.Information(
                        "ExecuteOpenPlotForTag: Aanmaken nieuwe plot tab voor Modbus TagName: {TagName}",
                        tagConfig.TagName
                    );
                    var newPlotTab = new PlotTabViewModel(
                        plotTabIdentifier,
                        $"Grafiek (Modbus): {tagConfig.TagName}",
                        RemovePlotTab,
                        _specificLogger
                    );
                    newPlotTab.EnsureSeriesExists(tagConfig.TagName, tagConfig.TagName);
                    ActivePlotTabs.Add(newPlotTab);
                    SelectedPlotTab = newPlotTab;
                }
                else
                {
                    SelectedPlotTab = existingPlotTab;
                    SelectedPlotTab.EnsureSeriesExists(tagConfig.TagName, tagConfig.TagName);
                    _specificLogger.Information(
                        "Bestaande plot tab geselecteerd voor Modbus TagName: {TagName}",
                        tagConfig.TagName
                    );
                }
            }
        }

        private bool CanExecuteAddSelectedTagToCurrentPlot(object parameter)
        {
            if (
                parameter is ModbusTagConfig tagConfigToAdd
                && tagConfigToAdd.IsActive
                && SelectedPlotTab != null
            )
            {
                return !SelectedPlotTab.PlotModel.Series.Any(s =>
                    s.Title == tagConfigToAdd.TagName
                );
            }
            return false;
        }

        private void ExecuteAddSelectedTagToCurrentPlot(object parameter)
        {
            if (
                parameter is ModbusTagConfig tagConfigToAdd
                && tagConfigToAdd.IsActive
                && SelectedPlotTab != null
            )
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
                    "ExecuteAddSelectedTagToCurrentPlot: Voegt Modbus tag '{TagName}' als series toe aan plot '{PlotTabTitle}'",
                    tagConfigToAdd.TagName,
                    SelectedPlotTab.Header
                );
                SelectedPlotTab.EnsureSeriesExists(tagConfigToAdd.TagName, tagConfigToAdd.TagName);

                UpdateCommandStates();
            }
            else
            {
                _specificLogger.Warning(
                    "Kon Modbus tag niet toevoegen aan huidige plot: geen plot of tag geselecteerd/actief."
                );
            }
        }

        private void RemovePlotTab(PlotTabViewModel plotTabToRemove)
        {
            if (plotTabToRemove != null && ActivePlotTabs.Contains(plotTabToRemove))
            {
                (plotTabToRemove as IDisposable)?.Dispose();
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

        #region Modbus Specific Alarm/Outlier Logic (Behouden en Aanpassen)
        private void InitializeBaselineStates()
        {
            _tagBaselineStates.Clear();
            if (ModbusConfig?.TagsToMonitor != null)
            {
                foreach (
                    var tagConfig in ModbusConfig.TagsToMonitor.Where(tc =>
                        tc.IsActive && tc.IsOutlierDetectionEnabled
                    )
                )
                {
                    _tagBaselineStates[tagConfig.TagName] = new TagBaselineState(
                        tagConfig.TagName,
                        _specificLogger
                    );
                }
            }
        }

        #endregion

        #region Configuration Update
        public void UpdateConfiguration(ModbusTcpConnectionConfig newConfig)
        {
            _saveChangesDebounceTimer?.Stop();
            
            if (newConfig == null)
            {
                _specificLogger.Error(
                    "UpdateConfiguration aangeroepen met null nieuwe configuratie voor Modbus tab {DisplayName}",
                    DisplayName
                );
                return;
            }

            var oldConfig = this.ModbusConfig;
            if (oldConfig == null)
            {
                _specificLogger.Error(
                    "UpdateConfiguration: Huidige ModbusConfig is null voor {DisplayName}, kan niet updaten.",
                    DisplayName
                );

                ConnectionConfiguration = newConfig;
                OnPropertyChanged(nameof(ModbusConfig));
                if (DisplayName != newConfig.ConnectionName)
                    DisplayName = newConfig.ConnectionName;
                _modbusService.Reconfigure(newConfig);
                return;
            }

            _specificLogger.Information(
                "ModbusTabViewModel '{DisplayName}': Configuratie wordt bijgewerkt naar nieuwe naam '{NewName}'.",
                DisplayName,
                newConfig.ConnectionName
            );

            ConnectionConfiguration = newConfig;

            OnPropertyChanged(nameof(ModbusConfig));

            if (DisplayName != newConfig.ConnectionName)
            {
                DisplayName = newConfig.ConnectionName;
                _specificLogger.ForContext("ConnectionName", DisplayName);
            }

            _modbusService.Reconfigure(newConfig);

            if (
                _scanTimer != null
                && oldConfig.ScanIntervalSeconds != newConfig.ScanIntervalSeconds
            )
            {
                _specificLogger.Information(
                    "Scan interval voor Modbus tab '{DisplayName}' gewijzigd van {OldInterval}s naar {NewInterval}s.",
                    DisplayName,
                    oldConfig.ScanIntervalSeconds,
                    newConfig.ScanIntervalSeconds
                );
                bool restartTimer = _scanTimer.IsEnabled;
                _scanTimer.Stop();
                _scanTimer.Interval = TimeSpan.FromSeconds(
                    newConfig.ScanIntervalSeconds > 0 ? newConfig.ScanIntervalSeconds : 5
                );
                if (restartTimer && IsConnected)
                {
                    _scanTimer.Start();
                }
            }

            bool tagsChangedSignificantly =
                oldConfig.TagsToMonitor.Count != newConfig.TagsToMonitor.Count
                || !oldConfig
                    .TagsToMonitor.Select(t => t.TagName)
                    .SequenceEqual(newConfig.TagsToMonitor.Select(t => t.TagName))
                || !oldConfig
                    .TagsToMonitor.Select(t => t.Address)
                    .SequenceEqual(newConfig.TagsToMonitor.Select(t => t.Address))
                || !oldConfig
                    .TagsToMonitor.Select(t => t.RegisterType)
                    .SequenceEqual(newConfig.TagsToMonitor.Select(t => t.RegisterType))
                || !oldConfig
                    .TagsToMonitor.Select(t => t.DataType)
                    .SequenceEqual(newConfig.TagsToMonitor.Select(t => t.DataType))
                || oldConfig.TagsToMonitor.Any(ot =>
                {
                    var nt = newConfig.TagsToMonitor.FirstOrDefault(n => n.TagName == ot.TagName);
                    return nt == null
                        || ot.IsOutlierDetectionEnabled != nt.IsOutlierDetectionEnabled
                        || ot.BaselineSampleSize != nt.BaselineSampleSize
                        || ot.OutlierStandardDeviationFactor != nt.OutlierStandardDeviationFactor;
                });

            if (tagsChangedSignificantly)
            {
                _specificLogger.Information(
                    "Modbus tag configuratie significant gewijzigd voor '{DisplayName}', herinitialiseren baseline states.",
                    DisplayName
                );
                InitializeBaselineStates();
                ClearUIData();
            }
            
            SynchronizeDataValuesWithConfiguration();

            Application.Current?.Dispatcher.Invoke(() =>
            {
                var activeNewTagNames = newConfig
                    .TagsToMonitor.Where(t => t.IsActive)
                    .Select(t => t.TagName)
                    .ToList();
                foreach (var plotTab in ActivePlotTabs.ToList())
                {
                    var seriesToRemoveFromPlot = plotTab
                        .PlottedSeriesInfos.Where(psi => !activeNewTagNames.Contains(psi.SeriesKey))
                        .ToList();

                    foreach (var seriesInfo in seriesToRemoveFromPlot)
                    {
                        plotTab.ExecuteRemoveSeriesFromPlot(seriesInfo);
                    }

                    foreach (var seriesInfo in plotTab.PlottedSeriesInfos.ToList())
                    {
                        if (activeNewTagNames.Contains(seriesInfo.SeriesKey))
                        {
                            plotTab.EnsureSeriesExists(seriesInfo.SeriesKey, seriesInfo.SeriesKey);
                        }
                    }
                }
            });

            UpdateCommandStates();
            _specificLogger.Debug(
                "ModbusTabViewModel '{DisplayName}' configuratie update voltooid.",
                DisplayName
            );
        }
        #endregion

        #region Helper Methods
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
            if (value is short s_val)
            {
                result = s_val;
                return true;
            }
            if (value is ushort us_val)
            {
                result = us_val;
                return true;
            }
            if (value is int i_val)
            {
                result = i_val;
                return true;
            }
            if (value is uint ui_val)
            {
                result = ui_val;
                return true;
            }
            if (value is long l_val)
            {
                result = l_val;
                return true;
            }
            if (value is ulong ul_val)
            {
                result = ul_val;
                return true;
            }
            if (value is byte b_val)
            {
                result = b_val;
                return true;
            }
            if (value is bool bool_val)
            {
                result = bool_val ? 1.0 : 0.0;
                return true;
            }

            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (InvalidCastException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
        #endregion

        #region IDisposable
        private bool _disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _specificLogger.Debug(
                        "Dispose(true) aangeroepen voor ModbusTabViewModel: {ConnectionName}",
                        DisplayName
                    );
                    _scanTimer?.Stop();
                    _scanTimer = null;
                    if (_modbusService != null)
                    {
                        _modbusService.ConnectionStatusChanged -= OnModbusConnectionStatusChanged;
                        _modbusService.TagsDataReceived -= OnModbusTagsDataReceived;
                        _modbusService.Dispose();
                    }

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        foreach (var plotTab in ActivePlotTabs.ToList())
                        {
                            RemovePlotTab(plotTab);
                        }
                        ActivePlotTabs.Clear();
                    });
                    
                    _saveChangesDebounceTimer?.Stop();
                    if (_saveChangesDebounceTimer != null)
                    {
                        _saveChangesDebounceTimer.Tick -= SaveChangesDebounceTimer_Tick;
                    }
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
