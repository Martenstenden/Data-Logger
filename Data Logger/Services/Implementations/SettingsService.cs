using System;
using System.IO;
using System.Reflection;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Newtonsoft.Json;
using Serilog;

namespace Data_Logger.Services.Implementations
{
    public class SettingsService : ISettingsService
    {
        private readonly ILogger _logger;
        private readonly IStatusService _statusService;
        private const string SettingsFileName = "DataLoggerSettings.json";
        private string _settingsFilePath;

        private AppSettings _currentSettings;
        public AppSettings CurrentSettings
        {
            get => _currentSettings;
            private set => _currentSettings = value;
        }

        public SettingsService(ILogger logger, IStatusService statusService)
        {
            _logger = logger;
            _statusService = statusService;

            string executableLocation = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location
            );
            _settingsFilePath = Path.Combine(executableLocation, SettingsFileName);
            _logger.Information(
                "Pad naar instellingenbestand: {SettingsFilePath}",
                _settingsFilePath
            );

            LoadSettings();
        }

        public void LoadSettings()
        {
            _statusService.SetStatus(Enums.ApplicationStatus.Loading, "Instellingen laden...");
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    _logger.Information("Instellingenbestand gevonden. Bezig met laden...");
                    string json = File.ReadAllText(_settingsFilePath);

                    var serializerSettings = new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Objects,
                        Formatting = Formatting.Indented,
                    };

                    _currentSettings = JsonConvert.DeserializeObject<AppSettings>(
                        json,
                        serializerSettings
                    );
                    _logger.Information("Instellingen succesvol geladen.");
                }
                else
                {
                    _logger.Warning(
                        "Instellingenbestand niet gevonden op {SettingsFilePath}. Standaardinstellingen worden geladen.",
                        _settingsFilePath
                    );
                    LoadDefaultSettings();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout bij het laden van instellingen. Standaardinstellingen worden geladen."
                );
                LoadDefaultSettings();
            }
            _statusService.SetStatus(Enums.ApplicationStatus.Idle, "Instellingen verwerkt.");
        }

        public void SaveSettings()
        {
            _statusService.SetStatus(Enums.ApplicationStatus.Saving, "Instellingen opslaan...");
            try
            {
                var serializerSettings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Objects,
                    Formatting = Formatting.Indented,
                };

                string json = JsonConvert.SerializeObject(CurrentSettings, serializerSettings);
                File.WriteAllText(_settingsFilePath, json);
                _logger.Information(
                    "Instellingen succesvol opgeslagen in {SettingsFilePath}",
                    _settingsFilePath
                );
                _statusService.SetStatus(Enums.ApplicationStatus.Idle, "Instellingen opgeslagen.");
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout bij het opslaan van instellingen naar {SettingsFilePath}",
                    _settingsFilePath
                );
                _statusService.SetStatus(
                    Enums.ApplicationStatus.Error,
                    "Fout bij opslaan instellingen."
                );
            }
        }

        public void LoadDefaultSettings()
        {
            _logger.Information("Standaardinstellingen worden geconfigureerd.");
            _currentSettings = new AppSettings();
            _currentSettings.Connections.Add(
                new ModbusTcpConnectionConfig
                {
                    ConnectionName = "Voorbeeld Modbus Device",
                    IpAddress = "192.168.1.100",
                    Port = 502,
                    IsEnabled = false,
                    ScanIntervalSeconds = 5,
                }
            );
        }
    }
}
