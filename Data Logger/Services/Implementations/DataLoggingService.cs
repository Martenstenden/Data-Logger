using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Serilog;

namespace Data_Logger.Services.Implementations
{
    public class DataLoggingService : IDataLoggingService
    {
        private readonly ILogger _logger;
        private readonly string _baseLogDirectory;
        private readonly object _fileLock = new object();

        public DataLoggingService(ILogger logger)
        {
            _logger = logger.ForContext<DataLoggingService>();

            string executableLocation = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location
            );
            _baseLogDirectory = Path.Combine(executableLocation, "LoggedData");

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

        public void LogTagValues(string connectionName, IEnumerable<LoggedTagValue> tagValues)
        {
            if (tagValues == null || !tagValues.Any())
            {
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

            foreach (var tagValue in tagValues)
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
                if (!tagValues.Any() && !fileExistsAndHasContent)
                    return;
                if (
                    tagValues.Any()
                    && !fileExistsAndHasContent
                    && contentToWrite.StartsWith("Timestamp,TagName")
                )
                { /* Alleen header, wel schrijven als er data is */
                }
                else if (string.IsNullOrWhiteSpace(contentToWrite.Replace(Environment.NewLine, "")))
                {
                    return;
                }
            }

            try
            {
                lock (_fileLock)
                {
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
                    tagValues.Count()
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

        private bool IsFileLocked(IOException exception)
        {
            int errorCode = Marshal.GetHRForException(exception) & 0xFFFF;
            return errorCode == 32 || errorCode == 33;
        }

        private string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "DefaultConnection";
            return Path.GetInvalidFileNameChars()
                .Aggregate(name, (current, c) => current.Replace(c.ToString(), string.Empty));
        }

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
