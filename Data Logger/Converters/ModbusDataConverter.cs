using System;
using Data_Logger.Enums;
using Serilog;

namespace Data_Logger.Converters
{
    /// <summary>
    /// Utility klasse voor het converteren van Modbus register data (ushort arrays)
    /// naar verschillende .NET datatypes, rekening houdend met Modbus byte-volgorde conventies.
    /// </summary>
    public static class ModbusDataConverter
    {
        /// <summary>
        /// Interpreteert een array van Modbus registers (ushorts) naar het gespecificeerde ModbusDataType.
        /// Modbus data wordt typisch Big Endian verzonden (meest significante byte eerst).
        /// Deze methode houdt rekening met de endianness van het huidige systeem bij het converteren.
        /// </summary>
        /// <param name="registers">De array van ushorts die de Modbus register data bevatten.</param>
        /// <param name="dataType">Het gewenste Modbus datatype om de registers naar te converteren.</param>
        /// <param name="logger">Optionele Serilog logger voor het loggen van fouten of waarschuwingen.</param>
        /// <returns>Een object dat de geconverteerde waarde representeert.</returns>
        /// <exception cref="ArgumentNullException">Als <paramref name="registers"/> null is.</exception>
        /// <exception cref="ArgumentException">Als <paramref name="registers"/> niet genoeg data bevat voor het gespecificeerde <paramref name="dataType"/>.</exception>
        public static object InterpretRegisterData(
            ushort[] registers,
            ModbusDataType dataType,
            ILogger logger = null
        )
        {
            if (registers == null)
            {
                logger?.Error("InterpretRegisterData: Input 'registers' array is null.");
                throw new ArgumentNullException(nameof(registers));
            }

            // Validatie van het aantal benodigde registers gebaseerd op het datatype.
            switch (dataType)
            {
                case ModbusDataType.Boolean:
                case ModbusDataType.Int16:
                case ModbusDataType.UInt16:
                    if (registers.Length < 1)
                    {
                        logger?.Error(
                            "InterpretRegisterData: Onvoldoende registers voor {DataType}. Verwacht 1, kreeg {Length}.",
                            dataType,
                            registers.Length
                        );
                        throw new ArgumentException(
                            $@"Onvoldoende registers voor {dataType}. Verwacht 1, kreeg {registers.Length}.",
                            nameof(registers)
                        );
                    }
                    break;
                case ModbusDataType.Int32:
                case ModbusDataType.UInt32:
                case ModbusDataType.Float32:
                    if (registers.Length < 2)
                    {
                        logger?.Error(
                            "InterpretRegisterData: Onvoldoende registers voor {DataType}. Verwacht 2, kreeg {Length}.",
                            dataType,
                            registers.Length
                        );
                        throw new ArgumentException(
                            $@"Onvoldoende registers voor {dataType}. Verwacht 2, kreeg {registers.Length}.",
                            nameof(registers)
                        );
                    }
                    break;
            }

            // Daadwerkelijke conversie
            switch (dataType)
            {
                case ModbusDataType.Boolean:
                    // Een niet-nul waarde in het register wordt als true geïnterpreteerd.
                    return registers[0] != 0;

                case ModbusDataType.Int16:
                    return (short)registers[0];

                case ModbusDataType.UInt16:
                    return registers[0];

                case ModbusDataType.Int32:
                    // Combineer twee ushorts (registers) naar een Int32.
                    // Modbus stuurt vaak MSB (Most Significant Byte) van het eerste register,
                    // dan LSB van het eerste register, dan MSB van het tweede register, etc. (Big Endian per register).
                    // BitConverter werkt met de endianness van het systeem.
                    byte[] bytesInt32 = new byte[4];
                    bytesInt32[0] = (byte)(registers[0] >> 8); // MSB van register 0
                    bytesInt32[1] = (byte)(registers[0] & 0xFF); // LSB van register 0
                    bytesInt32[2] = (byte)(registers[1] >> 8); // MSB van register 1
                    bytesInt32[3] = (byte)(registers[1] & 0xFF); // LSB van register 1

                    // Als het systeem Little Endian is, moeten de bytes omgedraaid worden
                    // omdat BitConverter.ToInt32 uitgaat van Little Endian byte-volgorde.
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(bytesInt32);
                    }
                    return BitConverter.ToInt32(bytesInt32, 0);

                case ModbusDataType.UInt32:
                    byte[] bytesUInt32 = new byte[4];
                    bytesUInt32[0] = (byte)(registers[0] >> 8);
                    bytesUInt32[1] = (byte)(registers[0] & 0xFF);
                    bytesUInt32[2] = (byte)(registers[1] >> 8);
                    bytesUInt32[3] = (byte)(registers[1] & 0xFF);
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(bytesUInt32);
                    }
                    return BitConverter.ToUInt32(bytesUInt32, 0);

                case ModbusDataType.Float32: // single-precision floating-point
                    byte[] bytesFloat32 = new byte[4];
                    bytesFloat32[0] = (byte)(registers[0] >> 8);
                    bytesFloat32[1] = (byte)(registers[0] & 0xFF);
                    bytesFloat32[2] = (byte)(registers[1] >> 8);
                    bytesFloat32[3] = (byte)(registers[1] & 0xFF);
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(bytesFloat32);
                    }
                    return BitConverter.ToSingle(bytesFloat32, 0);

                default:
                    logger?.Warning(
                        "Niet-ondersteund ModbusDataType ({DataType}) voor interpretatie. Probeert ruwe ushort[0] terug te geven.",
                        dataType
                    );
                    // Fallback voor een onbekend (maar wellicht 1-register) type.
                    if (registers.Length < 1)
                    {
                        logger?.Error(
                            "InterpretRegisterData: Onvoldoende registers voor default fallback (onbekend type). Verwacht 1, kreeg {Length}.",
                            registers.Length
                        );
                        throw new ArgumentException(
                            $@"Onvoldoende registers voor default fallback (onbekend type). Verwacht 1, kreeg {registers.Length}.",
                            nameof(registers)
                        );
                    }
                    return registers[0];
            }
        }
    }
}
