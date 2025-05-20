namespace Data_Logger.Enums
{
    /// <summary>
    /// Definieert de verschillende types Modbus registers.
    /// </summary>
    public enum ModbusRegisterType
    {
        /// <summary>
        /// Een 16-bit read/write register.
        /// </summary>
        HoldingRegister,

        /// <summary>
        /// Een 16-bit read-only register.
        /// </summary>
        InputRegister,

        /// <summary>
        /// Een enkel-bit read/write register (vaak gebruikt voor digitale outputs).
        /// </summary>
        Coil,

        /// <summary>
        /// Een enkel-bit read-only register (vaak gebruikt voor digitale inputs).
        /// </summary>
        DiscreteInput,
    }
}
