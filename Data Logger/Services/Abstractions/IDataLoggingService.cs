using System.Collections.Generic;
using Data_Logger.Models;

namespace Data_Logger.Services.Abstractions
{
    public interface IDataLoggingService
    {
        void LogTagValues(string connectionName, IEnumerable<LoggedTagValue> tagValues);
    }
}
