using System;
using System.IO;
using System.Text.Json;

using System.Collections.Generic;
using PrintTrackerApp.Models;

namespace PrintTrackerApp.Services
{
    public class PrinterProfile
    {
        public string ProfileName { get; set; } = "Default SAVIN";
        public string FoxitPropertiesWindowName { get; set; } = "Properties";
        public string FoxitJobDetailsWindowName { get; set; } = "Job Type Details";
        public string SavinDetailsBtnId { get; set; } = "1018";
        public string SavinUserIdTextBoxId { get; set; } = "1004";
        public string SavinFileNameTextBoxId { get; set; } = "1007";
        public string SavinDetailsOkBtnId { get; set; } = "1";
        public string SavinPropertiesOkBtnId { get; set; } = "1";
    }

    public class CustomDateFilter
    {
        public string Name { get; set; } = "Week 1";
        public DateTime StartDate { get; set; } = DateTime.Now.Date;
        public DateTime EndDate { get; set; } = DateTime.Now.Date;
    }

    public class AppSettings
    {
        public List<CustomDateFilter> CustomDateFilters { get; set; } = new List<CustomDateFilter>();
        public string CsvExportPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public string PrinterIp { get; set; } = "192.168.1.75";
        public string PrinterName { get; set; } = "SAVIN MP 7502";
        public int RefreshIntervalSeconds { get; set; } = 1;
        public string SourceFolderPath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public List<AppNotification> Notifications { get; set; } = new List<AppNotification>();
        
        // Google Sheets Integration
        public string GoogleSpreadsheetId { get; set; } = "";
        public string TeacherDataSpreadsheetId { get; set; } = "";
        public string GoogleSheetStartCell { get; set; } = "A1";

        // Dashboard Level Categorization
        public string FtLevels { get; set; } = "1A, 1B, 2A, 2B, 3A, 3B, 4A, 4B, 5A, 5B, Pre2AI, Pre2AII, KGMA, KGMB, KGHA, KGHB, KGHC";
        public string PtLevels { get; set; } = "FA, FB, L1, L2, L3, L4, L5, L6, L7, L8, Pre5, Pre8, PreA";
        public string KhLevels { get; set; } = "G1, G2, KGL, SMC3";
        
        // Auto Print Settings
        public string FoxitPath { get; set; } = "";
        public string HoldPrintUserId { get; set; } = "";
        public int AutoPrintCopies { get; set; } = 1;
        public string FoxitWindowStyle { get; set; } = "Normal";
        public int DelayBetweenPrints { get; set; } = 2;
        
        // Advanced Auto Print Settings
        public bool SkipBlankPage { get; set; } = false;
        public bool EnableBatchPrinting { get; set; } = false;
        public int BatchSize { get; set; } = 5;
        public bool EnableUiStepDelay { get; set; } = false;
        public int UiStepDelayMs { get; set; } = 300;

        // Foxit UI Automation Settings
        public string FoxitPrintWindowName { get; set; } = "Print";
        public string FoxitPrintOkBtnId { get; set; } = "1";
        public string FoxitPropertiesBtnId { get; set; } = "10380";
        public string FoxitCopiesSpinnerId { get; set; } = "10590";
        public string FoxitPagesRadioBtnId { get; set; } = "10433";
        public string FoxitPagesTextBoxId { get; set; } = "10415";
        public string FoxitCopiesTextBoxId { get; set; } = "10408";
        public string FoxitShortEdgeRadioBtnId { get; set; } = "10431";
        public string FoxitLongEdgeRadioBtnId { get; set; } = "";

        // SAVIN Printer Driver Automation Settings (Legacy - Kept for migration)
        public string SavinDetailsBtnId { get; set; } = "1018";
        public string SavinUserIdTextBoxId { get; set; } = "1004";
        public string SavinFileNameTextBoxId { get; set; } = "1007";
        public string SavinDetailsOkBtnId { get; set; } = "1";
        public string SavinPropertiesOkBtnId { get; set; } = "1";

        // Printer Profiles
        public List<PrinterProfile> PrinterProfiles { get; set; } = new List<PrinterProfile>();
        public string ActivePrinterProfileName { get; set; } = "Default SAVIN";
        
        // Priority Printing Settings
        public bool EnablePriority1 { get; set; } = false;
        public string Priority1Prefixes { get; set; } = "";
        public bool EnablePriority2 { get; set; } = false;
        public string Priority2Prefixes { get; set; } = "";
        public bool EnablePriority3 { get; set; } = false;
        public string Priority3Prefixes { get; set; } = "";

        // Telegram Notification Settings
        public string TelegramBotUrl { get; set; } = "https://api.telegram.org/bot";
        public string TelegramBotToken { get; set; } = "";
        public string TelegramChatId { get; set; } = "";
        public string DailyReportTime { get; set; } = "17:00";
        public bool NotifySentToPrinter { get; set; } = true;
        public bool NotifyStoringCompleted { get; set; } = true;
        public bool NotifyPrintCompleted { get; set; } = true;

        // System Settings
        public bool EnableAutoShutdown { get; set; } = false;
        public int AutoShutdownMode { get; set; } = 0; // 0 = After Print Complete, 1 = Specific Time
        public int AutoShutdownDelayMinutes { get; set; } = 5;
        public string AutoShutdownTime { get; set; } = "18:00";

        // Dashboard UI Preferences
        public string DashboardSortOrder { get; set; } = "default"; // default, name_asc, name_desc, level_asc, level_desc, grade_asc, grade_desc
    }

    public static class SettingsManager
    {
        private static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintTrackerApp");
        private static readonly string SettingsFile = Path.Combine(AppDataFolder, "appsettings.json");
        private static readonly string LegacySettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        public static AppSettings LoadSettings()
        {
            // Migrate legacy settings if AppData settings don't exist yet
            if (!File.Exists(SettingsFile) && File.Exists(LegacySettingsFile))
            {
                try
                {
                    Directory.CreateDirectory(AppDataFolder);
                    File.Copy(LegacySettingsFile, SettingsFile);
                }
                catch { }
            }

            string fileToLoad = File.Exists(SettingsFile) ? SettingsFile : (File.Exists(LegacySettingsFile) ? LegacySettingsFile : null);

            if (fileToLoad != null)
            {
                try
                {
                    string json = File.ReadAllText(fileToLoad);
                    AppSettings loadedSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    
                    // Migration: If no profiles exist, create a default one from legacy properties
                    if (loadedSettings.PrinterProfiles == null || loadedSettings.PrinterProfiles.Count == 0)
                    {
                        loadedSettings.PrinterProfiles = new List<PrinterProfile>();
                        loadedSettings.PrinterProfiles.Add(new PrinterProfile
                        {
                            ProfileName = "Default SAVIN",
                            FoxitPropertiesWindowName = "Properties",
                            FoxitJobDetailsWindowName = "Job Type Details",
                            SavinDetailsBtnId = loadedSettings.SavinDetailsBtnId ?? "1018",
                            SavinUserIdTextBoxId = loadedSettings.SavinUserIdTextBoxId ?? "1004",
                            SavinFileNameTextBoxId = loadedSettings.SavinFileNameTextBoxId ?? "1007",
                            SavinDetailsOkBtnId = loadedSettings.SavinDetailsOkBtnId ?? "1",
                            SavinPropertiesOkBtnId = loadedSettings.SavinPropertiesOkBtnId ?? "1"
                        });
                        loadedSettings.ActivePrinterProfileName = "Default SAVIN";
                    }

                    return loadedSettings;
                }
                catch { }
            }
            return new AppSettings();
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }
                
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }
    }
}
