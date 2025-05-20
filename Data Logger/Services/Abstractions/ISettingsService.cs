using Data_Logger.Models;

namespace Data_Logger.Services.Abstractions
{
    /// <summary>
    /// Definieert het contract voor een service die verantwoordelijk is voor het beheren
    /// (laden, opslaan, en eventueel standaardinstellingen bieden) van de applicatie-instellingen.
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// Haalt de momenteel geladen applicatie-instellingen op.
        /// De property zelf is read-only vanuit het perspectief van de interface-gebruiker;
        /// wijzigingen aan de instellingen gebeuren via de methoden van deze service
        /// of direct op het <see cref="AppSettings"/> object zelf (indien het mutable is).
        /// </summary>
        AppSettings CurrentSettings { get; }

        /// <summary>
        /// Laadt de applicatie-instellingen van een persistent medium (bijv. een JSON-bestand).
        /// Als er geen instellingen gevonden worden, kunnen standaardinstellingen geladen worden.
        /// </summary>
        void LoadSettings();

        /// <summary>
        /// Slaat de huidige applicatie-instellingen (<see cref="CurrentSettings"/>) op naar een persistent medium.
        /// </summary>
        void SaveSettings();

        /// <summary>
        /// Laadt een set van standaardinstellingen in <see cref="CurrentSettings"/>.
        /// </summary>
        void LoadDefaultSettings();
    }
}
