using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Data_Logger.ViewModels;
using Newtonsoft.Json;
using Opc.Ua;
using Opc.Ua.Client;
using Serilog;

namespace Data_Logger.Services.Implementations
{
    public class OpcUaService : IOpcUaService
    {
        #region Fields

        private readonly ILogger _logger;
        private OpcUaConnectionConfig _config;
        private Session _session;
        private Subscription _subscription;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly ApplicationConfiguration _appConfig;
        private bool _isConnected;

        private SessionReconnectHandler _reconnectHandler;
        private readonly object _sessionLock = new object();

        private const int InitialReconnectDelayMs = 2000;
        private const int MaxReconnectDelayMs = 30000;

        #endregion

        #region Properties

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

        public NamespaceTable NamespaceUris => _session?.NamespaceUris;

        #endregion

        #region Events

        public event EventHandler ConnectionStatusChanged;
        public event EventHandler<IEnumerable<LoggedTagValue>> TagsDataReceived;

        #endregion

        #region Constructor

        public OpcUaService(
            ILogger logger,
            OpcUaConnectionConfig config,
            ApplicationConfiguration appConfig
        )
        {
            _logger =
                logger
                    ?.ForContext<OpcUaService>()
                    .ForContext(
                        "ConnectionName",
                        config?.ConnectionName ?? "UnknownOpcUaConnection"
                    ) ?? throw new ArgumentNullException(nameof(logger));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
            _logger.Debug(
                "OpcUaService geïnstantieerd voor {ConnectionName}",
                _config.ConnectionName
            );
        }

        #endregion

        #region Connection Management

        public async Task<bool> ConnectAsync()
        {
            if (IsConnected)
                return true;

            await _semaphore.WaitAsync();
            try
            {
                if (IsConnected)
                    return true;

                _logger.Information(
                    "Bezig met verbinden met OPC UA server: {EndpointUrl} voor connectie {ConnectionName}",
                    _config.EndpointUrl,
                    _config.ConnectionName
                );

                EndpointDescription endpointDescription = null;
                EndpointDescriptionCollection serverEndpoints = null;

                try
                {
                    var discoveryEndpointConfiguration = EndpointConfiguration.Create(_appConfig);
                    discoveryEndpointConfiguration.OperationTimeout = 5000;

                    using (
                        var discoveryClient = DiscoveryClient.Create(
                            _appConfig,
                            new Uri(_config.EndpointUrl),
                            discoveryEndpointConfiguration
                        )
                    )
                    {
                        _logger.Information(
                            "Probeert endpoints op te halen van {DiscoveryUrl} voor {ConnectionName}",
                            discoveryClient.Endpoint.EndpointUrl,
                            _config.ConnectionName
                        );
                        serverEndpoints = await discoveryClient
                            .GetEndpointsAsync(null)
                            .ConfigureAwait(false);
                        _logger.Information(
                            "{Count} endpoints ontvangen van server {EndpointUrl} voor {ConnectionName}.",
                            serverEndpoints?.Count ?? 0,
                            _config.EndpointUrl,
                            _config.ConnectionName
                        );
                    }

                    if (serverEndpoints == null || serverEndpoints.Count == 0)
                    {
                        _logger.Error(
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
                catch (Exception ex)
                {
                    _logger.Error(
                        ex,
                        "Fout bij het ophalen of selecteren van het OPC UA endpoint voor {EndpointUrl} (Conn: {ConnectionName})",
                        _config.EndpointUrl,
                        _config.ConnectionName
                    );
                    return false;
                }

                if (endpointDescription == null)
                {
                    _logger.Error(
                        "Geen geschikt OPC UA endpoint gevonden voor {EndpointUrl} (Conn: {ConnectionName}) met huidige security instellingen.",
                        _config.EndpointUrl,
                        _config.ConnectionName
                    );
                    return false;
                }

                _logger.Information(
                    "Geselecteerd endpoint voor {ConnectionName}: {SelectedEndpointUrl}, SecurityMode: {SecurityMode}, SecurityPolicy: {SecurityPolicy}",
                    _config.ConnectionName,
                    endpointDescription.EndpointUrl,
                    endpointDescription.SecurityMode,
                    endpointDescription.SecurityPolicyUri
                );

                bool userSpecifiedSecurity =
                    !string.IsNullOrEmpty(_config.SecurityPolicyUri)
                    && _config.SecurityPolicyUri != SecurityPolicies.None;
                if (
                    userSpecifiedSecurity
                    && (
                        endpointDescription.SecurityMode != _config.SecurityMode
                        || endpointDescription.SecurityPolicyUri != _config.SecurityPolicyUri
                    )
                )
                {
                    _logger.Information(
                        "Gebruiker heeft specifieke security policy ({UserPolicy}) en mode ({UserMode}) opgegeven voor {ConnectionName}.",
                        _config.SecurityPolicyUri,
                        _config.SecurityMode,
                        _config.ConnectionName
                    );
                    var matchingEndpoint = serverEndpoints.FirstOrDefault(ep =>
                        IsEndpointUrlMatch(ep.EndpointUrl, endpointDescription.EndpointUrl)
                        && ep.SecurityMode == _config.SecurityMode
                        && ep.SecurityPolicyUri == _config.SecurityPolicyUri
                    );
                    if (matchingEndpoint != null)
                    {
                        endpointDescription = matchingEndpoint;
                        _logger.Information(
                            "Endpoint voor {ConnectionName} succesvol aangepast aan gebruikersspecificatie: Policy {SecurityPolicy}, Mode {SecurityMode}",
                            _config.ConnectionName,
                            endpointDescription.SecurityPolicyUri,
                            endpointDescription.SecurityMode
                        );
                    }
                    else
                    {
                        _logger.Warning(
                            "Kon geen endpoint vinden voor {ConnectionName} dat exact overeenkomt met gespecificeerde SecurityMode '{UserMode}' en Policy '{UserPolicy}'. Valt terug op automatisch geselecteerd endpoint.",
                            _config.ConnectionName,
                            _config.SecurityMode,
                            _config.SecurityPolicyUri
                        );
                    }
                }

                var endpointConfiguration = EndpointConfiguration.Create(_appConfig);
                var configuredEndpoint = new ConfiguredEndpoint(
                    null,
                    endpointDescription,
                    endpointConfiguration
                );

                _session = await Session
                    .Create(
                        _appConfig,
                        configuredEndpoint,
                        updateBeforeConnect: false,
                        checkDomain: false,
                        sessionName: $"{_appConfig.ApplicationName} Session ({_config.ConnectionName})",
                        sessionTimeout: (uint)(
                            _appConfig.ClientConfiguration.DefaultSessionTimeout > 0
                                ? _appConfig.ClientConfiguration.DefaultSessionTimeout
                                : 60000
                        ),
                        identity: GetUserIdentity(),
                        preferredLocales: null
                    )
                    .ConfigureAwait(false);

                if (_session == null)
                {
                    _logger.Error(
                        "Kon geen OPC UA sessie aanmaken met {EndpointUrl} voor {ConnectionName}",
                        _config.EndpointUrl,
                        _config.ConnectionName
                    );
                    return false;
                }

                _session.KeepAlive += Session_KeepAlive;

                lock (_sessionLock)
                {
                    if (_reconnectHandler != null)
                    {
                        _reconnectHandler.Dispose();
                    }

                    _reconnectHandler = new SessionReconnectHandler(true, MaxReconnectDelayMs);
                }

                IsConnected = true;
                _logger.Information(
                    "Succesvol verbonden met OPC UA server: {EndpointUrl} voor {ConnectionName}",
                    _config.EndpointUrl,
                    _config.ConnectionName
                );
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Algemene fout bij verbinden met OPC UA server {EndpointUrl} (Conn: {ConnectionName})",
                    _config.EndpointUrl,
                    _config.ConnectionName
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

        public async Task DisconnectAsync()
        {
            if (!IsConnected)
                return;
            await _semaphore.WaitAsync();
            try
            {
                _logger.Information(
                    "Verbinding met OPC UA server verbreken: {EndpointUrl} (Conn: {ConnectionName})",
                    _config.EndpointUrl,
                    _config.ConnectionName
                );
                await StopMonitoringTagsAsync();

                lock (_sessionLock)
                {
                    if (_reconnectHandler != null)
                    {
                        _reconnectHandler.Dispose();
                        _reconnectHandler = null;
                    }
                }

                _session?.Close();
                _session?.Dispose();
                _session = null;
                IsConnected = false;
                _logger.Information(
                    "Verbinding met OPC UA server verbroken voor {ConnectionName}.",
                    _config.ConnectionName
                );
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout bij verbreken van OPC UA verbinding voor {ConnectionName}.",
                    _config.ConnectionName
                );
                IsConnected = false;
                _session = null;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Reconfigure(OpcUaConnectionConfig newConfig)
        {
            _logger.Information(
                "Herconfigureren van OpcUaService {OldConnectionName} naar {NewConnectionName}. Endpoint: {NewEndpoint}",
                _config?.ConnectionName ?? "N/A",
                newConfig.ConnectionName,
                newConfig.EndpointUrl
            );

            if (_config == null)
            {
                _config = CreateDeepCopy(newConfig);
                _logger.Information(
                    "Eerste configuratie voor OpcUaService, geen vergelijking nodig."
                );

                if (IsConnected)
                {
                    Task.Run(async () =>
                    {
                        await StopMonitoringTagsAsync();
                        await StartMonitoringTagsAsync();
                    });
                }

                return;
            }

            bool endpointChanged =
                _config.EndpointUrl != newConfig.EndpointUrl
                || _config.SecurityMode != newConfig.SecurityMode
                || _config.SecurityPolicyUri != newConfig.SecurityPolicyUri
                || _config.UserName != newConfig.UserName
                || _config.Password != newConfig.Password;

            bool monitoringParametersChanged = false;
            if (!endpointChanged)
            {
                monitoringParametersChanged = HaveMonitoringParametersChanged(
                    _config.TagsToMonitor,
                    newConfig.TagsToMonitor
                );
            }

            _config = CreateDeepCopy(newConfig);

            if (IsConnected)
            {
                if (endpointChanged)
                {
                    _logger.Information(
                        "Endpoint parameters gewijzigd voor {ConnectionName}, verbinding wordt herstart.",
                        _config.ConnectionName
                    );
                    Task.Run(async () =>
                    {
                        await DisconnectAsync();
                        await ConnectAsync();
                    });
                }
                else if (monitoringParametersChanged)
                {
                    _logger.Information(
                        "OPC UA monitoring parameters (NodeIds, SamplingIntervals, Actieve status) gewijzigd voor {ConnectionName}, herstart monitoring.",
                        _config.ConnectionName
                    );
                    Task.Run(async () =>
                    {
                        await StopMonitoringTagsAsync();
                        await StartMonitoringTagsAsync();
                    });
                }
                else
                {
                    _logger.Information(
                        "Alleen client-side tag parameters (bijv. alarmgrenzen) gewijzigd voor {ConnectionName}. OPC UA Subscription wordt NIET herstart.",
                        _config.ConnectionName
                    );
                }
            }
        }

        private OpcUaConnectionConfig CreateDeepCopy(OpcUaConnectionConfig original)
        {
            if (original == null)
                return null;
            try
            {
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Objects,
                };
                string serialized = JsonConvert.SerializeObject(original, settings);
                return JsonConvert.DeserializeObject<OpcUaConnectionConfig>(serialized, settings);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Kon geen diepe kopie maken van OpcUaConnectionConfig. Fallback naar shallow copy (RISKANT)."
                );

                throw;
            }
        }

        private bool HaveMonitoringParametersChanged(
            ObservableCollection<OpcUaTagConfig> oldTags,
            ObservableCollection<OpcUaTagConfig> newTags
        )
        {
            _logger.Debug(
                "HaveMonitoringParametersChanged: Vergelijkt oude tags (Count={OldCount}) met nieuwe tags (Count={NewCount})",
                oldTags?.Count ?? -1,
                newTags?.Count ?? -1
            );

            var oldActiveMonitoringParams = oldTags
                .Where(t => t.IsActive)
                .Select(t => new { t.NodeId, t.SamplingInterval })
                .OrderBy(t => t.NodeId)
                .ToList();

            var newActiveMonitoringParams = newTags
                .Where(t => t.IsActive)
                .Select(t => new { t.NodeId, t.SamplingInterval })
                .OrderBy(t => t.NodeId)
                .ToList();

            try
            {
                _logger.Debug(
                    "Old Active Monitoring Params ({Count}): {ParamsJson}",
                    oldActiveMonitoringParams.Count,
                    JsonConvert.SerializeObject(oldActiveMonitoringParams, Formatting.None)
                );
                _logger.Debug(
                    "New Active Monitoring Params ({Count}): {ParamsJson}",
                    newActiveMonitoringParams.Count,
                    JsonConvert.SerializeObject(newActiveMonitoringParams, Formatting.None)
                );
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout bij het serialiseren van monitoring params voor logging in HaveMonitoringParametersChanged."
                );
            }

            bool areSequentiallyEqual = oldActiveMonitoringParams.SequenceEqual(
                newActiveMonitoringParams
            );
            _logger.Debug(
                "HaveMonitoringParametersChanged: Resultaat SequenceEqual was {SeqEqual}. Return: {ReturnValue}",
                areSequentiallyEqual,
                !areSequentiallyEqual
            );

            return !areSequentiallyEqual;
        }

        private IUserIdentity GetUserIdentity()
        {
            if (!string.IsNullOrEmpty(_config.UserName))
            {
                return new UserIdentity(_config.UserName, _config.Password ?? string.Empty);
            }

            return new UserIdentity();
        }

        private bool IsEndpointUrlMatch(string urlFromServerList, string selectedUrl)
        {
            if (string.Equals(urlFromServerList, selectedUrl, StringComparison.OrdinalIgnoreCase))
                return true;
            try
            {
                var uriFromServer = new Uri(urlFromServerList);
                var uriSelected = new Uri(selectedUrl);
                return uriFromServer.Scheme == uriSelected.Scheme
                    && string.Equals(
                        uriFromServer.DnsSafeHost,
                        uriSelected.DnsSafeHost,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && uriFromServer.Port == uriSelected.Port;
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        private void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            lock (_sessionLock)
            {
                if (
                    session == null
                    || _session == null
                    || session.SessionId != _session.SessionId
                    || _reconnectHandler == null
                )
                {
                    return;
                }

                if (ServiceResult.IsBad(e.Status))
                {
                    _logger.Warning(
                        "OPC UA Sessie KeepAlive mislukt voor {ConnectionName}: {Status}. Server status: {CurrentState}. Start Reconnect.",
                        _config.ConnectionName,
                        e.Status,
                        e.CurrentState
                    );

                    IsConnected = false;

                    if (_reconnectHandler.State == SessionReconnectHandler.ReconnectState.Ready)
                    {
                        _logger.Information(
                            "Session_KeepAlive: Reconnect wordt gestart met initiële vertraging {InitialReconnectDelayMs}ms omdat de handler 'Ready' is.",
                            InitialReconnectDelayMs
                        );
                        _reconnectHandler.BeginReconnect(
                            _session,
                            InitialReconnectDelayMs,
                            Client_ReconnectComplete
                        );
                    }
                    else
                    {
                        _logger.Debug(
                            "Session_KeepAlive: Reconnect NIET opnieuw gestart. Handler is al bezig of niet in de juiste staat. Huidige staat handler: {CurrentState}.",
                            _reconnectHandler.State
                        );
                    }

                    return;
                }

                _logger.Debug(
                    "OPC UA Sessie KeepAlive succesvol ontvangen voor {ConnectionName}. Server status: {CurrentState}",
                    _config.ConnectionName,
                    e.CurrentState
                );
            }
        }

        private void Client_BeginReconnect(object sender, EventArgs e)
        {
            if (sender is SessionReconnectHandler handler)
            {
                _logger.Information(
                    "Client_BeginReconnect: Reconnect proces gestart voor sessie. Huidige staat handler: {State}",
                    handler.State
                );
            }
        }

        private void Client_ReconnectComplete(object sender, EventArgs e)
        {
            lock (_sessionLock)
            {
                if (_reconnectHandler == null || !Object.ReferenceEquals(sender, _reconnectHandler))
                {
                    _logger.Debug(
                        "Client_ReconnectComplete: Callback van een oude of ongeldige ReconnectHandler genegeerd."
                    );
                    return;
                }

                _logger.Information(
                    "Client_ReconnectComplete: Reconnect poging voltooid. Nieuwe sessie staat: {SessionState}",
                    _reconnectHandler.Session?.SessionName ?? "NULL"
                );

                if (_reconnectHandler.Session != null && _reconnectHandler.Session.Connected)
                {
                    if (!Object.ReferenceEquals(_session, _reconnectHandler.Session))
                    {
                        _logger.Information(
                            "Nieuwe sessie instantie ({NewSessionName}) na reconnect. Oude sessie ({OldSessionName}) wordt opgeruimd.",
                            _reconnectHandler.Session.SessionName,
                            _session?.SessionName ?? "NULL"
                        );

                        if (_session != null)
                        {
                            _session.KeepAlive -= Session_KeepAlive;
                            Utils.SilentDispose(_session);
                        }

                        _session = (Session)_reconnectHandler.Session;
                        _session.KeepAlive += Session_KeepAlive;
                    }
                    else
                    {
                        _logger.Information(
                            "Bestaande sessie ({SessionName}) is gereactiveerd na reconnect.",
                            _session.SessionName
                        );

                        _session.KeepAlive -= Session_KeepAlive;
                        _session.KeepAlive += Session_KeepAlive;
                    }

                    IsConnected = true;
                    _logger.Information(
                        "Succesvol herverbonden met OPC UA server: {EndpointUrl} voor {ConnectionName}",
                        _config.EndpointUrl,
                        _config.ConnectionName
                    );

                    _logger.Information("Start monitoring opnieuw na succesvolle reconnect...");
                    Task.Run(async () =>
                        {
                            await StopMonitoringTagsAsync();
                            await StartMonitoringTagsAsync();
                        })
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                            {
                                _logger.Error(
                                    t.Exception,
                                    "Fout tijdens herstarten monitoring na reconnect."
                                );
                            }
                        });
                }
                else
                {
                    _logger.Error(
                        "Client_ReconnectComplete: Herverbinden met OPC UA server MISLUKT voor {ConnectionName}. Sessie niet verbonden.",
                        _config.ConnectionName
                    );
                }
            }
        }

        #endregion

        #region Tag Monitoring (Subscriptions)

        public async Task StartMonitoringTagsAsync()
        {
            if (!IsConnected || _session == null)
            {
                _logger.Information(
                    "Kan monitoring niet starten voor {ConnectionName}: niet verbonden of geen sessie.",
                    _config.ConnectionName
                );
                return;
            }

            if (
                _config == null
                || _config.TagsToMonitor == null
                || !_config.TagsToMonitor.Any(t => t.IsActive)
            )
            {
                _logger.Information(
                    "Kan monitoring niet starten voor {ConnectionName}: geen actieve tags geconfigureerd.",
                    _config.ConnectionName
                );
                return;
            }

            await _semaphore.WaitAsync();
            try
            {
                if (_subscription != null)
                {
                    _logger.Information(
                        "Verwijdert bestaande subscription voor {ConnectionName} alvorens een nieuwe te starten.",
                        _config.ConnectionName
                    );
                    try
                    {
                        _subscription.Delete(true);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(
                            ex,
                            "Fout bij verwijderen oude subscription voor {ConnectionName}.",
                            _config.ConnectionName
                        );
                    }

                    _subscription.Dispose();
                    _subscription = null;
                }

                _subscription = new Subscription(_session.DefaultSubscription)
                {
                    DisplayName = $"{_config.ConnectionName} Subscription",
                    PublishingInterval = _config
                        .TagsToMonitor.Where(t => t.IsActive && t.SamplingInterval > 0)
                        .Any()
                        ? _config
                            .TagsToMonitor.Where(t => t.IsActive && t.SamplingInterval > 0)
                            .Min(t => t.SamplingInterval)
                        : 1000,
                    KeepAliveCount = 10,
                    LifetimeCount = 30,
                    MaxNotificationsPerPublish = 0,
                    PublishingEnabled = true,
                    TimestampsToReturn = TimestampsToReturn.Both,
                };

                var itemsToMonitor = new List<MonitoredItem>();
                foreach (var tagConfig in _config.TagsToMonitor.Where(t => t.IsActive))
                {
                    try
                    {
                        var item = new MonitoredItem(_subscription.DefaultItem)
                        {
                            DisplayName = tagConfig.TagName,
                            StartNodeId = ParseNodeId(tagConfig.NodeId),
                            AttributeId = Attributes.Value,
                            SamplingInterval =
                                tagConfig.SamplingInterval > 0 ? tagConfig.SamplingInterval : -1,
                            QueueSize = 1,
                            DiscardOldest = true,
                        };
                        item.Notification += OnMonitoredItemNotification;
                        itemsToMonitor.Add(item);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(
                            ex,
                            "Fout bij voorbereiden MonitoredItem voor NodeId {NodeId} (Tag: {TagName}) voor {ConnectionName}.",
                            tagConfig.NodeId,
                            tagConfig.TagName,
                            _config.ConnectionName
                        );
                    }
                }

                if (!itemsToMonitor.Any())
                {
                    _logger.Information(
                        "Geen actieve en valide tags om te monitoren voor {ConnectionName}",
                        _config.ConnectionName
                    );
                    _subscription.Dispose();
                    _subscription = null;
                    return;
                }

                _subscription.AddItems(itemsToMonitor);
                _logger.Information(
                    "Probeert subscription aan te maken voor {ItemCount} items op {ConnectionName}",
                    itemsToMonitor.Count,
                    _config.ConnectionName
                );
                _session.AddSubscription(_subscription);
                await _subscription.CreateAsync().ConfigureAwait(false);

                _logger.Information(
                    "Subscription aangemaakt voor {ConnectionName}. Client ingesteld PublishingInterval: {ClientSetPubInt}ms. Server gereviseerd CurrentPublishingInterval: {RevisedPubInt}ms. Client ingesteld KeepAliveCount: {ClientSetKeepAlive}, Server gereviseerd CurrentKeepAliveCount: {RevisedKeepAlive}",
                    _config.ConnectionName,
                    _subscription.PublishingInterval,
                    _subscription.CurrentPublishingInterval,
                    _subscription.KeepAliveCount,
                    _subscription.CurrentKeepAliveCount
                );

                foreach (var monitoredItem in _subscription.MonitoredItems)
                {
                    _logger.Information(
                        "Tag: {DisplayName} (NodeId: {NodeId}) - Client gevraagd SamplingInterval: {RequestedSamplingInt}ms. Server gereviseerd SamplingInterval: {RevisedSamplingInt}ms. Client gevraagd QueueSize: {RequestedQueueSize}, Server gereviseerd QueueSize: {RevisedQueueSize}",
                        monitoredItem.DisplayName,
                        monitoredItem.StartNodeId,
                        monitoredItem.SamplingInterval,
                        monitoredItem.QueueSize
                    );
                }

                await _subscription.ApplyChangesAsync().ConfigureAwait(false);
                _logger.Information(
                    "Subscription succesvol aangemaakt en items worden gemonitord voor {ConnectionName}",
                    _config.ConnectionName
                );

                var initialReadValueIds = new ReadValueIdCollection();
                List<OpcUaTagConfig> tagsForInitialRead = new List<OpcUaTagConfig>();

                foreach (var item in _subscription.MonitoredItems.Where(mi => mi.Status.Created))
                {
                    var tagConfig = _config.TagsToMonitor.FirstOrDefault(tc =>
                        tc.NodeId == item.StartNodeId.ToString() && tc.IsActive
                    );
                    if (tagConfig != null)
                    {
                        initialReadValueIds.Add(
                            new ReadValueId
                            {
                                NodeId = item.StartNodeId,
                                AttributeId = Attributes.Value,
                            }
                        );
                        tagsForInitialRead.Add(tagConfig);
                        _logger.Debug(
                            "Voorbereiden initiele leesactie voor tag: {TagName} (NodeId: {NodeId})",
                            tagConfig.TagName,
                            tagConfig.NodeId
                        );
                    }
                }

                if (initialReadValueIds.Any())
                {
                    _logger.Information(
                        "Uitvoeren initiele leesactie voor {Count} tags na start monitoring.",
                        initialReadValueIds.Count
                    );
                    try
                    {
                        var response = await _session
                            .ReadAsync(
                                null,
                                0,
                                TimestampsToReturn.Source,
                                initialReadValueIds,
                                CancellationToken.None
                            )
                            .ConfigureAwait(false);

                        DataValueCollection results = response.Results;
                        ClientBase.ValidateResponse(results, initialReadValueIds);

                        List<LoggedTagValue> initialLoggedValues = new List<LoggedTagValue>();
                        for (int i = 0; i < results.Count; i++)
                        {
                            var tagConfig = tagsForInitialRead.FirstOrDefault(tc =>
                                initialReadValueIds[i].NodeId.ToString() == tc.NodeId
                            );
                            if (tagConfig != null)
                            {
                                var loggedValue = new LoggedTagValue
                                {
                                    TagName = tagConfig.TagName,
                                    Value = results[i].Value,
                                    Timestamp =
                                        results[i].SourceTimestamp != DateTime.MinValue
                                            ? results[i].SourceTimestamp
                                            : results[i].ServerTimestamp,
                                    IsGoodQuality = StatusCode.IsGood(results[i].StatusCode),
                                    ErrorMessage = StatusCode.IsBad(results[i].StatusCode)
                                        ? results[i].StatusCode.ToString()
                                        : null,
                                };
                                initialLoggedValues.Add(loggedValue);
                                _logger.Debug(
                                    "Initiele waarde voor {TagName}: {Value}, Kwaliteit: {Quality}, Tijd: {Timestamp}",
                                    loggedValue.TagName,
                                    loggedValue.Value,
                                    loggedValue.IsGoodQuality,
                                    loggedValue.Timestamp
                                );
                            }
                        }

                        TagsDataReceived?.Invoke(this, initialLoggedValues);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(
                            ex,
                            "Fout tijdens initiele leesactie na start monitoring voor {ConnectionName}",
                            _config.ConnectionName
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout bij starten van OPC UA tag monitoring voor {ConnectionName}",
                    _config.ConnectionName
                );
                if (_subscription != null)
                {
                    try
                    {
                        _subscription.Delete(true);
                    }
                    catch
                    {
                        /* ignore */
                    }

                    _subscription.Dispose();
                    _subscription = null;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task StopMonitoringTagsAsync()
        {
            if (_subscription == null)
                return;
            await _semaphore.WaitAsync();
            try
            {
                if (_subscription == null || !_session.Subscriptions.Contains(_subscription))
                {
                    _logger.Debug(
                        "Subscription al verwijderd of niet aanwezig in sessie voor {ConnectionName}.",
                        _config.ConnectionName
                    );
                    _subscription?.Dispose();
                    _subscription = null;
                    return;
                }

                _logger.Information(
                    "Stopt OPC UA tag monitoring voor {ConnectionName}",
                    _config.ConnectionName
                );
                _subscription.Delete(true);
                _session.RemoveSubscription(_subscription);
                _subscription.Dispose();
                _subscription = null;
                _logger.Information(
                    "OPC UA tag monitoring gestopt voor {ConnectionName}",
                    _config.ConnectionName
                );
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout bij stoppen van OPC UA tag monitoring voor {ConnectionName}",
                    _config.ConnectionName
                );

                _subscription?.Dispose();
                _subscription = null;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private void OnMonitoredItemNotification(
            MonitoredItem monitoredItem,
            MonitoredItemNotificationEventArgs e
        )
        {
            if (
                !(e.NotificationValue is MonitoredItemNotification notification)
                || notification.Value == null
            )
                return;

            var loggedValue = new LoggedTagValue
            {
                TagName = monitoredItem.DisplayName,
                Value = notification.Value.Value,
                Timestamp =
                    notification.Value.SourceTimestamp != DateTime.MinValue
                        ? notification.Value.SourceTimestamp
                        : notification.Value.ServerTimestamp,
                IsGoodQuality = StatusCode.IsGood(notification.Value.StatusCode),
                ErrorMessage = StatusCode.IsBad(notification.Value.StatusCode)
                    ? notification.Value.StatusCode.ToString()
                    : null,
            };
            TagsDataReceived?.Invoke(this, new List<LoggedTagValue> { loggedValue });
        }

        #endregion

        #region Data Reading

        public async Task<IEnumerable<LoggedTagValue>> ReadCurrentTagValuesAsync()
        {
            if (!IsConnected || _session == null)
            {
                _logger.Warning(
                    "Kan tags niet lezen voor {ConnectionName}: niet verbonden of geen sessie.",
                    _config.ConnectionName
                );
                return Enumerable.Empty<LoggedTagValue>();
            }

            if (
                _config == null
                || _config.TagsToMonitor == null
                || !_config.TagsToMonitor.Any(t => t.IsActive)
            )
            {
                _logger.Warning(
                    "Kan tags niet lezen voor {ConnectionName}: geen actieve tags geconfigureerd.",
                    _config.ConnectionName
                );
                return Enumerable.Empty<LoggedTagValue>();
            }

            var loggedValues = new List<LoggedTagValue>();
            var nodesToRead = new ReadValueIdCollection();
            var activeTagConfigs = _config.TagsToMonitor.Where(t => t.IsActive).ToList();

            foreach (var tagConfig in activeTagConfigs)
            {
                try
                {
                    nodesToRead.Add(
                        new ReadValueId
                        {
                            NodeId = ParseNodeId(tagConfig.NodeId),
                            AttributeId = Attributes.Value,
                        }
                    );
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        ex,
                        "Ongeldige NodeId {NodeId} voor tag {TagName} (Conn: {ConnectionName})",
                        tagConfig.NodeId,
                        tagConfig.TagName,
                        _config.ConnectionName
                    );
                    loggedValues.Add(
                        new LoggedTagValue
                        {
                            TagName = tagConfig.TagName,
                            IsGoodQuality = false,
                            ErrorMessage = $"Ongeldige NodeId: {tagConfig.NodeId}",
                            Timestamp = DateTime.UtcNow,
                        }
                    );
                }
            }

            if (!nodesToRead.Any())
                return loggedValues;

            try
            {
                _logger.Debug(
                    "Leest {Count} OPC UA tags voor {ConnectionName}.",
                    nodesToRead.Count,
                    _config.ConnectionName
                );
                var response = await _session
                    .ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Source,
                        nodesToRead,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                DataValueCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                ClientBase.ValidateResponse(results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                for (int i = 0; i < results.Count; i++)
                {
                    var correspondingTagConfig = activeTagConfigs.FirstOrDefault(tc =>
                    {
                        try
                        {
                            return ParseNodeId(tc.NodeId).Equals(nodesToRead[i].NodeId);
                        }
                        catch
                        {
                            return false;
                        }
                    });

                    if (correspondingTagConfig == null)
                    {
                        _logger.Warning(
                            "Kon geen overeenkomende tagConfig vinden voor gelezen NodeId {ReadNodeId} (Conn: {ConnectionName})",
                            nodesToRead[i].NodeId,
                            _config.ConnectionName
                        );
                        continue;
                    }

                    loggedValues.Add(
                        new LoggedTagValue
                        {
                            TagName = correspondingTagConfig.TagName,
                            Value = results[i].Value,
                            Timestamp =
                                results[i].SourceTimestamp != DateTime.MinValue
                                    ? results[i].SourceTimestamp
                                    : results[i].ServerTimestamp,
                            IsGoodQuality = StatusCode.IsGood(results[i].StatusCode),
                            ErrorMessage = StatusCode.IsBad(results[i].StatusCode)
                                ? results[i].StatusCode.ToString()
                                : null,
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout tijdens het lezen van OPC UA tags voor {ConnectionName}.",
                    _config.ConnectionName
                );
                foreach (var readValueIdWithError in nodesToRead)
                {
                    var tagConfig = activeTagConfigs.FirstOrDefault(tc =>
                    {
                        try
                        {
                            return ParseNodeId(tc.NodeId).Equals(readValueIdWithError.NodeId);
                        }
                        catch
                        {
                            return false;
                        }
                    });
                    if (
                        tagConfig != null
                        && !loggedValues.Any(lv =>
                            lv.TagName == tagConfig.TagName && !lv.IsGoodQuality
                        )
                    )
                    {
                        loggedValues.Add(
                            new LoggedTagValue
                            {
                                TagName = tagConfig.TagName,
                                IsGoodQuality = false,
                                ErrorMessage = $"Algemene leesfout: {ex.Message}",
                                Timestamp = DateTime.UtcNow,
                            }
                        );
                    }
                }
            }

            return loggedValues;
        }

        public async Task<DataValue> ReadValueAsync(NodeId nodeId)
        {
            if (!IsConnected || _session == null)
            {
                _logger.Warning(
                    "Kan waarde niet lezen voor {NodeId} (Conn: {ConnectionName}): geen actieve sessie.",
                    nodeId,
                    _config.ConnectionName
                );
                return new DataValue(StatusCodes.BadNotConnected);
            }

            ReadValueId nodeToRead = new ReadValueId
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value,
            };
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection { nodeToRead };
            try
            {
                var response = await _session
                    .ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Source,
                        nodesToRead,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, nodesToRead);
                return (response.Results != null && response.Results.Count > 0)
                    ? response.Results[0]
                    : new DataValue(StatusCodes.BadNoDataAvailable);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout tijdens lezen waarde voor NodeId: {NodeId} (Conn: {ConnectionName})",
                    nodeId,
                    _config.ConnectionName
                );
            }

            return null;
        }

        public async Task<List<NodeAttributeViewModel>> ReadNodeAttributesAsync(NodeId nodeId)
        {
            var attributes = new List<NodeAttributeViewModel>();
            if (!IsConnected || _session == null)
            {
                attributes.Add(
                    new NodeAttributeViewModel(
                        "Error",
                        "Niet verbonden",
                        StatusCodes.BadNotConnected
                    )
                );
                return attributes;
            }

            uint[] attributeIdsToRead = new uint[]
            {
                Attributes.NodeId,
                Attributes.NodeClass,
                Attributes.BrowseName,
                Attributes.DisplayName,
                Attributes.Description,
                Attributes.WriteMask,
                Attributes.UserWriteMask,
                Attributes.DataType,
                Attributes.ValueRank,
                Attributes.ArrayDimensions,
                Attributes.AccessLevel,
                Attributes.UserAccessLevel,
                Attributes.MinimumSamplingInterval,
                Attributes.Historizing,
                Attributes.Value,
            };
            var nodesToRead = new ReadValueIdCollection(
                attributeIdsToRead.Select(attrId => new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = attrId,
                })
            );
            try
            {
                var response = await _session
                    .ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Neither,
                        nodesToRead,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, nodesToRead);
                for (int i = 0; i < response.Results.Count; i++)
                {
                    string attrName =
                        Attributes.GetBrowseName(nodesToRead[i].AttributeId)
                        ?? $"AttrID {nodesToRead[i].AttributeId}";
                    object val = response.Results[i].Value;
                    if (nodesToRead[i].AttributeId == Attributes.NodeClass && val is int ncInt)
                        val = (NodeClass)ncInt;
                    else if (
                        nodesToRead[i].AttributeId == Attributes.DataType
                        && val is NodeId dtNodeId
                    )
                    {
                        var dtNode = _session.NodeCache.Find(dtNodeId);
                        val = dtNode?.DisplayName?.Text ?? dtNodeId.ToString();
                    }

                    attributes.Add(
                        new NodeAttributeViewModel(attrName, val, response.Results[i].StatusCode)
                    );
                }
            }
            catch (Exception ex)
            {
                attributes.Add(
                    new NodeAttributeViewModel("Error", ex.Message, StatusCodes.BadUnexpectedError)
                );
                _logger.Error(ex, "Error ReadNodeAttributesAsync");
            }

            return attributes;
        }

        public async Task<LocalizedText> ReadNodeDisplayNameAsync(NodeId nodeId)
        {
            if (!IsConnected || _session == null)
                return null;
            ReadValueId nodeToRead = new ReadValueId
            {
                NodeId = nodeId,
                AttributeId = Attributes.DisplayName,
            };
            try
            {
                var response = await _session
                    .ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Neither,
                        new ReadValueIdCollection { nodeToRead },
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                return (
                    response.Results != null
                    && response.Results.Count > 0
                    && StatusCode.IsGood(response.Results[0].StatusCode)
                )
                    ? response.Results[0].Value as LocalizedText
                    : null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error ReadNodeDisplayNameAsync for {NodeId}", nodeId);
                return null;
            }
        }

        public async Task<ReferenceDescriptionCollection> BrowseAllReferencesAsync(
            NodeId nodeIdToBrowse,
            BrowseDirection direction = BrowseDirection.Both
        )
        {
            return await BrowseAsync(
                nodeIdToBrowse,
                null,
                true,
                direction,
                NodeClass.Unspecified,
                CancellationToken.None
            );
        }

        #endregion

        #region Browse

        public async Task<ReferenceDescriptionCollection> BrowseAsync(
            NodeId nodeIdToBrowse,
            NodeId referenceTypeId = null,
            bool includeSubtypes = true,
            BrowseDirection direction = BrowseDirection.Forward,
            NodeClass nodeClassMask = NodeClass.Unspecified,
            CancellationToken ct = default
        )
        {
            if (!IsConnected || _session == null)
            {
                _logger.Warning("BrowseAsync: Not connected.");
                return new ReferenceDescriptionCollection();
            }

            try
            {
                BrowseDescription nodeToBrowseDesc = new BrowseDescription
                {
                    NodeId = nodeIdToBrowse,
                    BrowseDirection = direction,
                    ReferenceTypeId = referenceTypeId ?? ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = includeSubtypes,
                    NodeClassMask = (uint)nodeClassMask,
                    ResultMask = (uint)BrowseResultMask.All,
                };
                BrowseResponse response = await _session
                    .BrowseAsync(
                        null,
                        null,
                        0,
                        new BrowseDescriptionCollection { nodeToBrowseDesc },
                        ct
                    )
                    .ConfigureAwait(false);
                ClientBase.ValidateResponse(
                    response.Results,
                    new BrowseDescriptionCollection { nodeToBrowseDesc }
                );
                ClientBase.ValidateDiagnosticInfos(
                    response.DiagnosticInfos,
                    new BrowseDescriptionCollection { nodeToBrowseDesc }
                );
                if (StatusCode.IsBad(response.Results[0].StatusCode))
                {
                    _logger.Error(
                        "Browse error for {NodeId}: {StatusCode}",
                        nodeIdToBrowse,
                        response.Results[0].StatusCode
                    );
                    return new ReferenceDescriptionCollection();
                }

                ReferenceDescriptionCollection references = response.Results[0].References;
                ByteStringCollection continuationPoints = new ByteStringCollection();
                if (response.Results[0].ContinuationPoint != null)
                    continuationPoints.Add(response.Results[0].ContinuationPoint);

                while (continuationPoints.Any() && continuationPoints[0] != null)
                {
                    var browseNextResponse = await _session
                        .BrowseNextAsync(null, false, continuationPoints, ct)
                        .ConfigureAwait(false);
                    ClientBase.ValidateResponse(browseNextResponse.Results, continuationPoints);
                    ClientBase.ValidateDiagnosticInfos(
                        browseNextResponse.DiagnosticInfos,
                        continuationPoints
                    );
                    if (StatusCode.IsBad(browseNextResponse.Results[0].StatusCode))
                    {
                        _logger.Error(
                            "BrowseNext error for {NodeId}: {StatusCode}",
                            nodeIdToBrowse,
                            browseNextResponse.Results[0].StatusCode
                        );
                        break;
                    }

                    references.AddRange(browseNextResponse.Results[0].References);
                    continuationPoints.Clear();
                    if (browseNextResponse.Results[0].ContinuationPoint != null)
                        continuationPoints.Add(browseNextResponse.Results[0].ContinuationPoint);
                }

                return references;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception during BrowseAsync for {NodeId}", nodeIdToBrowse);
                return new ReferenceDescriptionCollection();
            }
        }

        public async Task<ReferenceDescriptionCollection> BrowseRootAsync()
        {
            return await BrowseAsync(ObjectIds.ObjectsFolder, ct: CancellationToken.None);
        }

        public NodeId ParseNodeId(string nodeIdString)
        {
            if (_session == null || _session.MessageContext == null)
            {
                _logger.Warning(
                    "ParseNodeId: No active session/context, using default parse for '{NodeIdString}'.",
                    nodeIdString
                );
                return NodeId.Parse(nodeIdString);
            }

            return NodeId.Parse(_session.MessageContext, nodeIdString);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _logger.Debug(
                "Dispose aangeroepen voor OpcUaService {ConnectionName}",
                _config.ConnectionName
            );
            bool waited = _semaphore.Wait(1000);
            try
            {
                if (IsConnected)
                {
                    Task.Run(async () => await DisconnectAsync()).Wait(TimeSpan.FromSeconds(2));
                }

                _subscription?.Dispose();
                _session?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout tijdens Dispose van OpcUaService voor {ConnectionName}",
                    _config.ConnectionName
                );
            }
            finally
            {
                if (waited)
                    _semaphore.Release();
                _semaphore?.Dispose();
            }
        }

        #endregion

        public async Task<List<NodeSearchResult>> SearchNodesRecursiveAsync(
            NodeId startNodeId,
            string regexPattern,
            bool caseSensitive,
            int maxDepth = 5,
            CancellationToken ct = default
        )
        {
            var results = new List<NodeSearchResult>();
            if (!IsConnected || _session == null || maxDepth < 0)
            {
                return results;
            }

            var regexOptions = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            Regex regex = null;
            try
            {
                regex = new Regex(regexPattern, regexOptions);
            }
            catch (ArgumentException ex)
            {
                _logger.Error(
                    ex,
                    "Ongeldige RegEx voor SearchNodesRecursiveAsync: {Pattern}",
                    regexPattern
                );
                return results; // Of gooi exception
            }

            await BrowseAndSearchRecursive(startNodeId, regex, maxDepth, 0, "", results, ct);
            return results;
        }

        private async Task BrowseAndSearchRecursive(
            NodeId currentNodeId,
            Regex regex,
            int maxDepth,
            int currentDepth,
            string currentPath,
            List<NodeSearchResult> results,
            CancellationToken ct
        )
        {
            if (currentDepth > maxDepth || ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                BrowseDescription nodeToBrowse = new BrowseDescription
                {
                    NodeId = currentNodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences, // Of een andere relevante referentie
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)(
                        NodeClass.Object | NodeClass.Variable | NodeClass.View | NodeClass.Method
                    ), // Pas aan indien nodig
                    ResultMask = (uint)BrowseResultMask.All,
                };
                BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection
                {
                    nodeToBrowse,
                };

                // CORRECTIE HIER: BrowseAsync retourneert BrowseResponse
                BrowseResponse browseResponse = await _session.BrowseAsync(
                    null,
                    null,
                    0,
                    nodesToBrowse,
                    ct
                );

                // Valideer de response (ResponseHeader wordt impliciet gecheckt door ValidateResponse)
                ClientBase.ValidateResponse(browseResponse.Results, nodesToBrowse);
                // Optioneel: ClientBase.ValidateDiagnosticInfos(browseResponse.DiagnosticInfos, nodesToBrowse);

                // Werk met browseResponse.Results (dit is de BrowseResultCollection)
                if (
                    browseResponse.Results != null
                    && browseResponse.Results.Count > 0
                    && StatusCode.IsGood(browseResponse.Results[0].StatusCode)
                )
                {
                    foreach (var rd in browseResponse.Results[0].References) // Referenties van het eerste (en enige) resultaat
                    {
                        if (ct.IsCancellationRequested)
                            break;

                        NodeId targetNodeId = ExpandedNodeId.ToNodeId(
                            rd.NodeId,
                            _session.NamespaceUris
                        );
                        string displayName = rd.DisplayName?.Text ?? targetNodeId.ToString(); // Gebruik TargetNodeId als fallback voor naam
                        string newPath = string.IsNullOrEmpty(currentPath)
                            ? displayName
                            : $"{currentPath}/{displayName}";

                        if (regex.IsMatch(displayName))
                        {
                            // Zorg ervoor dat je niet dezelfde node meerdere keren toevoegt als deze via verschillende paden wordt gevonden
                            // (tenzij je pad-specifieke resultaten wilt)
                            if (!results.Any(r => r.NodeId == targetNodeId)) // Simpele check op NodeId
                            {
                                results.Add(
                                    new NodeSearchResult
                                    {
                                        NodeId = targetNodeId,
                                        DisplayName = displayName,
                                        NodeClass = rd.NodeClass,
                                        Path = newPath,
                                    }
                                );
                            }
                        }

                        // Recursief verder zoeken voor Objects en Views (of andere relevante NodeClasses die kinderen kunnen hebben)
                        if (rd.NodeClass == NodeClass.Object || rd.NodeClass == NodeClass.View)
                        {
                            await BrowseAndSearchRecursive(
                                targetNodeId,
                                regex,
                                maxDepth,
                                currentDepth + 1,
                                newPath,
                                results,
                                ct
                            );
                        }
                    }
                }
                else if (browseResponse.Results != null && browseResponse.Results.Count > 0)
                {
                    _logger?.Warning(
                        "Browse operatie voor NodeId {NodeId} gaf een Bad StatusCode terug: {StatusCode}",
                        currentNodeId,
                        browseResponse.Results[0].StatusCode
                    );
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning(
                    ex,
                    "Fout tijdens recursief browsen/zoeken vanaf NodeId {NodeId}",
                    currentNodeId
                );
            }
        }
    }
}
