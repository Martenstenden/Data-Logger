using System.Windows; 

namespace Data_Logger.Views
{
    /// <summary>
    /// Code-behind logica voor het BrowseOpcUaNodesView.xaml venster.
    /// Dit venster wordt gebruikt om door de adresruimte van een OPC UA server te navigeren.
    /// De daadwerkelijke browse-logica en data wordt afgehandeld door de geassocieerde ViewModel.
    /// </summary>
    public partial class BrowseOpcUaNodesView : Window
    {
        /// <summary>
        /// Initialiseert een nieuwe instantie van het <see cref="BrowseOpcUaNodesView"/> venster.
        /// </summary>
        public BrowseOpcUaNodesView()
        {
            InitializeComponent(); // Laadt de XAML-componenten en initialiseert het venster.
        }
    }
}