using System;
using System.Windows.Controls;
using System.Windows.Threading;
using Data_Logger.ViewModels;

namespace Data_Logger.Views
{
    /// <summary>
    /// Code-behind logica voor de ModbusTabView.xaml UserControl.
    /// Deze view toont details en bedieningselementen voor een Modbus TCP connectie,
    /// inclusief tag-configuratie en live data.
    /// </summary>
    public partial class ModbusTabView : UserControl
    {
        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="ModbusTabView"/> UserControl.
        /// </summary>
        public ModbusTabView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Helper methode om de SaveChangesForModbusConfigAndService methode op de ViewModel aan te roepen.
        /// Dit wordt gebruikt om wijzigingen in de DataGrid op te slaan.
        /// </summary>
        private void TriggerSaveChanges()
        {
            if (DataContext is ModbusTabViewModel viewModel)
            {
                viewModel.SaveChangesForModbusConfigAndService();
            }
        }

        /// <summary>
        /// Event handler die wordt aangeroepen wanneer het bewerken van een rij in de DataGrid is voltooid.
        /// Roept <see cref="TriggerSaveChanges"/> aan om de wijzigingen persistent te maken.
        /// <see cref="Dispatcher.BeginInvoke"/> wordt gebruikt om de UI de kans te geven de bindingen bij te werken
        /// voordat de save actie wordt uitgevoerd.
        /// </summary>
        /// <param name="sender">De DataGrid.</param>
        /// <param name="e">Event data met informatie over de bewerkte rij.</param>
        private void DataGrid_RowEditEnding_SaveChanges(object sender, DataGridRowEditEndingEventArgs e)
        {
            // Wacht tot de huidige bewerkingscyclus (en binding updates) is voltooid
            // voordat de save-actie wordt getriggerd.
            Dispatcher.BeginInvoke(new Action(TriggerSaveChanges), DispatcherPriority.Background);
        }

        /// <summary>
        /// Event handler die wordt aangeroepen wanneer het bewerken van een cel in de DataGrid is voltooid
        /// en de wijziging is gecommit.
        /// Roept <see cref="TriggerSaveChanges"/> aan. Dit is vooral nuttig voor cellen zoals CheckBoxen
        /// die mogelijk niet altijd `RowEditEnding` triggeren op dezelfde manier als tekstcellen.
        /// </summary>
        /// <param name="sender">De DataGrid.</param>
        /// <param name="e">Event data met informatie over de bewerkte cel.</param>
        private void DataGrid_CellEditEnding_SaveChanges(object sender, DataGridCellEditEndingEventArgs e)
        {
            // Alleen triggeren als de bewerking daadwerkelijk is doorgevoerd (commit).
            if (e.EditAction == DataGridEditAction.Commit)
            {
                Dispatcher.BeginInvoke(new Action(TriggerSaveChanges), DispatcherPriority.Background);
            }
        }
    }
}
