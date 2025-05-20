using System;
using Data_Logger.Converters;
using Data_Logger.Enums;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Serilog;

namespace DataLogger.Tests
{
    [TestFixture]
    [Category("Unit")]
    public class ModbusDataConverterTests
    {
        private Mock<ILogger> _mockLogger;

        [SetUp]
        public void Setup()
        {
            _mockLogger = new Mock<ILogger>();

            _mockLogger.Setup(log => log.Warning(It.IsAny<string>(), It.IsAny<object[]>()));
            _mockLogger.Setup(log => log.Error(It.IsAny<string>(), It.IsAny<object[]>()));
        }

        [TestCase(new ushort[] { 0 }, ModbusDataType.UInt16, (ushort)0)]
        [TestCase(new ushort[] { 12345 }, ModbusDataType.UInt16, (ushort)12345)]
        [TestCase(new ushort[] { 65535 }, ModbusDataType.UInt16, (ushort)65535)]
        public void InterpretRegisterData_UInt16_ReturnsCorrectValue(
            ushort[] registers,
            ModbusDataType dataType,
            ushort expectedValue
        )
        {
            object result = ModbusDataConverter.InterpretRegisterData(registers, dataType);

            ClassicAssert.IsInstanceOf<ushort>(result, "Resultaat zou een ushort moeten zijn.");
            ClassicAssert.AreEqual(expectedValue, (ushort)result);
        }

        [TestCase(new ushort[] { 0 }, ModbusDataType.Int16, (short)0)]
        [TestCase(new ushort[] { 32767 }, ModbusDataType.Int16, (short)32767)]
        [TestCase(new ushort[] { 0x8000 }, ModbusDataType.Int16, short.MinValue)]
        [TestCase(new ushort[] { 0xFFFF }, ModbusDataType.Int16, (short)-1)]
        public void InterpretRegisterData_Int16_ReturnsCorrectValue(
            ushort[] registers,
            ModbusDataType dataType,
            short expectedValue
        )
        {
            object result = ModbusDataConverter.InterpretRegisterData(registers, dataType);

            ClassicAssert.IsInstanceOf<short>(result, "Resultaat zou een short moeten zijn.");
            ClassicAssert.AreEqual(expectedValue, (short)result);
        }

        [TestCase(new ushort[] { 0 }, ModbusDataType.Boolean, false)]
        [TestCase(new ushort[] { 1 }, ModbusDataType.Boolean, true)]
        [TestCase(new ushort[] { 12345 }, ModbusDataType.Boolean, true)]
        public void InterpretRegisterData_BooleanFromRegister_ReturnsCorrectValue(
            ushort[] registers,
            ModbusDataType dataType,
            bool expectedValue
        )
        {
            object result = ModbusDataConverter.InterpretRegisterData(registers, dataType);

            ClassicAssert.IsInstanceOf<bool>(result, "Resultaat zou een bool moeten zijn.");
            ClassicAssert.AreEqual(expectedValue, (bool)result);
        }

        [TestCase(new ushort[] { 0x0000, 0x0000 }, ModbusDataType.Int32, 0)]
        [TestCase(new ushort[] { 0x0000, 0x0001 }, ModbusDataType.Int32, 1)]
        [TestCase(new ushort[] { 0xFFFF, 0xFFFF }, ModbusDataType.Int32, -1)]
        [TestCase(new ushort[] { 0x1234, 0x5678 }, ModbusDataType.Int32, 0x12345678)]
        [TestCase(new ushort[] { 0x8000, 0x0000 }, ModbusDataType.Int32, int.MinValue)]
        [TestCase(new ushort[] { 0x7FFF, 0xFFFF }, ModbusDataType.Int32, int.MaxValue)]
        public void InterpretRegisterData_Int32_ReturnsCorrectValue(
            ushort[] registers,
            ModbusDataType dataType,
            int expectedValue
        )
        {
            object result = ModbusDataConverter.InterpretRegisterData(registers, dataType);

            ClassicAssert.IsInstanceOf<int>(result, "Resultaat zou een int moeten zijn.");
            ClassicAssert.AreEqual(expectedValue, (int)result);
        }

        [TestCase(new ushort[] { 0x0000, 0x0000 }, ModbusDataType.UInt32, (uint)0)]
        [TestCase(new ushort[] { 0x0000, 0x0001 }, ModbusDataType.UInt32, (uint)1)]
        [TestCase(new ushort[] { 0xFFFF, 0xFFFF }, ModbusDataType.UInt32, uint.MaxValue)]
        [TestCase(new ushort[] { 0x1234, 0x5678 }, ModbusDataType.UInt32, (uint)0x12345678)]
        public void InterpretRegisterData_UInt32_ReturnsCorrectValue(
            ushort[] registers,
            ModbusDataType dataType,
            uint expectedValue
        )
        {
            object result = ModbusDataConverter.InterpretRegisterData(registers, dataType);

            ClassicAssert.IsInstanceOf<uint>(result, "Resultaat zou een uint moeten zijn.");
            ClassicAssert.AreEqual(expectedValue, (uint)result);
        }

        [TestCase(new ushort[] { 0x0000, 0x0000 }, ModbusDataType.Float32, 0.0f)]
        [TestCase(new ushort[] { 0x3F80, 0x0000 }, ModbusDataType.Float32, 1.0f)]
        [TestCase(new ushort[] { 0xBF80, 0x0000 }, ModbusDataType.Float32, -1.0f)]
        [TestCase(new ushort[] { 0x42F6, 0xE979 }, ModbusDataType.Float32, 123.456f)]
        public void InterpretRegisterData_Float32_ReturnsCorrectValue(
            ushort[] registers,
            ModbusDataType dataType,
            float expectedValue
        )
        {
            object result = ModbusDataConverter.InterpretRegisterData(registers, dataType);

            ClassicAssert.IsInstanceOf<float>(result, "Resultaat zou een float moeten zijn.");

            ClassicAssert.AreEqual(expectedValue, (float)result, 0.0001f);
        }

        [Test]
        public void InterpretRegisterData_Int32_ThrowsArgumentException_WhenInsufficientRegisters()
        {
            var registers = new ushort[] { 0x1234 };

            Assert.Throws<ArgumentException>(() =>
                ModbusDataConverter.InterpretRegisterData(registers, ModbusDataType.Int32)
            );
        }

        [Test]
        public void InterpretRegisterData_Float32_ThrowsArgumentException_WhenInsufficientRegisters()
        {
            var registers = new ushort[] { 0x1234 };

            Assert.Throws<ArgumentException>(() =>
                ModbusDataConverter.InterpretRegisterData(registers, ModbusDataType.Float32)
            );
        }

        [Test]
        public void InterpretRegisterData_NullRegisters_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ModbusDataConverter.InterpretRegisterData(
                    null,
                    ModbusDataType.UInt16,
                    _mockLogger.Object
                )
            );
        }

        [Test]
        public void InterpretRegisterData_EmptyRegisters_ForUInt16_ThrowsArgumentException()
        {
            var registers = new ushort[] { };

            Assert.Throws<ArgumentException>(() =>
                ModbusDataConverter.InterpretRegisterData(
                    registers,
                    ModbusDataType.UInt16,
                    _mockLogger.Object
                )
            );
        }

        [TestCase(ModbusDataType.Int32)]
        [TestCase(ModbusDataType.UInt32)]
        [TestCase(ModbusDataType.Float32)]
        public void InterpretRegisterData_SingleRegister_For32BitTypes_ThrowsArgumentException(
            ModbusDataType dataType
        )
        {
            var registers = new ushort[] { 0x1234 };

            Assert.Throws<ArgumentException>(() =>
                ModbusDataConverter.InterpretRegisterData(registers, dataType, _mockLogger.Object)
            );
        }

        [Test]
        public void InterpretRegisterData_UnsupportedDataType_ReturnsFirstRegisterValueAndLogsWarning()
        {
            var registers = new ushort[] { 9876, 5432 };
            var unsupportedDataType = (ModbusDataType)99;

            object result = ModbusDataConverter.InterpretRegisterData(
                registers,
                unsupportedDataType,
                _mockLogger.Object
            );

            ClassicAssert.IsInstanceOf<ushort>(
                result,
                "Resultaat zou een ushort moeten zijn (fallback)."
            );
            ClassicAssert.AreEqual(
                registers[0],
                (ushort)result,
                "Zou de waarde van het eerste register moeten retourneren."
            );

            _mockLogger.Verify(
                log =>
                    log.Warning(
                        "Niet-ondersteund ModbusDataType voor interpretatie: {DataType}. Probeert ruwe ushort[0] terug te geven.",
                        It.Is<ModbusDataType>(dt => dt == unsupportedDataType)
                    ),
                Times.Once
            );
        }

        [Test]
        public void InterpretRegisterData_UnsupportedDataType_EmptyRegisters_ThrowsArgumentException()
        {
            var registers = new ushort[] { };
            var unsupportedDataType = (ModbusDataType)99;

            Assert.Throws<ArgumentException>(() =>
                ModbusDataConverter.InterpretRegisterData(
                    registers,
                    unsupportedDataType,
                    _mockLogger.Object
                )
            );
        }
    }
}
