using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace Data_Logger.Services.Implementations
{
    public sealed partial class OpcUaService
    {
        /// <inheritdoc/>
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
                _specificLogger.Warning(
                    "BrowseAsync ({ConnectionName}): Kan niet browsen, geen actieve OPC UA sessie.",
                    _config?.ConnectionName ?? "N/A"
                );
                return new ReferenceDescriptionCollection(); // Retourneer een lege collectie
            }

            if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false))
            {
                _specificLogger.Warning(
                    "BrowseAsync ({ConnectionName}): Timeout (10s) bij wachten op semaphore voor browsen van NodeId {NodeIdToBrowse}. Operatie afgebroken.",
                    _config?.ConnectionName ?? "N/A",
                    nodeIdToBrowse
                );
                return new ReferenceDescriptionCollection(); // Retourneer een lege collectie
            }

            try
            {
                _specificLogger.Debug(
                    "BrowseAsync ({ConnectionName}): Starten met browsen vanaf NodeId: {NodeIdToBrowse}, ReferenceType: {ReferenceType}, Direction: {Direction}",
                    _config?.ConnectionName ?? "N/A",
                    nodeIdToBrowse,
                    referenceTypeId ?? ReferenceTypeIds.HierarchicalReferences,
                    direction
                );

                var nodeToBrowseDesc = new BrowseDescription
                {
                    NodeId = nodeIdToBrowse,
                    BrowseDirection = direction,
                    ReferenceTypeId = referenceTypeId ?? ReferenceTypeIds.HierarchicalReferences, // Default naar hiërarchische referenties
                    IncludeSubtypes = includeSubtypes,
                    NodeClassMask = (uint)nodeClassMask,
                    ResultMask = (uint)BrowseResultMask.All, // Vraag alle beschikbare informatie op
                };
                var nodesToBrowse = new BrowseDescriptionCollection { nodeToBrowseDesc };

                // RequestHeader en ViewDescription kunnen null zijn voor standaard gedrag
                RequestHeader requestHeader = null;
                ViewDescription viewDescription = null;
                uint maxResultsToReturn = 0; // 0 voor geen limiet door de client (server kan nog steeds limiteren)

                // Roep de Browse service aan op de sessie
                BrowseResponse response = await _session
                    .BrowseAsync(
                        requestHeader,
                        viewDescription,
                        maxResultsToReturn,
                        nodesToBrowse,
                        ct
                    )
                    .ConfigureAwait(false);

                // Valideer de response
                ClientBase.ValidateResponse(response.Results, nodesToBrowse);

                ReferenceDescriptionCollection allReferences = new ReferenceDescriptionCollection();
                ByteStringCollection continuationPoints = null;

                // Verwerk de eerste set resultaten
                if (response.Results != null && response.Results.Count > 0)
                {
                    if (StatusCode.IsBad(response.Results[0].StatusCode))
                    {
                        _specificLogger.Warning(
                            "BrowseAsync ({ConnectionName}): Browse operatie voor NodeId {NodeIdToBrowse} gaf een slechte statuscode: {StatusCode}",
                            _config?.ConnectionName ?? "N/A",
                            nodeIdToBrowse,
                            response.Results[0].StatusCode
                        );
                        return allReferences; // Retourneer lege collectie bij slechte status
                    }
                    allReferences.AddRange(response.Results[0].References);

                    // Controleer of er een continuation point is voor meer resultaten
                    if (
                        response.Results[0].ContinuationPoint != null
                        && response.Results[0].ContinuationPoint.Length > 0
                    )
                    {
                        continuationPoints = new ByteStringCollection
                        {
                            response.Results[0].ContinuationPoint,
                        };
                        _specificLogger.Verbose(
                            "BrowseAsync ({ConnectionName}): ContinuationPoint ontvangen voor NodeId {NodeIdToBrowse}, er zijn meer resultaten.",
                            _config?.ConnectionName ?? "N/A",
                            nodeIdToBrowse
                        );
                    }
                }

                // Haal eventuele vervolgresultaten op met BrowseNext
                while (
                    continuationPoints != null
                    && continuationPoints.Count > 0
                    && continuationPoints[0] != null
                    && continuationPoints[0].Length > 0
                    && !ct.IsCancellationRequested
                )
                {
                    _specificLogger.Verbose(
                        "BrowseAsync ({ConnectionName}): BrowseNext aanroepen voor NodeId {NodeIdToBrowse}...",
                        _config?.ConnectionName ?? "N/A",
                        nodeIdToBrowse
                    );
                    BrowseNextResponse nextResponse = await _session
                        .BrowseNextAsync(null, false, continuationPoints, ct)
                        .ConfigureAwait(false);
                    ClientBase.ValidateResponse(nextResponse.Results, continuationPoints); // Valideer

                    if (nextResponse.Results != null && nextResponse.Results.Count > 0)
                    {
                        if (StatusCode.IsBad(nextResponse.Results[0].StatusCode))
                        {
                            _specificLogger.Warning(
                                "BrowseAsync ({ConnectionName}): BrowseNext operatie voor NodeId {NodeIdToBrowse} gaf een slechte statuscode: {StatusCode}",
                                _config?.ConnectionName ?? "N/A",
                                nodeIdToBrowse,
                                nextResponse.Results[0].StatusCode
                            );
                            break; // Stop met browsen bij een slechte status
                        }
                        allReferences.AddRange(nextResponse.Results[0].References);

                        // Reset continuationPoints en vul opnieuw als er een nieuwe is
                        continuationPoints = new ByteStringCollection();
                        if (
                            nextResponse.Results[0].ContinuationPoint != null
                            && nextResponse.Results[0].ContinuationPoint.Length > 0
                        )
                        {
                            continuationPoints.Add(nextResponse.Results[0].ContinuationPoint);
                        }
                    }
                    else
                    {
                        break; // Geen resultaten meer
                    }
                }

                _specificLogger.Debug(
                    "BrowseAsync ({ConnectionName}): {ReferenceCount} referenties gevonden voor NodeId: {NodeIdToBrowse}",
                    _config?.ConnectionName ?? "N/A",
                    allReferences.Count,
                    nodeIdToBrowse
                );
                return allReferences;
            }
            catch (ServiceResultException sre) // Specifieke OPC UA fouten
            {
                _specificLogger.Error(
                    sre,
                    "ServiceResultException tijdens BrowseAsync ({ConnectionName}) voor NodeId: {NodeIdToBrowse}. Status: {StatusCode}",
                    _config?.ConnectionName ?? "N/A",
                    nodeIdToBrowse,
                    sre.StatusCode
                );
                return new ReferenceDescriptionCollection();
            }
            catch (Exception ex) // Algemene andere fouten
            {
                _specificLogger.Error(
                    ex,
                    "Algemene Exception tijdens BrowseAsync ({ConnectionName}) voor NodeId: {NodeIdToBrowse}",
                    _config?.ConnectionName ?? "N/A",
                    nodeIdToBrowse
                );
                return new ReferenceDescriptionCollection();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<ReferenceDescriptionCollection> BrowseRootAsync()
        {
            return await BrowseAsync(ObjectIds.ObjectsFolder, ct: CancellationToken.None)
                .ConfigureAwait(false);
        }

        /// <inheritdoc/>
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
                )
                .ConfigureAwait(false);
        }
    }
}
