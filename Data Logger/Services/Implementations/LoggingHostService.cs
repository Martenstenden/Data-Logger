using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;

namespace Data_Logger.Services.Implementations
{
    public class LoggingHostService : ILoggingHostService
    {
        public string InstanceId { get; }
        private static int _instanceCounter = 0;

        public ObservableCollection<UiLogEntry> LogEntries { get; }

        private const int MaxLogEntries = 1000;

        public LoggingHostService()
        {
            InstanceId = $"LHS_Instance_{Interlocked.Increment(ref _instanceCounter)}";

            LogEntries = new ObservableCollection<UiLogEntry>();
        }

        public void AddLogEntry(UiLogEntry entry)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (MaxLogEntries > 0 && LogEntries.Count >= MaxLogEntries)
                {
                    LogEntries.RemoveAt(LogEntries.Count - 1);
                }
                LogEntries.Insert(0, entry);
            });
        }

        public void ClearLogs()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogEntries.Clear();
            });
        }
    }
}
