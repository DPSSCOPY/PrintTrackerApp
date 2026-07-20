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
        public string PrintLogSpreadsheetId { get; set; } = "";
        public string GoogleSheetStartCell { get; set; } = "A1";
        public string GoogleSheetDropdownCell { get; set; } = "A2";

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
        public string TelegramTrackingBotToken { get; set; } = "";
        public string TelegramChatId { get; set; } = "";
        [Obsolete("TeacherName is no longer used in Settings UI or sync, kept for backwards compatibility in JSON serialization.")]
        public string TeacherName { get; set; } = "";
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
        private static readonly string UserAppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintTrackerApp");
        private static readonly string CommonAppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PrintTrackerApp");
        private static readonly string BaseDirFolder = AppDomain.CurrentDomain.BaseDirectory;

        private static readonly string UserSettingsFile = Path.Combine(UserAppDataFolder, "appsettings.json");
        private static readonly string CommonSettingsFile = Path.Combine(CommonAppDataFolder, "appsettings.json");
        private static readonly string LegacySettingsFile = Path.Combine(BaseDirFolder, "appsettings.json");

        private static readonly string UserSettingsBak = Path.Combine(UserAppDataFolder, "appsettings.json.bak");
        private static readonly string CommonSettingsBak = Path.Combine(CommonAppDataFolder, "appsettings.json.bak");
        private static readonly string LegacySettingsBak = Path.Combine(BaseDirFolder, "appsettings.json.bak");

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly object SyncLock = new object();

        public static AppSettings LoadSettings()
        {
            lock (SyncLock)
            {
                var candidateFiles = new[]
                {
                    UserSettingsFile,
                    CommonSettingsFile,
                    LegacySettingsFile,
                    UserSettingsBak,
                    CommonSettingsBak,
                    LegacySettingsBak
                };

                AppSettings? bestSettings = null;

                foreach (var filePath in candidateFiles)
                {
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            string json = File.ReadAllText(filePath);
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                AppSettings? candidate = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                                if (candidate != null)
                                {
                                    if (bestSettings == null)
                                    {
                                        bestSettings = candidate;
                                    }
                                    else
                                    {
                                        // Hydrate any missing critical fields from older or secondary sources
                                        MergeSettings(bestSettings, candidate);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"SettingsManager Load Error ({filePath}): " + ex.Message);
                        }
                    }
                }

                if (bestSettings == null)
                {
                    bestSettings = new AppSettings();
                }

                // Ensure PrinterProfiles is initialized
                if (bestSettings.PrinterProfiles == null || bestSettings.PrinterProfiles.Count == 0)
                {
                    bestSettings.PrinterProfiles = new List<PrinterProfile>
                    {
                        new PrinterProfile
                        {
                            ProfileName = "Default SAVIN",
                            FoxitPropertiesWindowName = "Properties",
                            FoxitJobDetailsWindowName = "Job Type Details",
                            SavinDetailsBtnId = bestSettings.SavinDetailsBtnId ?? "1018",
                            SavinUserIdTextBoxId = bestSettings.SavinUserIdTextBoxId ?? "1004",
                            SavinFileNameTextBoxId = bestSettings.SavinFileNameTextBoxId ?? "1007",
                            SavinDetailsOkBtnId = bestSettings.SavinDetailsOkBtnId ?? "1",
                            SavinPropertiesOkBtnId = bestSettings.SavinPropertiesOkBtnId ?? "1"
                        }
                    };
                    bestSettings.ActivePrinterProfileName = "Default SAVIN";
                }

                return bestSettings;
            }
        }

        private static void MergeSettings(AppSettings target, AppSettings source)
        {
            if (target == null || source == null) return;

            if (string.IsNullOrWhiteSpace(target.GoogleSpreadsheetId) && !string.IsNullOrWhiteSpace(source.GoogleSpreadsheetId))
                target.GoogleSpreadsheetId = source.GoogleSpreadsheetId;

            if (string.IsNullOrWhiteSpace(target.PrintLogSpreadsheetId) && !string.IsNullOrWhiteSpace(source.PrintLogSpreadsheetId))
                target.PrintLogSpreadsheetId = source.PrintLogSpreadsheetId;

            if (string.IsNullOrWhiteSpace(target.TeacherDataSpreadsheetId) && !string.IsNullOrWhiteSpace(source.TeacherDataSpreadsheetId))
                target.TeacherDataSpreadsheetId = source.TeacherDataSpreadsheetId;

            if (string.IsNullOrWhiteSpace(target.TelegramBotToken) && !string.IsNullOrWhiteSpace(source.TelegramBotToken))
                target.TelegramBotToken = source.TelegramBotToken;

            if (string.IsNullOrWhiteSpace(target.TelegramTrackingBotToken) && !string.IsNullOrWhiteSpace(source.TelegramTrackingBotToken))
                target.TelegramTrackingBotToken = source.TelegramTrackingBotToken;

            if (string.IsNullOrWhiteSpace(target.TelegramChatId) && !string.IsNullOrWhiteSpace(source.TelegramChatId))
                target.TelegramChatId = source.TelegramChatId;

            if (string.IsNullOrWhiteSpace(target.FoxitPath) && !string.IsNullOrWhiteSpace(source.FoxitPath))
                target.FoxitPath = source.FoxitPath;

            if (string.IsNullOrWhiteSpace(target.HoldPrintUserId) && !string.IsNullOrWhiteSpace(source.HoldPrintUserId))
                target.HoldPrintUserId = source.HoldPrintUserId;

            if (string.IsNullOrWhiteSpace(target.PrinterIp) && !string.IsNullOrWhiteSpace(source.PrinterIp))
                target.PrinterIp = source.PrinterIp;

            if (string.IsNullOrWhiteSpace(target.PrinterName) && !string.IsNullOrWhiteSpace(source.PrinterName))
                target.PrinterName = source.PrinterName;

            if ((target.PrinterProfiles == null || target.PrinterProfiles.Count == 0) && source.PrinterProfiles != null && source.PrinterProfiles.Count > 0)
            {
                target.PrinterProfiles = source.PrinterProfiles;
                target.ActivePrinterProfileName = source.ActivePrinterProfileName;
            }
        }

        public static void SaveSettings(AppSettings settings)
        {
            if (settings == null) return;

            lock (SyncLock)
            {
                try
                {
                    // Protection: Load existing disk settings and merge non-empty properties into incoming settings to prevent wiping credentials
                    AppSettings existingDiskSettings = LoadSettings();
                    if (existingDiskSettings != null)
                    {
                        MergeSettings(settings, existingDiskSettings);
                    }

                    string json = JsonSerializer.Serialize(settings, JsonOpts);

                    // Save to User AppData
                    SaveToLocation(UserSettingsFile, UserSettingsBak, json);

                    // Save to Common ProgramData (accessible by all user contexts / admin)
                    SaveToLocation(CommonSettingsFile, CommonSettingsBak, json);

                    // Save to Legacy BaseDirectory if writable
                    SaveToLocation(LegacySettingsFile, LegacySettingsBak, json);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("SettingsManager Save Error: " + ex.Message);
                }
            }
        }

        private static void SaveToLocation(string filePath, string bakPath, string json)
        {
            try
            {
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Create backup of existing file before writing
                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Copy(filePath, bakPath, overwrite: true);
                    }
                    catch { }
                }

                SafeFileHelper.WriteAllText(filePath, json);
            }
            catch { }
        }
    }
}
