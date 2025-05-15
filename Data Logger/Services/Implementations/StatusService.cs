using Data_Logger.Core;
using Data_Logger.Enums;
using Data_Logger.Services.Abstractions;
using Serilog;

namespace Data_Logger.Services.Implementations
{
    public class StatusService : ObservableObject, IStatusService
    {
        private readonly ILogger _logger;
        private ApplicationStatus _currentStatus;
        private string _statusMessage;

        public ApplicationStatus CurrentStatus
        {
            get => _currentStatus;
            private set => SetProperty(ref _currentStatus, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public StatusService(ILogger logger)
        {
            _logger = logger;

            SetStatus(ApplicationStatus.Idle, "Applicatie gereed.");
        }

        public void SetStatus(ApplicationStatus status, string message)
        {
            CurrentStatus = status;
            StatusMessage = message;
            _logger.Information(
                "Applicatiestatus gewijzigd naar: {Status} - Bericht: {Message}",
                status,
                message
            );
        }
    }
}
