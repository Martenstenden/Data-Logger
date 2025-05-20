using System.Collections.ObjectModel;
using Data_Logger.Models;

namespace Data_Logger.Services.Abstractions
{
    /// <summary>
    /// Definieert het contract voor een service die logberichten van de applicatie host
    /// en beschikbaar maakt voor weergave in de gebruikersinterface.
    /// Deze service fungeert als een brug tussen het logging-framework (bijv. Serilog) en de UI.
    /// </summary>
    public interface ILoggingHostService
    {
        /// <summary>
        /// Haalt een unieke identifier op voor deze instantie van de logging host service.
        /// Nuttig voor diagnostische doeleinden om instanties te onderscheiden.
        /// </summary>
        string InstanceId { get; }

        /// <summary>
        /// Haalt de observeerbare collectie van <see cref="UiLogEntry"/> objecten op.
        /// UI-elementen kunnen aan deze collectie binden om logberichten weer te geven.
        /// </summary>
        ObservableCollection<UiLogEntry> LogEntries { get; }

        /// <summary>
        /// Voegt een nieuw logbericht toe aan de <see cref="LogEntries"/> collectie.
        /// Deze methode wordt typisch aangeroepen door een custom Serilog sink.
        /// </summary>
        /// <param name="entry">Het <see cref="UiLogEntry"/> object dat toegevoegd moet worden.</param>
        void AddLogEntry(UiLogEntry entry);

        /// <summary>
        /// Wist alle logberichten uit de <see cref="LogEntries"/> collectie.
        /// </summary>
        void ClearLogs();
    }
}
