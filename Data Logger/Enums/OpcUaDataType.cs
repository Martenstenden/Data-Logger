namespace Data_Logger.Enums
{
    /// <summary>
    /// Definieert een selectie van OPC UA datatypes die door de applicatie gebruikt kunnen worden
    /// om de interpretatie van OPC UA node waarden te specificeren.
    /// </summary>
    public enum OpcUaDataType
    {
        /// <summary>
        /// De waarde wordt als een <see cref="Opc.Ua.Variant"/> behandeld; het datatype wordt bepaald door de server.
        /// </summary>
        Variant,

        /// <summary>
        /// Een boolean waarde (true/false).
        /// </summary>
        Boolean,

        /// <summary>
        /// Een 8-bit integer met teken.
        /// </summary>
        SByte,

        /// <summary>
        /// Een 8-bit integer zonder teken.
        /// </summary>
        Byte,

        /// <summary>
        /// Een 16-bit integer met teken.
        /// </summary>
        Int16,

        /// <summary>
        /// Een 16-bit integer zonder teken.
        /// </summary>
        UInt16,

        /// <summary>
        /// Een 32-bit integer met teken.
        /// </summary>
        Int32,

        /// <summary>
        /// Een 32-bit integer zonder teken.
        /// </summary>
        UInt32,

        /// <summary>
        /// Een 64-bit integer met teken.
        /// </summary>
        Int64,

        /// <summary>
        /// Een 64-bit integer zonder teken.
        /// </summary>
        UInt64,

        /// <summary>
        /// Een 32-bit floating-point getal (single-precision).
        /// </summary>
        Float,

        /// <summary>
        /// Een 64-bit floating-point getal (double-precision).
        /// </summary>
        Double,

        /// <summary>
        /// Een tekst string.
        /// </summary>
        String,

        /// <summary>
        /// Een datum en tijd waarde.
        /// </summary>
        DateTime,
    }
}
