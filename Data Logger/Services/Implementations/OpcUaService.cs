using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Opc.Ua;
using Opc.Ua.Client;
using Serilog;

namespace Data_Logger.Services.Implementations
{
    /// <summary>
    /// Service voor het afhandelen van OPC UA client communicatie.
    /// Beheert sessies, subscriptions, browsen van de adresruimte en het lezen/schrijven van node-waarden.
    /// Implementeert <see cref="IOpcUaService"/> en <see cref="IDisposable"/>.
    /// </summary>
    public sealed partial class OpcUaService : IOpcUaService
    {
        #region Readonly Fields
        private readonly ApplicationConfiguration _appConfig; // OPC UA client applicatie configuratie
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1); // Voor synchronisatie van kritieke secties
        private readonly object _sessionLock = new object(); // Voor synchronisatie van _session en _reconnectHandler toegang
        private const int InitialReconnectDelayMs = 2000; // Initiele vertraging voor herverbinden in ms
        private const int MaxReconnectDelayMs = 30000; // Maximale vertraging voor herverbinden in ms
        private ILogger _specificLogger;
        #endregion

        #region Fields
        private OpcUaConnectionConfig _config;
        private Session _session;
        private Subscription _subscription;
        private bool _isConnected;
        private SessionReconnectHandler _reconnectHandler;
        private bool _disposedValue;
        #endregion

        #region Properties
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
                    _specificLogger.Information(
                        "OPC UA connectiestatus gewijzigd naar: {Status} voor {ConnectionName}",
                        _isConnected,
                        _config?.ConnectionName ?? "N/A"
                    );
                }
            }
        }

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => _session?.NamespaceUris;
        #endregion

        #region Events
        /// <inheritdoc/>
        public event EventHandler ConnectionStatusChanged;

        /// <inheritdoc/>
        public event EventHandler<IEnumerable<LoggedTagValue>> TagsDataReceived;
        #endregion

        #region Constructor
        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="OpcUaService"/> klasse.
        /// </summary>
        /// <param name="logger">De Serilog logger instantie.</param>
        /// <param name="config">De initiële OPC UA connectieconfiguratie.</param>
        /// <param name="appConfig">De OPC UA applicatieconfiguratie voor de client.</param>
        /// <exception cref="ArgumentNullException">Als logger, config, of appConfig null is.</exception>
        public OpcUaService(
            ILogger logger,
            OpcUaConnectionConfig config,
            ApplicationConfiguration appConfig
        )
        {
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _specificLogger = (logger ?? throw new ArgumentNullException(nameof(logger)))
                .ForContext<OpcUaService>()
                .ForContext("ConnectionName", _config.ConnectionName);
            _specificLogger.Debug(
                "OpcUaService geïnstantieerd voor {ConnectionName}",
                _config.ConnectionName
            );
        }
        #endregion

        #region IDisposable
        /// <summary>
        /// Geeft beheerde en onbeheerde resources vrij die door de <see cref="OpcUaService"/> worden gebruikt.
        /// </summary>
        /// <param name="disposing">True om zowel beheerde als onbeheerde resources vrij te geven; false om alleen onbeheerde resources vrij te geven.</param>
        private void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _specificLogger.Debug(
                        "OpcUaService.Dispose({Disposing}) aangeroepen voor {ConnectionName}",
                        true,
                        _config?.ConnectionName ?? "N/A"
                    );

                    bool acquired = false;
                    try
                    {
                        acquired = _semaphore.Wait(TimeSpan.FromSeconds(2));
                        if (acquired)
                        {
                            _specificLogger.Debug(
                                "OpcUaService.Dispose: Semaphore verkregen voor {ConnectionName}.",
                                _config?.ConnectionName ?? "N/A"
                            );
                            if (IsConnected || _session != null)
                            {
                                Task.Run(async () => await DisconnectAsync().ConfigureAwait(false))
                                    .Wait(TimeSpan.FromSeconds(5));
                            }

                            lock (_sessionLock)
                            {
                                _reconnectHandler?.Dispose();
                                _reconnectHandler = null;
                            }
                            _session?.Dispose();
                            _subscription?.Dispose();
                        }
                        else
                        {
                            _specificLogger.Warning(
                                "OpcUaService.Dispose: Timeout bij wachten op semaphore voor {ConnectionName}. Resources worden mogelijk niet volledig vrijgegeven.",
                                _config?.ConnectionName ?? "N/A"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _specificLogger.Error(
                            ex,
                            "OpcUaService.Dispose: Fout tijdens vrijgeven resources voor {ConnectionName}.",
                            _config?.ConnectionName ?? "N/A"
                        );
                    }
                    finally
                    {
                        if (acquired)
                        {
                            _semaphore.Release();
                        }
                        _semaphore.Dispose();
                    }
                }
                IsConnected = false;
                _session = null;
                _subscription = null;
                _disposedValue = true;
                _specificLogger.Information(
                    "OpcUaService voor {ConnectionName} is gedisposed.",
                    _config?.ConnectionName ?? "N/A"
                );
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
