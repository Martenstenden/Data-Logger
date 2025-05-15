using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Data_Logger.Models;
using Opc.Ua;

namespace Data_Logger.Views
{
    public partial class SettingsView : Window
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        private void DataGrid_AddingNewItem_ModbusTag(object sender, AddingNewItemEventArgs e)
        {
            e.NewItem = new ModbusTagConfig
            {
                TagName = "Nieuwe Modbus Tag",
                Address = 0,
                IsActive = true,
                IsAlarmingEnabled = false, 
                IsOutlierDetectionEnabled = false, 
                BaselineSampleSize = 20,
                OutlierStandardDeviationFactor = 3.0
                
            };
        }
    }
    
    public static class OpcUaSecurityModeValues
    {
        
        public static IEnumerable<MessageSecurityMode> Instance => 
            Enum.GetValues(typeof(MessageSecurityMode)).Cast<MessageSecurityMode>();
    }

    public static class OpcUaSecurityPolicyValues
    {
        public static IEnumerable<string> GetInstance()
        {
            return new List<string>
            {
                SecurityPolicies.None,
                SecurityPolicies.Basic256Sha256,
            };
        }
    }
}