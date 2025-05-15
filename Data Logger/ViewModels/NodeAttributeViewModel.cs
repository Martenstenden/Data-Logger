using Data_Logger.Core;
using Opc.Ua;

namespace Data_Logger.ViewModels;

public class NodeAttributeViewModel : ObservableObject
{
    private string _attributeName;
    public string AttributeName
    {
        get => _attributeName;
        set => SetProperty(ref _attributeName, value);
    }

    private object _value;
    public object Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    private StatusCode _statusCode;
    public StatusCode StatusCode
    {
        get => _statusCode;
        set => SetProperty(ref _statusCode, value);
    }

    public string StatusCodeDisplay => StatusCode.ToString();
    public bool IsGood => StatusCode.IsGood(StatusCode);

    public NodeAttributeViewModel(string attributeName, object value, StatusCode statusCode)
    {
        AttributeName = attributeName;
        Value = value;
        StatusCode = statusCode;
    }
}
