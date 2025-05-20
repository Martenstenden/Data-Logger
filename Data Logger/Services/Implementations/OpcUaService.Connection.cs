using System;
using System.Linq;
using System.Threading.Tasks;
using Data_Logger.Models;
using Opc.Ua;
using Opc.Ua.Client;

namespace Data_Logger.Services.Implementations
{
    /// <summary>
    /// Partial class voor OpcUaService die methoden bevat gerelateerd aan connectiebeheer.
    /// </summary>
    public sealed partial class OpcUaService
    {
        /// <inheritdoc/>
        public async Task<bool> ConnectAsync()
        {
            if (IsConnected)
            {
                _specificLogger.Debug(
                    "ConnectAsync aangeroepen terwijl al verbonden voor {ConnectionName}.",
                    _config.ConnectionName
                );
                return true;
            }

            if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
            {
                _specificLogger.Warning(
                    "ConnectAsync: Timeout (10s) bij wachten op semaphore voor {ConnectionName}. Verbindingspoging afgebroken.",
                    _config.ConnectionName
                );
                return false;
            }

            try
            {
                if (IsConnected)
                {
                    _specificLogger.Debug(
                        "ConnectAsync: Al verbonden na verkrijgen semaphore voor {ConnectionName}.",
                        _config.ConnectionName
                    );
                    return true;
                }

                _specificLogger.Information(
                    "Bezig met verbinden met OPC UA server: {EndpointUrl} voor connectie {ConnectionName}",
                    _config.EndpointUrl,
                    _config.ConnectionName
                );

                EndpointDescription endpointDescription;
                EndpointDescriptionCollection serverEndpoints;
                try
                {
                    var discoveryUri = new Uri(_config.EndpointUrl);
                    var discoveryConfiguration = EndpointConfiguration.Create(_appConfig);
                    discoveryConfiguration.OperationTimeout = 5000;

                    using (
                        var discoveryClient = DiscoveryClient.Create(
                            _appConfig,
                            discoveryUri,
                            discoveryConfiguration
                        )
                    )
                    {
                        serverEndpoints = await discoveryClient
                            .GetEndpointsAsync(null)
                            .ConfigureAwait(false);
                        if (serverEndpoints == null || serverEndpoints.Count == 0)
                        {
                            _specificLogger.Error(
                                "Geen endpoints ontvangen van server via {EndpointUrl} voor {ConnectionName}",
                                _config.EndpointUrl,
                                _config.ConnectionName
                            );
                            return false;
                        }
                        endpointDescription = CoreClientUtils.SelectEndpoint(
                            _appConfig,
                            new Uri(_config.EndpointUrl),
                            serverEndpoints,
                            _config.SecurityMode != MessageSecurityMode.None
                        );
                    }
                }
                catch (Exception ex)
                {
                    _specificLogger.Error(
                        ex,
                        "Fout bij het ophalen/selecteren van OPC UA endpoint voor {EndpointUrl} (Conn: {ConnectionName})",
                        _config.EndpointUrl,
                        _config.ConnectionName
                    );
                    return false;
                }

                if (endpointDescription == null)
                {
                    _specificLogger.Error(
                        "Geen geschikt OPC UA endpoint gevonden voor {EndpointUrl} (Conn: {ConnectionName}) met security mode {SecurityMode} en policy {SecurityPolicyUri}.",
                        _config.EndpointUrl,
                        _config.ConnectionName,
                        _config.SecurityMode,
                        _config.SecurityPolicyUri ?? "N/A"
                    );
                    return false;
                }

                _specificLogger.Information(
                    "Geselecteerd endpoint: URL='{SelectedEndpointUrl}', SecurityMode='{SecurityMode}', SecurityPolicy='{SecurityPolicyUri}'",
                    endpointDescription.EndpointUrl,
                    endpointDescription.SecurityMode,
                    endpointDescription.SecurityPolicyUri
                );

                bool userSpecifiedSecurity =
                    !string.IsNullOrEmpty(_config.SecurityPolicyUri)
                    && _config.SecurityPolicyUri != SecurityPolicies.None;
                if (
                    userSpecifiedSecurity
                    && serverEndpoints != null
                    && (
                        endpointDescription.SecurityMode != _config.SecurityMode
                        || endpointDescription.SecurityPolicyUri != _config.SecurityPolicyUri
                    )
                )
                {
                    var matchingEndpoint = serverEndpoints.FirstOrDefault(ep =>
                        IsEndpointUrlMatch(ep.EndpointUrl, endpointDescription.EndpointUrl)
                        && ep.SecurityMode == _config.SecurityMode
                        && ep.SecurityPolicyUri == _config.SecurityPolicyUri
                    );
                    if (matchingEndpoint != null)
                        endpointDescription = matchingEndpoint;
                    else
                        _specificLogger.Warning(
                            "Kon geen exacte security match vinden voor user-specified policy."
                        );
                }

                var sessionConfiguration = EndpointConfiguration.Create(_appConfig);
                var configuredEndpoint = new ConfiguredEndpoint(
                    null,
                    endpointDescription,
                    sessionConfiguration
                );

                _session = await Session
                    .Create(
                        _appConfig,
                        configuredEndpoint,
                        false,
                        false,
                        _config.ConnectionName ?? _appConfig.ApplicationName,
                        (uint)_appConfig.ClientConfiguration.DefaultSessionTimeout, // Standaard sessie timeout
                        GetUserIdentity(),
                        null
                    )
                    .ConfigureAwait(false);

                if (_session == null)
                {
                    _specificLogger.Error("Kon geen OPC UA sessie aanmaken.");
                    return false;
                }

                _session.KeepAlive += Session_KeepAlive_EventHandler; // Monitor de sessie's keep alive

                lock (_sessionLock)
                {
                    _reconnectHandler?.Dispose();
                    _reconnectHandler = new SessionReconnectHandler(false, MaxReconnectDelayMs);
                }

                IsConnected = true; // Status wordt pas echt 'verbonden' na succesvolle sessiecreatie
                _specificLogger.Information(
                    "Succesvol OPC UA sessie aangemaakt: {EndpointUrl}. Sessie ID: {SessionId}",
                    _config.EndpointUrl,
                    _session.SessionId
                );
                return true;
            }
            catch (Exception ex)
            {
                _specificLogger.Error(
                    ex,
                    "Algemene fout bij verbinden met OPC UA server {EndpointUrl}",
                    _config.EndpointUrl
                );
                IsConnected = false;
                _session?.Dispose();
                _session = null;
                return false;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <inheritdoc/>
        public async Task DisconnectAsync()
        {
            if (!IsConnected && _session == null) // Check ook _session, want IsConnected kan al false zijn
            {
                _specificLogger.Debug(
                    "DisconnectAsync aangeroepen terwijl niet verbonden/geen sessie voor {ConnectionName}.",
                    _config.ConnectionName
                );
                return;
            }

            if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
            {
                _specificLogger.Warning(
                    "DisconnectAsync: Timeout bij wachten op semaphore voor {ConnectionName}.",
                    _config.ConnectionName
                );
                if (IsConnected)
                    IsConnected = false; // Forceer status als we niet verder kunnen
                return;
            }

            try
            {
                _specificLogger.Information(
                    "Verbinding met OPC UA server verbreken voor {ConnectionName}...",
                    _config.ConnectionName
                );

                await StopMonitoringTagsAsync().ConfigureAwait(false);

                lock (_sessionLock)
                {
                    if (_reconnectHandler != null)
                    {
                        _reconnectHandler.CancelReconnect();
                        _reconnectHandler.Dispose();
                        _reconnectHandler = null;
                    }
                }

                if (_session != null)
                {
                    _session.KeepAlive -= Session_KeepAlive_EventHandler;
                    await _session.CloseAsync(10000).ConfigureAwait(false); // Timeout voor de close operatie
                    _session.Dispose();
                    _session = null;
                }

                IsConnected = false; // Zet status en trigger event
                _specificLogger.Information(
                    "Verbinding met OPC UA server verbroken voor {ConnectionName}.",
                    _config.ConnectionName
                );
            }
            catch (Exception ex)
            {
                _specificLogger.Error(
                    ex,
                    "Fout bij verbreken van OPC UA verbinding voor {ConnectionName}.",
                    _config.ConnectionName
                );
                IsConnected = false;
                _session?.Dispose();
                _session = null;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <inheritdoc/>
        public void Reconfigure(OpcUaConnectionConfig newConfig)
        {
            if (newConfig == null)
            {
                _specificLogger.Error("Reconfigure aangeroepen met null nieuwe configuratie.");
                throw new ArgumentNullException(nameof(newConfig));
            }

            _specificLogger.Information(
                "Herconfigureren van OpcUaService van '{OldConnectionName}' (Endpoint: {OldEndpoint}) naar '{NewConnectionName}' (Endpoint: {NewEndpoint})",
                _config?.ConnectionName ?? "N/A",
                _config?.EndpointUrl ?? "N/A",
                newConfig.ConnectionName,
                newConfig.EndpointUrl
            );

            bool endpointChanged =
                _config == null
                || _config.EndpointUrl != newConfig.EndpointUrl
                || _config.SecurityMode != newConfig.SecurityMode
                || _config.SecurityPolicyUri != newConfig.SecurityPolicyUri
                || _config.UserName != newConfig.UserName
                || _config.Password != newConfig.Password;

            bool monitoringParamsChanged =
                _config == null
                || HaveMonitoringParametersChanged(_config.TagsToMonitor, newConfig.TagsToMonitor);

            _config = CreateDeepCopy(newConfig);

            if (IsConnected)
            {
                if (endpointChanged)
                {
                    _specificLogger.Information(
                        "Endpoint parameters gewijzigd voor {ConnectionName}, verbinding wordt herstart.",
                        _config.ConnectionName
                    );
                    Task.Run(async () =>
                        {
                            await DisconnectAsync().ConfigureAwait(false);
                            await Task.Delay(500).ConfigureAwait(false);
                            await ConnectAsync().ConfigureAwait(false);
                        })
                        .ConfigureAwait(false);
                }
                else if (monitoringParamsChanged)
                {
                    _specificLogger.Information(
                        "OPC UA monitoring parameters gewijzigd voor {ConnectionName}, herstart monitoring.",
                        _config.ConnectionName
                    );
                    Task.Run(async () =>
                        {
                            await StopMonitoringTagsAsync().ConfigureAwait(false);
                            await Task.Delay(200).ConfigureAwait(false);
                            await StartMonitoringTagsAsync().ConfigureAwait(false);
                        })
                        .ConfigureAwait(false);
                }
                else
                {
                    _specificLogger.Debug(
                        "Client-side tag parameters (bijv. alarmgrenzen) gewijzigd voor {ConnectionName}. OPC UA Subscription wordt NIET herstart.",
                        _config.ConnectionName
                    );
                }
            }
            else if (endpointChanged || monitoringParamsChanged)
            {
                _specificLogger.Information(
                    "Configuratie gewijzigd voor {ConnectionName} terwijl niet verbonden. Wijzigingen worden toegepast bij volgende ConnectAsync.",
                    _config.ConnectionName
                );
            }
        }

        #region Reconnect Logic Callbacks
        /// <summary>
        /// Deze methode wordt de CALLBACK voor de SessionReconnectHandler's BeginReconnect *methode*.
        /// De handler zelf heeft geen publieke BeginReconnect *event*.
        /// </summary>
        private void Session_ReconnectAttemptComplete_Handler(object sender, EventArgs e)
        {
            lock (_sessionLock)
            {
                if (_reconnectHandler == null || !ReferenceEquals(sender, _reconnectHandler))
                {
                    _specificLogger.Debug(
                        "Session_ReconnectAttemptComplete_Handler: Callback van oude/ongeldige handler genegeerd voor {ConnectionName}.",
                        _config.ConnectionName
                    );
                    return;
                }

                var reconnectedSession = _reconnectHandler.Session;

                _specificLogger.Information(
                    "Session_ReconnectAttemptComplete_Handler: Poging voltooid voor {ConnectionName}. Handler's sessie (ID: {HandlerSessionId}) Verbonden: {IsConnected}",
                    _config.ConnectionName,
                    reconnectedSession?.SessionId?.ToString() ?? "NULL",
                    reconnectedSession?.Connected ?? false
                );

                if (reconnectedSession != null && reconnectedSession.Connected)
                {
                    if (!ReferenceEquals(_session, reconnectedSession))
                    {
                        _specificLogger.Information(
                            "Nieuwe sessie (ID: {NewId}) na reconnect voor {ConnectionName}. Oude sessie (ID: {OldId}) opruimen.",
                            reconnectedSession.SessionId,
                            _config.ConnectionName,
                            _session?.SessionId.ToString() ?? "NULL"
                        );
                        if (_session != null)
                            _session.KeepAlive -= Session_KeepAlive_EventHandler;
                        Utils.SilentDispose(_session);
                        _session = (Session)reconnectedSession;
                        _session.KeepAlive += Session_KeepAlive_EventHandler;
                    }

                    IsConnected = true;
                    _specificLogger.Information(
                        "Succesvol herverbonden met server: {EndpointUrl} voor {ConnectionName}",
                        _config.EndpointUrl,
                        _config.ConnectionName
                    );

                    _specificLogger.Information(
                        "Start monitoring opnieuw na succesvolle reconnect voor {ConnectionName}...",
                        _config.ConnectionName
                    );
                    Task.Run(async () =>
                        {
                            await StopMonitoringTagsAsync().ConfigureAwait(false); // Stop eventuele oude
                            await Task.Delay(200).ConfigureAwait(false);
                            await StartMonitoringTagsAsync().ConfigureAwait(false); // Start met nieuwe sessie
                        })
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                _specificLogger.Error(
                                    t.Exception,
                                    "Fout bij herstarten monitoring na reconnect."
                                );
                        });
                }
                else
                {
                    _specificLogger.Error(
                        "Session_ReconnectAttemptComplete_Handler: Herverbinden MISLUKT voor {ConnectionName}. Handler's sessie niet verbonden.",
                        _config.ConnectionName
                    );
                    IsConnected = false;
                }
            }
        }

        /// <summary>
        /// Event handler voor OPC UA sessie KeepAlive events. Monitort de gezondheid van de sessie.
        /// Als de KeepAlive faalt, wordt <see cref="IsConnected"/> op false gezet.
        /// De <see cref="SessionReconnectHandler"/> zou dit moeten detecteren en het herverbindingsproces initiëren.
        /// </summary>
        private void Session_KeepAlive_EventHandler(ISession session, KeepAliveEventArgs e) // Hernoemd voor duidelijkheid
        {
            lock (_sessionLock)
            {
                if (
                    session == null
                    || _session == null
                    || !ReferenceEquals(session, _session)
                    || _reconnectHandler == null
                )
                {
                    _specificLogger.Verbose(
                        "Session_KeepAlive: Event van een oude of ongeldige sessie/handler genegeerd."
                    );
                    return;
                }

                if (ServiceResult.IsBad(e.Status))
                {
                    _specificLogger.Warning(
                        "OPC UA Sessie KeepAlive mislukt voor {ConnectionName}: {Status}. Server status: {CurrentState}. SessionReconnectHandler zal proberen te herverbinden.",
                        _config.ConnectionName,
                        e.Status,
                        e.CurrentState
                    );
                    IsConnected = false;
                }
                else
                {
                    _specificLogger.Verbose(
                        "OPC UA Sessie KeepAlive succesvol ontvangen voor {ConnectionName}. Server status: {CurrentState}",
                        _config.ConnectionName,
                        e.CurrentState
                    );
                }
            }
        }

        /// <summary>
        /// Event handler die wordt aangeroepen wanneer een OPC UA sessie herverbindingspoging is voltooid door de <see cref="SessionReconnectHandler"/>.
        /// Werkt de sessie en monitoring bij indien succesvol.
        /// </summary>
        private void Client_ReconnectComplete_EventHandler(object sender, EventArgs e) // Hernoemd voor duidelijkheid
        {
            lock (_sessionLock)
            {
                if (_reconnectHandler == null || !ReferenceEquals(sender, _reconnectHandler))
                {
                    _specificLogger.Debug(
                        "Client_ReconnectComplete ({ConnectionName}): Callback van een oude of ongeldige ReconnectHandler genegeerd.",
                        _config.ConnectionName
                    );
                    return;
                }

                _specificLogger.Information(
                    "Client_ReconnectComplete ({ConnectionName}): Reconnect poging voltooid. Handler's sessie (ID: {HandlerSessionId}) status: Verbonden={IsHandlerSessionConnected}",
                    _config.ConnectionName,
                    _reconnectHandler.Session?.SessionId?.ToString() ?? "NULL",
                    _reconnectHandler.Session?.Connected ?? false
                );

                if (_reconnectHandler.Session != null && _reconnectHandler.Session.Connected)
                {
                    if (!ReferenceEquals(_session, _reconnectHandler.Session))
                    {
                        _specificLogger.Information(
                            "Nieuwe sessie instantie (ID: {NewSessionId}) na reconnect voor {ConnectionName}. Oude sessie (ID: {OldSessionId}) wordt opgeruimd.",
                            _reconnectHandler.Session.SessionId,
                            _config.ConnectionName,
                            _session?.SessionId?.ToString() ?? "NULL"
                        );

                        if (_session != null)
                        {
                            _session.KeepAlive -= Session_KeepAlive_EventHandler;
                            Utils.SilentDispose(_session);
                        }

                        _session = (Session)_reconnectHandler.Session; // Gebruik de nieuwe/herstelde sessie
                        _session.KeepAlive += Session_KeepAlive_EventHandler;
                    }

                    IsConnected = true;
                    _specificLogger.Information(
                        "Succesvol herverbonden met OPC UA server: {EndpointUrl} voor {ConnectionName}",
                        _config.EndpointUrl,
                        _config.ConnectionName
                    );

                    _specificLogger.Information(
                        "Start monitoring opnieuw na succesvolle reconnect voor {ConnectionName}...",
                        _config.ConnectionName
                    );
                    Task.Run(async () =>
                        {
                            await StopMonitoringTagsAsync().ConfigureAwait(false);
                            await Task.Delay(200).ConfigureAwait(false);
                            await StartMonitoringTagsAsync().ConfigureAwait(false);
                        })
                        .ContinueWith(
                            t =>
                            {
                                if (t.IsFaulted && t.Exception != null)
                                {
                                    _specificLogger.Error(
                                        t.Exception,
                                        "Fout tijdens herstarten monitoring na reconnect voor {ConnectionName}.",
                                        _config.ConnectionName
                                    );
                                }
                            },
                            TaskScheduler.Default
                        );
                }
                else
                {
                    _specificLogger.Error(
                        "Client_ReconnectComplete ({ConnectionName}): Herverbinden met OPC UA server MISLUKT. Handler's sessie niet verbonden.",
                        _config.ConnectionName
                    );
                    IsConnected = false;
                }
            }
        }
        #endregion
    }
}
