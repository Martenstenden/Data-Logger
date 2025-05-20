using System.Collections.Generic;
using Data_Logger.Models;

namespace Data_Logger.Services.Abstractions
{
    /// <summary>
    /// Definieert het contract voor een service die verantwoordelijk is voor het loggen van data,
    /// zoals tag-waarden, naar een persistent medium (bijv. bestanden, database).
    /// </summary>
    public interface IDataLoggingService
    {
        /// <summary>
        /// Logt een collectie van tag-waarden die geassocieerd zijn met een specifieke connectie.
        /// </summary>
        /// <param name="connectionName">De naam van de connectie waarvan de tag-waarden afkomstig zijn.
        /// Dit wordt vaak gebruikt om logs te groeperen of bestandsnamen te genereren.</param>
        /// <param name="tagValues">Een <see cref="IEnumerable{T}"/> van <see cref="LoggedTagValue"/> objecten die gelogd moeten worden.</param>
        void LogTagValues(string connectionName, IEnumerable<LoggedTagValue> tagValues);
    }
}
