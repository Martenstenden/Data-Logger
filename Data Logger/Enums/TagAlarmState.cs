namespace Data_Logger.Enums // Puntkomma verwijderd
{
    /// <summary>
    /// Definieert de mogelijke alarmstatussen voor een gemonitorde tag.
    /// </summary>
    public enum TagAlarmState
    {
        /// <summary>
        /// De tag bevindt zich binnen normale operationele grenzen.
        /// </summary>
        Normal,

        /// <summary>
        /// De tagwaarde heeft een hoge alarmgrens overschreden.
        /// </summary>
        High,

        /// <summary>
        /// De tagwaarde heeft een zeer hoge (HighHigh) alarmgrens overschreden.
        /// </summary>
        HighHigh,

        /// <summary>
        /// De tagwaarde heeft een lage alarmgrens onderschreden.
        /// </summary>
        Low,

        /// <summary>
        /// De tagwaarde heeft een zeer lage (LowLow) alarmgrens onderschreden.
        /// </summary>
        LowLow,

        /// <summary>
        /// De tagwaarde wordt beschouwd als een statistische uitschieter (outlier)
        /// ten opzichte van zijn normale gedragspatroon.
        /// </summary>
        Outlier,

        /// <summary>
        /// Er is een fout opgetreden bij het lezen of interpreteren van de tagwaarde,
        /// of de kwaliteit van de data is slecht.
        /// </summary>
        Error,
    }
}
