using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Data_Logger.Models;

namespace Data_Logger.Services.Abstractions
{
    /// <summary>
    /// Representeert een eenvoudig datapunt zoals gelezen van een Modbus-apparaat.
    /// Bevat het adres, de onbewerkte waarde en een tijdstempel.
    /// </summary>
    public class ModbusDataPoint
    {
        /// <summary>
        /// Haalt het Modbus-adres van het datapunt op of stelt deze in.
        /// </summary>
        public ushort Address { get; set; }

        /// <summary>
        /// Haalt de onbewerkte (ushort) waarde van het Modbus-register op of stelt deze in.
        /// </summary>
        public ushort Value { get; set; }

        /// <summary>
        /// Haalt het tijdstempel van het moment dat het datapunt is gelezen, op of stelt deze in.
        /// </summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Definieert het contract voor een service die Modbus TCP communicatie afhandelt.
    /// Dit omvat het beheren van verbindingen, het pollen van tags, en het ontvangen van data.
    /// Implementeert <see cref="IDisposable"/> voor het correct vrijgeven van resources.
    /// </summary>
    public interface IModbusService : IDisposable
    {
        /// <summary>
        /// Haalt een waarde op die aangeeft of de service momenteel verbonden is met een Modbus server.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Event dat wordt getriggerd wanneer de connectiestatus (verbonden/niet verbonden) verandert.
        /// </summary>
        event EventHandler ConnectionStatusChanged;

        /// <summary>
        /// Event dat wordt getriggerd wanneer er nieuwe tag-data is ontvangen van de Modbus server.
        /// De data wordt geleverd als een collectie van <see cref="LoggedTagValue"/> objecten.
        /// </summary>
        event EventHandler<IEnumerable<LoggedTagValue>> TagsDataReceived;

        /// <summary>
        /// Probeert asynchroon een verbinding op te zetten met de Modbus server,
        /// gebaseerd op de huidige configuratie.
        /// </summary>
        /// <returns>Een Task die resulteert in true als de verbinding succesvol is opgezet, anders false.</returns>
        Task<bool> ConnectAsync();

        /// <summary>
        /// Verbreekt asynchroon de huidige verbinding met de Modbus server.
        /// </summary>
        /// <returns>Een Task die de disconnectie-operatie representeert.</returns>
        Task DisconnectAsync();

        /// <summary>
        /// Pollt asynchroon de geconfigureerde Modbus-tags voor hun actuele waarden.
        /// Na het lezen wordt het <see cref="TagsDataReceived"/> event getriggerd.
        /// </summary>
        /// <returns>Een Task die de poll-operatie representeert.</returns>
        Task PollConfiguredTagsAsync();

        /// <summary>
        /// Herconfigureert de Modbus service met een nieuwe set van verbindings- en tag-instellingen.
        /// Dit kan nodig zijn als de gebruiker instellingen wijzigt.
        /// Een actieve verbinding kan mogelijk verbroken en opnieuw opgezet moeten worden.
        /// </summary>
        /// <param name="newConfig">De nieuwe <see cref="ModbusTcpConnectionConfig"/> om te gebruiken.</param>
        void Reconfigure(ModbusTcpConnectionConfig newConfig);
    }
}
