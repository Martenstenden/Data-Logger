using Data_Logger.Models;

namespace Data_Logger.Services.Abstractions
{
    public interface ISettingsService
    {
        AppSettings CurrentSettings { get; }

        void LoadSettings();

        void SaveSettings();

        void LoadDefaultSettings();
    }
}
