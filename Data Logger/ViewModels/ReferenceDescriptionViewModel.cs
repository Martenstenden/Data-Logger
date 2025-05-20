using System;
using Data_Logger.Core;
using Opc.Ua;

namespace Data_Logger.ViewModels
{
    /// <summary>
    /// ViewModel die de details van een <see cref="ReferenceDescription"/> representeert,
    /// gebruikt voor het weergeven van referenties tussen OPC UA nodes in de UI.
    /// </summary>
    public class ReferenceDescriptionViewModel : ObservableObject
    {
        /// <summary>
        /// Haalt de <see cref="NodeId"/> van het referentietype op (bijv. Organizes, HasComponent).
        /// </summary>
        public NodeId ReferenceTypeId { get; }

        /// <summary>
        /// Haalt de weergavenaam van het referentietype op.
        /// </summary>
        public string ReferenceTypeDisplay { get; }

        /// <summary>
        /// Haalt een boolean waarde op die aangeeft of dit een voorwaartse (true) of achterwaartse (false) referentie is.
        /// </summary>
        public bool IsForward { get; }

        /// <summary>
        /// Haalt de <see cref="NodeId"/> van de doel-node van deze referentie op.
        /// </summary>
        public NodeId TargetNodeId { get; }

        /// <summary>
        /// Haalt een string representatie van de <see cref="TargetNodeId"/> op.
        /// </summary>
        public string TargetNodeIdString => TargetNodeId?.ToString() ?? "N/A";

        /// <summary>
        /// Haalt de <see cref="NodeClass"/> van de doel-node op.
        /// </summary>
        public NodeClass TargetNodeClass { get; }

        /// <summary>
        /// Haalt de weergavenaam (DisplayName) van de doel-node op.
        /// </summary>
        public string TargetDisplayName { get; }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="ReferenceDescriptionViewModel"/> klasse.
        /// </summary>
        /// <param name="rd">De OPC UA <see cref="ReferenceDescription"/> waarvan de details weergegeven moeten worden.</param>
        /// <param name="referenceTypeDisplay">De weergavenaam van het referentietype.</param>
        /// <param name="isForward">Geeft aan of de referentie voorwaarts is.</param>
        /// <param name="targetNodeId">De <see cref="NodeId"/> van de doel-node.</param>
        /// <exception cref="ArgumentNullException">Als <paramref name="rd"/> null is.</exception>
        public ReferenceDescriptionViewModel(
            ReferenceDescription rd,
            string referenceTypeDisplay,
            bool isForward,
            NodeId targetNodeId
        )
        {
            if (rd == null)
                throw new ArgumentNullException(nameof(rd));

            ReferenceTypeId = rd.ReferenceTypeId;
            ReferenceTypeDisplay = referenceTypeDisplay;
            IsForward = isForward;
            TargetNodeId = targetNodeId;
            TargetNodeClass = rd.NodeClass;
            TargetDisplayName = rd.DisplayName?.Text ?? "N/A";
        }
    }
}
