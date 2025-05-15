using System;
using Serilog.Events;

namespace Data_Logger.Models
{
    public class UiLogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogEventLevel Level { get; set; }
        public string LevelDisplay => Level.ToString();
        public string Message { get; set; }
        public string RenderedMessage { get; set; }
        public string Exception { get; set; }
    }
}
