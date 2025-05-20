using System.Windows.Controls;

namespace Data_Logger.Views
{
    /// <summary>
    /// Code-behind logica voor de SinglePlotView.xaml UserControl.
    /// Deze UserControl is verantwoordelijk voor het weergeven van een enkele grafiek
    /// met data van een of meerdere tags. De plotfunctionaliteit wordt typisch
    /// afgehandeld door een geassocieerde PlotViewModel (bijv. PlotTabViewModel).
    /// </summary>
    public partial class SinglePlotView : UserControl
    {
        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="SinglePlotView"/> UserControl.
        /// </summary>
        public SinglePlotView()
        {
            InitializeComponent();
        }
    }
}
