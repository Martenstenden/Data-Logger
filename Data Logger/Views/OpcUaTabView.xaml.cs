using System.Windows;
using System.Windows.Controls;
using Data_Logger.Models;
using Data_Logger.ViewModels;

namespace Data_Logger.Views
{
    /// <summary>
    /// Code-behind logica voor de OpcUaTabView.xaml UserControl.
    /// Deze view toont details en bedieningselementen voor een OPC UA connectie,
    /// inclusief een node browser (TreeView), tag-configuratie en live data.
    /// </summary>
    public partial class OpcUaTabView : UserControl
    {
        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="OpcUaTabView"/> UserControl.
        /// </summary>
        public OpcUaTabView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Event handler voor de <see cref="TreeView.SelectedItemChanged"/> event.
        /// Werkt de <see cref="OpcUaTabViewModel.SelectedOpcUaNodeInTree"/> property in de ViewModel bij
        /// met de geselecteerde node.
        /// </summary>
        /// <param name="sender">De TreeView.</param>
        /// <param name="e">Event data met de oude en nieuwe geselecteerde items.</param>
        private void TreeView_SelectedItemChanged(
            object sender,
            RoutedPropertyChangedEventArgs<object> e
        )
        {
            if (DataContext is OpcUaTabViewModel viewModel)
            {
                viewModel.SelectedOpcUaNodeInTree = e.NewValue as OpcUaNodeViewModel;
            }
        }

        /// <summary>
        /// Event handler die wordt aangeroepen wanneer een TextBox voor tag-configuratie de focus verliest.
        /// Roept <see cref="OpcUaTabViewModel.SaveChangesForTagConfig"/> aan om de wijzigingen persistent te maken.
        /// </summary>
        /// <param name="sender">De TextBox die de focus heeft verloren.</param>
        /// <param name="e">Event data.</param>
        private void ConfigTagTextBox_LostFocus_SaveChanges(object sender, RoutedEventArgs e)
        {
            if (DataContext is OpcUaTabViewModel viewModel && sender is FrameworkElement element)
            {
                if (element.DataContext is OpcUaTagConfig tagConfig)
                {
                    viewModel.SaveChangesForTagConfig(tagConfig);
                }
            }
        }

        /// <summary>
        /// Event handler die wordt aangeroepen wanneer de staat van een CheckBox voor tag-configuratie verandert.
        /// Roept <see cref="OpcUaTabViewModel.SaveChangesForTagConfig"/> aan om de wijzigingen persistent te maken.
        /// </summary>
        /// <param name="sender">De CheckBox waarvan de staat is gewijzigd.</param>
        /// <param name="e">Event data.</param>
        private void ConfigTagCheckBox_Changed_SaveChanges(object sender, RoutedEventArgs e)
        {
            if (DataContext is OpcUaTabViewModel viewModel && sender is FrameworkElement element)
            {
                if (element.DataContext is OpcUaTagConfig tagConfig)
                {
                    viewModel.SaveChangesForTagConfig(tagConfig);
                }
            }
        }
    }
}
