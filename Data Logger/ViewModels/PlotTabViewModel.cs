using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Data_Logger.Core;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using Serilog;
using FontWeights = OxyPlot.FontWeights;
using HorizontalAlignment = OxyPlot.HorizontalAlignment;
using SvgExporter = OxyPlot.Wpf.SvgExporter;
using VerticalAlignment = OxyPlot.VerticalAlignment;

namespace Data_Logger.ViewModels
{
    /// <summary>
    /// ViewModel voor een tabblad dat een enkele OxyPlot-grafiek weergeeft.
    /// Beheert de data series, assen, legendes, annotaties en UI-commando's gerelateerd aan de plot.
    /// Implementeert <see cref="IDisposable"/> om resources zoals timers correct vrij te geven.
    /// </summary>
    public class PlotTabViewModel : ObservableObject, IDisposable
    {
        #region Readonly Fields
        private readonly Action<PlotTabViewModel> _closeAction; // Actie om deze tab te sluiten
        private readonly ILogger _logger;
        private readonly object _plotDataLock = new object(); // Voor thread-safe toegang tot plot data
        #endregion

        #region Fields
        private PlotModel _plotModel;
        private string _header;
        private DispatcherTimer _plotUpdateTimer;
        private bool _needsPlotRefresh;
        private bool _disposedValue; // Voor IDisposable patroon
        private const int PlotUpdateIntervalMs = 100; // Update interval voor de plot in milliseconden

        // Een voorgedefinieerd palet van kleuren voor de series
        private static readonly OxyColor[] CustomColorPalette =
        {
            OxyColor.FromRgb(31, 119, 180),
            OxyColor.FromRgb(255, 127, 14),
            OxyColor.FromRgb(44, 160, 44),
            OxyColor.FromRgb(214, 39, 40),
            OxyColor.FromRgb(148, 103, 189),
            OxyColor.FromRgb(140, 86, 75),
            OxyColor.FromRgb(227, 119, 194),
            OxyColor.FromRgb(127, 127, 127),
            OxyColor.FromRgb(188, 189, 34),
            OxyColor.FromRgb(23, 190, 207),
            OxyColors.OrangeRed,
            OxyColors.MediumPurple,
            OxyColors.SeaGreen,
            OxyColors.SteelBlue,
            OxyColors.Tomato,
            OxyColors.Turquoise,
            OxyColors.Violet,
            OxyColors.YellowGreen,
            OxyColors.SaddleBrown,
            OxyColors.RosyBrown,
        };
        #endregion

        #region Properties
        /// <summary>
        /// Haalt het <see cref="OxyPlot.PlotModel"/> op dat de daadwerkelijke grafiek representeert, of stelt deze in.
        /// </summary>
        public PlotModel PlotModel
        {
            get => _plotModel;
            private set => SetProperty(ref _plotModel, value);
        }

        /// <summary>
        /// Haalt de header (titel) van dit plot-tabblad op of stelt deze in.
        /// </summary>
        public string Header
        {
            get => _header;
            set => SetProperty(ref _header, value);
        }

        /// <summary>
        /// Haalt de primaire identifier op van de tag of data die in deze plot wordt weergegeven.
        /// Wordt gebruikt om de plot-tab uniek te identificeren.
        /// </summary>
        public string TagIdentifier { get; private set; }

        /// <summary>
        /// Haalt een observeerbare collectie op van <see cref="PlottedSeriesDisplayInfo"/> objecten.
        /// Elk object in deze collectie beheert de weergave-opties (zoals statistische lijnen)
        /// voor een corresponderende data serie in de <see cref="PlotModel"/>.
        /// </summary>
        public ObservableCollection<PlottedSeriesDisplayInfo> PlottedSeriesInfos { get; }
        #endregion

        #region Commands
        /// <summary> Commando om de assen van de plot te resetten zodat alle data zichtbaar is (Zoom Fit). </summary>
        public ICommand ZoomFitCommand { get; }

        /// <summary> Commando om de huidige plot te exporteren als een SVG-afbeelding. </summary>
        public ICommand ExportSvgCommand { get; }

        /// <summary> Commando om een (voorbeeld)tekstannotatie aan de plot toe te voegen. </summary>
        public ICommand AddAnnotationCommand { get; }

        /// <summary> Commando om dit plot-tabblad te sluiten. </summary>
        public ICommand CloseTabCommand { get; }

        /// <summary> Commando om een specifieke data serie (en bijbehorende statistische lijnen) uit de plot te verwijderen. </summary>
        public ICommand RemoveSeriesFromPlotCommand { get; }
        #endregion

        #region Constructor
        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="PlotTabViewModel"/> klasse.
        /// </summary>
        /// <param name="initialTagIdentifier">De primaire identifier voor de data die in deze plot wordt getoond (bijv. TagName of NodeId).</param>
        /// <param name="header">De tekst die als header voor dit tabblad wordt gebruikt.</param>
        /// <param name="closeAction">Een actie die wordt aangeroepen wanneer de gebruiker dit tabblad wil sluiten.</param>
        /// <param name="logger">Optionele Serilog logger instantie voor diagnostische output.</param>
        /// <exception cref="ArgumentNullException">Als <paramref name="initialTagIdentifier"/> of <paramref name="closeAction"/> null is.</exception>
        public PlotTabViewModel(
            string initialTagIdentifier,
            string header,
            Action<PlotTabViewModel> closeAction,
            ILogger logger = null
        )
        {
            if (string.IsNullOrEmpty(initialTagIdentifier))
                throw new ArgumentNullException(nameof(initialTagIdentifier));
            _closeAction = closeAction ?? throw new ArgumentNullException(nameof(closeAction));

            TagIdentifier = initialTagIdentifier;
            Header = header;
            _logger = logger?.ForContext<PlotTabViewModel>().ForContext("PlotHeader", Header);

            PlottedSeriesInfos = new ObservableCollection<PlottedSeriesDisplayInfo>();
            PlotModel = new PlotModel
            {
                TitlePadding = 0, // Geen extra padding rond de titel van de plot zelf
                Padding = new OxyThickness(0), // Geen padding binnen het plotgebied
                PlotAreaBorderThickness = new OxyThickness(1), // Dikte van de rand rond het plotgebied
                PlotMargins = new OxyThickness(45, 10, 10, 30), // Marges: Links, Boven, Rechts, Onder
                IsLegendVisible = true, // Legenda standaard zichtbaar
            };

            PlotModel.Legends.Add(
                new Legend
                {
                    LegendPlacement = LegendPlacement.Inside,
                    LegendPosition = LegendPosition.TopRight,
                    LegendOrientation = LegendOrientation.Vertical,
                    LegendBorderThickness = 0,
                    LegendPadding = 5,
                    LegendMargin = 0,
                    LegendFontSize = 10,
                }
            );

            SetupAxes(); // Configureer de X- en Y-assen

            _plotUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(PlotUpdateIntervalMs),
            };
            _plotUpdateTimer.Tick += PlotUpdateTimer_Tick;
            _plotUpdateTimer.Start();

            // Initialiseer commando's
            ZoomFitCommand = new RelayCommand(ExecuteZoomFit, CanExecutePlotInteraction);
            ExportSvgCommand = new RelayCommand(ExecuteExportSvg, CanExecutePlotInteraction);
            AddAnnotationCommand = new RelayCommand(
                ExecuteAddAnnotation,
                CanExecutePlotInteraction
            );
            CloseTabCommand = new RelayCommand(param => _closeAction(this)); // Geef 'this' mee aan de close action
            RemoveSeriesFromPlotCommand = new RelayCommand(
                ExecuteRemoveSeriesFromPlot,
                CanExecuteRemoveSeriesFromPlot
            );

            _logger?.Debug(
                "PlotTabViewModel '{Header}' (ID: {TagIdentifier}) geïnitialiseerd.",
                Header,
                TagIdentifier
            );
        }
        #endregion

        #region Plot Setup and Data Handling
        /// <summary>
        /// Configureert de standaard X-as (DateTime) en Y-as (Linear) voor de plot.
        /// </summary>
        private void SetupAxes()
        {
            var timeAxis = new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "HH:mm:ss", // Tijdnotatie op de as
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                TimeZone = TimeZoneInfo.Local, // Gebruik lokale tijdzone
                AxisTickToLabelDistance = 2,
                AxisTitleDistance = 5,
                MinorTickSize = 2,
                MajorTickSize = 4,
                // IsZoomEnabled en IsPanEnabled zijn standaard true
            };
            PlotModel.Axes.Add(timeAxis);

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Waarde",
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                MinimumPadding = 0.1, // 10% padding aan de onderkant
                MaximumPadding = 0.1, // 10% padding aan de bovenkant
                AbsoluteMinimum = double.NaN, // Geen absolute grenzen, auto-scaling
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

        /// <summary>
        /// Zorgt ervoor dat een <see cref="LineSeries"/> met de gegeven sleutel en titel bestaat in het <see cref="PlotModel"/>.
        /// Als de serie nog niet bestaat, wordt deze aangemaakt en toegevoegd.
        /// Beheert ook de bijbehorende <see cref="PlottedSeriesDisplayInfo"/>.
        /// </summary>
        /// <param name="seriesKey">De unieke sleutel voor de data serie (vaak TagName of NodeId).</param>
        /// <param name="seriesTitle">De titel die voor de serie in de legenda wordt gebruikt. Indien null, wordt <paramref name="seriesKey"/> gebruikt.</param>
        public void EnsureSeriesExists(string seriesKey, string seriesTitle = null)
        {
            if (string.IsNullOrEmpty(seriesKey))
            {
                _logger?.Warning(
                    "EnsureSeriesExists aangeroepen met lege seriesKey voor plot '{Header}'.",
                    Header
                );
                return;
            }
            seriesTitle ??= seriesKey; // Gebruik seriesKey als titel indien niet opgegeven

            LineSeries lineSeries = PlotModel
                .Series.OfType<LineSeries>()
                .FirstOrDefault(s => (string)s.Tag == seriesKey); // Gebruik Tag voor key matching

            if (lineSeries == null)
            {
                lineSeries = new LineSeries
                {
                    Title = seriesTitle, // Titel voor de legenda
                    MarkerType = MarkerType.None, // Geen markers op de punten zelf
                    StrokeThickness = 1.5,
                    Tag = seriesKey, // Sla de key op in de Tag property voor latere identificatie
                };

                // Wijs een kleur toe uit het palet
                int colorIndex =
                    PlotModel.Series.OfType<LineSeries>().Count() % CustomColorPalette.Length;
                lineSeries.Color = CustomColorPalette[colorIndex];

                PlotModel.Series.Add(lineSeries);
                _logger?.Information(
                    "Nieuwe LineSeries '{SeriesTitle}' (Key: {SeriesKey}) toegevoegd aan plot '{PlotHeader}'.",
                    seriesTitle,
                    seriesKey,
                    Header
                );
            }
            else if (lineSeries.Title != seriesTitle) // Update titel als deze veranderd is
            {
                _logger?.Debug(
                    "Titel van bestaande LineSeries (Key: {SeriesKey}) bijgewerkt van '{OldTitle}' naar '{NewTitle}' in plot '{PlotHeader}'.",
                    seriesKey,
                    lineSeries.Title,
                    seriesTitle,
                    Header
                );
                lineSeries.Title = seriesTitle;
            }

            // Zorg ervoor dat er een PlottedSeriesDisplayInfo object is voor deze serie
            var existingSeriesInfo = PlottedSeriesInfos.FirstOrDefault(psi =>
                psi.SeriesKey == seriesKey
            );
            if (existingSeriesInfo == null)
            {
                var newSeriesInfo = new PlottedSeriesDisplayInfo(seriesKey);
                newSeriesInfo.OnStatLineVisibilityChanged += HandleStatLineVisibilityChanged;

                // Voeg toe op de UI thread als we niet al op de UI thread zijn
                if (
                    Application.Current?.Dispatcher != null
                    && !Application.Current.Dispatcher.CheckAccess()
                )
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        PlottedSeriesInfos.Add(newSeriesInfo)
                    );
                }
                else
                {
                    PlottedSeriesInfos.Add(newSeriesInfo);
                }
                _logger?.Information(
                    "Nieuwe PlottedSeriesInfo voor '{SeriesKey}' toegevoegd aan plot '{PlotHeader}'.",
                    seriesKey,
                    Header
                );
            }
            // PlotModel.InvalidatePlot(false); // Wordt afgehandeld door de timer of na data toevoeging
        }

        /// <summary>
        /// Voegt een nieuw datapunt (tijdstempel en waarde) toe aan de gespecificeerde data serie.
        /// Als de serie nog niet bestaat, wordt deze eerst aangemaakt.
        /// Beperkt het aantal punten per serie tot <c>maxPointsPerSeries</c>.
        /// </summary>
        /// <param name="timestamp">Het tijdstempel van het datapunt.</param>
        /// <param name="value">De numerieke waarde van het datapunt.</param>
        /// <param name="seriesKey">De unieke sleutel van de serie waaraan het punt moet worden toegevoegd.</param>
        public void AddDataPoint(DateTime timestamp, double value, string seriesKey)
        {
            if (string.IsNullOrEmpty(seriesKey))
            {
                _logger?.Warning(
                    "AddDataPoint aangeroepen zonder seriesKey voor plot '{PlotHeader}'. Punt niet toegevoegd.",
                    Header
                );
                return;
            }

            EnsureSeriesExists(seriesKey, seriesKey); // Zorg dat de serie bestaat (titel is hier gelijk aan key)

            LineSeries lineSeries = PlotModel
                .Series.OfType<LineSeries>()
                .FirstOrDefault(s => (string)s.Tag == seriesKey);

            if (lineSeries != null)
            {
                lock (_plotDataLock) // Bescherm toegang tot de Points collectie
                {
                    lineSeries.Points.Add(DateTimeAxis.CreateDataPoint(timestamp, value));

                    const int maxPointsPerSeries = 2000; // Maximaal aantal punten om te bewaren per serie
                    if (lineSeries.Points.Count > maxPointsPerSeries)
                    {
                        lineSeries.Points.RemoveRange(
                            0,
                            lineSeries.Points.Count - maxPointsPerSeries
                        );
                    }
                }
                _needsPlotRefresh = true; // Markeer dat de plot een refresh nodig heeft
            }
            else
            {
                _logger?.Warning(
                    "AddDataPoint: Kon LineSeries niet vinden voor key '{SeriesKey}' in plot '{PlotHeader}'. Punt niet toegevoegd.",
                    seriesKey,
                    Header
                );
            }
        }
        #endregion

        #region Timer and Plot Refresh
        /// <summary>
        /// Event handler voor de <see cref="_plotUpdateTimer"/>.
        /// Als <see cref="_needsPlotRefresh"/> true is, worden statistische lijnen bijgewerkt
        /// en wordt de plot opnieuw getekend.
        /// </summary>
        private void PlotUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_needsPlotRefresh)
            {
                bool statsUpdatedThisTick = false;
                // Update statistische lijnen indien nodig (buiten de lock voor UI interactie)
                foreach (var seriesInfo in PlottedSeriesInfos.ToList())
                {
                    if (seriesInfo.ShowMeanLine)
                    {
                        UpdateStatisticalLine(seriesInfo.SeriesKey, "mean", true, false);
                        statsUpdatedThisTick = true;
                    }
                    if (seriesInfo.ShowMaxLine)
                    {
                        UpdateStatisticalLine(seriesInfo.SeriesKey, "max", true, false);
                        statsUpdatedThisTick = true;
                    }
                    if (seriesInfo.ShowMinLine)
                    {
                        UpdateStatisticalLine(seriesInfo.SeriesKey, "min", true, false);
                        statsUpdatedThisTick = true;
                    }
                }

                PlotModel.InvalidatePlot(true); // Vraag een volledige redraw van de plot aan
                _needsPlotRefresh = false;
                _logger?.Verbose(
                    "Plot '{Header}' refreshed via timer. Stats bijgewerkt: {StatsUpdatedFlag}",
                    Header,
                    statsUpdatedThisTick
                );
            }
        }
        #endregion

        #region Command CanExecute Helpers
        /// <summary>
        /// Bepaalt of commando's die interactie met de plot vereisen (zoals ZoomFit, Export, AddAnnotation) uitgevoerd kunnen worden.
        /// Vereist dat er een PlotModel is en dat er ten minste één serie met data punten bestaat.
        /// </summary>
        private bool CanExecutePlotInteraction(object parameter)
        {
            return PlotModel != null
                && PlotModel.Series.Any(s => s is LineSeries ls && ls.Points.Any());
        }

        /// <summary>
        /// Bepaalt of het <see cref="RemoveSeriesFromPlotCommand"/> uitgevoerd kan worden.
        /// Vereist dat de parameter een <see cref="PlottedSeriesDisplayInfo"/> is en de corresponderende serie bestaat.
        /// </summary>
        private bool CanExecuteRemoveSeriesFromPlot(object parameter)
        {
            return parameter is PlottedSeriesDisplayInfo psi
                && PlotModel.Series.Any(s => (string)s.Tag == psi.SeriesKey);
        }
        #endregion

        #region Command Implementations
        /// <summary>
        /// Voert het ZoomFit commando uit: reset alle assen van de plot zodat alle data zichtbaar is.
        /// </summary>
        private void ExecuteZoomFit(object parameter)
        {
            PlotModel.ResetAllAxes();
            PlotModel.InvalidatePlot(true); // Forceer refresh
            _logger?.Debug("ZoomFit uitgevoerd voor plot '{Header}'.", Header);
        }

        /// <summary>
        /// Voert het ExportSvg commando uit: toont een SaveFileDialog en exporteert de huidige plot als een SVG-bestand.
        /// </summary>
        private void ExecuteExportSvg(object parameter)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "SVG Vector Graphic (*.svg)|*.svg",
                Title = "Exporteer grafiek als SVG",
                FileName =
                    $"{Header.Replace(" ", "_").Replace(":", "").Replace("/", "-")}_{DateTime.Now:yyyyMMddHHmmss}.svg",
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    using (var stream = File.Create(dialog.FileName))
                    {
                        var exporter = new SvgExporter { Width = 1024, Height = 768 };
                        exporter.Export(PlotModel, stream);
                    }
                    MessageBox.Show(
                        $"Grafiek opgeslagen als: {dialog.FileName}",
                        "Exporteren Succesvol",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    _logger?.Information(
                        "Plot '{Header}' succesvol geëxporteerd naar SVG: {FilePath}",
                        Header,
                        dialog.FileName
                    );
                }
                catch (Exception ex)
                {
                    _logger?.Error(
                        ex,
                        "Fout bij exporteren van plot '{Header}' naar SVG: {FilePath}",
                        Header,
                        dialog.FileName
                    );
                    MessageBox.Show(
                        $"Fout bij exporteren: {ex.Message}",
                        "Exporteren Mislukt",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            }
        }

        /// <summary>
        /// Voert het AddAnnotation commando uit: voegt een voorbeeld tekstannotatie toe aan de plot.
        /// </summary>
        private void ExecuteAddAnnotation(object parameter)
        {
            // Zoek de eerste serie met punten om de annotatie relatief te positioneren
            if (
                PlotModel.Series.FirstOrDefault(s => s is LineSeries ls && ls.Points.Any())
                is LineSeries seriesWithData
            )
            {
                var lastPoint = seriesWithData.Points.Last();
                var textAnnotation = new TextAnnotation
                {
                    Text = "Voorbeeld Annotatie!",
                    TextPosition = new DataPoint(lastPoint.X, lastPoint.Y), // Positioneer bij het laatste punt
                    Font = "Arial",
                    FontSize = 12,
                    TextColor = OxyColors.DarkBlue,
                    Background = OxyColor.FromAColor(180, OxyColors.LightYellow), // Semi-transparante achtergrond
                    Padding = new OxyThickness(5),
                    TextHorizontalAlignment = HorizontalAlignment.Left,
                    TextVerticalAlignment = VerticalAlignment.Bottom,
                    Layer = AnnotationLayer.AboveSeries, // Zorg dat het boven de series getekend wordt
                };
                PlotModel.Annotations.Add(textAnnotation);
                PlotModel.InvalidatePlot(true);
                _logger?.Debug("Annotatie toegevoegd aan plot '{Header}'.", Header);
            }
            else
            {
                MessageBox.Show(
                    "Voeg eerst data toe aan de grafiek om een annotatie te kunnen plaatsen.",
                    "Annotatie Plaatsen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
        }

        /// <summary>
        /// Voert het RemoveSeriesFromPlot commando uit: verwijdert de gespecificeerde data serie
        /// en de bijbehorende <see cref="PlottedSeriesDisplayInfo"/> uit de plot.
        /// </summary>
        /// <param name="parameter">De <see cref="PlottedSeriesDisplayInfo"/> van de te verwijderen serie.</param>
        internal void ExecuteRemoveSeriesFromPlot(object parameter) // Internal voor toegang vanuit tests of andere ViewModels indien nodig
        {
            if (parameter is PlottedSeriesDisplayInfo seriesInfoToRemove)
            {
                string seriesKeyToRemove = seriesInfoToRemove.SeriesKey;
                var lineSeriesToRemove = PlotModel
                    .Series.OfType<LineSeries>()
                    .FirstOrDefault(s => (string)s.Tag == seriesKeyToRemove);

                if (lineSeriesToRemove != null)
                {
                    PlotModel.Series.Remove(lineSeriesToRemove);
                    _logger?.Information(
                        "LineSeries '{SeriesTitle}' (Key: {SeriesKey}) verwijderd uit plot '{Header}'.",
                        lineSeriesToRemove.Title,
                        seriesKeyToRemove,
                        Header
                    );
                }

                RemoveStatisticalAnnotationsForSeries(seriesKeyToRemove); // Verwijder ook bijbehorende stat-lijnen

                seriesInfoToRemove.OnStatLineVisibilityChanged -= HandleStatLineVisibilityChanged; // Unsubscribe
                if (
                    Application.Current?.Dispatcher != null
                    && !Application.Current.Dispatcher.CheckAccess()
                )
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        PlottedSeriesInfos.Remove(seriesInfoToRemove)
                    );
                }
                else
                {
                    PlottedSeriesInfos.Remove(seriesInfoToRemove);
                }
                _logger?.Information(
                    "PlottedSeriesInfo voor '{SeriesKey}' verwijderd uit plot '{Header}'.",
                    seriesKeyToRemove,
                    Header
                );

                if (!PlotModel.Series.Any()) // Als er geen series meer zijn, reset de assen
                {
                    PlotModel.ResetAllAxes();
                }
                PlotModel.InvalidatePlot(true);
            }
        }
        #endregion

        #region Statistical Lines Logic
        /// <summary>
        /// Event handler voor wijzigingen in de zichtbaarheid van statistische lijnen vanuit <see cref="PlottedSeriesDisplayInfo"/>.
        /// Roept <see cref="UpdateStatisticalLine"/> aan om de plot bij te werken.
        /// </summary>
        private void HandleStatLineVisibilityChanged(
            PlottedSeriesDisplayInfo seriesInfo,
            string lineType,
            bool visibility
        )
        {
            _logger?.Debug(
                "HandleStatLineVisibilityChanged voor plot '{Header}': Series='{SeriesKey}', Type='{LineType}', Zichtbaar={Visibility}. Roept UpdateStatisticalLine aan.",
                Header,
                seriesInfo.SeriesKey,
                lineType,
                visibility
            );
            UpdateStatisticalLine(
                seriesInfo.SeriesKey,
                lineType,
                visibility,
                invalidatePlotImmediately: true
            );
        }

        /// <summary>
        /// Werkt een statistische lijn (gemiddelde, min, max) voor een gegeven data serie bij of verwijdert deze.
        /// </summary>
        /// <param name="seriesKey">De sleutel van de data serie.</param>
        /// <param name="lineType">Het type statistische lijn ("mean", "max", "min").</param>
        /// <param name="show">True om de lijn te tonen/bij te werken, false om te verwijderen.</param>
        /// <param name="invalidatePlotImmediately">True om de plot direct te verversen na de update.</param>
        private void UpdateStatisticalLine(
            string seriesKey,
            string lineType,
            bool show,
            bool invalidatePlotImmediately = true
        )
        {
            string annotationTag = null;
            if (!string.IsNullOrEmpty(seriesKey))
            {
                annotationTag = $"{seriesKey}_{lineType}Line"; // Unieke tag voor de annotatie
            }

            _logger?.Debug(
                "UpdateStatisticalLine voor plot '{Header}': SeriesKey='{SeriesKey}', LineType='{LineType}', Show={Show}, InvalidateNow={InvalidateNow}, AnnotationTag='{AnnotationTag}'",
                Header,
                seriesKey,
                lineType,
                show,
                invalidatePlotImmediately,
                annotationTag
            );

            // Verwijder eventuele bestaande annotatie voor deze lijn/serie
            if (annotationTag != null)
            {
                var existingAnnotation = PlotModel.Annotations.FirstOrDefault(a =>
                    (a.Tag as string) == annotationTag
                );
                if (existingAnnotation != null)
                {
                    PlotModel.Annotations.Remove(existingAnnotation);
                    _logger?.Verbose(
                        "Bestaande annotatie met Tag '{AnnotationTag}' verwijderd uit plot '{Header}'.",
                        annotationTag,
                        Header
                    );
                }
            }
            else if (string.IsNullOrEmpty(seriesKey) && !show) // Generiek verwijderen als seriesKey null is
            {
                var genericTagEnding = $"_{lineType}Line";
                var annotationsToRemove = PlotModel
                    .Annotations.Where(a => (a.Tag as string)?.EndsWith(genericTagEnding) ?? false)
                    .ToList();
                foreach (var ann in annotationsToRemove)
                    PlotModel.Annotations.Remove(ann);
                if (annotationsToRemove.Any())
                    _logger?.Debug(
                        "Verwijderde {Count} generieke '{LineType}' annotaties (seriesKey was null, show=false) uit plot '{Header}'.",
                        annotationsToRemove.Count,
                        lineType,
                        Header
                    );
            }

            // Voeg nieuwe annotatie toe als 'show' true is en de serie bestaat
            if (show && !string.IsNullOrEmpty(seriesKey))
            {
                var series = PlotModel
                    .Series.OfType<LineSeries>()
                    .FirstOrDefault(s => (string)s.Tag == seriesKey);
                if (series != null)
                {
                    List<DataPoint> pointsSnapshot;
                    lock (_plotDataLock) // Veilige toegang tot de puntenlijst
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
                                textPrefix = "Gem";
                                color = OxyColors.DarkGreen;
                                break;
                            case "max":
                                statValue = pointsSnapshot.Max(p => p.Y);
                                textPrefix = "Max";
                                color = OxyColors.DarkRed;
                                break;
                            case "min":
                                statValue = pointsSnapshot.Min(p => p.Y);
                                textPrefix = "Min";
                                color = OxyColors.DarkBlue;
                                break;
                            default:
                                _logger?.Warning(
                                    "Onbekend statistisch lijntype '{LineType}' voor serie '{SeriesKey}' in plot '{Header}'.",
                                    lineType,
                                    seriesKey,
                                    Header
                                );
                                if (invalidatePlotImmediately)
                                    PlotModel.InvalidatePlot(true);
                                return;
                        }

                        _logger?.Debug(
                            "Berekende '{LineType}' voor '{SeriesKey}' in plot '{Header}': {StatValue} (op basis van {PointCount} punten)",
                            lineType,
                            seriesKey,
                            Header,
                            statValue,
                            pointsSnapshot.Count
                        );

                        var annotation = new LineAnnotation
                        {
                            Type = LineAnnotationType.Horizontal, // Horizontale lijn
                            Y = statValue,
                            Text =
                                $"{textPrefix}: {statValue.ToString("F2", CultureInfo.InvariantCulture)}",
                            Color = color,
                            LineStyle = style,
                            TextColor = color,
                            FontWeight = FontWeights.Normal,
                            FontSize = 10,
                            Tag = annotationTag, // Belangrijk voor identificatie
                            TextHorizontalAlignment = HorizontalAlignment.Right, // Tekst rechts op de lijn
                            TextPadding = 4,
                            TextVerticalAlignment = (
                                lineType == "max" ? VerticalAlignment.Top : VerticalAlignment.Bottom
                            ), // Positie tekst relatief t.o.v. lijn
                            Layer = AnnotationLayer.BelowSeries, // Teken onder de data series
                        };
                        PlotModel.Annotations.Add(annotation);
                        _logger?.Verbose(
                            "Annotatie '{AnnotationTag}' toegevoegd aan plot '{Header}'.",
                            annotationTag,
                            Header
                        );
                    }
                    else
                    {
                        _logger?.Warning(
                            "Kan statistische lijn '{LineType}' niet toevoegen voor serie '{SeriesKey}' in plot '{Header}': serie is leeg.",
                            lineType,
                            seriesKey,
                            Header
                        );
                    }
                }
                else
                {
                    _logger?.Warning(
                        "Kan statistische lijn '{LineType}' niet toevoegen voor serie '{SeriesKey}' in plot '{Header}': serie niet gevonden.",
                        lineType,
                        seriesKey,
                        Header
                    );
                }
            }

            if (invalidatePlotImmediately)
            {
                PlotModel.InvalidatePlot(true);
                _logger?.Verbose(
                    "Plot '{Header}' geïnvalideerd (direct) na UpdateStatisticalLine voor '{SeriesKey}', Type '{LineType}', Show={Show}",
                    Header,
                    seriesKey,
                    lineType,
                    show
                );
            }
        }

        /// <summary>
        /// Verwijdert alle statistische annotaties (gemiddelde, min, max) voor een gegeven data serie.
        /// </summary>
        /// <param name="seriesKey">De sleutel van de data serie waarvan de statistische lijnen verwijderd moeten worden.</param>
        private void RemoveStatisticalAnnotationsForSeries(string seriesKey)
        {
            if (string.IsNullOrEmpty(seriesKey))
                return;
            string[] lineTypes = { "mean", "max", "min" };
            foreach (var lineType in lineTypes)
            {
                string annotationTag = $"{seriesKey}_{lineType}Line";
                var annotation = PlotModel.Annotations.FirstOrDefault(a =>
                    (a.Tag as string) == annotationTag
                );
                if (annotation != null)
                {
                    PlotModel.Annotations.Remove(annotation);
                }
            }
            _logger?.Debug(
                "Alle statistische annotaties verwijderd voor serie '{SeriesKey}' in plot '{Header}'.",
                seriesKey,
                Header
            );
        }
        #endregion

        #region IDisposable Implementation
        /// <summary>
        /// Geeft beheerde en onbeheerde resources vrij die door de <see cref="PlotTabViewModel"/> worden gebruikt.
        /// </summary>
        /// <param name="disposing">True om zowel beheerde als onbeheerde resources vrij te geven; false om alleen onbeheerde resources vrij te geven.</param>
        private void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _logger?.Debug(
                        "Dispose(true) aangeroepen voor PlotTabViewModel: '{Header}' (ID: {TagIdentifier})",
                        Header,
                        TagIdentifier
                    );
                    if (_plotUpdateTimer != null)
                    {
                        _plotUpdateTimer.Stop();
                        _plotUpdateTimer.Tick -= PlotUpdateTimer_Tick;
                        _plotUpdateTimer = null;
                    }

                    if (PlottedSeriesInfos != null)
                    {
                        // Unsubscribe van events van alle PlottedSeriesInfo objecten
                        foreach (var seriesInfo in PlottedSeriesInfos)
                        {
                            seriesInfo.OnStatLineVisibilityChanged -=
                                HandleStatLineVisibilityChanged;
                        }
                        PlottedSeriesInfos.Clear(); // Maak de collectie leeg
                    }
                    // PlotModel zelf heeft geen expliciete Dispose, wordt beheerd door OxyPlot/GC.
                }
                _disposedValue = true;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
