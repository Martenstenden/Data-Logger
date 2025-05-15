using Data_Logger.Core;
using Opc.Ua;

namespace Data_Logger.ViewModels;

public class ReferenceDescriptionViewModel : ObservableObject
{
    public NodeId ReferenceTypeId { get; }
    public string ReferenceTypeDisplay { get; }
    public bool IsForward { get; }
    public NodeId TargetNodeId { get; }
    public string TargetNodeIdString => TargetNodeId?.ToString() ?? "N/A";
    public NodeClass TargetNodeClass { get; }
    public string TargetBrowseName { get; }
    public string TargetDisplayName { get; }

    public ReferenceDescriptionViewModel(
        ReferenceDescription rd,
        NodeId referenceTypeId,
        string referenceTypeDisplay,
        bool isForward,
        NodeId targetNodeId
    )
    {
        ReferenceTypeId = referenceTypeId;
        ReferenceTypeDisplay = referenceTypeDisplay;
        IsForward = isForward;
        TargetNodeId = targetNodeId;
        TargetNodeClass = rd.NodeClass;
        TargetBrowseName = rd.BrowseName?.ToString() ?? "N/A";
        TargetDisplayName = rd.DisplayName?.Text ?? "N/A";
    }
}
