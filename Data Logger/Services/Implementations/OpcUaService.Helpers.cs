using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Data_Logger.Enums;
using Data_Logger.Models;
using Newtonsoft.Json;
using Opc.Ua;

namespace Data_Logger.Services.Implementations
{
    /// <summary>
    /// Partial class voor OpcUaService die diverse private helper-methoden bevat.
    /// </summary>
    public sealed partial class OpcUaService
    {
        /// <summary>
        /// Creëert een <see cref="IUserIdentity"/> object op basis van de gebruikersnaam en wachtwoord
        /// geconfigureerd in <see cref="_config"/>.
        /// Retourneert een anonieme identity als er geen gebruikersnaam is opgegeven.
        /// </summary>
        /// <returns>Een <see cref="IUserIdentity"/> object.</returns>
        private IUserIdentity GetUserIdentity()
        {
            if (!string.IsNullOrEmpty(_config.UserName))
            {
                _specificLogger.Debug(
                    "Gebruikersidentiteit wordt aangemaakt voor gebruiker: {UserName}",
                    _config.UserName
                );
                return new UserIdentity(_config.UserName, _config.Password ?? string.Empty);
            }
            _specificLogger.Debug("Anonieme gebruikersidentiteit wordt gebruikt.");
            return new UserIdentity(); // Anoniem
        }

        /// <summary>
        /// Vergelijkt twee endpoint URL's, waarbij rekening wordt gehouden met mogelijke variaties
        /// zoals verschillen in hoofdlettergebruik in de hostnaam of een afsluitende slash.
        /// </summary>
        /// <param name="urlFromServerList">De URL zoals ontvangen van de serverlijst.</param>
        /// <param name="selectedUrlFromDiscovery">De URL zoals geselecteerd na discovery (of uit configuratie).</param>
        /// <returns>True als de URLs als equivalent worden beschouwd; anders false.</returns>
        private bool IsEndpointUrlMatch(string urlFromServerList, string selectedUrlFromDiscovery)
        {
            if (
                string.Equals(
                    urlFromServerList,
                    selectedUrlFromDiscovery,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }
            try
            {
                var uriFromServer = new Uri(urlFromServerList);
                var uriSelected = new Uri(selectedUrlFromDiscovery);

                // Vergelijk scheme, host (case-insensitive), poort, en pad
                return uriFromServer.Scheme == uriSelected.Scheme
                    && string.Equals(
                        uriFromServer.DnsSafeHost,
                        uriSelected.DnsSafeHost,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && uriFromServer.Port == uriSelected.Port
                    && uriFromServer.AbsolutePath.TrimEnd('/')
                        == uriSelected.AbsolutePath.TrimEnd('/');
            }
            catch (UriFormatException ex)
            {
                _specificLogger.Warning(
                    ex,
                    "Fout bij het parsen van endpoint URL's voor vergelijking: '{Url1}' vs '{Url2}'",
                    urlFromServerList,
                    selectedUrlFromDiscovery
                );
                return false;
            }
        }

        /// <summary>
        /// Creëert een diepe kopie van het opgegeven <see cref="OpcUaConnectionConfig"/> object.
        /// Dit wordt gedaan via JSON serialisatie en deserialisatie om ervoor te zorgen dat alle geneste
        /// objecten en collecties ook gekloond worden.
        /// </summary>
        /// <param name="original">Het originele <see cref="OpcUaConnectionConfig"/> object om te klonen.</param>
        /// <returns>Een diepe kopie van het originele object, of null als het origineel null was.</returns>
        /// <exception cref="JsonException">Als er een fout optreedt tijdens serialisatie of deserialisatie.</exception>
        private OpcUaConnectionConfig CreateDeepCopy(OpcUaConnectionConfig original)
        {
            if (original == null)
            {
                return null;
            }
            try
            {
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Objects,
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                };
                string serialized = JsonConvert.SerializeObject(original, settings);
                return JsonConvert.DeserializeObject<OpcUaConnectionConfig>(serialized, settings);
            }
            catch (Exception ex)
            {
                _specificLogger.Error(
                    ex,
                    "Kon geen diepe kopie maken van OpcUaConnectionConfig. Naam: {ConnectionName}",
                    original.ConnectionName
                );
                throw;
            }
        }

        /// <summary>
        /// Controleert of er significante wijzigingen zijn in de monitoringparameters tussen twee
        /// collecties van <see cref="OpcUaTagConfig"/>.
        /// Kijkt naar NodeId en SamplingInterval van actieve tags.
        /// </summary>
        /// <param name="oldTags">De oude collectie van tag-configuraties.</param>
        /// <param name="newTags">De nieuwe collectie van tag-configuraties.</param>
        /// <returns>True als er significante wijzigingen zijn die een herstart van de monitoring vereisen; anders false.</returns>
        private bool HaveMonitoringParametersChanged(
            ObservableCollection<OpcUaTagConfig> oldTags,
            ObservableCollection<OpcUaTagConfig> newTags
        )
        {
            if (oldTags == null && newTags == null)
                return false;
            if (oldTags == null || newTags == null)
                return true;

            // Selecteer alleen actieve tags en de eigenschappen die de subscription beïnvloeden
            var oldActiveMonitoringParams = oldTags
                .Where(t => t.IsActive)
                .Select(t => new { t.NodeId, t.SamplingInterval })
                .OrderBy(t => t.NodeId)
                .ToList();

            var newActiveMonitoringParams = newTags
                .Where(t => t.IsActive)
                .Select(t => new { t.NodeId, t.SamplingInterval })
                .OrderBy(t => t.NodeId)
                .ToList();

            // Vergelijk de lijsten van anonieme objecten
            return !oldActiveMonitoringParams.SequenceEqual(newActiveMonitoringParams);
        }

        #region Alarm and Outlier Logic
        /// <summary>
        /// Bepaalt de alarmstatus van een tag op basis van geconfigureerde drempelwaarden.
        /// </summary>
        /// <param name="tagConfig">De configuratie van de tag, inclusief alarmgrenzen.</param>
        /// <param name="numericValue">De huidige numerieke waarde van de tag.</param>
        /// <param name="limitDetails">Output parameter; de specifieke limiet die is overschreden, indien van toepassing.</param>
        /// <returns>De berekende <see cref="TagAlarmState"/> op basis van drempels.</returns>
        private TagAlarmState DetermineThresholdAlarmState(
            OpcUaTagConfig tagConfig,
            double numericValue,
            out double? limitDetails
        )
        {
            limitDetails = null;
            if (!tagConfig.IsAlarmingEnabled)
            {
                return TagAlarmState.Normal;
            }

            // Controleer van meest kritiek (HighHigh, LowLow) naar minder kritiek.
            if (tagConfig.HighHighLimit.HasValue && numericValue >= tagConfig.HighHighLimit.Value)
            {
                limitDetails = tagConfig.HighHighLimit.Value;
                return TagAlarmState.HighHigh;
            }
            if (tagConfig.LowLowLimit.HasValue && numericValue <= tagConfig.LowLowLimit.Value)
            {
                limitDetails = tagConfig.LowLowLimit.Value;
                return TagAlarmState.LowLow;
            }
            if (tagConfig.HighLimit.HasValue && numericValue >= tagConfig.HighLimit.Value)
            {
                limitDetails = tagConfig.HighLimit.Value;
                return TagAlarmState.High;
            }
            if (tagConfig.LowLimit.HasValue && numericValue <= tagConfig.LowLimit.Value)
            {
                limitDetails = tagConfig.LowLimit.Value;
                return TagAlarmState.Low;
            }

            return TagAlarmState.Normal;
        }

        /// <summary>
        /// Bepaalt of de huidige waarde van een tag een statistische uitschieter (outlier) is,
        /// gebaseerd op een expanding window baseline berekening (Welford's algoritme stijl).
        /// Werkt de baseline statistieken (<see cref="OpcUaTagConfig.BaselineMean"/>, <see cref="OpcUaTagConfig.BaselineStandardDeviation"/>, etc.) bij.
        /// </summary>
        /// <param name="tagConfig">De configuratie van de tag, inclusief baseline parameters en staat.</param>
        /// <param name="numericValue">De huidige numerieke waarde van de tag.</param>
        /// <returns>True als de waarde als een outlier wordt beschouwd; anders false.</returns>
        private bool IsCurrentValueOutlier(OpcUaTagConfig tagConfig, double numericValue)
        {
            if (!tagConfig.IsOutlierDetectionEnabled)
            {
                return false;
            }

            // Welford's algorithm for online variance/std deviation
            tagConfig.CurrentBaselineCount++;
            double delta = numericValue - tagConfig.BaselineMean;
            tagConfig.BaselineMean += delta / tagConfig.CurrentBaselineCount;
            double delta2 = numericValue - tagConfig.BaselineMean;
            tagConfig.SumOfSquaresForBaseline += delta * delta2;

            bool baselineJustEstablished = false;
            if (
                !tagConfig.IsBaselineEstablished
                && tagConfig.CurrentBaselineCount >= tagConfig.BaselineSampleSize
            )
            {
                tagConfig.IsBaselineEstablished = true;
                baselineJustEstablished = true;
                if (tagConfig.CurrentBaselineCount > 1)
                {
                    double variance =
                        tagConfig.SumOfSquaresForBaseline / (tagConfig.CurrentBaselineCount - 1);
                    tagConfig.BaselineStandardDeviation = Math.Sqrt(Math.Max(0, variance)); // Voorkom negatieve wortel door floating point onnauwkeurigheden
                }
                else
                {
                    tagConfig.BaselineStandardDeviation = 0; // StdDev van 1 punt is 0
                }
                _specificLogger.Information(
                    "Expanding baseline VASTGESTELD voor tag {TagName} ({ConnectionName}) na {Samples} samples: Mean={Mean:F2}, StdDev={StdDev:F2}",
                    tagConfig.TagName,
                    _config.ConnectionName,
                    tagConfig.CurrentBaselineCount,
                    tagConfig.BaselineMean,
                    tagConfig.BaselineStandardDeviation
                );
            }
            else if (tagConfig.IsBaselineEstablished && tagConfig.CurrentBaselineCount > 1) // Blijf StdDev updaten als baseline is vastgesteld
            {
                double variance =
                    tagConfig.SumOfSquaresForBaseline / (tagConfig.CurrentBaselineCount - 1);
                tagConfig.BaselineStandardDeviation = Math.Sqrt(Math.Max(0, variance));
                _specificLogger.Verbose(
                    "Expanding baseline BIJGEWERKT voor {TagName} ({ConnectionName}) (N={N}): Mean={Mean:F2}, StdDev={StdDev:F2}",
                    tagConfig.TagName,
                    _config.ConnectionName,
                    tagConfig.CurrentBaselineCount,
                    tagConfig.BaselineMean,
                    tagConfig.BaselineStandardDeviation
                );
            }
            else if (tagConfig.CurrentBaselineCount == 1) // Eerste datapunt voor baseline
            {
                tagConfig.BaselineStandardDeviation = 0; // Standaarddeviatie van 1 punt is 0
                _specificLogger.Debug(
                    "Expanding Baseline voor {TagName} ({ConnectionName}): Eerste datapunt (N=1) ontvangen: {Value}. Mean={Mean}, StdDev=0.",
                    tagConfig.TagName,
                    _config.ConnectionName,
                    numericValue,
                    tagConfig.BaselineMean
                );
            }

            if (!tagConfig.IsBaselineEstablished || baselineJustEstablished)
            {
                return false;
            }

            // Als standaarddeviatie (bijna) nul is, beschouw elke afwijking van het gemiddelde als een outlier.
            if (tagConfig.BaselineStandardDeviation < 1e-9) // Een kleine tolerantie voor floating point issues
            {
                // Is de waarde significant anders dan het gemiddelde?
                bool isDifferent = Math.Abs(numericValue - tagConfig.BaselineMean) > 1e-9; // Vergelijk met een kleine epsilon
                if (isDifferent)
                {
                    _specificLogger.Information(
                        "Outlier (zero StdDev) gedetecteerd voor {TagName} ({ConnectionName}): Waarde {NumericValue} != BaselineMean {BaselineMean}",
                        tagConfig.TagName,
                        _config.ConnectionName,
                        numericValue,
                        tagConfig.BaselineMean
                    );
                }
                return isDifferent;
            }

            double deviation = Math.Abs(numericValue - tagConfig.BaselineMean);
            bool isAnOutlier =
                deviation
                > (tagConfig.OutlierStandardDeviationFactor * tagConfig.BaselineStandardDeviation);

            if (isAnOutlier)
            {
                _specificLogger.Information(
                    "Outlier gedetecteerd voor {TagName} ({ConnectionName}): Waarde {NumericValue}. Afwijking: {Deviation:F2} > (Factor {Factor} * StdDev {StdDev:F2} = Drempel {Threshold:F2})",
                    tagConfig.TagName,
                    _config.ConnectionName,
                    numericValue,
                    deviation,
                    tagConfig.OutlierStandardDeviationFactor,
                    tagConfig.BaselineStandardDeviation,
                    (tagConfig.OutlierStandardDeviationFactor * tagConfig.BaselineStandardDeviation)
                );
            }
            return isAnOutlier;
        }

        /// <summary>
        /// Werkt de <see cref="OpcUaTagConfig.CurrentAlarmState"/> bij en logt een bericht als de status verandert.
        /// Stelt ook de <see cref="OpcUaTagConfig.AlarmTimestamp"/> in.
        /// </summary>
        /// <param name="tagConfig">De configuratie van de tag.</param>
        /// <param name="liveValue">De <see cref="LoggedTagValue"/> met de actuele data.</param>
        /// <param name="newFinalState">De nieuw berekende finale alarmstatus (na drempels en outlier check).</param>
        /// <param name="numericValueForLog">De numerieke waarde die gebruikt is voor de alarmbepaling (kan null zijn als conversie faalde).</param>
        /// <param name="limitDetailsForLog">De specifieke limiet die is overschreden (kan null zijn).</param>
        private void UpdateAndLogFinalAlarmState(
            OpcUaTagConfig tagConfig,
            LoggedTagValue liveValue,
            TagAlarmState newFinalState,
            double? numericValueForLog,
            double? limitDetailsForLog
        )
        {
            if (tagConfig.CurrentAlarmState != newFinalState)
            {
                var previousState = tagConfig.CurrentAlarmState;
                tagConfig.CurrentAlarmState = newFinalState; // Update de status in de tag configuratie (ObservableObject triggert UI)
                string valueString = numericValueForLog.HasValue
                    ? numericValueForLog.Value.ToString("G", CultureInfo.InvariantCulture)
                    : liveValue.Value?.ToString() ?? "N/A";

                if (newFinalState != TagAlarmState.Normal && newFinalState != TagAlarmState.Error) // Een daadwerkelijk alarm of outlier
                {
                    tagConfig.AlarmTimestamp = liveValue.Timestamp; // Zet timestamp voor het actieve alarm
                    string alarmDetail = "";
                    if (newFinalState == TagAlarmState.Outlier)
                        alarmDetail =
                            $"Afwijking van baseline (Mean: {tagConfig.BaselineMean:F2}, StdDev: {tagConfig.BaselineStandardDeviation:F2}, Factor: {tagConfig.OutlierStandardDeviationFactor})";
                    else if (limitDetailsForLog.HasValue)
                        alarmDetail =
                            $"Limiet ({limitDetailsForLog.Value.ToString(CultureInfo.InvariantCulture)}) overschreden";
                    else
                        alarmDetail = "Limiet overschreden (details niet gespecificeerd)";

                    // Formatteer het alarmbericht
                    string formattedMessage = tagConfig
                        .AlarmMessageFormat.Replace("{TagName}", tagConfig.TagName)
                        .Replace("{AlarmState}", newFinalState.ToString())
                        .Replace("{Value}", valueString)
                        .Replace(
                            "{Limit}",
                            limitDetailsForLog?.ToString(CultureInfo.InvariantCulture) ?? "N/A"
                        );

                    _specificLogger.Warning(
                        "ALARMSTAAT GEWIJZIGD ({ConnectionName}): Tag {TagName} van {PreviousState} naar {NewState}. Waarde: {LiveValue}. Details: {AlarmDetail}. Bericht: {FormattedMessage}",
                        _config.ConnectionName,
                        tagConfig.TagName,
                        previousState,
                        newFinalState,
                        valueString,
                        alarmDetail,
                        formattedMessage
                    );
                }
                else if (
                    newFinalState == TagAlarmState.Normal
                    && (
                        previousState != TagAlarmState.Normal
                        && previousState != TagAlarmState.Error
                    )
                )
                {
                    tagConfig.AlarmTimestamp = null; // Reset timestamp, alarm is weg
                    _specificLogger.Information(
                        "ALARM HERSTELD ({ConnectionName}): Tag {TagName} van {PreviousState} naar Normaal. Waarde: {LiveValue}",
                        _config.ConnectionName,
                        tagConfig.TagName,
                        previousState,
                        valueString
                    );
                }
                else if (
                    newFinalState == TagAlarmState.Error
                    && previousState != TagAlarmState.Error
                )
                {
                    tagConfig.AlarmTimestamp = liveValue.Timestamp; // Zet timestamp voor error
                    _specificLogger.Error(
                        "FOUTSTATUS ({ConnectionName}): Tag {TagName} van {PreviousState} naar status Error. Waarde: {LiveValue}, Oorspronkelijke Fout: {OriginalError}",
                        _config.ConnectionName,
                        tagConfig.TagName,
                        previousState,
                        valueString,
                        liveValue.ErrorMessage ?? "N/A"
                    );
                }
                else if (
                    newFinalState == TagAlarmState.Error
                    && previousState == TagAlarmState.Error
                ) { }
            }
        }

        /// <summary>
        /// Probeert de gegeven object waarde te converteren naar een double.
        /// Ondersteunt diverse numerieke types, boolean, en een string representatie (met InvariantCulture en CurrentCulture).
        /// </summary>
        /// <param name="value">De te converteren waarde.</param>
        /// <param name="result">Output parameter voor de geconverteerde double waarde.</param>
        /// <returns>True als de conversie succesvol was, anders false.</returns>
        private bool TryConvertToDouble(object value, out double result)
        {
            result = 0;
            if (value == null)
                return false;

            Type valueType = value.GetType();

            if (valueType == typeof(double))
            {
                result = (double)value;
                return true;
            }
            if (valueType == typeof(float))
            {
                result = (double)(float)value;
                return true;
            }
            if (valueType == typeof(int))
            {
                result = (double)(int)value;
                return true;
            }
            if (valueType == typeof(uint))
            {
                result = (double)(uint)value;
                return true;
            }
            if (valueType == typeof(long))
            {
                result = (double)(long)value;
                return true;
            }
            if (valueType == typeof(ulong))
            {
                result = (double)(ulong)value;
                return true;
            }
            if (valueType == typeof(short))
            {
                result = (double)(short)value;
                return true;
            }
            if (valueType == typeof(ushort))
            {
                result = (double)(ushort)value;
                return true;
            }
            if (valueType == typeof(byte))
            {
                result = (double)(byte)value;
                return true;
            }
            if (valueType == typeof(sbyte))
            {
                result = (double)(sbyte)value;
                return true;
            }
            if (valueType == typeof(decimal))
            {
                result = (double)(decimal)value;
                return true;
            }
            if (valueType == typeof(bool))
            {
                result = (bool)value ? 1.0 : 0.0;
                return true;
            }

            if (value is string sValue)
            {
                if (
                    double.TryParse(
                        sValue,
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out result
                    )
                )
                    return true;
                if (
                    double.TryParse(
                        sValue,
                        NumberStyles.Any,
                        CultureInfo.CurrentCulture,
                        out result
                    )
                )
                    return true;
            }

            try
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException) { }
            catch (InvalidCastException) { }
            catch (OverflowException) { }

            _specificLogger.Verbose(
                "TryConvertToDouble: Kon waarde '{Value}' (type: {ValueType}) niet converteren naar double.",
                value,
                valueType.Name
            );
            return false;
        }
        #endregion
    }
}
