
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading; 
using Data_Logger.Core;         
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Annotations;
using System.Globalization;     
using System.Windows.Input;     
using Serilog;                  
using System.Collections.Generic;
using OxyPlot.Legends;
using FontWeights = OxyPlot.FontWeights; 

namespace Data_Logger.ViewModels 
{
    public class PlotTabViewModel : ObservableObject, IDisposable
    {
        #region Fields
        private PlotModel _plotModel;
        private string _header;
        private readonly Action<PlotTabViewModel> _closeAction;
        private readonly ILogger _logger;
        private readonly Random _colorRandomGenerator = new Random();

        private DispatcherTimer _plotUpdateTimer;
        private bool _needsPlotRefresh = false;
        private const int PlotUpdateIntervalMs = 100; 
        private readonly object _plotDataLock = new object(); 

        
        private static readonly OxyColor[] CustomColorPalette = new OxyColor[] {
            OxyColor.FromRgb(31, 119, 180), OxyColor.FromRgb(255, 127, 14), OxyColor.FromRgb(44, 160, 44),
            OxyColor.FromRgb(214, 39, 40), OxyColor.FromRgb(148, 103, 189), OxyColor.FromRgb(140, 86, 75),
            OxyColor.FromRgb(227, 119, 194), OxyColor.FromRgb(127, 127, 127), OxyColor.FromRgb(188, 189, 34),
            OxyColor.FromRgb(23, 190, 207), OxyColors.OrangeRed, OxyColors.MediumPurple, OxyColors.SeaGreen,
            OxyColors.SteelBlue, OxyColors.Tomato, OxyColors.Turquoise, OxyColors.Violet, OxyColors.YellowGreen,
            OxyColors.SaddleBrown, OxyColors.RosyBrown
            
        };
        #endregion

        #region Properties
        public PlotModel PlotModel
        {
            get => _plotModel;
            private set => SetProperty(ref _plotModel, value);
        }

        public string Header
        {
            get => _header;
            set => SetProperty(ref _header, value);
        }

        
        public string TagIdentifier { get; private set; }

        
        public ObservableCollection<PlottedSeriesDisplayInfo> PlottedSeriesInfos { get; }

        #endregion

        #region Commands
        public ICommand ZoomFitCommand { get; }
        public ICommand ExportSvgCommand { get; }
        public ICommand AddAnnotationCommand { get; } 
        public ICommand CloseTabCommand { get; }
        public ICommand RemoveSeriesFromPlotCommand { get; }
        #endregion

        #region Constructor
        public PlotTabViewModel(string initialTagIdentifier, string header, Action<PlotTabViewModel> closeAction, ILogger logger = null)
        {
            TagIdentifier = initialTagIdentifier;
            Header = header;
            _closeAction = closeAction;
            _logger = logger?.ForContext<PlotTabViewModel>().ForContext("PlotHeader", header);

            PlottedSeriesInfos = new ObservableCollection<PlottedSeriesDisplayInfo>();
            PlotModel = new PlotModel
            {
                TitlePadding = 0,   
                Padding = new OxyThickness(0),
                PlotAreaBorderThickness = new OxyThickness(1),
                PlotMargins = new OxyThickness(45, 10, 10, 30),
                IsLegendVisible = true
            };
            
            PlotModel.Legends.Add(new Legend
            {
                LegendPlacement = LegendPlacement.Inside,
                LegendPosition = LegendPosition.TopRight,
                LegendOrientation = LegendOrientation.Vertical,
                LegendBorderThickness = 0,
                LegendPadding = 5,
                LegendMargin = 0,
                LegendFontSize = 10
            });
            SetupAxes();

            
            _plotUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(PlotUpdateIntervalMs)
            };
            _plotUpdateTimer.Tick += PlotUpdateTimer_Tick;
            _plotUpdateTimer.Start();

            
            ZoomFitCommand = new RelayCommand(ExecuteZoomFit, _ => PlotModel != null && PlotModel.Series.Any(s => s is LineSeries ls && ls.Points.Count > 0));
            ExportSvgCommand = new RelayCommand(ExecuteExportSvg, _ => PlotModel != null && PlotModel.Series.Any());
            AddAnnotationCommand = new RelayCommand(ExecuteAddAnnotation, _ => PlotModel != null && PlotModel.Series.Any(s => s is LineSeries ls && ls.Points.Count > 0));
            CloseTabCommand = new RelayCommand(_ => _closeAction?.Invoke(this));
            RemoveSeriesFromPlotCommand = new RelayCommand(ExecuteRemoveSeriesFromPlot, CanExecuteRemoveSeriesFromPlot);

            _logger?.Debug("PlotTabViewModel '{Header}' geïnitialiseerd.", Header);
        }
        #endregion

        #region Plot Setup and Data Handling
        private void SetupAxes()
        {
            var timeAxis = new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "HH:mm:ss",
                Title = "Tijd",
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                TimeZone = TimeZoneInfo.Local, 
                AxisTickToLabelDistance = 2, 
                AxisTitleDistance = 5,       
                MinorTickSize = 2,
                MajorTickSize = 4,
            };
            PlotModel.Axes.Add(timeAxis);

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Waarde",
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                MinimumPadding = 0.1, 
                MaximumPadding = 0.1,
                AbsoluteMinimum = double.NaN, 
                AbsoluteMaximum = double.NaN,
                IsZoomEnabled = true,
                IsPanEnabled = true,
                AxisTickToLabelDistance = 2,
                AxisTitleDistance = 5,
                MinorTickSize = 2,
                MajorTickSize = 4,
            };
            PlotModel.Axes.Add(valueAxis);

            
            
            
            
            
            
            
            
            
        }

        public void EnsureSeriesExists(string seriesKey, string seriesTitle = null)
        {
            if (string.IsNullOrEmpty(seriesTitle)) seriesTitle = seriesKey;

            var existingSeriesInfo = PlottedSeriesInfos.FirstOrDefault(psi => psi.SeriesKey == seriesKey);
            LineSeries lineSeries = PlotModel.Series.OfType<LineSeries>().FirstOrDefault(s => s.Title == seriesTitle);

            if (lineSeries == null)
            {
                lineSeries = new LineSeries
                {
                    Title = seriesTitle, 
                    MarkerType = MarkerType.None,
                    StrokeThickness = 1.5,
                    Tag = seriesKey 
                };

                if (CustomColorPalette.Length > 0)
                {
                    
                    int colorIndex = PlotModel.Series.OfType<LineSeries>().Count() % CustomColorPalette.Length;
                    lineSeries.Color = CustomColorPalette[colorIndex];
                }
                else 
                {
                    var palette = OxyPalettes.Hot64;
                    lineSeries.Color = palette.Colors[PlotModel.Series.OfType<LineSeries>().Count() % palette.Colors.Count];
                }

                PlotModel.Series.Add(lineSeries);
                _logger?.Information("Nieuwe LineSeries '{SeriesTitle}' (key: {SeriesKey}) toegevoegd aan plot '{PlotTitle}'", seriesTitle, seriesKey, PlotModel.Title);
            }

            if (existingSeriesInfo == null)
            {
                var newSeriesInfo = new PlottedSeriesDisplayInfo(seriesKey);
                newSeriesInfo.OnStatLineVisibilityChanged += HandleStatLineVisibilityChanged;
                Application.Current?.Dispatcher.Invoke(() => PlottedSeriesInfos.Add(newSeriesInfo));
                _logger?.Information("Nieuwe PlottedSeriesInfo voor '{SeriesKey}' toegevoegd.", seriesKey);
            }
            PlotModel.InvalidatePlot(false); 
        }

        public void AddDataPoint(DateTime timestamp, double value, string seriesKey)
        {
            if (string.IsNullOrEmpty(seriesKey))
            {
                _logger?.Warning("AddDataPoint aangeroepen zonder seriesKey voor plot '{PlotHeader}'", Header);
                return;
            }

            EnsureSeriesExists(seriesKey, seriesKey); 

            LineSeries lineSeries = PlotModel.Series.OfType<LineSeries>().FirstOrDefault(s => s.Title == seriesKey);

            if (lineSeries != null)
            {
                lock (_plotDataLock)
                {
                    lineSeries.Points.Add(DateTimeAxis.CreateDataPoint(timestamp, value));

                    const int maxPointsPerSeries = 2000; 
                    if (lineSeries.Points.Count > maxPointsPerSeries)
                    {
                        lineSeries.Points.RemoveRange(0, lineSeries.Points.Count - maxPointsPerSeries);
                    }
                }
                _needsPlotRefresh = true; 
            }
        }
        #endregion

        #region Timer and Plot Refresh
        private void PlotUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_needsPlotRefresh)
            {
                bool statsUpdatedThisTick = false;
                
                foreach (var seriesInfo in PlottedSeriesInfos.ToList())
                {
                    if (seriesInfo.ShowMeanLine) { UpdateStatisticalLine(seriesInfo.SeriesKey, "mean", true, false); statsUpdatedThisTick = true; }
                    if (seriesInfo.ShowMaxLine) { UpdateStatisticalLine(seriesInfo.SeriesKey, "max", true, false); statsUpdatedThisTick = true; }
                    if (seriesInfo.ShowMinLine) { UpdateStatisticalLine(seriesInfo.SeriesKey, "min", true, false); statsUpdatedThisTick = true; }
                }

                PlotModel.InvalidatePlot(true); 
                _needsPlotRefresh = false;
                 _logger?.Verbose("Plot '{Header}' refreshed via timer. Stats updated: {StatsUpdated}", Header, statsUpdatedThisTick);
            }
        }
        #endregion

        #region Command Implementations
        private void ExecuteZoomFit(object parameter)
        {
            PlotModel.ResetAllAxes();
            PlotModel.InvalidatePlot(true);
        }

        private void ExecuteExportSvg(object parameter)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "SVG Vector Graphic (*.svg)|*.svg",
                Title = "Exporteer grafiek als SVG",
                FileName = $"{this.Header.Replace(" ", "_").Replace(":", "").Replace("/", "-")}_{DateTime.Now:yyyyMMddHHmmss}.svg"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    using (var stream = System.IO.File.Create(dialog.FileName))
                    {
                        var exporter = new OxyPlot.Wpf.SvgExporter { Width = 1024, Height = 768 }; 
                        exporter.Export(this.PlotModel, stream);
                    }
                    MessageBox.Show($"Grafiek opgeslagen als: {dialog.FileName}", "Exporteren Succesvol", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Fout bij exporteren van grafiek naar SVG: {FileName}", dialog.FileName);
                    MessageBox.Show($"Fout bij exporteren: {ex.Message}", "Exporteren Mislukt", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExecuteAddAnnotation(object parameter)
        {
            
            if (PlotModel.Series.FirstOrDefault() is LineSeries series && series.Points.Any())
            {
                var lastPoint = series.Points.Last();
                var textAnnotation = new TextAnnotation
                {
                    Text = "Annotatie!",
                    TextPosition = new DataPoint(lastPoint.X, lastPoint.Y), 
                    Font = "Arial",
                    FontSize = 12,
                    TextColor = OxyColors.DarkBlue,
                    Background = OxyColor.FromAColor(180, OxyColors.LightYellow),
                    Padding = new OxyThickness(5),
                    TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left,
                    TextVerticalAlignment = OxyPlot.VerticalAlignment.Bottom
                };
                PlotModel.Annotations.Add(textAnnotation);
                PlotModel.InvalidatePlot(true);
            } else {
                 MessageBox.Show("Voeg eerst data toe aan de grafiek om een annotatie te plaatsen.", "Annotatie", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private bool CanExecuteRemoveSeriesFromPlot(object parameter)
        {
            return parameter is PlottedSeriesDisplayInfo psi &&
                   PlotModel.Series.Any(s => s.Title == psi.SeriesKey);
        }

        internal void ExecuteRemoveSeriesFromPlot(object parameter)
        {
            if (parameter is PlottedSeriesDisplayInfo seriesInfoToRemove)
            {
                string seriesKeyToRemove = seriesInfoToRemove.SeriesKey;
                var lineSeriesToRemove = PlotModel.Series.OfType<LineSeries>().FirstOrDefault(s => s.Title == seriesKeyToRemove);
                if (lineSeriesToRemove != null)
                {
                    PlotModel.Series.Remove(lineSeriesToRemove);
                    _logger?.Information("LineSeries '{SeriesTitle}' verwijderd uit plot '{PlotTitle}'", lineSeriesToRemove.Title, PlotModel.Title);
                }

                RemoveStatisticalAnnotationsForSeries(seriesKeyToRemove);

                seriesInfoToRemove.OnStatLineVisibilityChanged -= HandleStatLineVisibilityChanged;
                Application.Current?.Dispatcher.Invoke(() => PlottedSeriesInfos.Remove(seriesInfoToRemove));
                _logger?.Information("PlottedSeriesInfo voor '{SeriesKey}' verwijderd.", seriesKeyToRemove);

                if (!PlotModel.Series.Any()) 
                {
                    
                    PlotModel.ResetAllAxes();
                }
                PlotModel.InvalidatePlot(true);
            }
        }
        #endregion

        #region Statistical Lines Logic
        private void HandleStatLineVisibilityChanged(PlottedSeriesDisplayInfo seriesInfo, string lineType, bool visibility)
        {
            _logger?.Debug("HandleStatLineVisibilityChanged: Series='{Series}', Type='{Type}', Visible={Visible}. Roept UpdateStatisticalLine aan.",
                            seriesInfo.SeriesKey, lineType, visibility);
            UpdateStatisticalLine(seriesInfo.SeriesKey, lineType, visibility, true); 
        }

        private void UpdateStatisticalLine(string seriesKey, string lineType, bool show, bool invalidatePlotImmediately = true)
        {
            string annotationTag = null;
            if (!string.IsNullOrEmpty(seriesKey))
            {
                annotationTag = $"{seriesKey}_{lineType}Line";
            }
            _logger?.Debug("UpdateStatisticalLine: SeriesKey='{SeriesKey}', LineType='{LineType}', Show={Show}, InvalidateNow={InvalidateNow}, AnnotationTag='{AnnotationTag}'",
                            seriesKey, lineType, show, invalidatePlotImmediately, annotationTag);

            if (annotationTag != null)
            {
                var existingAnnotation = PlotModel.Annotations.FirstOrDefault(a => (a.Tag as string) == annotationTag);
                if (existingAnnotation != null)
                {
                    PlotModel.Annotations.Remove(existingAnnotation);
                    _logger?.Debug("Verwijderde bestaande annotatie met Tag: {AnnotationTag}", annotationTag);
                }
            }
            else if (string.IsNullOrEmpty(seriesKey) && !show)
            {
                var genericTagEnding = $"_{lineType}Line";
                var annotationsToRemove = PlotModel.Annotations.Where(a => (a.Tag as string)?.EndsWith(genericTagEnding) ?? false).ToList();
                foreach (var ann in annotationsToRemove) PlotModel.Annotations.Remove(ann);
                if (annotationsToRemove.Any()) _logger?.Debug("Verwijderde {Count} generieke '{LineType}' annotaties omdat seriesKey null was en show=false.", annotationsToRemove.Count, lineType);
            }

            if (show && !string.IsNullOrEmpty(seriesKey))
            {
                var series = PlotModel.Series.OfType<LineSeries>().FirstOrDefault(s => s.Title == seriesKey);
                if (series != null)
                {
                    List<DataPoint> pointsSnapshot;
                    lock (_plotDataLock)
                    {
                        pointsSnapshot = new List<DataPoint>(series.Points);
                    }

                    if (pointsSnapshot.Any())
                    {
                        double statValue = 0;
                        string textPrefix = "";
                        OxyColor color = OxyColors.Transparent;
                        LineStyle style = LineStyle.Dash;

                        switch (lineType.ToLowerInvariant())
                        {
                            case "mean":
                                statValue = pointsSnapshot.Average(p => p.Y);
                                textPrefix = "Gem"; color = OxyColors.DarkGreen;
                                break;
                            case "max":
                                statValue = pointsSnapshot.Max(p => p.Y);
                                textPrefix = "Max"; color = OxyColors.DarkRed;
                                break;
                            case "min":
                                statValue = pointsSnapshot.Min(p => p.Y);
                                textPrefix = "Min"; color = OxyColors.DarkBlue;
                                break;
                            default:
                                _logger?.Warning("Onbekend statistisch lijntype: {LineType}", lineType);
                                if (invalidatePlotImmediately) PlotModel.InvalidatePlot(true);
                                return;
                        }
                        _logger?.Debug("Berekende '{LineType}' voor '{SeriesKey}': {StatValue} (op basis van {PointCount} punten)", lineType, seriesKey, statValue, pointsSnapshot.Count);

                        var annotation = new LineAnnotation
                        {
                            Type = LineAnnotationType.Horizontal,
                            Y = statValue,
                            Text = $"{textPrefix}: {statValue.ToString("F2", CultureInfo.InvariantCulture)}",
                            Color = color,
                            LineStyle = style,
                            TextColor = color, 
                            FontWeight = FontWeights.Normal,
                            FontSize = 10,
                            Tag = annotationTag,
                            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Right,
                            TextPadding = 4,
                            TextVerticalAlignment = (lineType == "max" ? OxyPlot.VerticalAlignment.Top : OxyPlot.VerticalAlignment.Bottom), 
                            Layer = AnnotationLayer.BelowSeries 
                        };
                        PlotModel.Annotations.Add(annotation);
                        _logger?.Debug("Annotatie '{AnnotationTag}' toegevoegd.", annotationTag);
                    }
                    else
                    {
                        _logger?.Warning("Kan statistische lijn '{LineType}' niet toevoegen voor series '{SeriesKey}': series is leeg.", lineType, seriesKey);
                    }
                }
                else
                {
                    _logger?.Warning("Kan statistische lijn '{LineType}' niet toevoegen voor series '{SeriesKey}': series niet gevonden.", lineType, seriesKey);
                }
            }

            if (invalidatePlotImmediately)
            {
                PlotModel.InvalidatePlot(true);
                _logger?.Debug("Plot geïnvalideerd (direct) na UpdateStatisticalLine voor '{SeriesKey}', Type '{LineType}', Show={Show}", seriesKey, lineType, show);
            }
        }

        private void RemoveStatisticalAnnotationsForSeries(string seriesKey)
        {
            if (string.IsNullOrEmpty(seriesKey)) return;
            string[] lineTypes = { "mean", "max", "min" };
            foreach (var lineType in lineTypes)
            {
                string annotationTag = $"{seriesKey}_{lineType}Line";
                var annotation = PlotModel.Annotations.FirstOrDefault(a => (a.Tag as string) == annotationTag);
                if (annotation != null)
                {
                    PlotModel.Annotations.Remove(annotation);
                }
            }
            _logger?.Debug("Alle statistische annotaties verwijderd voor series '{SeriesKey}'", seriesKey);
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_plotUpdateTimer != null)
                {
                    _plotUpdateTimer.Stop();
                    _plotUpdateTimer.Tick -= PlotUpdateTimer_Tick;
                    _plotUpdateTimer = null;
                }
                
                if (PlottedSeriesInfos != null)
                {
                    foreach(var seriesInfo in PlottedSeriesInfos)
                    {
                        seriesInfo.OnStatLineVisibilityChanged -= HandleStatLineVisibilityChanged;
                    }
                }
                _logger?.Debug("PlotTabViewModel '{Header}' disposed.", Header);
            }
        }
        #endregion
    }
}

