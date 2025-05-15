using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Data_Logger.Models;

namespace Data_Logger.Services.Abstractions
{
    public class ModbusDataPoint
    {
        public ushort Address { get; set; }
        public ushort Value { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public interface IModbusService : IDisposable
    {
        bool IsConnected { get; }

        event EventHandler ConnectionStatusChanged;

        event EventHandler<IEnumerable<LoggedTagValue>> TagsDataReceived; 

        Task<bool> ConnectAsync();

        Task DisconnectAsync();

        Task PollConfiguredTagsAsync();

        void Reconfigure(ModbusTcpConnectionConfig newConfig);
    }
}
