using System.Globalization;
using System.Threading;
using Data_Logger.Enums;
using Data_Logger.Models;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Serilog;

namespace DataLogger.Tests
{
    [TestFixture]
    [Category("Unit")]
    public class OpcUaTagConfigTests
    {
        private Mock<ILogger> _mockLogger;
        private OpcUaTagConfig _tagConfig;

        private CultureInfo _originalCulture;
        private CultureInfo _originalUiCulture;

        [SetUp]
        public void Setup()
        {
            _mockLogger = new Mock<ILogger>();
            _mockLogger.Setup(l => l.ForContext<OpcUaTagConfig>()).Returns(_mockLogger.Object);

            _tagConfig = new OpcUaTagConfig();

            _originalCulture = Thread.CurrentThread.CurrentCulture;
            _originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
        }

        [TearDown]
        public void TearDown()
        {
            Thread.CurrentThread.CurrentCulture = _originalCulture;
            Thread.CurrentThread.CurrentUICulture = _originalUiCulture;
        }

        [Test]
        public void Constructor_DefaultValues_AreSetCorrectly()
        {
            ClassicAssert.AreEqual("Nieuwe OPC UA Tag", _tagConfig.TagName);
            ClassicAssert.AreEqual("ns=2;s=MyVariable", _tagConfig.NodeId);
            ClassicAssert.AreEqual(OpcUaDataType.Variant, _tagConfig.DataType);
            ClassicAssert.AreEqual(1000, _tagConfig.SamplingInterval);
            ClassicAssert.IsTrue(_tagConfig.IsActive);
            ClassicAssert.IsFalse(_tagConfig.IsAlarmingEnabled);
            ClassicAssert.IsFalse(_tagConfig.IsOutlierDetectionEnabled);
            ClassicAssert.AreEqual(20, _tagConfig.BaselineSampleSize);
            ClassicAssert.AreEqual(3.0, _tagConfig.OutlierStandardDeviationFactor);
            ClassicAssert.AreEqual(
                "{TagName} is in alarm ({AlarmState}) met waarde {Value}",
                _tagConfig.AlarmMessageFormat
            );
            ClassicAssert.AreEqual(TagAlarmState.Normal, _tagConfig.CurrentAlarmState);
            ClassicAssert.IsNotNull(_tagConfig.BaselineDataPoints);
            ClassicAssert.AreEqual(0, _tagConfig.BaselineDataPoints.Count);
            ClassicAssert.IsFalse(_tagConfig.IsBaselineEstablished);
            ClassicAssert.AreEqual(0, _tagConfig.BaselineMean);
            ClassicAssert.AreEqual(0, _tagConfig.BaselineStandardDeviation);
            ClassicAssert.AreEqual(0, _tagConfig.CurrentBaselineCount);
            ClassicAssert.AreEqual(0, _tagConfig.SumOfValuesForBaseline);
            ClassicAssert.AreEqual(0, _tagConfig.SumOfSquaresForBaseline);
        }

        [Test]
        public void ResetBaselineState_ResetsAllBaselineProperties()
        {
            _tagConfig.IsBaselineEstablished = true;
            _tagConfig.BaselineMean = 10.0;
            _tagConfig.BaselineStandardDeviation = 1.0;
            _tagConfig.CurrentBaselineCount = 100;
            _tagConfig.SumOfValuesForBaseline = 1000;
            _tagConfig.SumOfSquaresForBaseline = 10100;
            _tagConfig.BaselineDataPoints.Add(1.0);

            _tagConfig.ResetBaselineState();

            ClassicAssert.IsFalse(
                _tagConfig.IsBaselineEstablished,
                "IsBaselineEstablished should be false after reset."
            );
            ClassicAssert.AreEqual(
                0,
                _tagConfig.BaselineMean,
                "BaselineMean should be 0 after reset."
            );
            ClassicAssert.AreEqual(
                0,
                _tagConfig.BaselineStandardDeviation,
                "BaselineStandardDeviation should be 0 after reset."
            );
            ClassicAssert.AreEqual(
                0,
                _tagConfig.CurrentBaselineCount,
                "CurrentBaselineCount should be 0 after reset."
            );
            ClassicAssert.AreEqual(
                0,
                _tagConfig.SumOfValuesForBaseline,
                "SumOfValuesForBaseline should be 0 after reset."
            );
            ClassicAssert.AreEqual(
                0,
                _tagConfig.SumOfSquaresForBaseline,
                "SumOfSquaresForBaseline should be 0 after reset."
            );
            ClassicAssert.AreEqual(
                0,
                _tagConfig.BaselineDataPoints.Count,
                "BaselineDataPoints should be empty after reset."
            );
        }

        [Test]
        public void IsOutlierDetectionEnabled_SetToTrue_WhenFalse_CallsResetBaselineState()
        {
            _tagConfig.IsOutlierDetectionEnabled = false;

            _tagConfig.IsBaselineEstablished = true;
            _tagConfig.CurrentBaselineCount = 5;

            _tagConfig.IsOutlierDetectionEnabled = true;

            ClassicAssert.IsFalse(
                _tagConfig.IsBaselineEstablished,
                "Baseline should be reset when IsOutlierDetectionEnabled changes."
            );
            ClassicAssert.AreEqual(
                0,
                _tagConfig.CurrentBaselineCount,
                "Baseline count should be reset."
            );
        }

        [Test]
        public void IsOutlierDetectionEnabled_SetToFalse_WhenTrue_CallsResetBaselineState()
        {
            _tagConfig.IsOutlierDetectionEnabled = true;
            _tagConfig.IsBaselineEstablished = true;
            _tagConfig.CurrentBaselineCount = 5;

            _tagConfig.IsOutlierDetectionEnabled = false;

            ClassicAssert.IsFalse(
                _tagConfig.IsBaselineEstablished,
                "Baseline should be reset when IsOutlierDetectionEnabled changes."
            );
            ClassicAssert.AreEqual(
                0,
                _tagConfig.CurrentBaselineCount,
                "Baseline count should be reset."
            );
        }

        [Test]
        public void IsOutlierDetectionEnabled_SetToSameValue_DoesNotCallResetBaselineStateMultipleTimes()
        {
            _tagConfig.IsOutlierDetectionEnabled = true;
            _tagConfig.CurrentBaselineCount = 5;

            _tagConfig.IsOutlierDetectionEnabled = true;

            ClassicAssert.AreEqual(
                5,
                _tagConfig.CurrentBaselineCount,
                "CurrentBaselineCount should not be reset if IsOutlierDetectionEnabled is set to the same value."
            );
        }

        [Test]
        public void TagName_PropertyChanged_EventIsRaised()
        {
            var eventRaised = false;
            string newName = "Updated OPC Tag";
            _tagConfig.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(OpcUaTagConfig.TagName))
                {
                    eventRaised = true;
                }
            };

            _tagConfig.TagName = newName;

            ClassicAssert.IsTrue(
                eventRaised,
                "PropertyChanged event should have been raised for TagName."
            );
            ClassicAssert.AreEqual(newName, _tagConfig.TagName);
        }

        [Test]
        public void FormattedLiveValue_WhenGoodQuality_ReturnsValueString()
        {
            _tagConfig.CurrentValue = 123.45;
            _tagConfig.IsGoodQuality = true;
            _tagConfig.ErrorMessage = null;

            string formatted = _tagConfig.FormattedLiveValue;

            ClassicAssert.AreEqual("123.45", formatted);
        }

        [Test]
        public void FormattedLiveValue_WhenGoodQualityAndNullValue_ReturnsNA()
        {
            _tagConfig.CurrentValue = null;
            _tagConfig.IsGoodQuality = true;
            _tagConfig.ErrorMessage = null;

            string formatted = _tagConfig.FormattedLiveValue;

            ClassicAssert.AreEqual("N/A", formatted);
        }

        [Test]
        public void FormattedLiveValue_WhenBadQualityWithError_ReturnsErrorMessage()
        {
            _tagConfig.CurrentValue = 123.45;
            _tagConfig.IsGoodQuality = false;
            _tagConfig.ErrorMessage = "Sensor Defect";

            string formatted = _tagConfig.FormattedLiveValue;

            ClassicAssert.AreEqual("Sensor Defect", formatted);
        }

        [Test]
        public void FormattedLiveValue_WhenBadQualityWithoutError_ReturnsBadQuality()
        {
            _tagConfig.CurrentValue = 123.45;
            _tagConfig.IsGoodQuality = false;
            _tagConfig.ErrorMessage = null;

            string formatted = _tagConfig.FormattedLiveValue;

            ClassicAssert.AreEqual("Bad Quality", formatted);
        }
    }
}
