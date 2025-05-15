using System.Collections.ObjectModel;
using Data_Logger.Models;

namespace Data_Logger.Services.Abstractions
{
    public interface ILoggingHostService
    {
        string InstanceId { get; }

        ObservableCollection<UiLogEntry> LogEntries { get; }

        void AddLogEntry(UiLogEntry entry);

        void ClearLogs();
    }
}
