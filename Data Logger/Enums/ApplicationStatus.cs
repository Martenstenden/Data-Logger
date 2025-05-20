namespace Data_Logger.Enums
{
    /// <summary>
    /// Definieert de verschillende operationele statussen van de Data Logger applicatie.
    /// </summary>
    public enum ApplicationStatus
    {
        /// <summary>
        /// De applicatie is inactief of wacht op input.
        /// </summary>
        Idle,

        /// <summary>
        /// De applicatie is bezig met het opzetten van een verbinding.
        /// </summary>
        Connecting,

        /// <summary>
        /// De applicatie is actief data aan het loggen.
        /// </summary>
        Logging,

        /// <summary>
        /// De applicatie heeft een waarschuwing gegenereerd; mogelijk is actie vereist maar niet kritiek.
        /// </summary>
        Warning,

        /// <summary>
        /// Er is een kritieke fout opgetreden in de applicatie.
        /// </summary>
        Error,

        /// <summary>
        /// De applicatie is bezig met het laden van data of configuratie.
        /// </summary>
        Loading,

        /// <summary>
        /// De applicatie is bezig met het opslaan van data of configuratie.
        /// </summary>
        Saving,
    }
}
