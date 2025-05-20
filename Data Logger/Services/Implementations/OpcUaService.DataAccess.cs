using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Data_Logger.Models;
using Data_Logger.ViewModels;
using Opc.Ua;

namespace Data_Logger.Services.Implementations
{
    /// <summary>
    /// Partial class voor OpcUaService die methoden bevat voor data access operaties
    /// zoals het lezen van waarden en attributen.
    /// </summary>
    public partial class OpcUaService
    {
        /// <inheritdoc/>
        public async Task<IEnumerable<LoggedTagValue>> ReadCurrentTagValuesAsync()
        {
            if (!IsConnected || _session == null)
            {
                _specificLogger.Warning(
                    "ReadCurrentTagValuesAsync ({ConnectionName}): Kan tags niet lezen, geen actieve OPC UA sessie.",
                    _config?.ConnectionName ?? "N/A"
                );
                return Enumerable.Empty<LoggedTagValue>();
            }
            if (_config?.TagsToMonitor == null || !_config.TagsToMonitor.Any(t => t.IsActive))
            {
                _specificLogger.Information(
                    "ReadCurrentTagValuesAsync ({ConnectionName}): Geen actieve tags geconfigureerd om te lezen.",
                    _config?.ConnectionName ?? "N/A"
                );
                return Enumerable.Empty<LoggedTagValue>();
            }

            var loggedValues = new List<LoggedTagValue>();
            var nodesToRead = new ReadValueIdCollection();
            // Maak een kopie van de actieve tags om te voorkomen dat de collectie wijzigt tijdens iteratie
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
                    _specificLogger.Error(
                        ex,
                        "ReadCurrentTagValuesAsync ({ConnectionName}): Ongeldige NodeId '{NodeIdString}' voor tag '{TagName}'.",
                        _config.ConnectionName,
                        tagConfig.NodeId,
                        tagConfig.TagName
                    );
                    // Voeg een LoggedTagValue toe met de foutinformatie
                    var errorValue = new LoggedTagValue
                    {
                        TagName = tagConfig.TagName,
                        Timestamp = DateTime.UtcNow,
                        IsGoodQuality = false,
                        ErrorMessage = $"Ongeldige NodeId: {tagConfig.NodeId}",
                    };
                    loggedValues.Add(errorValue);
                    tagConfig.CurrentValue = null;
                    tagConfig.Timestamp = errorValue.Timestamp;
                    tagConfig.IsGoodQuality = false;
                    tagConfig.ErrorMessage = errorValue.ErrorMessage;
                }
            }

            if (!nodesToRead.Any()) // Als er alleen tags met parseerfouten waren, of geen actieve tags na filter
            {
                return loggedValues; // Retourneer de lijst
            }

            if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                _specificLogger.Warning(
                    "ReadCurrentTagValuesAsync ({ConnectionName}): Timeout bij wachten op semaphore. Leesactie afgebroken.",
                    _config.ConnectionName
                );
                // Markeer de resterende tags als niet succesvol gelezen
                foreach (var nodeToRead in nodesToRead)
                {
                    var tagConfig = activeTagConfigs.FirstOrDefault(tc =>
                    {
                        try
                        {
                            return ParseNodeId(tc.NodeId).Equals(nodeToRead.NodeId);
                        }
                        catch
                        {
                            return false;
                        }
                    });
                    if (
                        tagConfig != null
                        && !loggedValues.Any(lv => lv.TagName == tagConfig.TagName)
                    ) // Voeg alleen toe als nog niet verwerkt (bijv. als error)
                    {
                        loggedValues.Add(
                            new LoggedTagValue
                            {
                                TagName = tagConfig.TagName,
                                IsGoodQuality = false,
                                ErrorMessage = "Timeout tijdens leesactie",
                                Timestamp = DateTime.UtcNow,
                            }
                        );
                    }
                }
                return loggedValues;
            }

            try
            {
                _specificLogger.Debug(
                    "ReadCurrentTagValuesAsync ({ConnectionName}): Leest {Count} OPC UA tags.",
                    _config.ConnectionName,
                    nodesToRead.Count
                );
                // RequestHeader en ViewDescription kunnen null zijn voor standaard gedrag
                ReadResponse response = await _session
                    .ReadAsync(
                        null, // requestHeader
                        0, // maxAge
                        TimestampsToReturn.Source, // Vraag SourceTimestamp op indien beschikbaar
                        nodesToRead,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);

                // Valideer de response
                ClientBase.ValidateResponse(response.Results, nodesToRead);

                for (int i = 0; i < response.Results.Count; i++)
                {
                    var originalNodeIdInRequest = nodesToRead[i].NodeId;
                    var correspondingTagConfig = activeTagConfigs.FirstOrDefault(tc =>
                    {
                        try
                        {
                            return ParseNodeId(tc.NodeId).Equals(originalNodeIdInRequest);
                        }
                        catch
                        {
                            return false;
                        } // Als parsen van tc.NodeId faalt
                    });

                    if (correspondingTagConfig == null)
                    {
                        _specificLogger.Warning(
                            "ReadCurrentTagValuesAsync ({ConnectionName}): Kon geen overeenkomende tagConfig vinden voor gelezen NodeId {ReadNodeId}. Resultaat wordt overgeslagen.",
                            _config.ConnectionName,
                            originalNodeIdInRequest
                        );
                        continue;
                    }

                    var dataValue = response.Results[i];
                    var loggedValue = new LoggedTagValue
                    {
                        TagName = correspondingTagConfig.TagName,
                        Value = dataValue.Value,
                        // Gebruik SourceTimestamp indien beschikbaar, anders ServerTimestamp, anders UtcNow als fallback
                        Timestamp =
                            dataValue.SourceTimestamp != DateTime.MinValue
                                ? dataValue.SourceTimestamp
                            : dataValue.ServerTimestamp != DateTime.MinValue
                                ? dataValue.ServerTimestamp
                            : DateTime.UtcNow,
                        IsGoodQuality = StatusCode.IsGood(dataValue.StatusCode),
                        ErrorMessage = StatusCode.IsBad(dataValue.StatusCode)
                            ? dataValue.StatusCode.ToString()
                            : null,
                    };
                    loggedValues.Add(loggedValue);

                    // Update de CurrentValue, Timestamp, etc. in de OpcUaTagConfig voor UI binding
                    correspondingTagConfig.CurrentValue = loggedValue.Value;
                    correspondingTagConfig.Timestamp = loggedValue.Timestamp;
                    correspondingTagConfig.IsGoodQuality = loggedValue.IsGoodQuality;
                    correspondingTagConfig.ErrorMessage = loggedValue.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                _specificLogger.Error(
                    ex,
                    "ReadCurrentTagValuesAsync ({ConnectionName}): Fout tijdens het lezen van OPC UA tags.",
                    _config.ConnectionName
                );
                // Markeer alle gevraagde (en nog niet succesvol verwerkte) tags als foutief
                foreach (var nodeToRead in nodesToRead)
                {
                    var tagConfig = activeTagConfigs.FirstOrDefault(tc =>
                    {
                        try
                        {
                            return ParseNodeId(tc.NodeId).Equals(nodeToRead.NodeId);
                        }
                        catch
                        {
                            return false;
                        }
                    });
                    // Voeg alleen een error entry toe als er nog geen entry (succes of error) voor deze tag is in deze batch
                    if (
                        tagConfig != null
                        && !loggedValues.Any(lv => lv.TagName == tagConfig.TagName)
                    )
                    {
                        var errorValue = new LoggedTagValue
                        {
                            TagName = tagConfig.TagName,
                            IsGoodQuality = false,
                            ErrorMessage =
                                $"Leesfout: {ex.Message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? ex.Message}",
                            Timestamp = DateTime.UtcNow,
                        };
                        loggedValues.Add(errorValue);
                        // Update ook de tagConfig in de UI
                        tagConfig.CurrentValue = null;
                        tagConfig.Timestamp = errorValue.Timestamp;
                        tagConfig.IsGoodQuality = false;
                        tagConfig.ErrorMessage = errorValue.ErrorMessage;
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }
            return loggedValues;
        }

        /// <inheritdoc/>
        public async Task<DataValue> ReadValueAsync(NodeId nodeId)
        {
            if (!IsConnected || _session == null)
            {
                _specificLogger.Warning(
                    "ReadValueAsync ({ConnectionName}): Kan waarde niet lezen voor NodeId {NodeId}, geen actieve OPC UA sessie.",
                    _config?.ConnectionName ?? "N/A",
                    nodeId
                );
                return new DataValue(StatusCodes.BadNotConnected);
            }
            if (nodeId == null)
            {
                _specificLogger.Warning(
                    "ReadValueAsync ({ConnectionName}): Aangeroepen met null NodeId.",
                    _config?.ConnectionName ?? "N/A"
                );
                return new DataValue(StatusCodes.BadNodeIdInvalid);
            }

            var nodeToRead = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value };
            var nodesToRead = new ReadValueIdCollection { nodeToRead };

            if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                _specificLogger.Warning(
                    "ReadValueAsync ({ConnectionName}): Timeout bij wachten op semaphore voor NodeId {NodeId}.",
                    _config?.ConnectionName ?? "N/A",
                    nodeId
                );
                return new DataValue(StatusCodes.BadTimeout);
            }

            try
            {
                _specificLogger.Debug(
                    "ReadValueAsync ({ConnectionName}): Leest waarde voor NodeId: {NodeId}",
                    _config.ConnectionName,
                    nodeId
                );
                ReadResponse response = await _session
                    .ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Both, // Vraag beide timestamps op
                        nodesToRead,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);

                ClientBase.ValidateResponse(response.Results, nodesToRead);
                return (response.Results != null && response.Results.Count > 0)
                    ? response.Results[0]
                    : new DataValue(StatusCodes.BadNoDataAvailable);
            }
            catch (Exception ex)
            {
                _specificLogger.Error(
                    ex,
                    "ReadValueAsync ({ConnectionName}): Fout tijdens lezen waarde voor NodeId: {NodeId}",
                    _config.ConnectionName,
                    nodeId
                );
            }
            finally
            {
                _semaphore.Release();
            }

            return null;
        }

        /// <inheritdoc/>
        public async Task<List<NodeAttributeViewModel>> ReadNodeAttributesAsync(NodeId nodeId)
        {
            var attributes = new List<NodeAttributeViewModel>();
            if (!IsConnected || _session == null)
            {
                _specificLogger.Warning(
                    "ReadNodeAttributesAsync ({ConnectionName}): Kan attributen niet lezen voor NodeId {NodeId}, geen actieve OPC UA sessie.",
                    _config?.ConnectionName ?? "N/A",
                    nodeId
                );
                attributes.Add(
                    new NodeAttributeViewModel(
                        "Error",
                        "Niet verbonden",
                        StatusCodes.BadNotConnected
                    )
                );
                return attributes;
            }
            if (nodeId == null)
            {
                _specificLogger.Warning(
                    "ReadNodeAttributesAsync ({ConnectionName}): Aangeroepen met null NodeId.",
                    _config?.ConnectionName ?? "N/A"
                );
                attributes.Add(
                    new NodeAttributeViewModel(
                        "Error",
                        "Ongeldige NodeId (null)",
                        StatusCodes.BadNodeIdInvalid
                    )
                );
                return attributes;
            }

            // Lijst van standaard attributen om te lezen
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

            if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                _specificLogger.Warning(
                    "ReadNodeAttributesAsync ({ConnectionName}): Timeout bij wachten op semaphore voor NodeId {NodeId}.",
                    _config?.ConnectionName ?? "N/A",
                    nodeId
                );
                attributes.Add(
                    new NodeAttributeViewModel(
                        "Error",
                        "Timeout tijdens attribuut leesactie",
                        StatusCodes.BadTimeout
                    )
                );
                return attributes;
            }
            try
            {
                _specificLogger.Debug(
                    "ReadNodeAttributesAsync ({ConnectionName}): Leest attributen voor NodeId: {NodeId}",
                    _config.ConnectionName,
                    nodeId
                );
                ReadResponse response = await _session
                    .ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Neither,
                        nodesToRead,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, nodesToRead);

                for (int i = 0; i < response.Results.Count; i++)
                {
                    string attrName =
                        Attributes.GetBrowseName(nodesToRead[i].AttributeId)
                        ?? $"AttrID {nodesToRead[i].AttributeId}";
                    object val = response.Results[i].Value;
                    StatusCode status = response.Results[i].StatusCode;

                    // Converteer specifieke attributen naar meer leesbare vormen
                    if (nodesToRead[i].AttributeId == Attributes.NodeClass && val is int ncInt)
                        val = (NodeClass)ncInt;
                    else if (
                        nodesToRead[i].AttributeId == Attributes.DataType
                        && val is NodeId dtNodeId
                        && _session.NodeCache != null
                    )
                    {
                        var dtNode = _session.NodeCache.Find(dtNodeId) as DataTypeNode; // Probeer de display naam van het datatype op te halen
                        val = dtNode?.DisplayName?.Text ?? dtNodeId.ToString();
                    }
                    else if (nodesToRead[i].AttributeId == Attributes.ValueRank && val is int vrInt)
                    {
                        val = vrInt switch
                        {
                            ValueRanks.Scalar => "Scalar (-1)",
                            ValueRanks.OneDimension => "OneDimension (1)",
                            ValueRanks.Any => "Any (0)",
                            ValueRanks.ScalarOrOneDimension => "ScalarOrOneDimension (-3)",
                            ValueRanks.OneOrMoreDimensions => "OneOrMoreDimensions (-2)",
                            _ => vrInt.ToString(),
                        };
                    }
                    attributes.Add(new NodeAttributeViewModel(attrName, val, status));
                }
            }
            catch (Exception ex)
            {
                _specificLogger.Error(
                    ex,
                    "ReadNodeAttributesAsync ({ConnectionName}): Fout bij het lezen van node attributen voor NodeId {NodeId}",
                    _config.ConnectionName,
                    nodeId
                );
                attributes.Clear(); // Verwijder eventuele deels gevulde attributen
                attributes.Add(
                    new NodeAttributeViewModel(
                        "Error",
                        $"Fout bij lezen attributen: {ex.Message}",
                        StatusCodes.BadUnexpectedError
                    )
                );
            }
            finally
            {
                _semaphore.Release();
            }
            return attributes;
        }

        /// <inheritdoc/>
        public async Task<LocalizedText> ReadNodeDisplayNameAsync(NodeId nodeId)
        {
            if (!IsConnected || _session == null)
            {
                _specificLogger.Warning(
                    "ReadNodeDisplayNameAsync ({ConnectionName}): Kan DisplayName niet lezen voor NodeId {NodeId}, geen actieve sessie.",
                    _config?.ConnectionName ?? "N/A",
                    nodeId
                );
                return null;
            }
            if (nodeId == null)
            {
                _specificLogger.Warning(
                    "ReadNodeDisplayNameAsync ({ConnectionName}): Aangeroepen met null NodeId.",
                    _config?.ConnectionName ?? "N/A"
                );
                return null;
            }

            var nodeToRead = new ReadValueId
            {
                NodeId = nodeId,
                AttributeId = Attributes.DisplayName,
            };
            var nodesToRead = new ReadValueIdCollection { nodeToRead };

            if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                _specificLogger.Warning(
                    "ReadNodeDisplayNameAsync ({ConnectionName}): Timeout bij wachten op semaphore voor NodeId {NodeId}.",
                    _config?.ConnectionName ?? "N/A",
                    nodeId
                );
                return null;
            }
            try
            {
                _specificLogger.Debug(
                    "ReadNodeDisplayNameAsync ({ConnectionName}): Leest DisplayName voor NodeId: {NodeId}",
                    _config.ConnectionName,
                    nodeId
                );
                ReadResponse response = await _session
                    .ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Neither,
                        nodesToRead,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, nodesToRead);
                return (
                    response.Results?.Count > 0 && StatusCode.IsGood(response.Results[0].StatusCode)
                )
                    ? response.Results[0].Value as LocalizedText
                    : null;
            }
            catch (Exception ex)
            {
                _specificLogger.Error(
                    ex,
                    "ReadNodeDisplayNameAsync ({ConnectionName}): Fout bij lezen DisplayName voor NodeId: {NodeId}",
                    _config.ConnectionName,
                    nodeId
                );
                return null;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <inheritdoc/>
        public NodeId ParseNodeId(string nodeIdString)
        {
            if (string.IsNullOrEmpty(nodeIdString))
            {
                _specificLogger.Warning("ParseNodeId aangeroepen met lege of null nodeIdString.");
                throw new ArgumentNullException(nameof(nodeIdString));
            }

            if (_session?.MessageContext != null)
            {
                try
                {
                    return NodeId.Parse(_session.MessageContext, nodeIdString);
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadNodeIdInvalid)
                {
                    _specificLogger.Warning(
                        "ParseNodeId ({ConnectionName}): Fout bij parsen van '{NodeIdString}' met sessie MessageContext. Status: {Status}. Probeert fallback parse.",
                        _config?.ConnectionName ?? "N/A",
                        nodeIdString,
                        sre.StatusCode
                    );
                }
            }

            // Fallback of als er geen sessie is
            try
            {
                return NodeId.Parse(nodeIdString);
            }
            catch (Exception ex)
            {
                _specificLogger.Error(
                    ex,
                    "ParseNodeId ({ConnectionName}): Fout bij parsen van NodeId string '{NodeIdString}' (ook na fallback).",
                    _config?.ConnectionName ?? "N/A",
                    nodeIdString
                );
                throw; // Gooi de exception door, want een ongeldige NodeId is kritiek.
            }
        }
    }
}
