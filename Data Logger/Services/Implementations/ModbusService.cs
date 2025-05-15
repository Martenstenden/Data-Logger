using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Data_Logger.Enums;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Data_Logger.Converters;
using Modbus;
using Modbus.Device;
using Serilog;

namespace Data_Logger.Services.Implementations
{
    public class ModbusService : IModbusService
    {
        private readonly ILogger _logger;
        private ModbusTcpConnectionConfig _config;
        private TcpClient _tcpClient;
        private ModbusIpMaster _master;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    ConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public event EventHandler ConnectionStatusChanged;
        public event EventHandler<IEnumerable<LoggedTagValue>> TagsDataReceived;

        public ModbusService(ILogger logger, ModbusTcpConnectionConfig config)
        {
            _logger = logger
                .ForContext<ModbusService>()
                .ForContext("ConnectionName", config.ConnectionName);
            _config = config;
        }

        public async Task<bool> ConnectAsync()
        {
            if (IsConnected)
                return true;

            _logger.Information(
                "Bezig met verbinden met Modbus server: {IpAddress}:{Port}",
                _config.IpAddress,
                _config.Port
            );
            try
            {
                _tcpClient = new TcpClient();
                Task connectTask = _tcpClient.ConnectAsync(_config.IpAddress, _config.Port);
                Task timeoutTask = Task.Delay(5000);

                Task completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == connectTask)
                {
                    await connectTask;

                    if (_tcpClient.Connected)
                    {
                        _master = ModbusIpMaster.CreateIp(_tcpClient);
                        if (_master != null)
                        {
                            _master.Transport.ReadTimeout = 2000;
                            _master.Transport.WriteTimeout = 2000;
                            IsConnected = true;
                            _logger.Information("Succesvol verbonden met Modbus server.");
                            return true;
                        }
                        else
                        {
                            _logger.Error(
                                "Kon ModbusIpMaster niet aanmaken na succesvolle TCP verbinding."
                            );
                            _tcpClient?.Close();
                            _tcpClient = null;
                            IsConnected = false;
                            return false;
                        }
                    }
                    else
                    {
                        _logger.Warning(
                            "Kon niet verbinden met Modbus server (ConnectAsync voltooid, maar niet verbonden). IP: {IpAddress}",
                            _config.IpAddress
                        );
                        _tcpClient?.Close();
                        _tcpClient = null;
                        IsConnected = false;
                        return false;
                    }
                }
                else
                {
                    _logger.Warning(
                        "Timeout ({Timeout}ms) tijdens verbinden met Modbus server: {IpAddress}:{Port}",
                        5000,
                        _config.IpAddress,
                        _config.Port
                    );

                    _tcpClient?.Close();
                    _tcpClient = null;
                    IsConnected = false;

                    return false;
                }
            }
            catch (SocketException sockEx)
            {
                _logger.Error(
                    sockEx,
                    "SocketException tijdens verbinden met Modbus server {IpAddress}:{Port}. Foutcode: {ErrorCode}",
                    _config.IpAddress,
                    _config.Port,
                    sockEx.SocketErrorCode
                );
                IsConnected = false;
                _tcpClient?.Close();
                _tcpClient = null;
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Algemene fout tijdens verbinden met Modbus server {IpAddress}:{Port}",
                    _config.IpAddress,
                    _config.Port
                );
                IsConnected = false;
                _tcpClient?.Close();
                _tcpClient = null;
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            if (!IsConnected)
                return;
            _logger.Information("Verbinding met Modbus server verbreken...");
            await _semaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                _logger.Debug("DisconnectAsync: Semaphore verkregen. Resources sluiten.");

                _master?.Dispose();
                _tcpClient?.Close();
                _tcpClient?.Dispose();

                _master = null;
                _tcpClient = null;
                IsConnected = false;
                _logger.Information("Verbinding met Modbus server daadwerkelijk verbroken.");
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    ex,
                    "Fout tijdens daadwerkelijk sluiten van Modbus resources in DisconnectAsync."
                );

                _master = null;
                _tcpClient = null;
                IsConnected = false;
            }
            finally
            {
                _semaphore.Release();
                _logger.Debug("DisconnectAsync: Semaphore vrijgegeven.");
            }
        }

        public void Reconfigure(ModbusTcpConnectionConfig newConfig)
        {
            if (newConfig == null)
                throw new ArgumentNullException(nameof(newConfig));

            _logger.Information(
                "Herconfigureren van ModbusService {ConnectionName} met nieuwe instellingen. Oude scan interval: {OldInterval}, Nieuw: {NewInterval}. Aantal oude tags: {OldTagCount}, Nieuw: {NewTagCount}",
                _config.ConnectionName,
                _config.ScanIntervalSeconds,
                newConfig.ScanIntervalSeconds,
                _config.TagsToMonitor.Count,
                newConfig.TagsToMonitor.Count
            );

            _config = newConfig;
        }

        public async Task PollConfiguredTagsAsync()
        {
            if (!IsConnected || _master == null)
            {
                _logger.Warning("Kan geconfigureerde tags niet pollen, niet verbonden.");
                return;
            }

            var results = new List<LoggedTagValue>();
            var now = DateTime.Now;

            foreach (var tag in _config.TagsToMonitor.Where(t => t.IsActive))
            {
                var loggedTag = new LoggedTagValue { TagName = tag.TagName, Timestamp = now };
                await _semaphore.WaitAsync();
                try
                {
                    object value = null;
                    ushort numRegistersToRead = 1;
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
                            bool[] coilValues = await _master.ReadCoilsAsync(
                                _config.UnitId,
                                tag.Address,
                                1
                            );
                            if (coilValues != null && coilValues.Length > 0)
                                value = coilValues[0];
                            else
                                throw new InvalidOperationException(
                                    "Lezen van Coil mislukt of gaf geen data."
                                );
                            break;

                        case ModbusRegisterType.DiscreteInput:
                            bool[] discreteValues = await _master.ReadInputsAsync(
                                _config.UnitId,
                                tag.Address,
                                1
                            );
                            if (discreteValues != null && discreteValues.Length > 0)
                                value = discreteValues[0];
                            else
                                throw new InvalidOperationException(
                                    "Lezen van Discrete Input mislukt of gaf geen data."
                                );
                            break;

                        case ModbusRegisterType.HoldingRegister:
                            ushort[] holdingRegs = await _master.ReadHoldingRegistersAsync(
                                _config.UnitId,
                                tag.Address,
                                numRegistersToRead
                            );
                            if (holdingRegs == null || holdingRegs.Length < numRegistersToRead)
                                throw new InvalidOperationException(
                                    "Lezen van Holding Register mislukt of gaf onvoldoende data."
                                );
                            value = ModbusDataConverter.InterpretRegisterData(holdingRegs, tag.DataType);
                            break;

                        case ModbusRegisterType.InputRegister:
                            ushort[] inputRegs = await _master.ReadInputRegistersAsync(
                                _config.UnitId,
                                tag.Address,
                                numRegistersToRead
                            );
                            if (inputRegs == null || inputRegs.Length < numRegistersToRead)
                                throw new InvalidOperationException(
                                    "Lezen van Input Register mislukt of gaf onvoldoende data."
                                );
                            value = ModbusDataConverter.InterpretRegisterData(inputRegs, tag.DataType);
                            break;
                    }
                    loggedTag.Value = value;
                    loggedTag.IsGoodQuality = true;
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        ex,
                        "Fout bij het lezen/interpreteren van Modbus tag: {TagName} (Adres: {Address}, Type: {RegisterType})",
                        tag.TagName,
                        tag.Address,
                        tag.RegisterType
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
            TagsDataReceived?.Invoke(this, results);
        }

        

        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _logger.Debug(
                    "ModbusService.Dispose({Disposing}) aangeroepen voor {ConnectionName}",
                    disposing,
                    _config.ConnectionName
                );

                bool acquired = false;
                try
                {
                    acquired = _semaphore.Wait(1000);
                    if (acquired)
                    {
                        _logger.Debug(
                            "ModbusService.Dispose: Semaphore verkregen. Resources sluiten."
                        );
                        _master?.Dispose();
                        _tcpClient?.Close();
                        _tcpClient?.Dispose();
                    }
                    else
                    {
                        _logger.Warning(
                            "ModbusService.Dispose: Timeout bij wachten op semaphore. Resources worden mogelijk niet correct vrijgegeven door deze Dispose aanroep."
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "ModbusService.Dispose: Fout bij vrijgeven resources.");
                }
                finally
                {
                    if (acquired)
                        _semaphore.Release();
                }

                _master = null;
                _tcpClient = null;
                IsConnected = false;

                _semaphore?.Dispose();
            }
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
