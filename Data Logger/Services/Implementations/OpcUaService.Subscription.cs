using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Data_Logger.Enums;
using Data_Logger.Models;
using Opc.Ua;
using Opc.Ua.Client;

namespace Data_Logger.Services.Implementations
{
    /// <summary>
    /// Partial class voor OpcUaService die methoden bevat gerelateerd aan OPC UA subscriptions en data monitoring.
    /// </summary>
    public sealed partial class OpcUaService
    {
        /// <inheritdoc/>
        public async Task StartMonitoringTagsAsync()
        {
            if (!IsConnected || _session == null)
            {
                _specificLogger.Information(
                    "StartMonitoringTagsAsync ({ConnectionName}): Kan monitoring niet starten, geen actieve OPC UA sessie.",
                    _config?.ConnectionName ?? "N/A"
                );
                return;
            }
            if (_config?.TagsToMonitor == null || !_config.TagsToMonitor.Any(t => t.IsActive))
            {
                _specificLogger.Information(
                    "StartMonitoringTagsAsync ({ConnectionName}): Geen actieve tags geconfigureerd om te monitoren.",
                    _config?.ConnectionName ?? "N/A"
                );
                return;
            }

            if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false))
            {
                _specificLogger.Warning(
                    "StartMonitoringTagsAsync ({ConnectionName}): Timeout (10s) bij wachten op semaphore. Operatie afgebroken.",
                    _config.ConnectionName
                );
                return;
            }

            try
            {
                // Stap 1: Verwijder eventuele bestaande subscription
                if (_subscription != null)
                {
                    _specificLogger.Information(
                        "StartMonitoringTagsAsync ({ConnectionName}): Verwijdert bestaande subscription (ID: {SubscriptionId}) alvorens een nieuwe te starten.",
                        _config.ConnectionName,
                        _subscription.Id
                    );
                    try
                    {
                        // Controleer of de subscription nog bestaat in de sessie.
                        if (_session.Subscriptions.Any(s => s.Id == _subscription.Id))
                        {
                            _subscription.Delete(true); // Verwijder van server
                            _session.RemoveSubscription(_subscription); // Verwijder van client sessie
                            _specificLogger.Debug(
                                "StartMonitoringTagsAsync ({ConnectionName}): Oude subscription (ID: {SubscriptionId}) succesvol verwijderd.",
                                _config.ConnectionName,
                                _subscription.Id
                            );
                        }
                        else
                        {
                            _specificLogger.Debug(
                                "StartMonitoringTagsAsync ({ConnectionName}): Oude subscription (ID: {SubscriptionId}) niet gevonden in sessie, mogelijk al verwijderd.",
                                _config.ConnectionName,
                                _subscription.Id
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _specificLogger.Warning(
                            ex,
                            "StartMonitoringTagsAsync ({ConnectionName}): Fout bij verwijderen oude subscription (ID: {SubscriptionId}).",
                            _config.ConnectionName,
                            _subscription.Id
                        );
                    }
                    finally // Zorg ervoor dat het object altijd gedisposed wordt
                    {
                        _subscription.Dispose();
                        _subscription = null;
                    }
                }

                // Stap 2: Maak nieuwe subscription aan
                int minSamplingInterval = _config
                    .TagsToMonitor.Where(t => t.IsActive && t.SamplingInterval > 0)
                    .Select(t => t.SamplingInterval)
                    .DefaultIfEmpty(1000) // Default naar 1000ms als er geen actieve tags met interval > 0 zijn
                    .Min();

                int publishingInterval = Math.Max(minSamplingInterval, 100); // Minimaal 100ms publishing interval

                _subscription = new Subscription(_session.DefaultSubscription)
                {
                    DisplayName = $"{_config.ConnectionName} Subscription",
                    PublishingInterval = publishingInterval,
                    KeepAliveCount = 10, // Aantal publishing intervals voordat keep-alive wordt verwacht
                    LifetimeCount = (uint)(10 * 3), // Lifetime = KeepAliveCount * 3
                    MaxNotificationsPerPublish = 0, // 0 = onbeperkt aantal notificaties per publish response
                    PublishingEnabled = true, // Start met publiceren ingeschakeld
                    TimestampsToReturn = TimestampsToReturn.Source, // Vraag primair SourceTimestamp op
                };
                _subscription.LifetimeCount = Math.Max(
                    _subscription.KeepAliveCount * 3,
                    _subscription.LifetimeCount
                );

                var itemsToMonitor = new List<MonitoredItem>();
                foreach (var tagConfig in _config.TagsToMonitor.Where(t => t.IsActive))
                {
                    try
                    {
                        var item = new MonitoredItem(_subscription.DefaultItem)
                        {
                            DisplayName = tagConfig.TagName, // Gebruik TagName als DisplayName voor MonitoredItem
                            StartNodeId = ParseNodeId(tagConfig.NodeId), // ParseNodeId is een helper
                            AttributeId = Attributes.Value,
                            // SamplingInterval: -1 voor server default, 0 voor snelst als per publishing interval, >0 voor specifiek
                            SamplingInterval =
                                tagConfig.SamplingInterval > 0 ? tagConfig.SamplingInterval : 0,
                            QueueSize = 1, // Bewaar alleen de laatste waarde (geen buffer voor oude waarden)
                            DiscardOldest = true, // Als de queue vol is, verwijder de oudste
                        };
                        item.Notification += OnMonitoredItemNotification;
                        itemsToMonitor.Add(item);
                    }
                    catch (Exception ex)
                    {
                        _specificLogger.Error(
                            ex,
                            "StartMonitoringTagsAsync ({ConnectionName}): Fout bij voorbereiden MonitoredItem voor NodeId {NodeId} (Tag: {TagName}).",
                            _config.ConnectionName,
                            tagConfig.NodeId,
                            tagConfig.TagName
                        );
                        // Update de tag configuratie om de fout weer te geven
                        tagConfig.IsGoodQuality = false;
                        tagConfig.ErrorMessage = $"Fout bij opzetten monitoring: {ex.Message}";
                        tagConfig.CurrentAlarmState = TagAlarmState.Error;
                    }
                }

                if (!itemsToMonitor.Any())
                {
                    _specificLogger.Information(
                        "StartMonitoringTagsAsync ({ConnectionName}): Geen actieve en valide tags om te monitoren.",
                        _config.ConnectionName
                    );
                    if (_subscription != null) // Als er wel een subscription object is aangemaakt maar geen items
                    {
                        _subscription.Dispose();
                        _subscription = null;
                    }
                    return;
                }

                _subscription.AddItems(itemsToMonitor);
                _specificLogger.Information(
                    "StartMonitoringTagsAsync ({ConnectionName}): Probeert subscription aan te maken voor {ItemCount} items.",
                    _config.ConnectionName,
                    itemsToMonitor.Count
                );

                _session.AddSubscription(_subscription); // Voeg subscription toe aan de sessie
                await _subscription.CreateAsync().ConfigureAwait(false); // Creëer de subscription op de server

                _specificLogger.Information(
                    "StartMonitoringTagsAsync ({ConnectionName}): Subscription aangemaakt. ID: {SubscriptionId}, PublishingInterval(Req/Actual): {ReqPubInt}ms/{ActualPubInt}ms. KeepAlive(Req/Actual): {ReqKeepAlive}/{ActualKeepAlive}.",
                    _config.ConnectionName,
                    _subscription.Id,
                    _subscription.PublishingInterval,
                    _subscription.CurrentPublishingInterval,
                    _subscription.KeepAliveCount,
                    _subscription.CurrentKeepAliveCount
                );

                // Log details van elk item en controleer op fouten na Create
                foreach (var monitoredItem in _subscription.MonitoredItems)
                {
                    _specificLogger.Debug(
                        "StartMonitoringTagsAsync ({ConnectionName}): Gemonitord Item: '{DisplayName}', NodeId: {NodeId}, ClientSampling: {ClientSampling}ms, ServerSampling: {ServerSampling}ms, QueueSize: {QueueSize}, Status: {StatusCode}, Error: {CreateError}",
                        _config.ConnectionName,
                        monitoredItem.DisplayName,
                        monitoredItem.StartNodeId,
                        monitoredItem.SamplingInterval,
                        monitoredItem.Status?.SamplingInterval ?? -1,
                        monitoredItem.QueueSize,
                        monitoredItem.Status?.Error?.StatusCode.ToString() ?? "OK",
                        monitoredItem.Status?.Error?.ToString() ?? "None"
                    );
                    if (
                        monitoredItem.Status?.Error != null
                        && StatusCode.IsBad(monitoredItem.Status.Error.StatusCode)
                    )
                    {
                        var tagCfg = _config.TagsToMonitor.FirstOrDefault(t =>
                            t.TagName == monitoredItem.DisplayName
                        );
                        if (tagCfg != null)
                        {
                            tagCfg.IsGoodQuality = false;
                            tagCfg.ErrorMessage =
                                $"Fout bij aanmaken MonitoredItem: {monitoredItem.Status.Error.StatusCode}";
                            tagCfg.CurrentAlarmState = TagAlarmState.Error;
                        }
                    }
                }

                // Activeer de items op de server (nodig na CreateAsync of AddItems)
                await _subscription.ApplyChangesAsync().ConfigureAwait(false);
                _specificLogger.Information(
                    "StartMonitoringTagsAsync ({ConnectionName}): Subscription succesvol geactiveerd.",
                    _config.ConnectionName
                );

                // Voer een initiële leesactie uit voor alle succesvol toegevoegde items
                // om direct een waarde te hebben en niet te hoeven wachten op de eerste notificatie.
                await PerformInitialReadForMonitoredItems().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _specificLogger.Error(
                    ex,
                    "StartMonitoringTagsAsync ({ConnectionName}): Algemene fout bij starten van OPC UA tag monitoring.",
                    _config.ConnectionName
                );
                if (_subscription != null)
                {
                    try
                    {
                        // Probeer op te ruimen als de sessie nog subscriptions bevat
                        if (
                            _session != null
                            && _session.Subscriptions.Any(s => s.Id == _subscription.Id)
                        )
                            _subscription.Delete(true);
                    }
                    catch (Exception cleanupEx)
                    {
                        _specificLogger.Warning(
                            cleanupEx,
                            "StartMonitoringTagsAsync ({ConnectionName}): Fout tijdens opruimen mislukte subscription.",
                            _config.ConnectionName
                        );
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

        /// <summary>
        /// Voert een initiële leesactie uit voor alle items die succesvol aan de subscription zijn toegevoegd.
        /// Dit zorgt ervoor dat de UI direct een waarde toont en niet hoeft te wachten op de eerste data change notificatie.
        /// </summary>
        private async Task PerformInitialReadForMonitoredItems()
        {
            // Controleer of er een subscription is en of er items zijn die succesvol zijn aangemaakt
            if (
                _subscription == null
                || !_subscription.MonitoredItems.Any(mi => mi.Status?.Created ?? false)
            )
            {
                _specificLogger.Debug(
                    "PerformInitialRead ({ConnectionName}): Geen (succesvol gecreëerde) monitored items om een initiële leesactie voor uit te voeren.",
                    _config?.ConnectionName ?? "N/A"
                );
                return;
            }

            var initialReadValueIds = new ReadValueIdCollection();
            var requestMap = new Dictionary<NodeId, OpcUaTagConfig>();

            foreach (
                var item in _subscription.MonitoredItems.Where(mi => mi.Status?.Created ?? false)
            )
            {
                var tagConfig = _config.TagsToMonitor.FirstOrDefault(tc =>
                    tc.NodeId == item.StartNodeId.ToString() && tc.IsActive
                );
                if (tagConfig != null)
                {
                    var readValueId = new ReadValueId
                    {
                        NodeId = item.StartNodeId,
                        AttributeId = Attributes.Value,
                    };
                    initialReadValueIds.Add(readValueId);
                    requestMap[item.StartNodeId] = tagConfig; // Map NodeId naar TagConfig
                }
            }

            if (initialReadValueIds.Any())
            {
                _specificLogger.Information(
                    "PerformInitialRead ({ConnectionName}): Uitvoeren initiële leesactie voor {Count} gemonitorde tags.",
                    _config.ConnectionName,
                    initialReadValueIds.Count
                );
                try
                {
                    ReadResponse response = await _session
                        .ReadAsync(
                            null, // requestHeader
                            0, // maxAge
                            TimestampsToReturn.Source, // Vraag SourceTimestamp
                            initialReadValueIds,
                            CancellationToken.None
                        )
                        .ConfigureAwait(false);

                    ClientBase.ValidateResponse(response.Results, initialReadValueIds); // Gooit exception bij mismatch

                    var initialLoggedValues = new List<LoggedTagValue>();
                    for (int i = 0; i < response.Results.Count; i++)
                    {
                        NodeId readNodeId = initialReadValueIds[i].NodeId;
                        if (requestMap.TryGetValue(readNodeId, out OpcUaTagConfig tagConfig))
                        {
                            var dataValue = response.Results[i];
                            var loggedValue = new LoggedTagValue
                            {
                                TagName = tagConfig.TagName,
                                Value = dataValue.Value,
                                Timestamp =
                                    dataValue.SourceTimestamp != DateTime.MinValue
                                        ? dataValue.SourceTimestamp
                                        : dataValue.ServerTimestamp,
                                IsGoodQuality = StatusCode.IsGood(dataValue.StatusCode),
                                ErrorMessage = StatusCode.IsBad(dataValue.StatusCode)
                                    ? dataValue.StatusCode.ToString()
                                    : null,
                            };
                            initialLoggedValues.Add(loggedValue);

                            // Update de CurrentValue in de tagConfig voor directe UI feedback
                            tagConfig.CurrentValue = loggedValue.Value;
                            tagConfig.Timestamp = loggedValue.Timestamp;
                            tagConfig.IsGoodQuality = loggedValue.IsGoodQuality;
                            tagConfig.ErrorMessage = loggedValue.ErrorMessage;

                            _specificLogger.Verbose(
                                "PerformInitialRead ({ConnectionName}): Initiële waarde voor {TagName}: {Value}, Kwaliteit: {Quality}, Tijd: {Timestamp}",
                                _config.ConnectionName,
                                loggedValue.TagName,
                                loggedValue.Value,
                                loggedValue.IsGoodQuality,
                                loggedValue.Timestamp
                            );

                            // Pas hier ook direct de alarm/outlier logica toe voor de initiële waarde
                            TagAlarmState finalAlarmState;
                            double? numericValueForAlarmCheck = null;
                            double? limitDetailsForThreshold = null;
                            if (!tagConfig.IsGoodQuality)
                            {
                                finalAlarmState = TagAlarmState.Error;
                                if (tagConfig.IsOutlierDetectionEnabled)
                                    tagConfig.ResetBaselineState();
                            }
                            else if (!TryConvertToDouble(loggedValue.Value, out double valForCheck))
                            {
                                finalAlarmState = TagAlarmState.Error;
                                if (tagConfig.IsOutlierDetectionEnabled)
                                    tagConfig.ResetBaselineState();
                            }
                            else
                            {
                                numericValueForAlarmCheck = valForCheck;
                                TagAlarmState thresholdState = DetermineThresholdAlarmState(
                                    tagConfig,
                                    numericValueForAlarmCheck.Value,
                                    out limitDetailsForThreshold
                                );
                                bool isOutlier = IsCurrentValueOutlier(
                                    tagConfig,
                                    numericValueForAlarmCheck.Value
                                );
                                finalAlarmState = isOutlier
                                    ? TagAlarmState.Outlier
                                    : thresholdState;
                            }
                            UpdateAndLogFinalAlarmState(
                                tagConfig,
                                loggedValue,
                                finalAlarmState,
                                numericValueForAlarmCheck,
                                limitDetailsForThreshold
                            );
                        }
                    }

                    if (initialLoggedValues.Any())
                    {
                        TagsDataReceived?.Invoke(this, initialLoggedValues);
                    }
                }
                catch (Exception ex)
                {
                    _specificLogger.Error(
                        ex,
                        "PerformInitialRead ({ConnectionName}): Fout tijdens initiële leesactie na start monitoring.",
                        _config.ConnectionName
                    );

                    foreach (var tagConfig in requestMap.Values)
                    {
                        tagConfig.IsGoodQuality = false;
                        tagConfig.ErrorMessage = "Fout bij initiële leesactie.";
                        tagConfig.CurrentAlarmState = TagAlarmState.Error;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task StopMonitoringTagsAsync()
        {
            // Controleer eerst of er überhaupt een subscription is om te stoppen.
            if (_subscription == null && (_session == null || _session.SubscriptionCount == 0))
            {
                _specificLogger.Debug(
                    "StopMonitoringTagsAsync ({ConnectionName}): Geen actieve subscription of sessie om te stoppen.",
                    _config?.ConnectionName ?? "N/A"
                );
                // Zorg dat _subscription ook null is als de sessie geen subscriptions heeft.
                if (_subscription != null)
                {
                    _subscription.Dispose();
                    _subscription = null;
                }
                return;
            }

            if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                _specificLogger.Warning(
                    "StopMonitoringTagsAsync ({ConnectionName}): Timeout bij wachten op semaphore. Stoppen monitoring mogelijk niet voltooid.",
                    _config?.ConnectionName ?? "N/A"
                );
                return;
            }

            try
            {
                // Her-controleer na verkrijgen semaphore.
                if (
                    _subscription == null
                    || _session == null
                    || !_session.Subscriptions.Any(s => s.Id == _subscription.Id)
                )
                {
                    _specificLogger.Debug(
                        "StopMonitoringTagsAsync ({ConnectionName}): Subscription al verwijderd of niet aanwezig in sessie na verkrijgen semaphore.",
                        _config?.ConnectionName ?? "N/A"
                    );
                    _subscription?.Dispose();
                    _subscription = null;
                    return;
                }

                _specificLogger.Information(
                    "StopMonitoringTagsAsync ({ConnectionName}): Stopt OPC UA tag monitoring (subscription ID: {SubscriptionId}).",
                    _config.ConnectionName,
                    _subscription.Id
                );

                // Verwijder items van de subscription
                if (_subscription.MonitoredItemCount > 0)
                {
                    // Maak een kopie van de lijst om te voorkomen dat de collectie wijzigt tijdens iteratie
                    var itemsToRemove = _subscription.MonitoredItems.ToList();
                    if (itemsToRemove.Any())
                    {
                        // Koppel event handlers los voordat items worden verwijderd
                        foreach (var item in itemsToRemove)
                            item.Notification -= OnMonitoredItemNotification;
                        _subscription.RemoveItems(itemsToRemove);
                        await _subscription.ApplyChangesAsync().ConfigureAwait(false); // Pas wijzigingen toe op server
                        _specificLogger.Debug(
                            "StopMonitoringTagsAsync ({ConnectionName}): {ItemCount} items verwijderd van subscription.",
                            _config.ConnectionName,
                            itemsToRemove.Count
                        );
                    }
                }

                // Verwijder de subscription van de server en de sessie
                _subscription.Delete(true); // true om ook van server te verwijderen
                _session.RemoveSubscription(_subscription);

                _specificLogger.Information(
                    "OPC UA tag monitoring (subscription ID: {SubscriptionId}) gestopt voor {ConnectionName}.",
                    _subscription.Id,
                    _config.ConnectionName
                );
            }
            catch (Exception ex)
            {
                _specificLogger.Error(
                    ex,
                    "StopMonitoringTagsAsync ({ConnectionName}): Fout bij stoppen van OPC UA tag monitoring.",
                    _config.ConnectionName
                );
            }
            finally
            {
                // Ruim altijd het _subscription object op, ook na fout.
                _subscription?.Dispose();
                _subscription = null;
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Callback methode voor datawijzigingen van gemonitorde OPC UA items.
        /// Verwerkt de ontvangen <see cref="DataValue"/>, werkt de corresponderende <see cref="OpcUaTagConfig"/> bij,
        /// past alarm- en outlierdetectielogica toe, en triggert het <see cref="TagsDataReceived"/> event.
        /// </summary>
        /// <param name="monitoredItem">Het <see cref="MonitoredItem"/> dat de notificatie heeft gegenereerd.</param>
        /// <param name="e">Argumenten die de notificatie bevatten, inclusief de <see cref="DataValue"/>.</param>
        private void OnMonitoredItemNotification(
            MonitoredItem monitoredItem,
            MonitoredItemNotificationEventArgs e
        )
        {
            if (
                !(e.NotificationValue is MonitoredItemNotification notification)
                || notification.Value == null
            )
            {
                _specificLogger.Verbose(
                    "OnMonitoredItemNotification ({ConnectionName}): Ongeldige of lege notificatie ontvangen voor item '{DisplayName}'.",
                    _config.ConnectionName,
                    monitoredItem.DisplayName
                );
                return;
            }

            var liveValue = new LoggedTagValue
            {
                TagName = monitoredItem.DisplayName, // DisplayName is ingesteld als de TagName
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

            _specificLogger.Verbose(
                "OnMonitoredItemNotification ({ConnectionName}): Data update voor Tag='{Tag}', Waarde='{Val}', Kwaliteit={Qual}, Tijdstempel='{Ts:O}'",
                _config.ConnectionName,
                liveValue.TagName,
                liveValue.Value,
                liveValue.IsGoodQuality,
                liveValue.Timestamp
            );

            // Zoek de corresponderende OpcUaTagConfig om de CurrentValue, Timestamp, etc. bij te werken voor UI binding
            // en om alarm/outlier logica toe te passen.
            var configuredTag = _config?.TagsToMonitor.FirstOrDefault(t =>
                t.TagName == liveValue.TagName && t.IsActive
            );

            if (configuredTag != null)
            {
                // Update live data in de configuratie (deze properties zijn ObservableObject, dus UI update)
                configuredTag.CurrentValue = liveValue.Value;
                configuredTag.Timestamp = liveValue.Timestamp;
                configuredTag.IsGoodQuality = liveValue.IsGoodQuality;
                configuredTag.ErrorMessage = liveValue.ErrorMessage;

                // Pas alarm en outlier logica toe
                TagAlarmState finalAlarmState;
                double? numericValueForAlarmCheck = null;
                double? limitDetailsForThreshold = null;

                if (!configuredTag.IsGoodQuality)
                {
                    finalAlarmState = TagAlarmState.Error;
                    // Reset baseline als de kwaliteit slecht is, omdat de waarde onbetrouwbaar is
                    if (configuredTag.IsOutlierDetectionEnabled)
                    {
                        configuredTag.ResetBaselineState();
                    }
                }
                else if (!TryConvertToDouble(liveValue.Value, out double valForCheck))
                {
                    _specificLogger.Warning(
                        "OnMonitoredItemNotification ({ConnectionName}): Waarde '{RawValue}' voor tag '{TagName}' kon niet naar double geconverteerd worden voor alarm/outlier check.",
                        _config.ConnectionName,
                        liveValue.Value,
                        configuredTag.TagName
                    );
                    finalAlarmState = TagAlarmState.Error; // Beschouw als error als conversie faalt voor alarm checks
                    if (configuredTag.IsOutlierDetectionEnabled)
                    {
                        configuredTag.ResetBaselineState();
                    }
                }
                else
                {
                    numericValueForAlarmCheck = valForCheck;
                    // Bepaal alarmstatus op basis van drempels
                    TagAlarmState thresholdState = DetermineThresholdAlarmState(
                        configuredTag,
                        numericValueForAlarmCheck.Value,
                        out limitDetailsForThreshold
                    );
                    // Bepaal of het een outlier is
                    bool isOutlier = IsCurrentValueOutlier(
                        configuredTag,
                        numericValueForAlarmCheck.Value
                    );

                    finalAlarmState = isOutlier ? TagAlarmState.Outlier : thresholdState;
                }
                // Werk de finale alarmstatus bij in de tagConfig en log indien gewijzigd
                UpdateAndLogFinalAlarmState(
                    configuredTag,
                    liveValue,
                    finalAlarmState,
                    numericValueForAlarmCheck,
                    limitDetailsForThreshold
                );
            }
            else
            {
                _specificLogger.Warning(
                    "OnMonitoredItemNotification ({ConnectionName}): Geen (actieve) geconfigureerde tag gevonden voor ontvangen TagName '{TagName}'. Data niet verwerkt voor alarmen/plots.",
                    _config.ConnectionName,
                    liveValue.TagName
                );
            }

            // Stuur de ontvangen live value door
            TagsDataReceived?.Invoke(this, new List<LoggedTagValue> { liveValue });
        }
    }
}
