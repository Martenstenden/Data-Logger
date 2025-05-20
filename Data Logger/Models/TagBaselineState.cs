using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace Data_Logger.Models
{
    /// <summary>
    /// Beheert de staat en berekeningen voor de baseline van een tag,
    /// gebruikt voor outlier (uitschieter) detectie.
    /// </summary>
    public class TagBaselineState
    {
        /// <summary>
        /// Haalt de lijst van datapunten op die gebruikt zijn om de baseline te vormen.
        /// </summary>
        public List<double> BaselineDataPoints { get; } = new List<double>();

        /// <summary>
        /// Haalt een waarde die aangeeft of de baseline is vastgesteld (d.w.z. voldoende samples zijn verzameld) op of stelt deze in.
        /// </summary>
        public bool IsBaselineEstablished { get; set; } = false;

        /// <summary>
        /// Haalt het berekende gemiddelde van de baseline datapunten op of stelt deze in.
        /// </summary>
        public double BaselineMean { get; set; } = 0;

        /// <summary>
        /// Haalt de berekende standaarddeviatie van de baseline datapunten op of stelt deze in.
        /// </summary>
        public double BaselineStandardDeviation { get; set; } = 0;

        private readonly ILogger _logger;
        private readonly string _tagName;

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="TagBaselineState"/> klasse.
        /// </summary>
        /// <param name="tagName">De naam van de tag waarvoor deze baseline staat wordt bijgehouden.</param>
        /// <param name="logger">Optionele Serilog logger voor diagnostische output.</param>
        public TagBaselineState(string tagName, ILogger logger = null)
        {
            _tagName = tagName;
            _logger = logger?.ForContext<TagBaselineState>().ForContext("MonitoredTag", tagName);
        }

        /// <summary>
        /// Voegt een nieuw datapunt toe aan de baselineberekening.
        /// Als het vereiste aantal samples is bereikt, worden het gemiddelde en de standaarddeviatie berekend
        /// en wordt <see cref="IsBaselineEstablished"/> op true gezet.
        /// </summary>
        /// <param name="value">Het toe te voegen datapunt.</param>
        /// <param name="requiredSampleSize">Het aantal samples dat nodig is om de baseline vast te stellen.</param>
        /// <param name="baselineJustEstablished">Output parameter; true als de baseline zojuist is vastgesteld met dit datapunt, anders false.</param>
        public void AddDataPoint(
            double value,
            int requiredSampleSize,
            out bool baselineJustEstablished
        )
        {
            baselineJustEstablished = false;
            if (IsBaselineEstablished)
            {
                return;
            }

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
                if (requiredSampleSize > 0) // Voorkom delen door nul of negatief.
                {
                    BaselineMean = BaselineDataPoints.Average();
                    if (requiredSampleSize > 1) // Standaarddeviatie is alleen zinvol bij meer dan 1 punt.
                    {
                        double sumOfSquaresOfDifferences = BaselineDataPoints
                            .Select(val => (val - BaselineMean) * (val - BaselineMean))
                            .Sum();
                        // Gebruik (N-1) voor de sample standaard deviatie.
                        BaselineStandardDeviation = Math.Sqrt(
                            sumOfSquaresOfDifferences / (requiredSampleSize - 1)
                        );
                    }
                    else
                    {
                        BaselineStandardDeviation = 0; // Standaarddeviatie van 1 punt is 0.
                    }
                }
                IsBaselineEstablished = true;
                baselineJustEstablished = true; // Geef aan dat de baseline zojuist is vastgesteld.

                _logger?.Information(
                    "Baseline vastgesteld voor tag {TagName}: Mean={Mean:F2}, StdDev={StdDev:F2} (samples={Samples})",
                    _tagName,
                    BaselineMean,
                    BaselineStandardDeviation,
                    requiredSampleSize
                );
            }
        }

        /// <summary>
        /// Reset de baseline staat, wist alle verzamelde datapunten en stelt gemiddelde/standaarddeviatie op nul.
        /// </summary>
        public void Reset()
        {
            BaselineDataPoints.Clear();
            IsBaselineEstablished = false;
            BaselineMean = 0;
            BaselineStandardDeviation = 0;
            _logger?.Debug("Baseline state gereset voor tag {TagName}", _tagName);
        }
    }
}
