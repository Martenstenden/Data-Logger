using System;
using Data_Logger.Enums;
using Serilog;

namespace Data_Logger;

public class ModbusDataConverter
{
    public static object InterpretRegisterData(ushort[] registers, ModbusDataType dataType, ILogger logger = null)
    {
        if (registers == null)
        {
            logger?.Error("InterpretRegisterData: Input 'registers' array is null.");
            throw new ArgumentNullException(nameof(registers));
        }
        
        switch (dataType)
        {
            case ModbusDataType.Boolean:
            case ModbusDataType.Int16:
            case ModbusDataType.UInt16:
                if (registers.Length < 1)
                {
                    logger?.Error("InterpretRegisterData: Onvoldoende registers voor {DataType}. Verwacht 1, kreeg {Length}.", dataType, registers.Length);
                    throw new ArgumentException($"Onvoldoende registers voor {dataType}. Verwacht 1, kreeg {registers.Length}.", nameof(registers));
                }
                break; // Validatie is ok, ga door naar de eigenlijke conversie

            case ModbusDataType.Int32:
            case ModbusDataType.UInt32:
            case ModbusDataType.Float32:
                if (registers.Length < 2)
                {
                    logger?.Error("InterpretRegisterData: Onvoldoende registers voor {DataType}. Verwacht 2, kreeg {Length}.", dataType, registers.Length);
                    throw new ArgumentException($"Onvoldoende registers voor {dataType}. Verwacht 2, kreeg {registers.Length}.", nameof(registers));
                }
                break; // Validatie is ok
        }
        
        switch (dataType)
        {
            case ModbusDataType.Boolean:
                return registers[0] != 0;
            case ModbusDataType.Int16:
                return (short)registers[0];
            case ModbusDataType.UInt16:
                return registers[0];

            case ModbusDataType.Int32:
                byte[] bytesInt32 = new byte[4];
                bytesInt32[0] = (byte)(registers[0] >> 8);
                bytesInt32[1] = (byte)(registers[0] & 0xFF);
                bytesInt32[2] = (byte)(registers[1] >> 8);
                bytesInt32[3] = (byte)(registers[1] & 0xFF);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytesInt32);
                return BitConverter.ToInt32(bytesInt32, 0);

            case ModbusDataType.UInt32:
                byte[] bytesUInt32 = new byte[4];
                bytesUInt32[0] = (byte)(registers[0] >> 8);
                bytesUInt32[1] = (byte)registers[0];
                bytesUInt32[2] = (byte)(registers[1] >> 8);
                bytesUInt32[3] = (byte)registers[1];
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytesUInt32);
                return BitConverter.ToUInt32(bytesUInt32, 0);

            case ModbusDataType.Float32:
                byte[] bytesFloat32 = new byte[4];
                bytesFloat32[0] = (byte)(registers[0] >> 8);
                bytesFloat32[1] = (byte)(registers[0] & 0xFF);
                bytesFloat32[2] = (byte)(registers[1] >> 8);
                bytesFloat32[3] = (byte)(registers[1] & 0xFF);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytesFloat32);
                return BitConverter.ToSingle(bytesFloat32, 0);

            default:
                logger?.Warning("Niet-ondersteund ModbusDataType voor interpretatie: {DataType}. Probeert ruwe ushort[0] terug te geven.", dataType);
                if (registers.Length < 1) // Nog een check voor de default case, hoewel al bovenaan gedekt
                {
                    logger?.Error("InterpretRegisterData: Onvoldoende registers voor default fallback. Verwacht 1, kreeg {Length}.", registers.Length);
                    throw new ArgumentException($"Onvoldoende registers voor default fallback. Verwacht 1, kreeg {registers.Length}.", nameof(registers));
                }
                return registers[0]; 
        }
    }
}
