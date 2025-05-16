using System.Windows;
using System.Windows.Controls;
using Data_Logger.Models;
using Data_Logger.ViewModels;

namespace Data_Logger.Views
{
    public partial class OpcUaTabView : UserControl
    {


        public OpcUaTabView()
        {
            InitializeComponent();
        }

        private void TreeView_SelectedItemChanged(
            object sender,
            RoutedPropertyChangedEventArgs<object> e
        )
        {
            if (
                this.DataContext is OpcUaTabViewModel viewModel
                && e.NewValue is OpcUaNodeViewModel selectedNode
            )
            {
                viewModel.SelectedOpcUaNodeInTree = selectedNode;
            }
            else if (this.DataContext is OpcUaTabViewModel viewModelWithNull && e.NewValue == null)
            {
                viewModelWithNull.SelectedOpcUaNodeInTree = null;
            }
        }

        private void ConfigTagTextBox_LostFocus_SaveChanges(object sender, RoutedEventArgs e)
        {
            if (
                this.DataContext is OpcUaTabViewModel viewModel
                && sender is FrameworkElement element
            )
            {
                if (element.DataContext is OpcUaTagConfig tagConfig)
                {
                    viewModel.SaveChangesForTagConfig(tagConfig);
                }
            }
        }

        private void ConfigTagCheckBox_Changed_SaveChanges(object sender, RoutedEventArgs e)
        {
            if (
                this.DataContext is OpcUaTabViewModel viewModel
                && sender is FrameworkElement element
            )
            {
                if (element.DataContext is OpcUaTagConfig tagConfig)
                {
                    viewModel.SaveChangesForTagConfig(tagConfig);
                }
            }
        }
        
        private void TreeViewItem_Loaded_Debug(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem item)
            {
                object dc = item.DataContext;
                // Zet hier een breakpoint en inspecteer 'dc'.
                // Is dc een OpcUaNodeViewModel?
                // Als dc de OpcUaNodeViewModel is, wat is de waarde van item.IsExpanded
                // en ((OpcUaNodeViewModel)dc).IsExpanded?
                if (dc is OpcUaNodeViewModel nodeVm)
                {
                    System.Diagnostics.Debug.WriteLine($"TreeViewItem Loaded: {nodeVm.DisplayName}, DataContextType: {dc.GetType().Name}, IsExpanded (VM): {nodeVm.IsExpanded}");
                }
            }
        }
    }
}
