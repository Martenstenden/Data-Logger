using System;
using System.IO;
using System.Reflection;
using Data_Logger.Enums;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Newtonsoft.Json;
using Serilog;

namespace Data_Logger.Services.Implementations
{
    /// <summary>
    /// Service voor het beheren van applicatie-instellingen.
    /// Implementeert <see cref="ISettingsService"/>.
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private readonly ILogger _logger;
        private readonly IStatusService _statusService;
        private readonly string _settingsFilePath;
        private const string SettingsFileName = "DataLoggerSettings.json";

        private AppSettings _currentSettings;

        /// <inheritdoc/>
        public AppSettings CurrentSettings => _currentSettings;

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="SettingsService"/> klasse.
        /// </summary>
        /// <param name="logger">De Serilog logger instantie.</param>
        /// <param name="statusService">De status service voor het communiceren van laad/opslag statussen.</param>
        public SettingsService(ILogger logger, IStatusService statusService)
        {
            _logger =
                logger?.ForContext<SettingsService>()
                ?? throw new ArgumentNullException(nameof(logger));
            _statusService =
                statusService ?? throw new ArgumentNullException(nameof(statusService));

            string executableLocation = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location
            );
            if (string.IsNullOrEmpty(executableLocation))
            {
                // Fallback voor het geval de locatie niet bepaald kan worden.
                _settingsFilePath = Path.Combine(Directory.GetCurrentDirectory(), SettingsFileName);
                _logger.Warning(
                    "Kon executielocatie niet bepalen, gebruikt huidige werkmap voor instellingenbestand: {SettingsFilePath}",
                    _settingsFilePath
                );
            }
            else
            {
                _settingsFilePath = Path.Combine(executableLocation, SettingsFileName);
            }

            _logger.Information(
                "Pad naar instellingenbestand: {SettingsFilePath}",
                _settingsFilePath
            );
            LoadSettings(); // Laad instellingen direct bij initialisatie
        }

        /// <inheritdoc/>
        public void LoadSettings()
        {
            _statusService.SetStatus(ApplicationStatus.Loading, "Instellingen laden...");
            _logger.Information(
                "Proberen instellingen te laden van: {SettingsFilePath}",
                _settingsFilePath
            );
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    _logger.Information("Instellingenbestand gevonden. Bezig met laden...");
                    string json = File.ReadAllText(_settingsFilePath);

                    var serializerSettings = new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Objects, // Cruciaal voor deserialisatie van afgeleide types
                        Formatting = Formatting.Indented,
                    };

                    _currentSettings = JsonConvert.DeserializeObject<AppSettings>(
                        json,
                        serializerSettings
                    );
                    if (_currentSettings == null)
                    {
                        _logger.Warning(
                            "Deserialisatie van instellingen resulteerde in null. Standaardinstellingen worden geladen."
                        );
                        LoadDefaultSettings();
                    }
                    else
                    {
                        _logger.Information(
                            "Instellingen succesvol geladen. Aantal connecties: {ConnectionCount}",
                            _currentSettings.Connections?.Count ?? 0
                        );
                    }
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
            catch (JsonSerializationException jsonEx)
            {
                _logger.Error(
                    jsonEx,
                    "Fout tijdens deserialiseren van instellingenbestand {SettingsFilePath}. Mogelijk corrupt of incompatibel formaat. Standaardinstellingen worden geladen.",
                    _settingsFilePath
                );
                LoadDefaultSettings();
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Algemene fout bij het laden van instellingen van {SettingsFilePath}. Standaardinstellingen worden geladen.",
                    _settingsFilePath
                );
                LoadDefaultSettings();
            }
            _statusService.SetStatus(ApplicationStatus.Idle, "Instellingen verwerkt.");
        }

        /// <inheritdoc/>
        public void SaveSettings()
        {
            _statusService.SetStatus(ApplicationStatus.Saving, "Instellingen opslaan...");
            _logger.Information("Instellingen opslaan naar: {SettingsFilePath}", _settingsFilePath);
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
                _statusService.SetStatus(ApplicationStatus.Idle, "Instellingen opgeslagen.");
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout bij het opslaan van instellingen naar {SettingsFilePath}",
                    _settingsFilePath
                );
                _statusService.SetStatus(
                    ApplicationStatus.Error,
                    $"Fout bij opslaan instellingen: {ex.Message}"
                );
            }
        }

        /// <inheritdoc/>
        public void LoadDefaultSettings()
        {
            _logger.Information("Standaardinstellingen worden geconfigureerd en geladen.");
            _currentSettings = new AppSettings(); // Creëer een nieuwe, lege instellingenset

            _currentSettings.Connections.Add(
                new ModbusTcpConnectionConfig
                {
                    ConnectionName = "Voorbeeld Modbus (default)",
                    IpAddress = "127.0.0.1",
                    Port = 502,
                    IsEnabled = false, // Standaard uitgeschakeld
                    ScanIntervalSeconds = 10,
                }
            );
            _currentSettings.Connections.Add(
                new OpcUaConnectionConfig
                {
                    ConnectionName = "Voorbeeld OPC UA (default)",
                    EndpointUrl = "opc.tcp://localhost:48010", // Voorbeeld endpoint
                    IsEnabled = false, // Standaard uitgeschakeld
                    ScanIntervalSeconds = 10,
                }
            );
            _logger.Information(
                "Standaardinstellingen geladen met {ConnectionCount} voorbeeldconnecties.",
                _currentSettings.Connections.Count
            );
        }
    }
}
