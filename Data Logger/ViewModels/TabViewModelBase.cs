using System;
using Data_Logger.Core;
using Data_Logger.Models;

namespace Data_Logger.ViewModels
{
    /// <summary>
    /// Een abstracte basisklasse voor ViewModels die een tabblad in de gebruikersinterface representeren.
    /// Elk tabblad is typisch geassocieerd met een specifieke <see cref="ConnectionConfigBase"/>.
    /// </summary>
    public abstract class TabViewModelBase : ObservableObject
    {
        private string _displayName;

        /// <summary>
        /// Haalt de weergavenaam voor dit tabblad op of stelt deze in.
        /// Wordt initieel ingesteld op de naam van de <see cref="ConnectionConfiguration"/>.
        /// </summary>
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        private ConnectionConfigBase _connectionConfiguration;

        /// <summary>
        /// Haalt de <see cref="ConnectionConfigBase"/> op die geassocieerd is met dit tabblad, of stelt deze in.
        /// </summary>
        public ConnectionConfigBase ConnectionConfiguration
        {
            get => _connectionConfiguration;
            protected set => SetProperty(ref _connectionConfiguration, value);
        }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="TabViewModelBase"/> klasse.
        /// </summary>
        /// <param name="connectionConfig">
        /// De <see cref="ConnectionConfigBase"/> die geassocieerd is met dit tabblad.
        /// Mag niet null zijn.
        /// </param>
        /// <exception cref="ArgumentNullException">Als <paramref name="connectionConfig"/> null is.</exception>
        protected TabViewModelBase(ConnectionConfigBase connectionConfig)
        {
            ConnectionConfiguration =
                connectionConfig ?? throw new ArgumentNullException(nameof(connectionConfig));
            DisplayName = ConnectionConfiguration.ConnectionName; // Standaard display naam
        }
    }
}
