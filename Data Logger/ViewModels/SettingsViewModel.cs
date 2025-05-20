using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Data_Logger.Core;
using Data_Logger.Enums;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Newtonsoft.Json;
using Serilog;

namespace Data_Logger.ViewModels
{
    /// <summary>
    /// ViewModel voor het <see cref="Views.SettingsView"/> venster.
    /// Beheert een werkkopie van de applicatie-instellingen, waardoor gebruikers
    /// wijzigingen kunnen aanbrengen en deze kunnen opslaan of annuleren.
    /// </summary>
    public class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly IStatusService _statusService;
        private readonly ILogger _logger;
        private readonly Action _closeWindowAction; // Actie om het settings venster te sluiten

        private ObservableCollection<ConnectionConfigBase> _workingConnections;

        /// <summary>
        /// Haalt de werkkopie van de connectieconfiguraties op of stelt deze in.
        /// Wijzigingen hierin worden pas definitief na aanroep van <see cref="SaveCommand"/>.
        /// </summary>
        public ObservableCollection<ConnectionConfigBase> WorkingConnections
        {
            get => _workingConnections;
            set => SetProperty(ref _workingConnections, value);
        }

        private ConnectionConfigBase _selectedConnection;

        /// <summary>
        /// Haalt de momenteel geselecteerde connectieconfiguratie in de UI op of stelt deze in.
        /// </summary>
        public ConnectionConfigBase SelectedConnection
        {
            get => _selectedConnection;
            set
            {
                if (SetProperty(ref _selectedConnection, value))
                {
                    // Update de CanExecute status van commando's die afhankelijk zijn van een selectie.
                    ((RelayCommand)RemoveConnectionCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)AddNewModbusTagCommand).RaiseCanExecuteChanged(); // Afhankelijk van type SelectedConnection
                    ((RelayCommand)RemoveSelectedModbusTagCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private ModbusTagConfig _selectedModbusTagInGrid;

        /// <summary>
        /// Haalt de momenteel geselecteerde Modbus-tag in een DataGrid op of stelt deze in.
        /// Wordt gebruikt voor commando's zoals het verwijderen van een geselecteerde tag.
        /// </summary>
        public ModbusTagConfig SelectedModbusTagInGrid
        {
            get => _selectedModbusTagInGrid;
            set
            {
                if (SetProperty(ref _selectedModbusTagInGrid, value))
                {
                    ((RelayCommand)RemoveSelectedModbusTagCommand).RaiseCanExecuteChanged();
                }
            }
        }

        #region Commands
        /// <summary>
        /// Commando om een nieuwe OPC UA connectieconfiguratie toe te voegen aan de werkkopie.
        /// </summary>
        public ICommand AddOpcUaConnectionCommand { get; }

        /// <summary>
        /// Commando om een nieuwe Modbus TCP connectieconfiguratie toe te voegen aan de werkkopie.
        /// </summary>
        public ICommand AddModbusTcpConnectionCommand { get; }

        /// <summary>
        /// Commando om de <see cref="SelectedConnection"/> te verwijderen uit de werkkopie.
        /// </summary>
        public ICommand RemoveConnectionCommand { get; }

        /// <summary>
        /// Commando om de wijzigingen in de werkkopie op te slaan in de daadwerkelijke applicatie-instellingen
        /// en het instellingenvenster te sluiten.
        /// </summary>
        public ICommand SaveCommand { get; }

        /// <summary>
        /// Commando om de wijzigingen in de werkkopie te annuleren en het instellingenvenster te sluiten.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Commando om een nieuwe Modbus-tag toe te voegen aan de <see cref="SelectedConnection"/>
        /// (indien dit een <see cref="ModbusTcpConnectionConfig"/> is).
        /// </summary>
        public ICommand AddNewModbusTagCommand { get; }

        /// <summary>
        /// Commando om de <see cref="SelectedModbusTagInGrid"/> te verwijderen uit de geselecteerde Modbus-connectie.
        /// </summary>
        public ICommand RemoveSelectedModbusTagCommand { get; }
        #endregion

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="SettingsViewModel"/> klasse.
        /// </summary>
        /// <param name="settingsService">De service voor het beheren van applicatie-instellingen.</param>
        /// <param name="statusService">De service voor het communiceren van de applicatiestatus.</param>
        /// <param name="logger">De Serilog logger instantie.</param>
        /// <param name="closeWindowAction">Een actie die wordt aangeroepen om het instellingenvenster te sluiten.</param>
        /// <exception cref="ArgumentNullException">Als een van de services of de closeWindowAction null is.</exception>
        public SettingsViewModel(
            ISettingsService settingsService,
            IStatusService statusService,
            ILogger logger,
            Action closeWindowAction
        )
        {
            _settingsService =
                settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _statusService =
                statusService ?? throw new ArgumentNullException(nameof(statusService));
            _logger =
                logger?.ForContext<SettingsViewModel>()
                ?? throw new ArgumentNullException(nameof(logger));
            _closeWindowAction =
                closeWindowAction ?? throw new ArgumentNullException(nameof(closeWindowAction));

            LoadWorkingCopyOfSettings();

            AddOpcUaConnectionCommand = new RelayCommand(execute: _ =>
                AddConnection(ConnectionType.OpcUa)
            );
            AddModbusTcpConnectionCommand = new RelayCommand(execute: _ =>
                AddConnection(ConnectionType.ModbusTcp)
            );
            RemoveConnectionCommand = new RelayCommand(
                execute: _ => RemoveSelectedConnection(),
                canExecute: _ => SelectedConnection != null
            );
            SaveCommand = new RelayCommand(execute: _ => SaveSettingsAndClose());
            CancelCommand = new RelayCommand(execute: _ => CancelAndClose());

            AddNewModbusTagCommand = new RelayCommand(
                execute: param => AddNewModbusTag(SelectedConnection as ModbusTcpConnectionConfig),
                canExecute: param => SelectedConnection is ModbusTcpConnectionConfig
            );
            RemoveSelectedModbusTagCommand = new RelayCommand(
                execute: param => RemoveModbusTag(SelectedModbusTagInGrid),
                canExecute: param =>
                    SelectedModbusTagInGrid != null
                    && SelectedConnection is ModbusTcpConnectionConfig
            );

            _logger.Information("SettingsViewModel geïnitialiseerd.");
        }

        /// <summary>
        /// Laadt een diepe kopie van de huidige applicatie-instellingen in de <see cref="WorkingConnections"/> collectie.
        /// Dit stelt de gebruiker in staat wijzigingen te maken zonder de actieve instellingen direct aan te passen,
        /// totdat expliciet wordt opgeslagen.
        /// </summary>
        private void LoadWorkingCopyOfSettings()
        {
            _logger.Debug("Werkkopie van instellingen laden...");
            var originalConnections = _settingsService.CurrentSettings.Connections;
            var tempWorkingConnections = new ObservableCollection<ConnectionConfigBase>();

            // Gebruik JSON serialisatie/deserialisatie voor een diepe kloon van de objecten.
            var serializerSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Objects,
            };

            if (originalConnections != null)
            {
                foreach (var conn in originalConnections)
                {
                    try
                    {
                        string jsonConn = JsonConvert.SerializeObject(conn, serializerSettings);
                        var clonedConn = JsonConvert.DeserializeObject<ConnectionConfigBase>(
                            jsonConn,
                            serializerSettings
                        );
                        if (clonedConn != null)
                        {
                            tempWorkingConnections.Add(clonedConn);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(
                            ex,
                            "Fout bij het klonen van een ConnectionConfig object: {ConnectionName}",
                            conn?.ConnectionName ?? "Onbekend"
                        );
                    }
                }
            }
            WorkingConnections = tempWorkingConnections;
            _logger.Information(
                "Werkkopie van {Count} verbindingen geladen.",
                WorkingConnections.Count
            );
        }

        /// <summary>
        /// Voegt een nieuwe connectie van het opgegeven type toe aan de werkkopie.
        /// </summary>
        /// <param name="type">Het <see cref="ConnectionType"/> van de toe te voegen verbinding.</param>
        private void AddConnection(ConnectionType type)
        {
            ConnectionConfigBase newConnection;
            switch (type)
            {
                case ConnectionType.ModbusTcp:
                    newConnection = new ModbusTcpConnectionConfig();
                    _logger.Information("Nieuwe Modbus TCP verbinding toegevoegd aan werkkopie.");
                    break;
                case ConnectionType.OpcUa:
                    newConnection = new OpcUaConnectionConfig();
                    _logger.Information("Nieuwe OPC UA verbinding toegevoegd aan werkkopie.");
                    break;
                default:
                    _logger.Warning(
                        "Onbekend verbindingstype ({Type}) gevraagd om toe te voegen.",
                        type
                    );
                    return;
            }

            WorkingConnections.Add(newConnection);
            SelectedConnection = newConnection;
        }

        /// <summary>
        /// Verwijdert de <see cref="SelectedConnection"/> uit de werkkopie van verbindingen.
        /// </summary>
        private void RemoveSelectedConnection()
        {
            if (SelectedConnection != null)
            {
                _logger.Information(
                    "Verbinding '{ConnectionName}' wordt verwijderd uit werkkopie.",
                    SelectedConnection.ConnectionName
                );
                WorkingConnections.Remove(SelectedConnection);
                SelectedConnection = WorkingConnections.FirstOrDefault(); // Selecteer de eerste of null
            }
        }

        /// <summary>
        /// Slaat de wijzigingen in de <see cref="WorkingConnections"/> op naar de
        /// <see cref="ISettingsService"/> en sluit het instellingenvenster.
        /// </summary>
        private void SaveSettingsAndClose()
        {
            _logger.Information("Instellingen opslaan vanuit SettingsViewModel...");
            _statusService.SetStatus(
                ApplicationStatus.Saving,
                "Bezig met opslaan van gewijzigde instellingen..."
            );

            // Update de daadwerkelijke instellingen met de werkkopie
            _settingsService.CurrentSettings.Connections.Clear();
            foreach (var conn in WorkingConnections)
            {
                _settingsService.CurrentSettings.Connections.Add(conn);
            }

            _settingsService.SaveSettings();
            _logger.Information("Instellingen succesvol opgeslagen.");
            _statusService.SetStatus(ApplicationStatus.Idle, "Instellingen opgeslagen.");
            _closeWindowAction(); // Sluit het venster
        }

        /// <summary>
        /// Annuleert de gemaakte wijzigingen door de werkkopie niet op te slaan en sluit het instellingenvenster.
        /// </summary>
        private void CancelAndClose()
        {
            _logger.Information("Wijzigingen in instellingen geannuleerd. Venster wordt gesloten.");
            _statusService.SetStatus(
                ApplicationStatus.Idle,
                "Wijzigingen in instellingen geannuleerd."
            );
            _closeWindowAction(); // Sluit het venster
        }

        /// <summary>
        /// Voegt een nieuwe Modbus-tag toe aan de geselecteerde Modbus TCP connectieconfiguratie.
        /// </summary>
        /// <param name="connectionConfig">De <see cref="ModbusTcpConnectionConfig"/> waaraan de tag moet worden toegevoegd.</param>
        private void AddNewModbusTag(ModbusTcpConnectionConfig connectionConfig)
        {
            if (connectionConfig == null)
                return;

            var newTag = new ModbusTagConfig
            {
                TagName = "Nieuwe Modbus Tag",
                Address = 0,
                RegisterType = ModbusRegisterType.HoldingRegister,
                DataType = ModbusDataType.UInt16,
                IsActive = true,
                IsAlarmingEnabled = false,
                IsOutlierDetectionEnabled = false,
                BaselineSampleSize = 20,
                OutlierStandardDeviationFactor = 3.0,
                AlarmMessageFormat = "{TagName} is {AlarmState}: {Value}",
            };
            connectionConfig.TagsToMonitor.Add(newTag);
            SelectedModbusTagInGrid = newTag; // Selecteer de nieuwe tag in de UI (indien gebonden)
            _logger.Information(
                "Nieuwe Modbus-tag '{TagName}' toegevoegd aan verbinding '{ConnectionName}'.",
                newTag.TagName,
                connectionConfig.ConnectionName
            );
        }

        /// <summary>
        /// Verwijdert de opgegeven Modbus-tag uit de geselecteerde Modbus TCP connectieconfiguratie.
        /// </summary>
        /// <param name="tagToRemove">De <see cref="ModbusTagConfig"/> die verwijderd moet worden.</param>
        private void RemoveModbusTag(ModbusTagConfig tagToRemove)
        {
            if (
                tagToRemove == null
                || !(SelectedConnection is ModbusTcpConnectionConfig modbusConnection)
            )
                return;

            if (modbusConnection.TagsToMonitor.Remove(tagToRemove))
            {
                _logger.Information(
                    "Modbus-tag '{TagName}' verwijderd van verbinding '{ConnectionName}'.",
                    tagToRemove.TagName,
                    modbusConnection.ConnectionName
                );
                SelectedModbusTagInGrid = modbusConnection.TagsToMonitor.FirstOrDefault(); // Selecteer een andere tag of null
            }
        }
    }
}
