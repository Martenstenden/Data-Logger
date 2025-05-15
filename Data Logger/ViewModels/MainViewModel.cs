using System;
using System.Collections.Generic;
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
    public class MainViewModel : ObservableObject
    {
        private readonly ILogger _logger;
        private readonly IStatusService _statusService;
        private readonly ISettingsService _settingsService;
        private readonly Func<Action, SettingsViewModel> _settingsViewModelFactory;

        private readonly Func<ModbusTcpConnectionConfig, IModbusService> _modbusServiceFactory;
        private readonly Func<OpcUaConnectionConfig, IOpcUaService> _opcUaServiceFactory;

        private readonly IDataLoggingService _dataLoggingService;

        private string _applicationTitle = "Data Logger Applicatie";
        public string ApplicationTitle
        {
            get => _applicationTitle;
            set => SetProperty(ref _applicationTitle, value);
        }

        public LogViewModel LogVm { get; }
        public ApplicationStatus CurrentApplicationStatus => _statusService.CurrentStatus;
        public string CurrentStatusMessage => _statusService.StatusMessage;

        public ObservableCollection<TabViewModelBase> ActiveTabs { get; } =
            new ObservableCollection<TabViewModelBase>();

        private TabViewModelBase _selectedTab;
        public TabViewModelBase SelectedTab
        {
            get => _selectedTab;
            set => SetProperty(ref _selectedTab, value);
        }

        public ICommand OpenSettingsCommand { get; }

        public MainViewModel(
            ILogger logger,
            LogViewModel logViewModel,
            IStatusService statusService,
            ISettingsService settingsService,
            Func<Action, SettingsViewModel> settingsViewModelFactory,
            Func<ModbusTcpConnectionConfig, IModbusService> modbusServiceFactory,
            Func<OpcUaConnectionConfig, IOpcUaService> opcUaServiceFactory,
            IDataLoggingService dataLoggingService
        )
        {
            _logger = logger;
            LogVm = logViewModel;
            _statusService = statusService;
            _settingsService = settingsService;
            _settingsViewModelFactory = settingsViewModelFactory;

            _modbusServiceFactory = modbusServiceFactory;
            _opcUaServiceFactory = opcUaServiceFactory;

            _dataLoggingService = dataLoggingService;

            if (_statusService is INotifyPropertyChanged notifier)
            {
                notifier.PropertyChanged += StatusService_PropertyChanged;
            }

            OpenSettingsCommand = new RelayCommand(_ => OpenSettingsWindow());

            _logger.Information("MainViewModel geïnitialiseerd.");
            _statusService.SetStatus(ApplicationStatus.Idle, "Applicatie succesvol geladen.");

            LoadTabsFromSettings();
        }

        private void OpenSettingsWindow()
        {
            _logger.Information("Instellingenvenster wordt geopend...");
            _statusService.SetStatus(ApplicationStatus.Idle, "Instellingen openen...");

            var settingsView = new Views.SettingsView();
            Action closeAction = () => settingsView.Close();
            var settingsVm = _settingsViewModelFactory(closeAction);
            settingsView.DataContext = settingsVm;

            if (
                Application.Current.MainWindow != null
                && Application.Current.MainWindow != settingsView
            )
            {
                settingsView.Owner = Application.Current.MainWindow;
            }

            settingsView.ShowDialog();

            _logger.Information("Instellingenvenster gesloten.");

            UpdateTabsAfterSettingsChange();

            _statusService.SetStatus(ApplicationStatus.Idle, "Klaar.");
        }

        private void UpdateTabsAfterSettingsChange()
        {
            _logger.Information("Tabs bijwerken na mogelijke instellingwijzigingen...");

            var newConfigsFromSettings = _settingsService.CurrentSettings.Connections.ToList();
            var currentActiveTabs = ActiveTabs.ToList();
            var handledNewConfigs = new HashSet<ConnectionConfigBase>();

            List<TabViewModelBase> tabsToRemove = new List<TabViewModelBase>();

            foreach (var tabVm in currentActiveTabs)
            {
                ConnectionConfigBase oldTabConfig = tabVm.ConnectionConfiguration;
                ConnectionConfigBase correspondingNewConfig = null;

                if (oldTabConfig is OpcUaConnectionConfig oldOpcUa)
                {
                    correspondingNewConfig = newConfigsFromSettings
                        .OfType<OpcUaConnectionConfig>()
                        .FirstOrDefault(newOpcUa =>
                            newOpcUa.IsEnabled
                            && oldOpcUa.EndpointUrl == newOpcUa.EndpointUrl
                            && oldOpcUa.SecurityMode == newOpcUa.SecurityMode
                            && oldOpcUa.SecurityPolicyUri == newOpcUa.SecurityPolicyUri
                            && oldOpcUa.UserName == newOpcUa.UserName
                        );
                }
                else if (oldTabConfig is ModbusTcpConnectionConfig oldModbus)
                {
                    correspondingNewConfig = newConfigsFromSettings
                        .OfType<ModbusTcpConnectionConfig>()
                        .FirstOrDefault(newModbus =>
                            newModbus.IsEnabled
                            && oldModbus.IpAddress == newModbus.IpAddress
                            && oldModbus.Port == newModbus.Port
                            && oldModbus.UnitId == newModbus.UnitId
                        );
                }

                if (correspondingNewConfig != null && correspondingNewConfig.IsEnabled)
                {
                    _logger.Information(
                        "Bestaande tab voor (oude naam) '{OldName}' wordt bijgewerkt met configuratie (nieuwe naam) '{NewName}'.",
                        oldTabConfig.ConnectionName,
                        correspondingNewConfig.ConnectionName
                    );

                    if (
                        tabVm is OpcUaTabViewModel opcUaTabVm
                        && correspondingNewConfig is OpcUaConnectionConfig newOpcConf
                    )
                    {
                        opcUaTabVm.UpdateConfiguration(newOpcConf);
                    }
                    else if (
                        tabVm is ModbusTabViewModel modbusTabVm
                        && correspondingNewConfig is ModbusTcpConnectionConfig newModConf
                    )
                    {
                        modbusTabVm.UpdateConfiguration(newModConf);
                    }
                    handledNewConfigs.Add(correspondingNewConfig);
                }
                else
                {
                    _logger.Information(
                        "Geen actieve overeenkomstige nieuwe configuratie gevonden voor tab: {ConnectionName}. Tab wordt verwijderd.",
                        oldTabConfig.ConnectionName
                    );
                    tabsToRemove.Add(tabVm);
                }
            }

            foreach (var tabVmToRemove in tabsToRemove)
            {
                if (tabVmToRemove is IDisposable disposable)
                    disposable.Dispose();
                ActiveTabs.Remove(tabVmToRemove);
            }

            foreach (
                var newConfig in newConfigsFromSettings.Where(nc =>
                    nc.IsEnabled && !handledNewConfigs.Contains(nc)
                )
            )
            {
                _logger.Information(
                    "Nieuwe actieve verbinding gevonden, tab aanmaken voor: {ConnectionName}",
                    newConfig.ConnectionName
                );
                CreateAndAddTab(newConfig);
            }

            if (ActiveTabs.Any() && SelectedTab == null)
                SelectedTab = ActiveTabs.First();
            else if (!ActiveTabs.Any())
                SelectedTab = null;
            else if (SelectedTab != null && !ActiveTabs.Contains(SelectedTab))
                SelectedTab = ActiveTabs.FirstOrDefault();
        }

        private void CreateAndAddTab(ConnectionConfigBase config)
        {
            TabViewModelBase tabVm = null;
            if (config is ModbusTcpConnectionConfig modbusConfig)
            {
                if (_modbusServiceFactory != null && _dataLoggingService != null)
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
                }
                else
                {
                    _logger.Error(
                        "Modbus service factory of data logging service niet geïnjecteerd. Kan ModbusTabViewModel niet aanmaken."
                    );
                }
            }
            else if (config is OpcUaConnectionConfig opcUaConfig)
            {
                if (_opcUaServiceFactory != null && _dataLoggingService != null)
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
                }
                else
                {
                    _logger.Error(
                        "OPC UA service factory of data logging service niet geïnjecteerd. Kan OpcUaTabViewModel niet aanmaken."
                    );
                }
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

        private void LoadTabsFromSettings()
        {
            _logger.Information("Tabs laden op basis van huidige instellingen...");
            var currentSelectedTabName = SelectedTab?.ConnectionConfiguration?.ConnectionName;
            var currentTabs = ActiveTabs.ToList();

            foreach (var tab in currentTabs)
            {
                if (tab is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                ActiveTabs.Remove(tab);
            }

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
                if (config.IsEnabled)
                {
                    TabViewModelBase tabVm = null;
                    switch (config.Type)
                    {
                        case ConnectionType.ModbusTcp:
                            if (
                                config is ModbusTcpConnectionConfig modbusConfig
                                && _modbusServiceFactory != null
                            )
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
                            else if (_modbusServiceFactory == null)
                            {
                                _logger.Error(
                                    "_modbusServiceFactory is niet geïnjecteerd in MainViewModel."
                                );
                            }
                            break;
                        case ConnectionType.OpcUa:
                            if (
                                config is OpcUaConnectionConfig opcUaConfig
                                && _opcUaServiceFactory != null
                            )
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
                            else if (_opcUaServiceFactory == null)
                            {
                                _logger.Error(
                                    "_opcUaServiceFactory is niet geïnjecteerd in MainViewModel."
                                );
                            }
                            break;
                        default:
                            _logger.Warning(
                                "Onbekend verbindingstype '{Type}' overgeslagen voor tab: {ConnectionName}",
                                config.Type,
                                config.ConnectionName
                            );
                            break;
                    }

                    if (tabVm != null)
                    {
                        ActiveTabs.Add(tabVm);
                    }
                }
                else
                {
                    _logger.Debug(
                        "Verbinding '{ConnectionName}' is uitgeschakeld en wordt niet als tab geladen.",
                        config.ConnectionName
                    );
                }
            }

            if (!string.IsNullOrEmpty(currentSelectedTabName))
            {
                SelectedTab = ActiveTabs.FirstOrDefault(t =>
                    t.ConnectionConfiguration.ConnectionName == currentSelectedTabName
                );
            }

            if (SelectedTab == null && ActiveTabs.Any())
            {
                SelectedTab = ActiveTabs.First();
            }
            else if (!ActiveTabs.Any())
            {
                SelectedTab = null;
            }
            _logger.Information("{Count} actieve tabs geladen.", ActiveTabs.Count);
        }

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
