using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using Data_Logger.Core;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Serilog.Events;

namespace Data_Logger.ViewModels
{
    /// <summary>
    /// ViewModel voor het weergeven en filteren van applicatielogboeken in de gebruikersinterface.
    /// </summary>
    public class LogViewModel : ObservableObject
    {
        private readonly ILoggingHostService _loggingHostService;

        private string _filterText;

        /// <summary>
        /// Haalt de tekst op die gebruikt wordt om logberichten te filteren, of stelt deze in.
        /// Het wijzigen van deze waarde ververst de <see cref="FilteredLogEntries"/>.
        /// </summary>
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    FilteredLogEntries?.Refresh();
                }
            }
        }

        /// <summary>
        /// Haalt de collectie van beschikbare logniveaus op voor filtering, inclusief een 'null' optie voor "alle niveaus".
        /// </summary>
        public ObservableCollection<LogEventLevel?> LogLevels { get; } =
            new ObservableCollection<LogEventLevel?>(
                new LogEventLevel?[] { null }.Concat(
                    Enum.GetValues(typeof(LogEventLevel)).Cast<LogEventLevel?>()
                )
            );

        private LogEventLevel? _selectedLogLevelFilter;

        /// <summary>
        /// Haalt het geselecteerde logniveau op dat gebruikt wordt om logberichten te filteren, of stelt deze in.
        /// Een null-waarde betekent dat er niet op logniveau gefilterd wordt.
        /// Het wijzigen van deze waarde ververst de <see cref="FilteredLogEntries"/>.
        /// </summary>
        public LogEventLevel? SelectedLogLevelFilter
        {
            get => _selectedLogLevelFilter;
            set
            {
                if (SetProperty(ref _selectedLogLevelFilter, value))
                {
                    FilteredLogEntries?.Refresh();
                }
            }
        }

        /// <summary>
        /// Haalt de onbewerkte collectie van logberichten rechtstreeks van de <see cref="ILoggingHostService"/> op.
        /// </summary>
        public ObservableCollection<UiLogEntry> LogEntries => _loggingHostService.LogEntries;

        /// <summary>
        /// Haalt de gefilterde view van de logberichten op. Deze view past de <see cref="FilterText"/>
        /// en <see cref="SelectedLogLevelFilter"/> toe.
        /// </summary>
        public ICollectionView FilteredLogEntries { get; }

        /// <summary>
        /// Commando om alle logberichten uit de <see cref="LogEntries"/> collectie te wissen.
        /// </summary>
        public ICommand ClearLogsCommand { get; }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="LogViewModel"/> klasse.
        /// </summary>
        /// <param name="loggingHostService">De service die de logberichten host. Mag niet null zijn.</param>
        /// <exception cref="ArgumentNullException">Als <paramref name="loggingHostService"/> null is.</exception>
        public LogViewModel(ILoggingHostService loggingHostService)
        {
            _loggingHostService =
                loggingHostService ?? throw new ArgumentNullException(nameof(loggingHostService));

            FilteredLogEntries = CollectionViewSource.GetDefaultView(LogEntries);
            if (FilteredLogEntries != null)
            {
                FilteredLogEntries.Filter = ApplyFilter;
            }

            ClearLogsCommand = new RelayCommand(
                execute: _ => _loggingHostService.ClearLogs(),
                canExecute: _ => LogEntries.Any()
            );

            LogEntries.CollectionChanged += (sender, e) =>
            {
                if (ClearLogsCommand is RelayCommand rc)
                {
                    rc.RaiseCanExecuteChanged();
                }
            };
        }

        /// <summary>
        /// Past het filter toe op een individueel logbericht.
        /// </summary>
        /// <param name="item">Het <see cref="UiLogEntry"/> object om te filteren.</param>
        /// <returns>True als het item voldoet aan de filtercriteria; anders false.</returns>
        private bool ApplyFilter(object item)
        {
            if (item is UiLogEntry entry)
            {
                bool logLevelMatch =
                    !_selectedLogLevelFilter.HasValue
                    || // Geen niveau filter geselecteerd OF
                    entry.Level >= _selectedLogLevelFilter.Value; // Log entry niveau is gelijk aan of hoger dan filter

                bool textMatch =
                    string.IsNullOrWhiteSpace(_filterText)
                    || // Geen tekst filter OF
                    (
                        entry.RenderedMessage != null
                        && entry.RenderedMessage.IndexOf(
                            _filterText,
                            StringComparison.OrdinalIgnoreCase
                        ) >= 0
                    )
                    || (
                        entry.Exception != null
                        && entry.Exception.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase)
                            >= 0
                    );

                return logLevelMatch && textMatch;
            }
            return false; // Item is geen UiLogEntry
        }
    }
}
