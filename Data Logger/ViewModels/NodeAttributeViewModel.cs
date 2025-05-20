using Data_Logger.Core;
using Opc.Ua;

namespace Data_Logger.ViewModels
{
    /// <summary>
    /// ViewModel die een enkel attribuut van een OPC UA node representeert,
    /// inclusief de naam, waarde en statuscode.
    /// </summary>
    public class NodeAttributeViewModel : ObservableObject
    {
        private string _attributeName;

        /// <summary>
        /// Haalt de naam van het OPC UA node-attribuut op of stelt deze in (bijv. DisplayName, NodeClass).
        /// </summary>
        public string AttributeName
        {
            get => _attributeName;
            set => SetProperty(ref _attributeName, value);
        }

        private object _value;

        /// <summary>
        /// Haalt de waarde van het OPC UA node-attribuut op of stelt deze in.
        /// </summary>
        public object Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        private StatusCode _statusCode;

        /// <summary>
        /// Haalt de <see cref="Opc.Ua.StatusCode"/> op die de kwaliteit van de attribuutwaarde aangeeft, of stelt deze in.
        /// </summary>
        public StatusCode StatusCode
        {
            get => _statusCode;
            set
            {
                if (SetProperty(ref _statusCode, value))
                {
                    OnPropertyChanged(nameof(StatusCodeDisplay)); // Update afhankelijke properties
                    OnPropertyChanged(nameof(IsGood));
                }
            }
        }

        /// <summary>
        /// Haalt een string representatie van de <see cref="StatusCode"/> op.
        /// </summary>
        public string StatusCodeDisplay => StatusCode.ToString();

        /// <summary>
        /// Haalt een boolean waarde op die aangeeft of de <see cref="StatusCode"/> een goede kwaliteit representeert.
        /// </summary>
        public bool IsGood => Opc.Ua.StatusCode.IsGood(StatusCode);

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="NodeAttributeViewModel"/> klasse.
        /// </summary>
        /// <param name="attributeName">De naam van het attribuut.</param>
        /// <param name="value">De waarde van het attribuut.</param>
        /// <param name="statusCode">De statuscode geassocieerd met de waarde van het attribuut.</param>
        public NodeAttributeViewModel(string attributeName, object value, StatusCode statusCode)
        {
            _attributeName = attributeName;
            _value = value;
            _statusCode = statusCode;
        }
    }
}
