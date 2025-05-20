using System.Windows;

namespace Data_Logger.Views
{
    /// <summary>
    /// Code-behind logica voor het MainWindow.xaml, het hoofdvenster van de applicatie.
    /// </summary>
    public partial class MainWindow : Window // Afgeleid van Window (impliciet via XAML)
    {
        /// <summary>
        /// Initialiseert een nieuwe instantie van het <see cref="MainWindow"/> venster.
        /// </summary>
        public MainWindow()
        {

            InitializeComponent(); // Laadt de XAML en initialiseert componenten.
        }

        /// <summary>
        /// Event handler voor de klikactie op een "Afsluiten" knop of menu-item.
        /// Sluit de huidige WPF-applicatie af.
        /// </summary>
        /// <param name="sender">De bron van het event.</param>
        /// <param name="e">Event data.</param>
        private void Afsluiten_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown(); // Sluit de applicatie correct af.
        }
    }
}
