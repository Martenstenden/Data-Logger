using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Data_Logger.Models;
using Data_Logger.ViewModels;
using Opc.Ua;

namespace Data_Logger.Services.Abstractions
{
    /// <summary>
    /// Definieert het contract voor een service die OPC UA (Open Platform Communications Unified Architecture)
    /// communicatie afhandelt. Dit omvat het beheren van sessies, subscriptions, browsen van de adresruimte,
    /// en het lezen/schrijven van node-waarden.
    /// Implementeert <see cref="IDisposable"/> voor het correct vrijgeven van resources.
    /// </summary>
    public interface IOpcUaService : IDisposable
    {
        /// <summary>
        /// Haalt een waarde op die aangeeft of de service momenteel verbonden is met een OPC UA server.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Haalt de <see cref="NamespaceTable"/> op die gebruikt wordt door de huidige OPC UA sessie.
        /// Deze tabel is nodig om NodeIds correct te parsen en te interpreteren.
        /// Kan null zijn als er geen actieve sessie is.
        /// </summary>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// Event dat wordt getriggerd wanneer de connectiestatus (verbonden/niet verbonden) verandert.
        /// </summary>
        event EventHandler ConnectionStatusChanged;

        /// <summary>
        /// Event dat wordt getriggerd wanneer er nieuwe tag-data is ontvangen van de OPC UA server
        /// (meestal via een subscription).
        /// De data wordt geleverd als een collectie van <see cref="LoggedTagValue"/> objecten.
        /// </summary>
        event EventHandler<IEnumerable<LoggedTagValue>> TagsDataReceived;

        /// <summary>
        /// Probeert asynchroon een verbinding en sessie op te zetten met de OPC UA server,
        /// gebaseerd op de huidige configuratie.
        /// </summary>
        /// <returns>Een Task die resulteert in true als de verbinding succesvol is opgezet, anders false.</returns>
        Task<bool> ConnectAsync();

        /// <summary>
        /// Verbreekt asynchroon de huidige sessie en verbinding met de OPC UA server.
        /// </summary>
        /// <returns>Een Task die de disconnectie-operatie representeert.</returns>
        Task DisconnectAsync();

        /// <summary>
        /// Herconfigureert de OPC UA service met een nieuwe set van verbindings- en tag-instellingen.
        /// Dit kan nodig zijn als de gebruiker instellingen wijzigt.
        /// Een actieve verbinding/sessie kan mogelijk herstart of aangepast moeten worden.
        /// </summary>
        /// <param name="newConfig">De nieuwe <see cref="OpcUaConnectionConfig"/> om te gebruiken.</param>
        void Reconfigure(OpcUaConnectionConfig newConfig);

        /// <summary>
        /// Start asynchroon het monitoren van de geconfigureerde OPC UA tags via een subscription.
        /// Het <see cref="TagsDataReceived"/> event zal worden getriggerd bij datawijzigingen.
        /// </summary>
        /// <returns>Een Task die de start-monitoring operatie representeert.</returns>
        Task StartMonitoringTagsAsync();

        /// <summary>
        /// Stopt asynchroon het monitoren van OPC UA tags en verwijdert de actieve subscription.
        /// </summary>
        /// <returns>Een Task die de stop-monitoring operatie representeert.</returns>
        Task StopMonitoringTagsAsync();

        /// <summary>
        /// Leest asynchroon de huidige waarden van alle actief geconfigureerde OPC UA tags.
        /// </summary>
        /// <returns>
        /// Een Task die resulteert in een <see cref="IEnumerable{T}"/> van <see cref="LoggedTagValue"/> objecten
        /// met de huidige waarden, timestamps en kwaliteitsinformatie.
        /// </returns>
        Task<IEnumerable<LoggedTagValue>> ReadCurrentTagValuesAsync();

        /// <summary>
        /// Browse asynchroon de adresruimte van de OPC UA server vanaf een gespecificeerde node.
        /// </summary>
        /// <param name="nodeIdToBrowse">De <see cref="NodeId"/> van de node om vanaf te browsen.</param>
        /// <param name="referenceTypeId">Optioneel; de <see cref="NodeId"/> van het referentietype om te filteren (bijv. HierarchicalReferences). Default is null (alle referentietypes).</param>
        /// <param name="includeSubtypes">Optioneel; true om ook subtypes van het <paramref name="referenceTypeId"/> mee te nemen. Default is true.</param>
        /// <param name="direction">Optioneel; de browse richting (<see cref="BrowseDirection.Forward"/>, <see cref="BrowseDirection.Backward"/>, of <see cref="BrowseDirection.Both"/>). Default is Forward.</param>
        /// <param name="nodeClassMask">Optioneel; een masker om te filteren op <see cref="NodeClass"/> (bijv. Object, Variable). Default is Unspecified (alle klasses).</param>
        /// <param name="ct">Optioneel; een <see cref="CancellationToken"/> voor de operatie.</param>
        /// <returns>
        /// Een Task die resulteert in een <see cref="ReferenceDescriptionCollection"/> met de gevonden referenties,
        /// of een lege collectie als er niets gevonden is of een fout optreedt.
        /// </returns>
        Task<ReferenceDescriptionCollection> BrowseAsync(
            NodeId nodeIdToBrowse,
            NodeId referenceTypeId = null, // Vaak Opc.Ua.ReferenceTypeIds.HierarchicalReferences
            bool includeSubtypes = true,
            BrowseDirection direction = BrowseDirection.Forward,
            NodeClass nodeClassMask = NodeClass.Unspecified,
            CancellationToken ct = default
        );

        /// <summary>
        /// Browse asynchroon de root van de OPC UA server adresruimte (meestal de "Objects" folder).
        /// </summary>
        /// <returns>
        /// Een Task die resulteert in een <see cref="ReferenceDescriptionCollection"/> met de items in de root.
        /// </returns>
        Task<ReferenceDescriptionCollection> BrowseRootAsync();

        /// <summary>
        /// Leest asynchroon de waarde van een specifieke OPC UA node.
        /// </summary>
        /// <param name="nodeId">De <see cref="NodeId"/> van de node waarvan de waarde gelezen moet worden.</param>
        /// <returns>
        /// Een Task die resulteert in een <see cref="DataValue"/> object dat de waarde, status en timestamps bevat.
        /// Kan null of een DataValue met een slechte statuscode retourneren bij een fout.
        /// </returns>
        Task<DataValue> ReadValueAsync(NodeId nodeId);

        /// <summary>
        /// Leest asynchroon een lijst van standaard attributen van een specifieke OPC UA node.
        /// </summary>
        /// <param name="nodeId">De <see cref="NodeId"/> van de node waarvan de attributen gelezen moeten worden.</param>
        /// <returns>
        /// Een Task die resulteert in een lijst van <see cref="NodeAttributeViewModel"/> objecten.
        /// </returns>
        Task<List<NodeAttributeViewModel>> ReadNodeAttributesAsync(NodeId nodeId);

        /// <summary>
        /// Browse asynchroon alle referenties (zowel forward als backward, tenzij anders gespecificeerd)
        /// van een gegeven node, zonder specifiek referentietype filter.
        /// </summary>
        /// <param name="nodeIdToBrowse">De <see cref="NodeId"/> om te browsen.</param>
        /// <param name="direction">De browse richting. Default is <see cref="BrowseDirection.Both"/>.</param>
        /// <returns>Een Task die resulteert in een <see cref="ReferenceDescriptionCollection"/>.</returns>
        Task<ReferenceDescriptionCollection> BrowseAllReferencesAsync(
            NodeId nodeIdToBrowse,
            BrowseDirection direction = BrowseDirection.Both
        );

        /// <summary>
        /// Leest asynchroon de DisplayName van een specifieke OPC UA node.
        /// </summary>
        /// <param name="nodeId">De <see cref="NodeId"/> van de node.</param>
        /// <returns>Een Task die resulteert in een <see cref="LocalizedText"/> object met de DisplayName, of null bij een fout.</returns>
        Task<LocalizedText> ReadNodeDisplayNameAsync(NodeId nodeId);

        /// <summary>
        /// Parset een string representatie van een NodeId naar een <see cref="NodeId"/> object,
        /// gebruikmakend van de namespace tabel van de huidige sessie indien beschikbaar.
        /// </summary>
        /// <param name="nodeIdString">De string representatie van de NodeId (bijv. "ns=2;i=1001").</param>
        /// <returns>Het geparste <see cref="NodeId"/> object.</returns>
        /// <exception cref="ServiceResultException">Als de string ongeldig is of de namespace URI niet gevonden kan worden.</exception>
        NodeId ParseNodeId(string nodeIdString);
    }
}
