using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
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
        private bool _useRegexFilter;

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
        
        public bool UseRegexFilter
        {
            get => _useRegexFilter;
            set
            {
                if (SetProperty(ref _useRegexFilter, value))
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
                
                bool textMatch = true; 
                if (!string.IsNullOrWhiteSpace(_filterText))
                {
                    if (UseRegexFilter) 
                    {
                        try
                        {
                            
                            textMatch = Regex.IsMatch(entry.RenderedMessage, _filterText, RegexOptions.IgnoreCase) ||
                                        (entry.Exception != null && Regex.IsMatch(entry.Exception, _filterText, RegexOptions.IgnoreCase));
                        }
                        catch (ArgumentException ex) 
                        {
                            _logger.Debug(ex, "Ongeldige reguliere expressie ingevoerd: {RegexPattern}", _filterText);
                            
                            
                            textMatch = false;
                        }
                    }
                    else
                    {
                        
                        textMatch =
                            entry.RenderedMessage.IndexOf(
                                _filterText,
                                StringComparison.OrdinalIgnoreCase
                            ) >= 0
                            || (
                                entry.Exception != null
                                && entry.Exception.IndexOf(_filterText, StringComparison.OrdinalIgnoreCase)
                                >= 0
                            );
                    }
                }
                return logLevelMatch && textMatch;
            }
            return false;
        }
    }
}
