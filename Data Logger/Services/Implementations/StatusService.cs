using System;
using Data_Logger.Core;
using Data_Logger.Enums;
using Data_Logger.Services.Abstractions;
using Serilog;

namespace Data_Logger.Services.Implementations
{
    /// <summary>
    /// Service voor het beheren en communiceren van de algehele applicatiestatus.
    /// Implementeert <see cref="IStatusService"/> en erft van <see cref="ObservableObject"/>
    /// om UI-updates mogelijk te maken via data binding.
    /// </summary>
    public class StatusService : ObservableObject, IStatusService
    {
        private readonly ILogger _logger;
        private ApplicationStatus _currentStatus;
        private string _statusMessage;

        /// <inheritdoc/>
        public ApplicationStatus CurrentStatus
        {
            get => _currentStatus;
            private set => SetProperty(ref _currentStatus, value);
        }

        /// <inheritdoc/>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="StatusService"/> klasse.
        /// </summary>
        /// <param name="logger">De Serilog logger instantie.</param>
        public StatusService(ILogger logger)
        {
            _logger =
                logger?.ForContext<StatusService>()
                ?? throw new ArgumentNullException(nameof(logger));
            // Initialiseer met een default status
            SetStatus(ApplicationStatus.Idle, "Applicatie gereed.");
        }

        /// <inheritdoc/>
        public void SetStatus(ApplicationStatus status, string message)
        {
            // Gebruik de properties om INotifyPropertyChanged te triggeren
            CurrentStatus = status;
            StatusMessage = message;

            // Log de statuswijziging
            _logger.Information(
                "Applicatiestatus gewijzigd naar: {Status} - Bericht: {Message}",
                status,
                message
            );
        }
    }
}
