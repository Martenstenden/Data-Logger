namespace Data_Logger.Enums
{
    /// <summary>
    /// Definieert de types van dataverbindingen die de applicatie ondersteunt.
    /// </summary>
    public enum ConnectionType
    {
        /// <summary>
        /// Een verbinding via het OPC UA (Open Platform Communications Unified Architecture) protocol.
        /// </summary>
        OpcUa,

        /// <summary>
        /// Een verbinding via het Modbus TCP/IP protocol.
        /// </summary>
        ModbusTcp,
    }
}
