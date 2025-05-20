using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Data_Logger.Models;
using Opc.Ua;

namespace Data_Logger.Views
{
    /// <summary>
    /// Code-behind logica voor het SettingsView.xaml venster.
    /// Dit venster stelt gebruikers in staat om applicatie-instellingen te configureren,
    /// zoals het toevoegen, bewerken en verwijderen van dataverbindingen en hun tags.
    /// </summary>
    public partial class SettingsView : Window
    {
        /// <summary>
        /// Initialiseert een nieuwe instantie van het <see cref="SettingsView"/> venster.
        /// </summary>
        public SettingsView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Event handler voor het <see cref="DataGrid.AddingNewItem"/> event, specifiek voor Modbus-tags.
        /// Stelt default waarden in voor een nieuw toegevoegde <see cref="ModbusTagConfig"/>.
        /// </summary>
        /// <param name="sender">De DataGrid.</param>
        /// <param name="e">Event data die het nieuwe item bevat.</param>
        private void DataGrid_AddingNewItem_ModbusTag(object sender, AddingNewItemEventArgs e)
        {
            e.NewItem = new ModbusTagConfig
            {
                TagName = "Nieuwe Modbus Tag", // Default naam
                Address = 0,
                IsActive = true,
                IsAlarmingEnabled = false, // Default geen alarmering
                IsOutlierDetectionEnabled = false, // Default geen outlier detectie
                BaselineSampleSize = 20, // Default sample size
                OutlierStandardDeviationFactor = 3.0 // Default factor
                // DataType en RegisterType behouden hun default van ModbusTagConfig constructor
            };
        }
    }

    /// <summary>
    /// Biedt een statische toegang tot de waarden van de <see cref="MessageSecurityMode"/> enum.
    /// Nuttig voor het binden van de enum waarden aan UI-elementen zoals een ComboBox in XAML.
    /// </summary>
    public static class OpcUaSecurityModeValues
    {
        /// <summary>
        /// Haalt een <see cref="IEnumerable{T}"/> op die alle waarden van de <see cref="MessageSecurityMode"/> enum bevat.
        /// </summary>
        public static IEnumerable<MessageSecurityMode> Instance =>
            Enum.GetValues(typeof(MessageSecurityMode)).Cast<MessageSecurityMode>();
    }

    /// <summary>
    /// Biedt een statische toegang tot een lijst van ondersteunde OPC UA Security Policy URI's.
    /// Nuttig voor het binden aan UI-elementen zoals een ComboBox in XAML.
    /// </summary>
    public static class OpcUaSecurityPolicyValues
    {
        /// <summary>
        /// Haalt een <see cref="IEnumerable{T}"/> van string-representaties van ondersteunde OPC UA Security Policy URI's op.
        /// </summary>
        public static IEnumerable<string> GetInstance()
        {
            // Deze lijst kan uitgebreid worden met meer policies indien ondersteund door de applicatie.
            return new List<string> { SecurityPolicies.None, SecurityPolicies.Basic256Sha256 };
        }
    }
}
