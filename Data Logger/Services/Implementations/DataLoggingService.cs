using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Serilog;

namespace Data_Logger.Services.Implementations
{
    /// <summary>
    /// Service verantwoordelijk voor het loggen van tag-data naar CSV-bestanden.
    /// Implementeert <see cref="IDataLoggingService"/>.
    /// </summary>
    public class DataLoggingService : IDataLoggingService
    {
        private readonly ILogger _logger;
        private readonly string _baseLogDirectory;
        private readonly object _fileLock = new object(); // Voor thread-safe bestandstoegang

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="DataLoggingService"/> klasse.
        /// </summary>
        /// <param name="logger">De Serilog logger instantie voor het loggen van interne berichten.</param>
        public DataLoggingService(ILogger logger)
        {
            _logger =
                logger?.ForContext<DataLoggingService>()
                ?? throw new ArgumentNullException(nameof(logger));

            string executableLocation = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location
            );
            if (string.IsNullOrEmpty(executableLocation))
            {
                // Fallback of error handling als de locatie niet bepaald kan worden.
                _baseLogDirectory = Path.Combine(Directory.GetCurrentDirectory(), "LoggedData");
                _logger.Warning(
                    "Kon executielocatie niet bepalen, gebruikt huidige werkmap voor LoggedData: {LogDirectory}",
                    _baseLogDirectory
                );
            }
            else
            {
                _baseLogDirectory = Path.Combine(executableLocation, "LoggedData");
            }

            try
            {
                if (!Directory.Exists(_baseLogDirectory))
                {
                    Directory.CreateDirectory(_baseLogDirectory);
                    _logger.Information(
                        "CSV Log data map aangemaakt: {LogDirectory}",
                        _baseLogDirectory
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Fout bij het aanmaken van de CSV log data map: {LogDirectory}",
                    _baseLogDirectory
                );
            }
        }

        /// <summary>
        /// Logt de opgegeven tag-waarden naar een CSV-bestand.
        /// Er wordt een dagelijks bestand per connectienaam aangemaakt.
        /// </summary>
        /// <param name="connectionName">De naam van de connectie, gebruikt voor de bestandsnaam.</param>
        /// <param name="tagValues">De collectie van <see cref="LoggedTagValue"/> om weg te schrijven.</param>
        public void LogTagValues(string connectionName, IEnumerable<LoggedTagValue> tagValues)
        {
            var loggedTagValues = tagValues as LoggedTagValue[] ?? tagValues.ToArray();
            if (!loggedTagValues.Any())
            {
                return; // Geen data om te loggen
            }

            if (string.IsNullOrEmpty(_baseLogDirectory))
            {
                _logger.Error(
                    "Base log directory is niet geïnitialiseerd. Kan data niet loggen voor connectie {ConnectionName}.",
                    connectionName
                );
                return;
            }

            string sanitizedConnectionName = SanitizeFileName(connectionName);
            string fileName = $"{sanitizedConnectionName}_{DateTime.Now:yyyyMMdd}.csv";
            string filePath = Path.Combine(_baseLogDirectory, fileName);

            StringBuilder csvBuilder = new StringBuilder();
            bool fileExistsAndHasContent =
                File.Exists(filePath) && new FileInfo(filePath).Length > 0;

            if (!fileExistsAndHasContent)
            {
                csvBuilder.AppendLine("Timestamp,TagName,Value,IsGoodQuality,ErrorMessage");
            }

            foreach (var tagValue in loggedTagValues)
            {
                string safeTagName = EscapeCsvField(tagValue.TagName);
                string safeValue = EscapeCsvField(tagValue.Value?.ToString() ?? string.Empty);
                string safeErrorMessage = EscapeCsvField(tagValue.ErrorMessage ?? string.Empty);

                csvBuilder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4}{5}",
                    tagValue.Timestamp.ToString(
                        "yyyy-MM-dd HH:mm:ss.fff",
                        CultureInfo.InvariantCulture
                    ),
                    safeTagName,
                    safeValue,
                    tagValue.IsGoodQuality,
                    safeErrorMessage,
                    Environment.NewLine
                );
            }

            string contentToWrite = csvBuilder.ToString();

            // Voorkom het schrijven van een bestand dat alleen een header bevat als er geen data is,
            // of een volledig leeg bestand.
            if (
                string.IsNullOrWhiteSpace(
                    contentToWrite
                        .Replace(
                            "Timestamp,TagName,Value,IsGoodQuality,ErrorMessage"
                                + Environment.NewLine,
                            ""
                        )
                        .Replace(Environment.NewLine, "")
                )
            )
            {
                if (!loggedTagValues.Any() && !fileExistsAndHasContent)
                    return; // Geen data, geen header, geen bestand = ok
                if (
                    loggedTagValues.Any()
                    && !fileExistsAndHasContent
                    && contentToWrite.StartsWith("Timestamp,TagName")
                ) { }
                else if (string.IsNullOrWhiteSpace(contentToWrite.Replace(Environment.NewLine, "")))
                    return; // Echt lege content
            }

            try
            {
                lock (_fileLock) // Synchroniseer toegang tot het bestand
                {
                    // Gebruik FileMode.Append om aan te vullen, FileAccess.Write, en FileShare.Read om andere processen toe te staan te lezen.
                    using (
                        FileStream stream = new FileStream(
                            filePath,
                            FileMode.Append,
                            FileAccess.Write,
                            FileShare.Read
                        )
                    )
                    using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
                    {
                        writer.Write(contentToWrite);
                    }
                }
                _logger.Debug(
                    "Data gelogd naar {FilePath} voor connectie {ConnectionName}. Aantal tags: {TagCount}",
                    filePath,
                    connectionName,
                    loggedTagValues.Length
                );
            }
            catch (IOException ioEx) when (IsFileLocked(ioEx))
            {
                _logger.Warning(
                    ioEx,
                    "CSV bestand {FilePath} is geblokkeerd (sharing violation/lock) tijdens poging tot schrijven. Data voor dit interval mogelijk niet gelogd.",
                    filePath
                );
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Algemene fout bij het wegschrijven van tag data naar CSV voor connectie {ConnectionName} naar bestand {FilePath}",
                    connectionName,
                    filePath
                );
            }
        }

        /// <summary>
        /// Controleert of een IOException is veroorzaakt door een file lock (sharing violation).
        /// Error codes 32 (sharing violation) en 33 (lock violation).
        /// </summary>
        private bool IsFileLocked(IOException exception)
        {
            int errorCode = Marshal.GetHRForException(exception) & 0xFFFF;
            return errorCode == 32 || errorCode == 33;
        }

        /// <summary>
        /// Maakt een bestandsnaam veilig door ongeldige karakters te verwijderen.
        /// </summary>
        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "DefaultConnection";

            return Path.GetInvalidFileNameChars()
                .Aggregate(name, (current, c) => current.Replace(c.ToString(), string.Empty));
        }

        /// <summary>
        /// Escapet een veld voor CSV-formaat. Als het veld komma's, quotes of newlines bevat,
        /// wordt het tussen dubbele quotes geplaatst en interne quotes worden verdubbeld.
        /// </summary>
        private string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;

            if (
                field.Contains(",")
                || field.Contains("\"")
                || field.Contains("\r")
                || field.Contains("\n")
            )
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }
    }
}
