using System;
using System.Collections.Generic;
using System.Linq;
using Data_Logger.Enums;

namespace Data_Logger.Models
{
    /// <summary>
    /// Biedt een statische toegang tot de waarden van de <see cref="OpcUaDataType"/> enum.
    /// Dit is nuttig voor het binden van de enum waarden aan UI-elementen, zoals een ComboBox.
    /// </summary>
    public static class OpcUaDataTypeValues
    {
        /// <summary>
        /// Haalt een <see cref="IEnumerable{T}"/> op die alle waarden van de <see cref="OpcUaDataType"/> enum bevat.
        /// </summary>
        public static IEnumerable<OpcUaDataType> Instance =>
            Enum.GetValues(typeof(OpcUaDataType)).Cast<OpcUaDataType>();
    }
}
