using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;

namespace Data_Logger.Services.Implementations
{
    /// <summary>
    /// Implementatie van <see cref="ILoggingHostService"/>.
    /// Beheert een collectie van <see cref="UiLogEntry"/> objecten die in de UI kunnen worden weergegeven.
    /// Zorgt voor thread-safe toevoeging aan en verwijdering uit de collectie vanuit de UI-thread.
    /// </summary>
    public class LoggingHostService : ILoggingHostService
    {
        /// <summary>
        /// Het maximale aantal logberichten dat in de <see cref="LogEntries"/> collectie wordt bewaard.
        /// Oudere berichten worden verwijderd als deze limiet wordt overschreden.
        /// </summary>
        private const int MaxLogEntries = 1000;

        private static int _instanceCounter; // Voor het genereren van unieke InstanceId's

        /// <inheritdoc/>
        public string InstanceId { get; }

        /// <inheritdoc/>
        public ObservableCollection<UiLogEntry> LogEntries { get; }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="LoggingHostService"/> klasse.
        /// </summary>
        public LoggingHostService()
        {
            InstanceId = $"LHS_Instance_{Interlocked.Increment(ref _instanceCounter)}";
            LogEntries = new ObservableCollection<UiLogEntry>();
        }

        /// <inheritdoc/>
        public void AddLogEntry(UiLogEntry entry)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (MaxLogEntries > 0 && LogEntries.Count >= MaxLogEntries)
                {
                    // Verwijder het oudste bericht (onderaan de lijst) als de limiet is bereikt.
                    // Aangezien nieuwe items bovenaan worden ingevoegd, is het laatste item het oudste.
                    LogEntries.RemoveAt(LogEntries.Count - 1);
                }
                // Voeg het nieuwste bericht bovenaan de lijst in.
                LogEntries.Insert(0, entry);
            });
        }

        /// <inheritdoc/>
        public void ClearLogs()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                LogEntries.Clear();
            });
        }
    }
}
