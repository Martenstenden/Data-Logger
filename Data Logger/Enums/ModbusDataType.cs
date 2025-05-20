namespace Data_Logger.Enums
{
    /// <summary>
    /// Definieert de ondersteunde datatypes voor Modbus communicatie.
    /// Deze specificeren hoe de ruwe data uit Modbus registers geïnterpreteerd moet worden.
    /// </summary>
    public enum ModbusDataType
    {
        /// <summary>
        /// Een boolean waarde (vaak van een coil of een enkel bit in een register).
        /// </summary>
        Boolean,

        /// <summary>
        /// Een 16-bit integer met teken.
        /// </summary>
        Int16,

        /// <summary>
        /// Een 16-bit integer zonder teken.
        /// </summary>
        UInt16,

        /// <summary>
        /// Een 32-bit integer met teken (vereist 2 Modbus registers).
        /// </summary>
        Int32,

        /// <summary>
        /// Een 32-bit integer zonder teken (vereist 2 Modbus registers).
        /// </summary>
        UInt32,

        /// <summary>
        /// Een 32-bit floating-point getal (vereist 2 Modbus registers).
        /// </summary>
        Float32,
    }
}
