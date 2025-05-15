using Data_Logger.Enums;

namespace Data_Logger.Services.Abstractions
{
    public interface IStatusService
    {
        ApplicationStatus CurrentStatus { get; }

        string StatusMessage { get; }

        void SetStatus(ApplicationStatus status, string message);
    }
}
