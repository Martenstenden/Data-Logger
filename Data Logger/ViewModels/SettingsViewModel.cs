using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Data_Logger.Core;
using Data_Logger.Enums;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Newtonsoft.Json;
using Serilog;

namespace Data_Logger.ViewModels
{
    public class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly IStatusService _statusService;
        private readonly ILogger _logger;

        private ObservableCollection<ConnectionConfigBase> _workingConnections;
        public ObservableCollection<ConnectionConfigBase> WorkingConnections
        {
            get => _workingConnections;
            set => SetProperty(ref _workingConnections, value);
        }

        private ConnectionConfigBase _selectedConnection;
        public ConnectionConfigBase SelectedConnection
        {
            get => _selectedConnection;
            set
            {
                if (SetProperty(ref _selectedConnection, value))
                {
                    ((RelayCommand)RemoveConnectionCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand AddOpcUaConnectionCommand { get; }
        public ICommand AddModbusTcpConnectionCommand { get; }
        public ICommand RemoveConnectionCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private Action _closeWindowAction;

        private ModbusTagConfig _selectedModbusTagInGrid;
        public ModbusTagConfig SelectedModbusTagInGrid
        {
            get => _selectedModbusTagInGrid;
            set => SetProperty(ref _selectedModbusTagInGrid, value);
        }

        public ICommand AddNewModbusTagCommand { get; }
        public ICommand RemoveSelectedModbusTagCommand { get; }

        public SettingsViewModel(
            ISettingsService settingsService,
            IStatusService statusService,
            ILogger logger,
            Action closeWindowAction
        )
        {
            _settingsService = settingsService;
            _statusService = statusService;
            _logger = logger;
            _closeWindowAction =
                closeWindowAction ?? throw new ArgumentNullException(nameof(closeWindowAction));

            LoadWorkingCopyOfSettings();

            AddOpcUaConnectionCommand = new RelayCommand(_ => AddConnection(ConnectionType.OpcUa));
            AddModbusTcpConnectionCommand = new RelayCommand(_ =>
                AddConnection(ConnectionType.ModbusTcp)
            );
            RemoveConnectionCommand = new RelayCommand(
                _ => RemoveSelectedConnection(),
                _ => SelectedConnection != null
            );
            SaveCommand = new RelayCommand(_ => SaveSettingsAndClose());
            CancelCommand = new RelayCommand(_ => CancelAndClose());

            AddNewModbusTagCommand = new RelayCommand(
                param => AddNewModbusTag(param as ModbusTcpConnectionConfig),
                param => param is ModbusTcpConnectionConfig
            );

            RemoveSelectedModbusTagCommand = new RelayCommand(
                param => RemoveModbusTag(param as ModbusTagConfig),
                param => param is ModbusTagConfig && SelectedConnection is ModbusTcpConnectionConfig
            );

            _logger.Information("SettingsViewModel geïnitialiseerd.");
        }

        private void LoadWorkingCopyOfSettings()
        {
            _logger.Debug("Werkkopie van instellingen laden...");

            var originalConnections = _settingsService.CurrentSettings.Connections;
            var tempWorkingConnections = new ObservableCollection<ConnectionConfigBase>();

            var serializerSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Objects,
            };

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
                        conn.ConnectionName
                    );
                }
            }
            WorkingConnections = tempWorkingConnections;
            _logger.Information(
                "Werkkopie van {Count} verbindingen geladen.",
                WorkingConnections.Count
            );
        }

        private void AddConnection(ConnectionType type)
        {
            ConnectionConfigBase newConnection = null;
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
                    _logger.Warning("Onbekend verbindingstype gevraagd: {Type}", type);
                    return;
            }

            if (newConnection != null)
            {
                WorkingConnections.Add(newConnection);
                SelectedConnection = newConnection;
            }
        }

        private void RemoveSelectedConnection()
        {
            if (SelectedConnection != null)
            {
                _logger.Information(
                    "Verbinding '{ConnectionName}' verwijderd uit werkkopie.",
                    SelectedConnection.ConnectionName
                );
                WorkingConnections.Remove(SelectedConnection);
                SelectedConnection = null;
            }
        }

        private void SaveSettingsAndClose()
        {
            _logger.Information("Instellingen opslaan vanuit SettingsViewModel...");
            _statusService.SetStatus(
                Enums.ApplicationStatus.Saving,
                "Bezig met opslaan van gewijzigde instellingen..."
            );

            _settingsService.CurrentSettings.Connections.Clear();
            foreach (var conn in WorkingConnections)
            {
                _settingsService.CurrentSettings.Connections.Add(conn);
            }

            _settingsService.SaveSettings();
            _logger.Information("Instellingen succesvol opgeslagen.");
            _statusService.SetStatus(Enums.ApplicationStatus.Idle, "Instellingen opgeslagen.");
            _closeWindowAction();
        }

        private void CancelAndClose()
        {
            _logger.Information("Wijzigingen in instellingen geannuleerd.");
            _statusService.SetStatus(
                Enums.ApplicationStatus.Idle,
                "Wijzigingen in instellingen geannuleerd."
            );
            _closeWindowAction();
        }

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
            SelectedModbusTagInGrid = newTag;
        }

        private void RemoveModbusTag(ModbusTagConfig tagToRemove)
        {
            if (
                tagToRemove == null
                || !(SelectedConnection is ModbusTcpConnectionConfig modbusConnection)
            )
                return;

            modbusConnection.TagsToMonitor.Remove(tagToRemove);
            SelectedModbusTagInGrid = null;
        }
    }
}
