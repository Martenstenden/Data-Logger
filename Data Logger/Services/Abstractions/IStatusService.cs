using Data_Logger.Enums;

namespace Data_Logger.Services.Abstractions
{
    /// <summary>
    /// Definieert het contract voor een service die de algehele status van de applicatie
    /// beheert en communiceert. Dit kan gebruikt worden om de gebruiker te informeren
    /// over wat de applicatie aan het doen is (bijv. laden, loggen, fout).
    /// </summary>
    public interface IStatusService
    {
        /// <summary>
        /// Haalt de huidige <see cref="ApplicationStatus"/> van de applicatie op.
        /// </summary>
        ApplicationStatus CurrentStatus { get; }

        /// <summary>
        /// Haalt een beschrijvend bericht op dat de <see cref="CurrentStatus"/> verder toelicht.
        /// </summary>
        string StatusMessage { get; }

        /// <summary>
        /// Stelt de status van de applicatie in.
        /// </summary>
        /// <param name="status">De nieuwe <see cref="ApplicationStatus"/>.</param>
        /// <param name="message">Een begeleidend bericht dat de status toelicht.</param>
        void SetStatus(ApplicationStatus status, string message);
    }
}
