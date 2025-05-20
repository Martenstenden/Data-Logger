using System;
using Serilog.Events;

namespace Data_Logger.Models
{
    /// <summary>
    /// Representeert een enkel logbericht voor weergave in de gebruikersinterface.
    /// </summary>
    public class UiLogEntry
    {
        /// <summary>
        /// Haalt het tijdstip van het logbericht op of stelt deze in.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Haalt het <see cref="LogEventLevel"/> (bijv. Informatie, Waarschuwing, Fout) van het logbericht op of stelt deze in.
        /// </summary>
        public LogEventLevel Level { get; set; }

        /// <summary>
        /// Haalt een stringrepresentatie van het <see cref="Level"/> op.
        /// </summary>
        public string LevelDisplay => Level.ToString();

        /// <summary>
        /// Haalt de gerenderde (geformatteerde) boodschap van het logbericht op of stelt deze in.
        /// </summary>
        public string RenderedMessage { get; set; }

        /// <summary>
        /// Haalt een stringrepresentatie van een eventuele bijbehorende exceptie op of stelt deze in.
        /// Kan null zijn als er geen exceptie is.
        /// </summary>
        public string Exception { get; set; }
    }
}
