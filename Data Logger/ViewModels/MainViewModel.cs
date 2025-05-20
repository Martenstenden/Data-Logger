using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Data_Logger.Core;
using Data_Logger.Enums;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Data_Logger.Views;
using Serilog;

namespace Data_Logger.ViewModels
{
    /// <summary>
    /// De hoofd ViewModel voor de Data Logger applicatie.
    /// Beheert de actieve dataverbinding-tabs en de algemene applicatiestatus.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        private readonly ILogger _logger;
        private readonly IStatusService _statusService;
        private readonly ISettingsService _settingsService;
        private readonly Func<ModbusTcpConnectionConfig, IModbusService> _modbusServiceFactory;
        private readonly Func<OpcUaConnectionConfig, IOpcUaService> _opcUaServiceFactory;
        private readonly IDataLoggingService _dataLoggingService;

        private string _applicationTitle = "Data Logger Applicatie";

        /// <summary>
        /// Haalt de titel van de applicatie op of stelt deze in, welke in het hoofdvenster getoond kan worden.
        /// </summary>
        public string ApplicationTitle
        {
            get => _applicationTitle;
            set => SetProperty(ref _applicationTitle, value);
        }

        public LogViewModel LogVm { get; }

        /// <summary>
        /// Haalt de huidige status van de applicatie op vanuit de <see cref="IStatusService"/>.
        /// </summary>
        public ApplicationStatus CurrentApplicationStatus => _statusService.CurrentStatus;

        /// <summary>
        /// Haalt het bericht dat de huidige applicatiestatus beschrijft op vanuit de <see cref="IStatusService"/>.
        /// </summary>
        public string CurrentStatusMessage => _statusService.StatusMessage;

        /// <summary>
        /// Haalt een observeerbare collectie van actieve tab ViewModels op. Elke tab representeert een dataverbinding.
        /// </summary>
        public ObservableCollection<TabViewModelBase> ActiveTabs { get; } =
            new ObservableCollection<TabViewModelBase>();

        private TabViewModelBase _selectedTab;

        /// <summary>
        /// Haalt de momenteel geselecteerde tab ViewModel op of stelt deze in.
        /// </summary>
        public TabViewModelBase SelectedTab
        {
            get => _selectedTab;
            set => SetProperty(ref _selectedTab, value);
        }

        private readonly Func<Action, SettingsViewModel> _settingsViewModelFactory;

        public ICommand OpenSettingsCommand { get; }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="MainViewModel"/> klasse.
        /// </summary>
        /// <param name="logViewModel"></param>
        /// <param name="logger">De Serilog logger instantie.</param>
        /// <param name="statusService">De service voor het beheren van de applicatiestatus.</param>
        /// <param name="settingsService">De service voor het beheren van applicatie-instellingen.</param>
        /// <param name="dataLoggingService">De service voor het loggen van data.</param>
        /// <param name="modbusServiceFactory">Een factory functie om <see cref="IModbusService"/> instanties te creëren.</param>
        /// <param name="opcUaServiceFactory">Een factory functie om <see cref="IOpcUaService"/> instanties te creëren.</param>
        /// <param name="settingsViewModelFactory">Een factory functie om <see cref="ISettingsService"/> instanties te creëren.</param>
        public MainViewModel(
            LogViewModel logViewModel,
            ILogger logger,
            IStatusService statusService,
            ISettingsService settingsService,
            IDataLoggingService dataLoggingService,
            Func<ModbusTcpConnectionConfig, IModbusService> modbusServiceFactory,
            Func<OpcUaConnectionConfig, IOpcUaService> opcUaServiceFactory,
            Func<Action, SettingsViewModel> settingsViewModelFactory
        )
        {
            LogVm = logViewModel ?? throw new ArgumentNullException(nameof(logViewModel));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _statusService =
                statusService ?? throw new ArgumentNullException(nameof(statusService));
            _settingsService =
                settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _modbusServiceFactory =
                modbusServiceFactory
                ?? throw new ArgumentNullException(nameof(modbusServiceFactory));
            _opcUaServiceFactory =
                opcUaServiceFactory ?? throw new ArgumentNullException(nameof(opcUaServiceFactory));
            _dataLoggingService =
                dataLoggingService ?? throw new ArgumentNullException(nameof(dataLoggingService));
            _settingsViewModelFactory =
                settingsViewModelFactory
                ?? throw new ArgumentNullException(nameof(settingsViewModelFactory));

            if (_statusService is INotifyPropertyChanged notifier)
            {
                notifier.PropertyChanged += StatusService_PropertyChanged;
            }

            OpenSettingsCommand = new RelayCommand(ExecuteOpenSettingsWindow);

            _logger.Information("MainViewModel geïnitialiseerd.");
            _statusService.SetStatus(ApplicationStatus.Idle, "Applicatie succesvol geladen.");

            LoadTabsFromSettings();
        }

        private void ExecuteOpenSettingsWindow(object obj)
        {
            _logger.Information(
                "OpenSettingsCommand uitgevoerd. Instellingenvenster wordt geopend."
            );
            try
            {
                var settingsView = new SettingsView();

                Action closeAction = () =>
                {
                    _logger.Debug(
                        "CloseAction aangeroepen vanuit SettingsViewModel. SettingsView wordt gesloten."
                    );
                    settingsView.Close();
                };

                var settingsViewModel = _settingsViewModelFactory(closeAction);
                settingsView.DataContext = settingsViewModel;

                settingsView.Owner = Application.Current.MainWindow;
                settingsView.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                settingsView.ShowDialog();

                _logger.Information("Instellingenvenster gesloten.");

                _logger.Information(
                    "Tabs opnieuw laden na het sluiten van het instellingenvenster."
                );
                LoadTabsFromSettings();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fout bij het openen van het instellingenvenster.");
                MessageBox.Show(
                    $"Er is een fout opgetreden bij het openen van de instellingen:\n{ex.Message}",
                    "Fout Instellingen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// Laadt de tabbladen op basis van de verbindingen die zijn gedefinieerd in de huidige applicatie-instellingen.
        /// Bestaande tabs worden eerst opgeruimd.
        /// </summary>
        private void LoadTabsFromSettings()
        {
            _logger.Information("Tabs laden op basis van huidige instellingen...");
            string currentSelectedTabNameBeforeReload = SelectedTab
                ?.ConnectionConfiguration
                ?.ConnectionName;

            foreach (var tab in ActiveTabs.ToList()) // ToList() om collectie te kunnen wijzigen tijdens iteratie
            {
                if (tab is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                ActiveTabs.Remove(tab);
            }
            _logger.Debug("Alle bestaande actieve tabs zijn opgeruimd.");

            if (_settingsService.CurrentSettings?.Connections == null)
            {
                _logger.Warning(
                    "Geen verbindingen gevonden in de instellingen om tabs voor te laden."
                );
                SelectedTab = null;
                return;
            }

            foreach (var config in _settingsService.CurrentSettings.Connections)
            {
                if (config.IsEnabled) // Alleen actieve verbindingen als tab laden
                {
                    CreateAndAddTab(config);
                }
                else
                {
                    _logger.Debug(
                        "Verbinding '{ConnectionName}' is uitgeschakeld en wordt niet als tab geladen.",
                        config.ConnectionName
                    );
                }
            }

            // Probeer de eerder geselecteerde tab opnieuw te selecteren
            if (!string.IsNullOrEmpty(currentSelectedTabNameBeforeReload))
            {
                SelectedTab = ActiveTabs.FirstOrDefault(t =>
                    t.ConnectionConfiguration.ConnectionName == currentSelectedTabNameBeforeReload
                );
            }

            // Als er geen (of geen geldige vorige) selectie is, selecteer de eerste tab indien beschikbaar.
            if (SelectedTab == null && ActiveTabs.Any())
            {
                SelectedTab = ActiveTabs.First();
            }
            else if (!ActiveTabs.Any())
            {
                SelectedTab = null; // Geen tabs om te selecteren
            }
            _logger.Information("{Count} actieve tabs geladen.", ActiveTabs.Count);
        }

        /// <summary>
        /// Creëert en voegt een nieuwe tab ViewModel toe aan de <see cref="ActiveTabs"/> collectie
        /// op basis van de gegeven verbindingsconfiguratie.
        /// </summary>
        /// <param name="config">De <see cref="ConnectionConfigBase"/> voor de nieuwe tab.</param>
        private void CreateAndAddTab(ConnectionConfigBase config)
        {
            TabViewModelBase tabVm = null;
            switch (config.Type)
            {
                case ConnectionType.ModbusTcp:
                    if (config is ModbusTcpConnectionConfig modbusConfig)
                    {
                        var modbusServiceInstance = _modbusServiceFactory(modbusConfig);
                        tabVm = new ModbusTabViewModel(
                            modbusConfig,
                            _logger,
                            modbusServiceInstance,
                            _statusService,
                            _dataLoggingService,
                            _settingsService
                        );
                        _logger.Debug(
                            "Modbus TCP Tab ViewModel aangemaakt voor: {ConnectionName}",
                            modbusConfig.ConnectionName
                        );
                    }
                    break;
                case ConnectionType.OpcUa:
                    if (config is OpcUaConnectionConfig opcUaConfig)
                    {
                        var opcUaServiceInstance = _opcUaServiceFactory(opcUaConfig);
                        tabVm = new OpcUaTabViewModel(
                            opcUaConfig,
                            _logger,
                            opcUaServiceInstance,
                            _statusService,
                            _dataLoggingService,
                            _settingsService
                        );
                        _logger.Debug(
                            "OPC UA Tab ViewModel aangemaakt voor: {ConnectionName}",
                            opcUaConfig.ConnectionName
                        );
                    }
                    break;
                default:
                    _logger.Warning(
                        "Onbekend verbindingstype '{ConfigType}' overgeslagen bij aanmaken tab voor: {ConnectionName}",
                        config.Type,
                        config.ConnectionName
                    );
                    break;
            }

            if (tabVm != null)
            {
                ActiveTabs.Add(tabVm);
                _logger.Information(
                    "Tab aangemaakt en toegevoegd voor {ConnectionName} ({ConnectionType})",
                    config.ConnectionName,
                    config.Type
                );
            }
        }

        /// <summary>
        /// Handler voor PropertyChanged events van de <see cref="IStatusService"/>.
        /// Zorgt ervoor dat de UI wordt bijgewerkt wanneer de applicatiestatus of het bericht verandert.
        /// </summary>
        private void StatusService_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IStatusService.CurrentStatus))
            {
                OnPropertyChanged(nameof(CurrentApplicationStatus));
            }
            else if (e.PropertyName == nameof(IStatusService.StatusMessage))
            {
                OnPropertyChanged(nameof(CurrentStatusMessage));
            }
        }
    }
}
