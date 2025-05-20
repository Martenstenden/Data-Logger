using System.Windows.Controls;

namespace Data_Logger.Views
{
    /// <summary>
    /// Code-behind logica voor de LogView.xaml UserControl.
    /// Deze UserControl is verantwoordelijk voor het weergeven van applicatielogboeken.
    /// De data en filterlogica worden typisch beheerd door een LogViewModel.
    /// </summary>
    public partial class LogView : UserControl
    {
        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="LogView"/> UserControl.
        /// </summary>
        public LogView()
        {
            InitializeComponent(); // Laadt de XAML-componenten en initialiseert de control.
        }
    }
}
