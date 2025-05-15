using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Data_Logger.Core;
using Data_Logger.Models;
using Data_Logger.Services.Abstractions;
using Serilog;
using Serilog.Events;

namespace Data_Logger.ViewModels
{
    public class LogViewModel : ObservableObject
    {
        private readonly ILoggingHostService _loggingHostService;
        private readonly ILogger _logger;

        private string _filterText;
        private LogEventLevel? _selectedLogLevelFilter;

        public ObservableCollection<UiLogEntry> LogEntries => _loggingHostService.LogEntries;
        public ICollectionView FilteredLogEntries { get; }

        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                    FilteredLogEntries.Refresh();
            }
        }

        public ObservableCollection<LogEventLevel?> LogLevels { get; } =
            new ObservableCollection<LogEventLevel?>(
                new LogEventLevel?[] { null }.Concat(
                    Enum.GetValues(typeof(LogEventLevel)).Cast<LogEventLevel?>()
                )
            );

        public LogEventLevel? SelectedLogLevelFilter
        {
            get => _selectedLogLevelFilter;
            set
            {
                if (SetProperty(ref _selectedLogLevelFilter, value))
                    FilteredLogEntries.Refresh();
            }
        }

        public ICommand ClearLogsCommand { get; }

        public LogViewModel(ILoggingHostService loggingHostService, ILogger logger)
        {
            _loggingHostService =
                loggingHostService ?? throw new ArgumentNullException(nameof(loggingHostService));
            _logger =
                logger?.ForContext<LogViewModel>()
                ?? throw new ArgumentNullException(nameof(logger));

            FilteredLogEntries = CollectionViewSource.GetDefaultView(LogEntries);
            FilteredLogEntries.Filter = ApplyFilter;

            ClearLogsCommand = new RelayCommand(
                _ => _loggingHostService.ClearLogs(),
                _ => LogEntries.Any()
            );

            LogEntries.CollectionChanged += (sender, e) =>
            {
                ((RelayCommand)ClearLogsCommand).RaiseCanExecuteChanged();
            };
        }

        private bool ApplyFilter(object item)
        {
            if (item is UiLogEntry entry)
            {
                bool logLevelMatch =
                    !_selectedLogLevelFilter.HasValue
                    || entry.Level >= _selectedLogLevelFilter.Value;
                bool textMatch =
                    string.IsNullOrWhiteSpace(_filterText)
                    || entry.RenderedMessage.IndexOf(
                        _filterText,
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0
                    || (
                        entry.Exception != null
                        && entry.Exception.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase)
                            >= 0
                    );
                return logLevelMatch && textMatch;
            }
            return false;
        }
    }
}
