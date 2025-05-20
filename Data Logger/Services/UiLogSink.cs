using System;
using System.IO; // Voor StringWriter
using Data_Logger.Models; // Voor UiLogEntry
using Data_Logger.Services.Abstractions; // Voor ILoggingHostService
using Serilog.Core; // Voor ILogEventSink
using Serilog.Events; // Voor LogEvent

namespace Data_Logger.Services
{
    /// <summary>
    /// Een Serilog Sink die log-events doorstuurt naar de <see cref="ILoggingHostService"/>
    /// voor weergave in de gebruikersinterface.
    /// Implementeert <see cref="ILogEventSink"/> om te integreren met Serilog.
    /// </summary>
    public class UiLogSink : ILogEventSink
    {
        private readonly ILoggingHostService _loggingHostService;
        private readonly IFormatProvider _formatProvider; // Voor het formatteren van logberichten
        private bool _instanceIdLoggedFromEmit; // Vlag om te zorgen dat InstanceId maar één keer gelogd wordt via Emit

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="UiLogSink"/> klasse.
        /// </summary>
        /// <param name="loggingHostService">
        /// De service die verantwoordelijk is voor het hosten en weergeven van logberichten in de UI.
        /// Mag niet null zijn.
        /// </param>
        /// <param name="formatProvider">
        /// Een optionele <see cref="IFormatProvider"/> die gebruikt kan worden bij het renderen van de logboodschap.
        /// Indien null, wordt de default formatter gebruikt.
        /// </param>
        /// <exception cref="ArgumentNullException">Als <paramref name="loggingHostService"/> null is.</exception>
        public UiLogSink(
            ILoggingHostService loggingHostService,
            IFormatProvider formatProvider = null
        )
        {
            _loggingHostService =
                loggingHostService ?? throw new ArgumentNullException(nameof(loggingHostService));
            _formatProvider = formatProvider;

            // Diagnostische output naar console bij creatie van de sink.
            // Nuttig voor het debuggen van de levenscyclus van DI-componenten.
            Console.WriteLine(
                $"[DIAGNOSTIC] UiLogSink Constructor: Gebruikt LoggingHostService met InstanceId: {_loggingHostService.InstanceId}"
            );
        }

        /// <summary>
        /// Verwerkt een log-event dat door Serilog is gegenereerd.
        /// Het event wordt geformatteerd en als een <see cref="UiLogEntry"/>
        /// toegevoegd aan de <see cref="ILoggingHostService"/>.
        /// </summary>
        /// <param name="logEvent">Het <see cref="LogEvent"/> dat door Serilog is uitgestoten.</param>
        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null)
            {
                return; // Negeer null events
            }

            // Diagnostische output voor de eerste keer dat Emit wordt aangeroepen.
            if (!_instanceIdLoggedFromEmit)
            {
                Console.WriteLine(
                    $"[DIAGNOSTIC] UiLogSink.Emit: Eerste aanroep met LoggingHostService InstanceId: {_loggingHostService.InstanceId}"
                );
                _instanceIdLoggedFromEmit = true;
            }

            string renderedMessage = RenderLogEvent(logEvent);

            var uiEntry = new UiLogEntry
            {
                Timestamp = logEvent.Timestamp.DateTime.ToLocalTime(), // Converteer naar lokale tijd voor UI weergave
                Level = logEvent.Level,
                RenderedMessage = renderedMessage,
                Exception = logEvent.Exception?.ToString(), // Converteer exceptie naar string
            };

            _loggingHostService.AddLogEntry(uiEntry);
        }

        /// <summary>
        /// Rendert de boodschap en exceptie van een <see cref="LogEvent"/> naar een string.
        /// </summary>
        /// <param name="logEvent">Het te renderen log-event.</param>
        /// <returns>Een string representatie van het log-event.</returns>
        private string RenderLogEvent(LogEvent logEvent)
        {
            using (var writer = new StringWriter()) // StringWriter is IDisposable
            {
                // Render de hoofdboodschap van het log event
                logEvent.RenderMessage(writer, _formatProvider);

                // Als er een exceptie is, voeg deze toe aan de output
                if (logEvent.Exception != null)
                {
                    writer.WriteLine(); // Extra witregel voor leesbaarheid
                    writer.Write(logEvent.Exception.ToString());
                }
                return writer.ToString();
            }
        }
    }
}
