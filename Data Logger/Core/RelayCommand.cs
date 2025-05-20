using System;
using System.Windows.Input;

namespace Data_Logger.Core
{
    /// <summary>
    /// Een generieke implementatie van <see cref="ICommand"/> die acties (delegates) kan doorsturen.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="RelayCommand"/> klasse.
        /// </summary>
        /// <param name="execute">De actie die uitgevoerd moet worden wanneer het commando wordt aangeroepen.</param>
        /// <exception cref="ArgumentNullException">Wordt geworpen als <paramref name="execute"/> null is.</exception>
        public RelayCommand(Action<object> execute)
            : this(execute, null) { }

        /// <summary>
        /// Initialiseert een nieuwe instantie van de <see cref="RelayCommand"/> klasse.
        /// </summary>
        /// <param name="execute">De actie die uitgevoerd moet worden wanneer het commando wordt aangeroepen.</param>
        /// <param name="canExecute">Een predikaat dat bepaalt of het commando uitgevoerd kan worden. Kan null zijn.</param>
        /// <exception cref="ArgumentNullException">Wordt geworpen als <paramref name="execute"/> null is.</exception>
        public RelayCommand(Action<object> execute, Predicate<object> canExecute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        /// <summary>
        /// Event dat wordt getriggerd wanneer er wijzigingen zijn die invloed hebben op of het commando uitgevoerd mag worden.
        /// In WPF wordt dit event vaak gekoppeld aan <see cref="CommandManager.RequerySuggested"/>,
        /// zodat de UI automatisch de status van gebonden elementen bijwerkt.
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>
        /// Bepaalt of het commando uitgevoerd kan worden in de huidige staat.
        /// </summary>
        /// <param name="parameter">Data gebruikt door het commando. Als het commando geen data vereist, kan dit object op null worden ingesteld.</param>
        /// <returns>
        /// True als dit commando uitgevoerd kan worden; anders, false.
        /// Als er geen <c>canExecute</c> predikaat is opgegeven in de constructor, retourneert deze methode altijd true.
        /// </returns>
        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        /// <summary>
        /// Voert de logica van het commando uit.
        /// </summary>
        /// <param name="parameter">Data gebruikt door het commando. Als het commando geen data vereist, kan dit object op null worden ingesteld.</param>
        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        /// <summary>
        /// Methode om handmatig het <see cref="CanExecuteChanged"/> event te triggeren.
        /// Dit kan nuttig zijn als de ViewModel weet dat de <see cref="CanExecute"/> status is gewijzigd
        /// door een andere actie dan een directe UI-interactie.
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
