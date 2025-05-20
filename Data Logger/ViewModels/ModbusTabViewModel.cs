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
    /// <summary>
    /// ViewModel voor een tabblad dat een Modbus TCP connectie representeert.
    /// Beheert de connectiestatus, tag-configuratie, live data, plotting en commando's gerelateerd aan deze verbinding.
    /// Implementeert <see cref="IDisposable"/> voor het correct vrijgeven van resources.
    /// </summary>
    public class ModbusTabViewModel : TabViewModelBase, IDisposable
    {
        #region Readonly Fields
        private ILogger _specificLogger; // Logger specifiek voor deze ViewModel instantie
        private readonly IModbusService _modbusService;
        private readonly IStatusService _statusService;
        private readonly IDataLoggingService _dataLoggingService;
        private readonly ISettingsService _settingsService;
        private readonly DispatcherTimer _scanTimer;
        private readonly DispatcherTimer _saveChangesDebounceTimer; // Timer voor het debouncen van opslagacties
        private readonly Dictionary<string, TagBaselineState> _tagBaselineStates =
            new Dictionary<string, TagBaselineState>();
        #endregion

        #region Fields
        private PlotTabViewModel _selectedPlotTab;
        private bool _disposedValue; // Voor IDisposable patroon
        private const int DebounceTimeMs = 750; // Debounce tijd in milliseconden voor opslaan
        #endregion

        #region Properties
        /// <summary>
        /// Haalt de sterk getypeerde Modbus TCP connectieconfiguratie op.
        /// </summary>
        public ModbusTcpConnectionConfig ModbusConfig =>
            ConnectionConfiguration as ModbusTcpConnectionConfig;

        /// <summary>
        /// Haalt een waarde op die aangeeft of de Modbus service momenteel verbonden is.
        /// </summary>
        public bool IsConnected => _modbusService?.IsConnected ?? false;

        /// <summary>
        /// Haalt een observeerbare collectie op van de laatst gelogde waarden voor de tags van deze verbinding.
        /// </summary>
        public ObservableCollection<LoggedTagValue> DataValues { get; } =
            new ObservableCollection<LoggedTagValue>();

        /// <summary>
        /// Haalt een observeerbare collectie op van actieve plot-tabbladen die geassocieerd zijn met deze Modbus-verbinding.
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
        /// <summary>
        /// Commando om een verbinding met de Modbus server op te zetten.
        /// </summary>
        public ICommand ConnectCommand { get; }

        /// <summary>
        /// Commando om de verbinding met de Modbus server te verbreken.
        /// </summary>
        public ICommand DisconnectCommand { get; }

        /// <summary>
        /// Commando om een nieuwe, aparte plot te openen voor een geselecteerde Modbus-tag.
        /// </summary>
        public ICommand OpenPlotForTagCommand { get; }

        /// <summary>
        /// Commando om een geselecteerde Modbus-tag toe te voegen aan de huidige actieve plot-tab.
        /// </summary>
        public ICommand AddSelectedTagToCurrentPlotCommand { get; }
        #endregion

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="ModbusTabViewModel"/> klasse.
        /// </summary>
        /// <param name="config">De Modbus TCP connectieconfiguratie.</param>
        /// <param name="logger">De globale Serilog logger instantie.</param>
        /// <param name="modbusService">De Modbus service instantie voor deze verbinding.</param>
        /// <param name="statusService">De service voor het beheren van de applicatiestatus.</param>
        /// <param name="dataLoggingService">De service voor het loggen van data.</param>
        /// <param name="settingsService">De service voor het beheren van instellingen.</param>
        /// <exception cref="ArgumentNullException">Als een van de vereiste parameters null is.</exception>
        public ModbusTabViewModel(
            ModbusTcpConnectionConfig config,
            ILogger logger,
            IModbusService modbusService,
            IStatusService statusService,
            IDataLoggingService dataLoggingService,
            ISettingsService settingsService
        )
            : base(config) // Initialiseert DisplayName en ConnectionConfiguration in base
        {
            _specificLogger =
                logger
                    ?.ForContext<ModbusTabViewModel>()
                    .ForContext("ConnectionName", config?.ConnectionName ?? "UnknownModbus")
                ?? throw new ArgumentNullException(nameof(logger));
            _modbusService =
                modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            _statusService =
                statusService ?? throw new ArgumentNullException(nameof(statusService));
            _dataLoggingService =
                dataLoggingService ?? throw new ArgumentNullException(nameof(dataLoggingService));
            _settingsService =
                settingsService ?? throw new ArgumentNullException(nameof(settingsService));

            ActivePlotTabs = new ObservableCollection<PlotTabViewModel>();

            ConnectCommand = new RelayCommand(
                async _ => await ConnectAsync().ConfigureAwait(false),
                _ => !IsConnected
            );
            DisconnectCommand = new RelayCommand(
                async _ => await DisconnectAsync().ConfigureAwait(false),
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
                Interval = TimeSpan.FromMilliseconds(DebounceTimeMs),
            };
            _saveChangesDebounceTimer.Tick += SaveChangesDebounceTimer_Tick;

            _scanTimer = new DispatcherTimer { IsEnabled = false };
            _scanTimer.Tick += async (s, e) => await ScanTimer_TickAsync().ConfigureAwait(false);
            UpdateScanTimerInterval(); // Stel initieel interval in

            _modbusService.ConnectionStatusChanged += OnModbusConnectionStatusChanged;
            _modbusService.TagsDataReceived += OnModbusTagsDataReceived;

            InitializeBaselineStates(); // Initialiseer baseline states voor outlier detectie
            SynchronizeDataValuesWithConfiguration(); // Zorg dat UI overeenkomt met config

            _specificLogger.Debug(
                "ModbusTabViewModel geïnitialiseerd voor {ConnectionName}",
                DisplayName
            );
        }

        #region Debounce Logic for Saving Changes
        /// <summary>
        /// Wordt aangeroepen wanneer de debounce timer afloopt, om wijzigingen daadwerkelijk op te slaan.
        /// </summary>
        private void SaveChangesDebounceTimer_Tick(object sender, EventArgs e)
        {
            _saveChangesDebounceTimer.Stop();
            _specificLogger.Debug(
                "Debounce timer Tick: daadwerkelijk opslaan en herconfigureren voor {ConnectionName}.",
                DisplayName
            );
            PersistAndReconfigureModbusService();
        }

        /// <summary>
        /// Start of herstart de debounce timer voor het opslaan van wijzigingen in de Modbus-configuratie.
        /// Dit voorkomt dat bij elke kleine wijziging direct wordt opgeslagen.
        /// </summary>
        public void SaveChangesForModbusConfigAndService()
        {
            _specificLogger.Debug(
                "SaveChangesForModbusConfigAndService aangeroepen voor {ConnectionName}. Debounce timer wordt (her)start.",
                DisplayName
            );
            _saveChangesDebounceTimer.Stop(); // Herstart de timer bij elke aanroep
            _saveChangesDebounceTimer.Start();
        }

        /// <summary>
        /// Slaat de huidige (werk)configuratie op via de <see cref="ISettingsService"/>
        /// en herconfigureert de <see cref="IModbusService"/> met de bijgewerkte configuratie.
        /// Herinitialiseert ook baseline states en synchroniseert UI data.
        /// </summary>
        private void PersistAndReconfigureModbusService()
        {
            _specificLogger.Information(
                "ModbusTabViewModel '{DisplayName}': Wijzigingen in ModbusConfig worden opgeslagen en service wordt geherconfigureerd.",
                DisplayName
            );

            _settingsService.SaveSettings();
            _specificLogger.Information(
                "Instellingen (incl. Modbus tag wijzigingen) opgeslagen voor {ConnectionName}.",
                DisplayName
            );

            // Herconfigureer de Modbus service met de mogelijk gewijzigde ModbusConfig
            if (ModbusConfig != null) // Null check voor ModbusConfig
            {
                _modbusService.Reconfigure(ModbusConfig);
                UpdateScanTimerInterval(); // Pas scan interval aan indien gewijzigd
                _specificLogger.Information(
                    "ModbusService geherconfigureerd voor {ConnectionName}.",
                    DisplayName
                );
            }

            InitializeBaselineStates(); // Herinitialiseer baseline states
            _specificLogger.Debug(
                "Baseline states hergeïnitialiseerd voor {ConnectionName}.",
                DisplayName
            );

            SynchronizeDataValuesWithConfiguration(); // Zorg dat de UI lijst van tags overeenkomt
            UpdateCommandStates(); // Update CanExecute van commando's
            _specificLogger.Debug(
                "ModbusTabViewModel '{DisplayName}': PersistAndReconfigureModbusService voltooid.",
                DisplayName
            );
        }
        #endregion

        #region Connection, Timer, Data Handling
        /// <summary>
        /// Synchroniseert de <see cref="DataValues"/> collectie (voor UI weergave)
        /// met de actieve tags in de <see cref="ModbusConfig"/>.
        /// Verwijdert tags die niet meer actief zijn en voegt placeholders toe voor nieuwe actieve tags.
        /// </summary>
        private void SynchronizeDataValuesWithConfiguration()
        {
            if (ModbusConfig?.TagsToMonitor == null)
            {
                Application.Current?.Dispatcher.Invoke(() => DataValues.Clear());
                _specificLogger.Debug(
                    "SynchronizeDataValues: ModbusConfig of TagsToMonitor is null voor {ConnectionName}, DataValues gewist.",
                    DisplayName
                );
                return;
            }

            var activeConfiguredTagNames = ModbusConfig
                .TagsToMonitor.Where(t => t.IsActive)
                .Select(t => t.TagName)
                .Distinct()
                .ToList();
            _specificLogger.Debug(
                "SynchronizeDataValues voor {ConnectionName}: Actieve geconfigureerde TagNames: [{ActiveTags}]",
                DisplayName,
                string.Join(", ", activeConfiguredTagNames)
            );

            Application.Current?.Dispatcher.Invoke(() =>
            {
                // Verwijder items uit DataValues die niet (meer) in de actieve configuratie staan.
                var tagsToRemoveFromDataValues = DataValues
                    .Where(dv => !activeConfiguredTagNames.Contains(dv.TagName))
                    .ToList();
                foreach (var tagToRemove in tagsToRemoveFromDataValues)
                {
                    DataValues.Remove(tagToRemove);
                    _specificLogger.Debug(
                        "SynchronizeDataValues: Verwijderd '{TagName}' uit live DataValues (niet meer actief/geconfigureerd) voor {ConnectionName}.",
                        tagToRemove.TagName,
                        DisplayName
                    );
                }

                // Voeg placeholders toe voor nieuwe actieve tags die nog niet in DataValues staan.
                foreach (var tagName in activeConfiguredTagNames)
                {
                    if (DataValues.All(dv => dv.TagName != tagName))
                    {
                        var placeholder = new LoggedTagValue
                        {
                            TagName = tagName,
                            Timestamp = DateTime.MinValue, // Indicatie van geen recente data
                            Value = "---",
                            IsGoodQuality = false,
                            ErrorMessage = "Wacht op data...",
                        };
                        DataValues.Add(placeholder);
                        _specificLogger.Debug(
                            "SynchronizeDataValues: Placeholder toegevoegd voor nieuwe actieve tag '{TagName}' in DataValues voor {ConnectionName}.",
                            tagName,
                            DisplayName
                        );
                    }
                }
            });
        }

        /// <summary>
        /// Stelt het interval van de scan timer in op basis van de huidige configuratie.
        /// </summary>
        private void UpdateScanTimerInterval()
        {
            if (_scanTimer != null)
            {
                _scanTimer.Interval = TimeSpan.FromSeconds(
                    ModbusConfig?.ScanIntervalSeconds > 0 ? ModbusConfig.ScanIntervalSeconds : 5
                );
                _specificLogger.Debug(
                    "Scan timer interval bijgewerkt naar {Interval}s voor {ConnectionName}.",
                    _scanTimer.Interval.TotalSeconds,
                    DisplayName
                );
            }
        }

        /// <summary>
        /// Wordt aangeroepen door de scan timer om de geconfigureerde tags te pollen.
        /// </summary>
        private async Task ScanTimer_TickAsync()
        {
            if (IsConnected && _modbusService != null)
            {
                await ReadConfiguredTagsAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Start de verbinding met de Modbus server en start de scan timer bij succes.
        /// </summary>
        private async Task ConnectAsync()
        {
            _statusService.SetStatus(
                ApplicationStatus.Connecting,
                $"Verbinden met Modbus: {DisplayName}..."
            );
            _specificLogger.Information(
                "Verbindingspoging gestart voor {ConnectionName}...",
                DisplayName
            );
            InitializeBaselineStates(); // Reset baselines bij (her)verbinden

            bool success = await _modbusService.ConnectAsync().ConfigureAwait(false);
            if (success)
            {
                _statusService.SetStatus(
                    ApplicationStatus.Logging,
                    $"Verbonden met Modbus: {DisplayName}."
                );
                _specificLogger.Information(
                    "Verbinding succesvol voor {ConnectionName}.",
                    DisplayName
                );
                SynchronizeDataValuesWithConfiguration();
                _scanTimer.Start(); // Start de timer pas na succesvolle verbinding
            }
            else
            {
                _statusService.SetStatus(
                    ApplicationStatus.Error,
                    $"Kon niet verbinden met Modbus: {DisplayName}."
                );
                _specificLogger.Warning("Verbinding mislukt voor {ConnectionName}.", DisplayName);
            }
            UpdateCommandStates();
        }

        /// <summary>
        /// Verbreekt de verbinding met de Modbus server en stopt de scan timer.
        /// </summary>
        private async Task DisconnectAsync()
        {
            _scanTimer.Stop();
            _specificLogger.Information(
                "Verbinding verbreken voor {ConnectionName}...",
                DisplayName
            );
            await _modbusService.DisconnectAsync().ConfigureAwait(false);
            _statusService.SetStatus(
                ApplicationStatus.Idle,
                $"Modbus verbinding verbroken: {DisplayName}."
            );
            _specificLogger.Information("Verbinding verbroken voor {ConnectionName}.", DisplayName);
            UpdateCommandStates();
            ClearUIData();

            // Sluit alle openstaande plot tabs voor deze verbinding
            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var plotTab in ActivePlotTabs.ToList()) // ToList() voor veilige iteratie
                {
                    RemovePlotTab(plotTab);
                }
            });
        }

        /// <summary>
        /// Roept de Modbus service aan om de geconfigureerde tags te pollen.
        /// </summary>
        private async Task ReadConfiguredTagsAsync()
        {
            if (!IsConnected || _modbusService == null)
                return;
            _specificLogger.Verbose(
                "Bezig met pollen van geconfigureerde Modbus tags voor {ConnectionName}",
                DisplayName
            ); // Verbose voor frequente actie
            await _modbusService.PollConfiguredTagsAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Event handler voor wijzigingen in de connectiestatus van de Modbus service.
        /// </summary>
        private void OnModbusConnectionStatusChanged(object sender, EventArgs e)
        {
            _specificLogger.Debug(
                "ModbusConnectionStatusChanged. IsConnected: {IsConnected} voor {ConnectionName}",
                _modbusService.IsConnected,
                DisplayName
            );
            OnPropertyChanged(nameof(IsConnected)); // Update UI
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
                // Als verbonden en timer niet loopt, maar er zijn actieve tags, start timer.
                if (
                    !_scanTimer.IsEnabled
                    && (ModbusConfig?.TagsToMonitor?.Any(t => t.IsActive) ?? false)
                )
                {
                    ClearUIData(); // Begin met schone lei voor data na herverbinding
                    SynchronizeDataValuesWithConfiguration(); // Zorg dat placeholders er zijn
                    _scanTimer.Start();
                    _specificLogger.Information(
                        "Scan timer gestart na (her)verbinding voor {ConnectionName}.",
                        DisplayName
                    );
                }
            }
        }

        /// <summary>
        /// Event handler voor ontvangen tag-data van de Modbus service.
        /// Werkt de <see cref="DataValues"/> collectie bij en stuurt data door naar actieve plots.
        /// </summary>
        private void OnModbusTagsDataReceived(
            object sender,
            IEnumerable<LoggedTagValue> receivedTagValues
        )
        {
            var tagValuesList = receivedTagValues?.ToList() ?? new List<LoggedTagValue>();
            if (!tagValuesList.Any())
                return;

            _specificLogger.Verbose(
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
                    else // Tag was nog niet in de UI lijst, voeg toe (bijv. na config wijziging)
                    {
                        DataValues.Add(liveValue);
                        existingTagInUI = liveValue; // Gebruik de nieuw toegevoegde voor alarm check
                    }

                    var configuredTag = ModbusConfig?.TagsToMonitor.FirstOrDefault(t =>
                        t.TagName == liveValue.TagName
                    );
                    if (configuredTag != null)
                    {
                        if (
                            liveValue.IsGoodQuality
                            && TryConvertToDouble(liveValue.Value, out double numericValue)
                        )
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
                                        "Plot Data Routing (Modbus): TagName='{PlotSeriesKey}', Timestamp='{Ts}', Value={Val} naar plotTab '{PlotHeader}' voor {ConnectionName}",
                                        configuredTag.TagName,
                                        liveValue.Timestamp,
                                        numericValue,
                                        plotTab.Header,
                                        DisplayName
                                    );
                                    plotTab.AddDataPoint(
                                        liveValue.Timestamp,
                                        numericValue,
                                        configuredTag.TagName
                                    );
                                }
                            }
                        }
                        else if (!liveValue.IsGoodQuality)
                        {
                            _specificLogger.Warning(
                                "Plotting (Modbus): Slechte kwaliteit data voor tag '{TagName}' ({ConnectionName}). Waarde: '{RawValue}', Error: '{Error}'. Geen punt toegevoegd.",
                                configuredTag.TagName,
                                DisplayName,
                                liveValue.Value,
                                liveValue.ErrorMessage
                            );
                        }
                        else // Kon niet converteren naar double
                        {
                            _specificLogger.Warning(
                                "Plotting (Modbus): Kon waarde '{RawValue}' voor tag '{TagName}' ({ConnectionName}) niet naar double converteren. Geen punt toegevoegd.",
                                liveValue.Value,
                                configuredTag.TagName,
                                DisplayName
                            );
                        }
                    }
                }
            });
            // Log data naar CSV bestand
            if (ModbusConfig != null) // Zorg dat config bestaat
            {
                _dataLoggingService.LogTagValues(ModbusConfig.ConnectionName, tagValuesList);
            }
        }

        /// <summary>
        /// Wist de live data waarden uit de UI.
        /// </summary>
        private void ClearUIData()
        {
            Application.Current?.Dispatcher.Invoke(() => DataValues.Clear());
            _specificLogger.Debug(
                "DataValues (live waarden) gewist voor {ConnectionName} (bijv. na disconnect).",
                DisplayName
            );
        }

        /// <summary>
        /// Werkt de CanExecute status van de commando's bij.
        /// </summary>
        private void UpdateCommandStates()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (OpenPlotForTagCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (AddSelectedTagToCurrentPlotCommand as RelayCommand)?.RaiseCanExecuteChanged();
            });
        }
        #endregion

        #region Plotting Command Implementations
        /// <summary>
        /// Bepaalt of het <see cref="OpenPlotForTagCommand"/> uitgevoerd kan worden.
        /// </summary>
        private bool CanExecuteOpenPlotForTag(object parameter)
        {
            // Kan alleen uitgevoerd worden als een ModbusTagConfig is meegegeven en deze actief is.
            return parameter is ModbusTagConfig tagConfig && tagConfig.IsActive;
        }

        /// <summary>
        /// Voert het <see cref="OpenPlotForTagCommand"/> uit: opent een nieuwe plot-tab voor de gegeven tag,
        /// of selecteert een bestaande plot-tab als deze al open is voor die tag.
        /// </summary>
        private void ExecuteOpenPlotForTag(object parameter)
        {
            if (parameter is ModbusTagConfig tagConfig && tagConfig.IsActive)
            {
                string plotTabIdentifier = tagConfig.TagName; // Gebruik TagName als unieke identifier voor de plot-tab
                var existingPlotTab = ActivePlotTabs.FirstOrDefault(pt =>
                    pt.TagIdentifier == plotTabIdentifier
                );

                if (existingPlotTab == null)
                {
                    _specificLogger.Information(
                        "ExecuteOpenPlotForTag: Aanmaken nieuwe plot tab voor Modbus TagName: {TagName} ({ConnectionName})",
                        tagConfig.TagName,
                        DisplayName
                    );
                    var newPlotTab = new PlotTabViewModel(
                        plotTabIdentifier,
                        $"Grafiek (Modbus): {tagConfig.TagName}", // Tab header
                        RemovePlotTab, // Actie om de tab te sluiten
                        _specificLogger // Doorgeven logger voor context
                    );
                    newPlotTab.EnsureSeriesExists(tagConfig.TagName, tagConfig.TagName); // Voeg de serie toe aan het plotmodel
                    ActivePlotTabs.Add(newPlotTab);
                    SelectedPlotTab = newPlotTab; // Selecteer de nieuwe tab
                }
                else
                {
                    SelectedPlotTab = existingPlotTab; // Selecteer bestaande tab
                    SelectedPlotTab.EnsureSeriesExists(tagConfig.TagName, tagConfig.TagName); // Zorg dat de serie bestaat (kan redundant zijn, maar veilig)
                    _specificLogger.Information(
                        "Bestaande plot tab geselecteerd voor Modbus TagName: {TagName} ({ConnectionName})",
                        tagConfig.TagName,
                        DisplayName
                    );
                }
            }
        }

        /// <summary>
        /// Bepaalt of het <see cref="AddSelectedTagToCurrentPlotCommand"/> uitgevoerd kan worden.
        /// </summary>
        private bool CanExecuteAddSelectedTagToCurrentPlot(object parameter)
        {
            if (
                parameter is ModbusTagConfig tagConfigToAdd
                && tagConfigToAdd.IsActive
                && SelectedPlotTab != null
            )
            {
                // Kan alleen uitgevoerd worden als de tag nog niet in de geselecteerde plot zit.
                return SelectedPlotTab.PlotModel.Series.All(s => s.Title != tagConfigToAdd.TagName);
            }
            return false;
        }

        /// <summary>
        /// Voert het <see cref="AddSelectedTagToCurrentPlotCommand"/> uit: voegt de gegeven tag als een nieuwe serie
        /// toe aan de momenteel geselecteerde plot-tab.
        /// </summary>
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
                        "Tag '{TagName}' is al aanwezig in de huidige plot tab '{PlotTabTitle}' ({ConnectionName}).",
                        tagConfigToAdd.TagName,
                        SelectedPlotTab.Header,
                        DisplayName
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
                    "ExecuteAddSelectedTagToCurrentPlot: Voegt Modbus tag '{TagName}' als series toe aan plot '{PlotTabTitle}' ({ConnectionName})",
                    tagConfigToAdd.TagName,
                    SelectedPlotTab.Header,
                    DisplayName
                );
                SelectedPlotTab.EnsureSeriesExists(tagConfigToAdd.TagName, tagConfigToAdd.TagName);
                UpdateCommandStates(); // Update CanExecute van commando's
            }
            else
            {
                _specificLogger.Warning(
                    "Kon Modbus tag niet toevoegen aan huidige plot ({ConnectionName}): geen plot of tag geselecteerd/actief.",
                    DisplayName
                );
            }
        }

        /// <summary>
        /// Verwijdert de opgegeven plot-tab uit de collectie van actieve plot-tabs.
        /// </summary>
        private void RemovePlotTab(PlotTabViewModel plotTabToRemove)
        {
            if (plotTabToRemove != null && ActivePlotTabs.Contains(plotTabToRemove))
            {
                plotTabToRemove.Dispose(); // Roep Dispose aan op de plot-tab ViewModel
                ActivePlotTabs.Remove(plotTabToRemove);
                _specificLogger.Information(
                    "Plot tab gesloten voor: {Header} ({ConnectionName})",
                    plotTabToRemove.Header,
                    DisplayName
                );
                if (SelectedPlotTab == plotTabToRemove)
                {
                    SelectedPlotTab = ActivePlotTabs.FirstOrDefault(); // Selecteer een andere tab of null
                }
            }
        }
        #endregion

        #region Modbus Specific Alarm/Outlier Logic
        /// <summary>
        /// Initialiseert of reset de baseline states voor alle actieve Modbus-tags
        /// waarvoor outlier detectie is ingeschakeld.
        /// </summary>
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
                    _specificLogger.Debug(
                        "Baseline state geïnitialiseerd voor Modbus-tag {TagName} ({ConnectionName}).",
                        tagConfig.TagName,
                        DisplayName
                    );
                }
            }
        }
        #endregion

        #region Configuration Update
        /// <summary>
        /// Werkt de configuratie van deze Modbus-tab bij met een nieuwe configuratie.
        /// Dit wordt typisch aangeroepen nadat instellingen zijn gewijzigd.
        /// </summary>
        /// <param name="newConfig">De nieuwe <see cref="ModbusTcpConnectionConfig"/>.</param>
        public void UpdateConfiguration(ModbusTcpConnectionConfig newConfig)
        {
            _saveChangesDebounceTimer?.Stop(); // Stop debounce timer tijdens directe update
            if (newConfig == null)
            {
                _specificLogger.Error(
                    "UpdateConfiguration aangeroepen met null nieuwe configuratie voor Modbus tab {DisplayName}",
                    DisplayName
                );
                return;
            }

            var oldConfig = ModbusConfig; // Huidige configuratie
            if (oldConfig == null) // Zou niet moeten gebeuren als constructor correct werkt
            {
                _specificLogger.Error(
                    "UpdateConfiguration: Huidige ModbusConfig is null voor {DisplayName}, kan niet updaten. Stelt nieuwe config in.",
                    DisplayName
                );
                ConnectionConfiguration = newConfig; // Probeer te herstellen
                OnPropertyChanged(nameof(ModbusConfig));
                if (DisplayName != newConfig.ConnectionName)
                    DisplayName = newConfig.ConnectionName;
                _modbusService.Reconfigure(newConfig);
                UpdateScanTimerInterval();
                InitializeBaselineStates();
                SynchronizeDataValuesWithConfiguration();
                UpdateCommandStates();
                return;
            }

            _specificLogger.Information(
                "ModbusTabViewModel '{OldDisplayName}' (nu '{DisplayName}'): Configuratie wordt bijgewerkt.",
                oldConfig.ConnectionName,
                newConfig.ConnectionName
            );
            ConnectionConfiguration = newConfig; // Update de base property
            OnPropertyChanged(nameof(ModbusConfig)); // Notificeer dat ModbusConfig (de getypte property) is veranderd

            if (DisplayName != newConfig.ConnectionName)
            {
                DisplayName = newConfig.ConnectionName; // Update DisplayName van de tab
                _specificLogger = Serilog
                    .Log.Logger.ForContext<ModbusTabViewModel>()
                    .ForContext("ConnectionName", DisplayName); // Update logger context
            }

            _modbusService.Reconfigure(newConfig); // Geef nieuwe config door aan de service

            // Update scan timer interval als dat gewijzigd is
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
                bool wasTimerEnabled = _scanTimer.IsEnabled;
                _scanTimer.Stop();
                UpdateScanTimerInterval(); // Stelt het nieuwe interval in
                if (wasTimerEnabled && IsConnected)
                    _scanTimer.Start(); // Herstart timer als deze liep en verbonden is
            }

            // Controleer of tag-gerelateerde instellingen significant zijn gewijzigd
            bool tagsChangedSignificantly = HaveTagsChangedSignificantly(
                oldConfig.TagsToMonitor,
                newConfig.TagsToMonitor
            );
            if (tagsChangedSignificantly)
            {
                _specificLogger.Information(
                    "Modbus tag configuratie significant gewijzigd voor '{DisplayName}', herinitialiseren baseline states en UI data.",
                    DisplayName
                );
                InitializeBaselineStates();
                ClearUIData(); // Begin met een schone lei voor live data
            }

            SynchronizeDataValuesWithConfiguration(); // Zorg dat UI overeenkomt met nieuwe config

            // Update plot tabs: verwijder series van tags die niet meer actief zijn, update titels
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var activeNewTagNames = newConfig
                    .TagsToMonitor.Where(t => t.IsActive)
                    .Select(t => t.TagName)
                    .ToList();
                foreach (var plotTab in ActivePlotTabs.ToList()) // ToList voor veilige iteratie
                {
                    // Verwijder series die niet meer in de actieve configuratie staan
                    var seriesToRemoveFromPlot = plotTab
                        .PlottedSeriesInfos.Where(psi => !activeNewTagNames.Contains(psi.SeriesKey))
                        .ToList();
                    foreach (var seriesInfo in seriesToRemoveFromPlot)
                    {
                        plotTab.ExecuteRemoveSeriesFromPlot(seriesInfo);
                    }
                }
            });

            UpdateCommandStates();
            _specificLogger.Debug(
                "ModbusTabViewModel '{DisplayName}' configuratie update voltooid.",
                DisplayName
            );
        }

        /// <summary>
        /// Controleert of er significante wijzigingen zijn in de tag-collectie.
        /// </summary>
        private bool HaveTagsChangedSignificantly(
            ObservableCollection<ModbusTagConfig> oldTags,
            ObservableCollection<ModbusTagConfig> newTags
        )
        {
            if (oldTags == null && newTags == null)
                return false;
            if (oldTags == null || newTags == null)
                return true; // Een van beide is null, dus significant anders
            if (oldTags.Count != newTags.Count)
                return true;

            // Vergelijk op basis van een set van kritieke eigenschappen
            var oldTagSignatures = oldTags
                .Select(t =>
                    $"{t.TagName}|{t.Address}|{t.RegisterType}|{t.DataType}|{t.IsActive}|{t.IsOutlierDetectionEnabled}|{t.BaselineSampleSize}|{t.OutlierStandardDeviationFactor.ToString(CultureInfo.InvariantCulture)}"
                )
                .OrderBy(s => s)
                .ToList();
            var newTagSignatures = newTags
                .Select(t =>
                    $"{t.TagName}|{t.Address}|{t.RegisterType}|{t.DataType}|{t.IsActive}|{t.IsOutlierDetectionEnabled}|{t.BaselineSampleSize}|{t.OutlierStandardDeviationFactor.ToString(CultureInfo.InvariantCulture)}"
                )
                .OrderBy(s => s)
                .ToList();

            return !oldTagSignatures.SequenceEqual(newTagSignatures);
        }

        #endregion

        #region Helper Methods
        /// <summary>
        /// Probeert de gegeven object waarde te converteren naar een double.
        /// Ondersteunt diverse numerieke types en een string representatie.
        /// </summary>
        /// <param name="value">De te converteren waarde.</param>
        /// <param name="result">Output parameter voor de geconverteerde double waarde.</param>
        /// <returns>True als de conversie succesvol was, anders false.</returns>
        protected bool TryConvertToDouble(object value, out double result)
        {
            result = 0;
            if (value == null)
                return false;

            if (value is double dVal)
            {
                result = dVal;
                return true;
            }
            if (value is float fVal)
            {
                result = (double)fVal;
                return true;
            }
            if (value is short sVal)
            {
                result = (double)sVal;
                return true;
            }
            if (value is ushort usVal)
            {
                result = (double)usVal;
                return true;
            }
            if (value is int iVal)
            {
                result = (double)iVal;
                return true;
            }
            if (value is uint uiVal)
            {
                result = (double)uiVal;
                return true;
            }
            if (value is long lVal)
            {
                result = (double)lVal;
                return true;
            }
            if (value is ulong ulVal)
            {
                result = (double)ulVal;
                return true;
            }
            if (value is byte bVal)
            {
                result = (double)bVal;
                return true;
            }
            if (value is bool boolVal)
            {
                result = boolVal ? 1.0 : 0.0;
                return true;
            }
            if (value is string strVal)
            {
                if (
                    double.TryParse(
                        strVal,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out result
                    )
                )
                    return true;
                if (
                    double.TryParse(
                        strVal,
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
            return false;
        }
        #endregion

        #region IDisposable
        /// <summary>
        /// Geeft beheerde en onbeheerde resources vrij die door de <see cref="ModbusTabViewModel"/> worden gebruikt.
        /// </summary>
        /// <param name="disposing">True om zowel beheerde als onbeheerde resources vrij te geven; false om alleen onbeheerde resources vrij te geven.</param>
        private void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _specificLogger.Debug(
                        "Dispose(true) aangeroepen voor ModbusTabViewModel: {ConnectionName}",
                        DisplayName
                    );

                    if (_scanTimer != null)
                    {
                        _scanTimer.Stop();
                    }

                    if (_saveChangesDebounceTimer != null)
                    {
                        _saveChangesDebounceTimer.Stop();
                        _saveChangesDebounceTimer.Tick -= SaveChangesDebounceTimer_Tick;
                    }

                    if (_modbusService != null)
                    {
                        _modbusService.ConnectionStatusChanged -= OnModbusConnectionStatusChanged;
                        _modbusService.TagsDataReceived -= OnModbusTagsDataReceived;
                        _modbusService.Dispose(); // Dispose de onderliggende service
                    }

                    // Ruim plot tabs op (indien de plot tabs zelf IDisposable zijn)
                    if (
                        Application.Current?.Dispatcher != null
                        && !Application.Current.Dispatcher.CheckAccess()
                    )
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            foreach (var plotTab in ActivePlotTabs.ToList())
                                RemovePlotTab(plotTab); // ToList voor iteratie en modificatie
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

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
