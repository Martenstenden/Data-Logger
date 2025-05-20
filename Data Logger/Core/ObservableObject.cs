using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Data_Logger.Core
{
    /// <summary>
    /// Een basisklasse voor objecten die de <see cref="INotifyPropertyChanged"/> interface implementeren.
    /// Deze klasse biedt een gestandaardiseerde manier om property change notificaties af te handelen.
    /// </summary>
    public class ObservableObject : INotifyPropertyChanged
    {
        /// <summary>
        /// Event dat wordt getriggerd wanneer de waarde van een property verandert.
        /// UI-elementen die gebonden zijn aan properties van dit object, luisteren naar dit event
        /// om zichzelf bij te werken.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Roept het <see cref="PropertyChanged"/> event aan om aan te geven dat een property is gewijzigd.
        /// </summary>
        /// <param name="propertyName">
        /// De naam van de property die is gewijzigd.
        /// Dankzij het <see cref="CallerMemberNameAttribute"/> wordt deze parameter automatisch ingevuld
        /// met de naam van de aanroepende property als deze niet expliciet wordt meegegeven.
        /// </param>
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Stelt de waarde van een property in en roept <see cref="OnPropertyChanged"/> aan als de waarde daadwerkelijk is gewijzigd.
        /// Deze methode helpt boilerplate code te verminderen in property setters.
        /// </summary>
        /// <typeparam name="T">Het type van de property.</typeparam>
        /// <param name="backingStore">Referentie naar het private backing field van de property.</param>
        /// <param name="value">De nieuwe waarde voor de property.</param>
        /// <param name="propertyName">
        /// De naam van de property die wordt ingesteld.
        /// Dankzij het <see cref="CallerMemberNameAttribute"/> wordt deze parameter automatisch ingevuld.
        /// </param>
        /// <returns>
        /// True als de waarde is gewijzigd en <see cref="OnPropertyChanged"/> is aangeroepen; anders false.
        /// </returns>
        protected bool SetProperty<T>(
            ref T backingStore,
            T value,
            [CallerMemberName] string propertyName = null
        )
        {
            // Controleer of de nieuwe waarde daadwerkelijk verschilt van de oude waarde.
            // EqualityComparer<T>.Default handelt correct nulls, value types, en reference types af.
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
            {
                return false; // Waarde is niet gewijzigd, doe niets.
            }

            backingStore = value; // Update de waarde van het backing field.
            OnPropertyChanged(propertyName); // Roep het PropertyChanged event aan.
            return true; // Geef aan dat de waarde is gewijzigd.
        }
    }
}
