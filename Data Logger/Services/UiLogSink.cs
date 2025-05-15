using System;
using System.IO;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Serilog.Core;
using Serilog.Events;

namespace Data_Logger.Services
{
    public class UiLogSink : ILogEventSink
    {
        private readonly ILoggingHostService _loggingHostService;
        private readonly IFormatProvider _formatProvider;
        private bool _instanceIdLoggedFromEmit = false;

        public UiLogSink(
            ILoggingHostService loggingHostService,
            IFormatProvider formatProvider = null
        )
        {
            _loggingHostService =
                loggingHostService ?? throw new ArgumentNullException(nameof(loggingHostService));
            _formatProvider = formatProvider;

            Console.WriteLine(
                $"[DIAGNOSTIC] UiLogSink Constructor: Gebruikt LoggingHostService met InstanceId: {_loggingHostService.InstanceId}"
            );
        }

        public void Emit(LogEvent logEvent)
        {
            if (!_instanceIdLoggedFromEmit)
            {
                Console.WriteLine(
                    $"[DIAGNOSTIC] UiLogSink.Emit: Eerste aanroep met LoggingHostService InstanceId: {_loggingHostService.InstanceId}"
                );
                _instanceIdLoggedFromEmit = true;
            }

            if (logEvent == null)
                return;

            var renderedMessage = RenderLogEvent(logEvent);

            var uiEntry = new UiLogEntry
            {
                Timestamp = logEvent.Timestamp.DateTime.ToLocalTime(),
                Level = logEvent.Level,
                Message = logEvent.MessageTemplate.Text,
                RenderedMessage = renderedMessage,
                Exception = logEvent.Exception?.ToString(),
            };

            _loggingHostService.AddLogEntry(uiEntry);
        }

        private string RenderLogEvent(LogEvent logEvent)
        {
            var writer = new StringWriter();
            logEvent.RenderMessage(writer, _formatProvider);
            if (logEvent.Exception != null)
            {
                writer.WriteLine();
                writer.Write(logEvent.Exception.ToString());
            }
            return writer.ToString();
        }
    }
}
