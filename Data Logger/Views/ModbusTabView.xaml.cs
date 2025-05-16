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
            
            
            Dispatcher.BeginInvoke(new Action(() => TriggerSaveChanges()), DispatcherPriority.Background);
        }

        private void DataGrid_CellEditEnding_SaveChanges(object sender, DataGridCellEditEndingEventArgs e)
        {
            
            
            
            if (e.EditAction == DataGridEditAction.Commit)
            {
                
                
                Dispatcher.BeginInvoke(new Action(() => TriggerSaveChanges()), DispatcherPriority.Background);
            }
        }
    }
}
