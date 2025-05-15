using Data_Logger;
using Data_Logger.Enums;
using Data_Logger.Models;
using Data_Logger.Services.Implementations;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Serilog;

[TestFixture]
public class ModbusServiceTests
{
    private Mock<ILogger> _mockLogger;
    private ModbusTcpConnectionConfig _dummyConfig;

    [SetUp]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger>();

        _mockLogger
            .Setup(log => log.ForContext(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<bool>()))
            .Returns(_mockLogger.Object);

        _dummyConfig = new ModbusTcpConnectionConfig { ConnectionName = "TestModbus" };
    }

    [Test]
    public void ModbusTagConfig_IsDataTypeSelectionEnabled_CoilOrDiscreteInput_ReturnsFalse()
    {
        var tagConfig = new ModbusTagConfig();

        tagConfig.RegisterType = ModbusRegisterType.Coil;
        ClassicAssert.IsFalse(
            tagConfig.IsDataTypeSelectionEnabled,
            "DataType selectie zou disabled moeten zijn voor Coil."
        );
        ClassicAssert.AreEqual(
            ModbusDataType.Boolean,
            tagConfig.DataType,
            "DataType zou Boolean moeten zijn voor Coil."
        );

        tagConfig.RegisterType = ModbusRegisterType.DiscreteInput;
        ClassicAssert.IsFalse(
            tagConfig.IsDataTypeSelectionEnabled,
            "DataType selectie zou disabled moeten zijn voor DiscreteInput."
        );
        ClassicAssert.AreEqual(
            ModbusDataType.Boolean,
            tagConfig.DataType,
            "DataType zou Boolean moeten zijn voor DiscreteInput."
        );
    }

    [Test]
    public void ModbusTagConfig_IsDataTypeSelectionEnabled_HoldingOrInputRegister_ReturnsTrue()
    {
        var tagConfig = new ModbusTagConfig();

        tagConfig.RegisterType = ModbusRegisterType.HoldingRegister;
        ClassicAssert.IsTrue(
            tagConfig.IsDataTypeSelectionEnabled,
            "DataType selectie zou enabled moeten zijn voor HoldingRegister."
        );

        tagConfig.RegisterType = ModbusRegisterType.InputRegister;
        ClassicAssert.IsTrue(
            tagConfig.IsDataTypeSelectionEnabled,
            "DataType selectie zou enabled moeten zijn voor InputRegister."
        );
    }

    [Test]
    public void ModbusTagConfig_SetDataType_WhenCoil_RemainsBoolean()
    {
        var tagConfig = new ModbusTagConfig { RegisterType = ModbusRegisterType.Coil };

        tagConfig.DataType = ModbusDataType.Int16;

        ClassicAssert.AreEqual(
            ModbusDataType.Boolean,
            tagConfig.DataType,
            "DataType zou Boolean moeten blijven voor Coil, ook na poging tot wijzigen."
        );
    }
}
