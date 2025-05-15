using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

public class TagBaselineState
{
    public List<double> BaselineDataPoints { get; } = new List<double>();
    public bool IsBaselineEstablished { get; set; } = false;
    public double BaselineMean { get; set; } = 0;
    public double BaselineStandardDeviation { get; set; } = 0;
    private readonly ILogger _logger;
    private readonly string _tagName;

    public TagBaselineState(string tagName, ILogger logger = null)
    {
        _tagName = tagName;
        _logger = logger?.ForContext<TagBaselineState>().ForContext("MonitoredTag", tagName);
    }

    public void AddDataPoint(double value, int requiredSampleSize, out bool baselineJustEstablished)
    {
        baselineJustEstablished = false;
        if (IsBaselineEstablished)
            return;

        BaselineDataPoints.Add(value);
        _logger?.Debug(
            "Baseline voor {TagName}: datapunt {Count}/{Target} toegevoegd: {Value}",
            _tagName,
            BaselineDataPoints.Count,
            requiredSampleSize,
            value
        );

        if (BaselineDataPoints.Count >= requiredSampleSize)
        {
            if (requiredSampleSize > 0)
            {
                BaselineMean = BaselineDataPoints.Average();
                if (requiredSampleSize > 1)
                {
                    double sumOfSquaresOfDifferences = BaselineDataPoints
                        .Select(val => (val - BaselineMean) * (val - BaselineMean))
                        .Sum();
                    BaselineStandardDeviation = Math.Sqrt(
                        sumOfSquaresOfDifferences / (requiredSampleSize - 1)
                    );
                }
                else
                {
                    BaselineStandardDeviation = 0;
                }
            }
            IsBaselineEstablished = true;
            baselineJustEstablished = true;

            _logger?.Information(
                "Baseline vastgesteld voor tag {TagName}: Mean={Mean:F2}, StdDev={StdDev:F2} (samples={Samples})",
                _tagName,
                BaselineMean,
                BaselineStandardDeviation,
                requiredSampleSize
            );
        }
    }

    public void Reset()
    {
        BaselineDataPoints.Clear();
        IsBaselineEstablished = false;
        BaselineMean = 0;
        BaselineStandardDeviation = 0;
        _logger?.Debug("Baseline state gereset voor tag {TagName}", _tagName);
    }
}
