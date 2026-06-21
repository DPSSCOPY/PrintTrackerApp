using System;
using System.IO;
using System.Text.Json;

using System.Collections.Generic;
using PrintTrackerApp.Models;

namespace PrintTrackerApp.Services
{
    public class AppSettings
    {
        public string CsvExportPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public string PrinterIp { get; set; } = "192.168.1.75";
        public string PrinterName { get; set; } = "SAVIN MP 7502";
        public int RefreshIntervalSeconds { get; set; } = 3;
        public string SourceFolderPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public List<AppNotification> Notifications { get; set; } = new List<AppNotification>();
        
        // Auto Print Settings
        public string FoxitPath { get; set; } = "";
        public string HoldPrintUserId { get; set; } = "";
        public int AutoPrintCopies { get; set; } = 1;
        public string FoxitWindowStyle { get; set; } = "Normal";
        public int DelayBetweenPrints { get; set; } = 2;
    }

    public static class SettingsManager
    {
        private static readonly string SettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        public static AppSettings LoadSettings()
        {
            if (File.Exists(SettingsFile))
            {
                try
                {
                    string json = File.ReadAllText(SettingsFile);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch { }
            }
            return new AppSettings();
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }
    }
}
