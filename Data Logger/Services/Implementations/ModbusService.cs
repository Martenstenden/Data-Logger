using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Data_Logger.Converters;
using Data_Logger.Enums;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Modbus.Device;
using Serilog;

namespace Data_Logger.Services.Implementations
{
    /// <summary>
    /// Service voor het afhandelen van Modbus TCP communicatie.
    /// Implementeert <see cref="IModbusService"/> en <see cref="IDisposable"/>.
    /// </summary>
    public class ModbusService : IModbusService
    {
        private ILogger _logger;
        private ModbusTcpConnectionConfig _config;
        private TcpClient _tcpClient;
        private ModbusIpMaster _master;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1); // Voor synchronisatie van toegang tot client/master

        private bool _isConnected;

        /// <inheritdoc/>
        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
                    _logger.Information(
                        "Modbus connectiestatus gewijzigd naar: {Status} voor {ConnectionName}",
                        _isConnected,
                        _config?.ConnectionName ?? "N/A"
                    );
                }
            }
        }

        /// <inheritdoc/>
        public event EventHandler ConnectionStatusChanged;

        /// <inheritdoc/>
        public event EventHandler<IEnumerable<LoggedTagValue>> TagsDataReceived;

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="ModbusService"/> klasse.
        /// </summary>
        /// <param name="logger">De Serilog logger instantie.</param>
        /// <param name="config">De initiële Modbus TCP connectieconfiguratie.</param>
        public ModbusService(ILogger logger, ModbusTcpConnectionConfig config)
        {
            _logger =
                logger?.ForContext<ModbusService>()
                ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            // Context van de logger wordt verder gespecificeerd met de ConnectionName.
            _logger = _logger.ForContext("ConnectionName", _config.ConnectionName);
        }

        /// <inheritdoc/>
        public async Task<bool> ConnectAsync()
        {
            if (IsConnected)
            {
                _logger.Debug(
                    "ConnectAsync aangeroepen terwijl al verbonden voor {ConnectionName}.",
                    _config.ConnectionName
                );
                return true;
            }

            _logger.Information(
                "Bezig met verbinden met Modbus server: {IpAddress}:{Port} voor {ConnectionName}",
                _config.IpAddress,
                _config.Port,
                _config.ConnectionName
            );

            // Zorg ervoor dat oude resources zijn opgeruimd (kan gebeuren als vorige verbinding mislukt is)
            CleanUpNetworkResources();

            try
            {
                _tcpClient = new TcpClient();
                // Gebruik een CancellationTokenSource voor een configureerbare timeout.
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5))) // 5 seconden timeout
                {
                    await _tcpClient
                        .ConnectAsync(_config.IpAddress, _config.Port)
                        .WithCancellation(cts.Token);
                }

                if (_tcpClient.Connected)
                {
                    _master = ModbusIpMaster.CreateIp(_tcpClient);
                    if (_master != null)
                    {
                        // Stel timeouts in voor lees/schrijfoperaties op de Modbus transportlaag
                        _master.Transport.ReadTimeout = 2000; // ms
                        _master.Transport.WriteTimeout = 2000; // ms
                        IsConnected = true; // Triggert ConnectionStatusChanged event
                        _logger.Information(
                            "Succesvol verbonden met Modbus server {IpAddress}:{Port} ({ConnectionName}).",
                            _config.IpAddress,
                            _config.Port,
                            _config.ConnectionName
                        );
                        return true;
                    }
                    _logger.Error(
                        "Kon ModbusIpMaster niet aanmaken na succesvolle TCP verbinding voor {ConnectionName}.",
                        _config.ConnectionName
                    );
                    CleanUpNetworkResources(); // TcpClient is verbonden maar master kon niet gemaakt worden
                    return false;
                }
                // Dit punt zou niet bereikt moeten worden als ConnectAsync faalt, want dat gooit een exception.
                _logger.Warning(
                    "TCPClient rapporteert niet verbonden na ConnectAsync voor {ConnectionName}.",
                    _config.ConnectionName
                );
                CleanUpNetworkResources();
                return false;
            }
            catch (OperationCanceledException ex) // Specifiek voor de CancellationTokenSource timeout
            {
                _logger.Warning(
                    ex,
                    "Timeout (5s) tijdens verbinden met Modbus server {IpAddress}:{Port} ({ConnectionName}).",
                    _config.IpAddress,
                    _config.Port,
                    _config.ConnectionName
                );
                CleanUpNetworkResources();
                IsConnected = false;
                return false;
            }
            catch (SocketException sockEx)
            {
                _logger.Error(
                    sockEx,
                    "SocketException tijdens verbinden met Modbus server {IpAddress}:{Port} ({ConnectionName}). Foutcode: {ErrorCode}",
                    _config.IpAddress,
                    _config.Port,
                    _config.ConnectionName,
                    sockEx.SocketErrorCode
                );
                CleanUpNetworkResources();
                IsConnected = false;
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Algemene fout tijdens verbinden met Modbus server {IpAddress}:{Port} ({ConnectionName})",
                    _config.IpAddress,
                    _config.Port,
                    _config.ConnectionName
                );
                CleanUpNetworkResources();
                IsConnected = false;
                return false;
            }
        }

        /// <summary>
        /// Ruimt netwerkresources (TcpClient en ModbusIpMaster) op.
        /// </summary>
        private void CleanUpNetworkResources()
        {
            _master?.Dispose();
            _master = null;
            _tcpClient?.Close(); // Close sluit de verbinding en disposed de underlying socket.
            _tcpClient?.Dispose(); // Expliciet Disposen van TcpClient zelf.
            _tcpClient = null;
        }

        /// <inheritdoc/>
        public async Task DisconnectAsync()
        {
            if (!IsConnected)
            {
                _logger.Debug(
                    "DisconnectAsync aangeroepen terwijl niet verbonden voor {ConnectionName}.",
                    _config.ConnectionName
                );
                return;
            }

            _logger.Information(
                "Verbinding met Modbus server verbreken voor {ConnectionName}...",
                _config.ConnectionName
            );

            // Wacht op de semaphore om exclusieve toegang te krijgen voor het afsluiten.
            // ConfigureAwait(false) wordt hier gebruikt om potentiële deadlocks in UI-contexten te voorkomen.
            if (await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                try
                {
                    _logger.Debug(
                        "DisconnectAsync: Semaphore verkregen. Resources sluiten voor {ConnectionName}.",
                        _config.ConnectionName
                    );
                    CleanUpNetworkResources();
                    IsConnected = false; // Triggert ConnectionStatusChanged event
                    _logger.Information(
                        "Verbinding met Modbus server daadwerkelijk verbroken voor {ConnectionName}.",
                        _config.ConnectionName
                    );
                }
                catch (Exception ex)
                {
                    _logger.Warning(
                        ex,
                        "Fout tijdens daadwerkelijk sluiten van Modbus resources in DisconnectAsync voor {ConnectionName}.",
                        _config.ConnectionName
                    );
                    // Forceer status en ruim resources op, ook bij fout.
                    IsConnected = false;
                    CleanUpNetworkResources();
                }
                finally
                {
                    _semaphore.Release();
                    _logger.Debug(
                        "DisconnectAsync: Semaphore vrijgegeven voor {ConnectionName}.",
                        _config.ConnectionName
                    );
                }
            }
            else
            {
                _logger.Warning(
                    "DisconnectAsync: Timeout bij wachten op semaphore voor {ConnectionName}. Verbinding wordt geforceerd als niet-verbonden ingesteld.",
                    _config.ConnectionName
                );
                IsConnected = false; // Forceer status update als semaphore niet verkregen kon worden.
            }
        }

        /// <inheritdoc/>
        public void Reconfigure(ModbusTcpConnectionConfig newConfig)
        {
            if (newConfig == null)
                throw new ArgumentNullException(nameof(newConfig));

            _logger.Information(
                "Herconfigureren van ModbusService van {OldConnectionName} (Interval: {OldInterval}s, Tags: {OldTagCount}) naar {NewConnectionName} (Interval: {NewInterval}s, Tags: {NewTagCount})",
                _config.ConnectionName,
                _config.ScanIntervalSeconds,
                _config.TagsToMonitor.Count,
                newConfig.ConnectionName,
                newConfig.ScanIntervalSeconds,
                newConfig.TagsToMonitor.Count
            );

            _config = newConfig;
            // Update de logger context als de connectienaam is veranderd
            _logger = Serilog
                .Log.Logger.ForContext<ModbusService>()
                .ForContext("ConnectionName", _config.ConnectionName);
        }

        /// <inheritdoc/>
        public async Task PollConfiguredTagsAsync()
        {
            if (!IsConnected || _master == null)
            {
                _logger.Debug(
                    "Kan geconfigureerde tags niet pollen voor {ConnectionName}, niet verbonden of master is null.",
                    _config.ConnectionName
                );
                return;
            }

            var results = new List<LoggedTagValue>();
            DateTime now = DateTime.Now;

            // Kopieer de lijst van te monitoren tags om thread-safety problemen te voorkomen
            // als de configuratie tijdens het pollen wordt gewijzigd.
            List<ModbusTagConfig> tagsToPoll = _config
                .TagsToMonitor.Where(t => t.IsActive)
                .ToList();

            foreach (var tag in tagsToPoll)
            {
                var loggedTag = new LoggedTagValue { TagName = tag.TagName, Timestamp = now };

                if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false))
                {
                    _logger.Warning(
                        "PollConfiguredTagsAsync: Timeout bij wachten op semaphore voor tag {TagName} ({ConnectionName}). Overslaan van deze tag in deze poll cyclus.",
                        tag.TagName,
                        _config.ConnectionName
                    );
                    loggedTag.IsGoodQuality = false;
                    loggedTag.ErrorMessage = "Polling overgeslagen vanwege resource conflict.";
                    results.Add(loggedTag);
                    continue;
                }

                try
                {
                    object value = null;
                    ushort numRegistersToRead = 1; // Default voor 16-bit types en booleans
                    if (
                        tag.DataType == ModbusDataType.Int32
                        || tag.DataType == ModbusDataType.UInt32
                        || tag.DataType == ModbusDataType.Float32
                    )
                    {
                        numRegistersToRead = 2;
                    }

                    switch (tag.RegisterType)
                    {
                        case ModbusRegisterType.Coil:
                            bool[] coilValues = await _master
                                .ReadCoilsAsync(_config.UnitId, tag.Address, 1)
                                .ConfigureAwait(false);
                            if (coilValues != null && coilValues.Length > 0)
                                value = coilValues[0];
                            else
                                throw new InvalidOperationException(
                                    "Lezen van Coil mislukt of gaf geen data."
                                );
                            break;
                        case ModbusRegisterType.DiscreteInput:
                            bool[] discreteValues = await _master
                                .ReadInputsAsync(_config.UnitId, tag.Address, 1)
                                .ConfigureAwait(false);
                            if (discreteValues != null && discreteValues.Length > 0)
                                value = discreteValues[0];
                            else
                                throw new InvalidOperationException(
                                    "Lezen van Discrete Input mislukt of gaf geen data."
                                );
                            break;
                        case ModbusRegisterType.HoldingRegister:
                            ushort[] holdingRegs = await _master
                                .ReadHoldingRegistersAsync(
                                    _config.UnitId,
                                    tag.Address,
                                    numRegistersToRead
                                )
                                .ConfigureAwait(false);
                            if (holdingRegs == null || holdingRegs.Length < numRegistersToRead)
                                throw new InvalidOperationException(
                                    "Lezen van Holding Register mislukt of gaf onvoldoende data."
                                );
                            value = ModbusDataConverter.InterpretRegisterData(
                                holdingRegs,
                                tag.DataType,
                                _logger
                            );
                            break;
                        case ModbusRegisterType.InputRegister:
                            ushort[] inputRegs = await _master
                                .ReadInputRegistersAsync(
                                    _config.UnitId,
                                    tag.Address,
                                    numRegistersToRead
                                )
                                .ConfigureAwait(false);
                            if (inputRegs == null || inputRegs.Length < numRegistersToRead)
                                throw new InvalidOperationException(
                                    "Lezen van Input Register mislukt of gaf onvoldoende data."
                                );
                            value = ModbusDataConverter.InterpretRegisterData(
                                inputRegs,
                                tag.DataType,
                                _logger
                            );
                            break;
                        default:
                            throw new NotSupportedException(
                                $"ModbusRegisterType {tag.RegisterType} wordt niet ondersteund voor pollen."
                            );
                    }
                    loggedTag.Value = value;
                    loggedTag.IsGoodQuality = true;
                }
                catch (Exception ex) // Vangt exceptions specifiek voor deze tag
                {
                    _logger.Error(
                        ex,
                        "Fout bij het lezen/interpreteren van Modbus tag: {TagName} (Adres: {Address}, Type: {RegisterType}) voor {ConnectionName}",
                        tag.TagName,
                        tag.Address,
                        tag.RegisterType,
                        _config.ConnectionName
                    );
                    loggedTag.IsGoodQuality = false;
                    loggedTag.ErrorMessage = ex.Message;
                }
                finally
                {
                    _semaphore.Release();
                }
                results.Add(loggedTag);
            }

            if (results.Any())
            {
                TagsDataReceived?.Invoke(this, results);
            }
        }

        private bool _disposedValue; // Om redundante aanroepen te detecteren

        /// <summary>
        /// Geeft beheerde en onbeheerde resources vrij die door de <see cref="ModbusService"/> worden gebruikt.
        /// </summary>
        /// <param name="disposing">True om zowel beheerde als onbeheerde resources vrij te geven; false om alleen onbeheerde resources vrij te geven.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _logger.Debug(
                        "ModbusService.Dispose({Disposing}) aangeroepen voor {ConnectionName}",
                        true,
                        _config?.ConnectionName ?? "N/A"
                    );

                    // Probeer de semaphore te verkrijgen met een korte timeout om deadlocks te voorkomen
                    bool acquired = false;
                    try
                    {
                        acquired = _semaphore.Wait(TimeSpan.FromMilliseconds(500));
                        if (acquired)
                        {
                            _logger.Debug(
                                "ModbusService.Dispose: Semaphore verkregen. Resources sluiten voor {ConnectionName}.",
                                _config?.ConnectionName ?? "N/A"
                            );
                            CleanUpNetworkResources();
                        }
                        else
                        {
                            _logger.Warning(
                                "ModbusService.Dispose: Timeout bij wachten op semaphore voor {ConnectionName}. Resources worden mogelijk niet correct vrijgegeven.",
                                _config?.ConnectionName ?? "N/A"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(
                            ex,
                            "ModbusService.Dispose: Fout bij verkrijgen semaphore of vrijgeven resources voor {ConnectionName}.",
                            _config?.ConnectionName ?? "N/A"
                        );
                    }
                    finally
                    {
                        if (acquired)
                        {
                            _semaphore.Release();
                        }
                    }

                    _semaphore.Dispose();
                }
                IsConnected = false; // Zorg dat status altijd false is na dispose
                _disposedValue = true;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Roep deze code niet meerdere keren aan.
            Dispose(disposing: true);
            GC.SuppressFinalize(this); // Voorkom dat de finalizer wordt aangeroepen
        }
    }

    /// <summary>
    /// Hulp-extensiemethode om een CancellationToken aan een Task te koppelen.
    /// </summary>
    internal static class TaskExtensions
    {
        public static async Task WithCancellation(
            this Task task,
            CancellationToken cancellationToken
        )
        {
            var tcs = new TaskCompletionSource<bool>();
            using (
                cancellationToken.Register(
                    s => ((TaskCompletionSource<bool>)s).TrySetResult(true),
                    tcs
                )
            )
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
            await task; // Gooi de originele task exception als die er was
        }
    }
}
