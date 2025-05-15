using System;
using System.Windows.Controls;
using System.Windows.Threading;
using Data_Logger.ViewModels;

namespace Data_Logger.Views
{
    public partial class ModbusTabView : UserControl
    {
    
        public ModbusTabView()
        {
            InitializeComponent();
        }
        
        private void TriggerSaveChanges()
        {
            if (DataContext is ModbusTabViewModel viewModel)
            {
                viewModel.SaveChangesForModbusConfigAndService();
            }
        }

        private void DataGrid_RowEditEnding_SaveChanges(object sender, DataGridRowEditEndingEventArgs e)
        {
            // Sla wijzigingen op nadat een hele rij is bewerkt
            // Wacht een heel klein beetje zodat de bindingen kunnen updaten
            Dispatcher.BeginInvoke(new Action(() => TriggerSaveChanges()), DispatcherPriority.Background);
        }

        private void DataGrid_CellEditEnding_SaveChanges(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Sla wijzigingen op nadat een cel is bewerkt en de focus verliest (LostFocus op binding)
            // of direct als de property is veranderd (PropertyChanged op binding)
            // Deze kan redundant zijn als RowEditEnding al alles afvangt, maar voor checkboxes is het directer.
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // Voor checkboxes die direct updaten via PropertyChanged op binding, is dit een goed moment.
                // Voor TextBoxes met LostFocus, zal RowEditEnding waarschijnlijk later komen.
                Dispatcher.BeginInvoke(new Action(() => TriggerSaveChanges()), DispatcherPriority.Background);
            }
        }
    }
}
