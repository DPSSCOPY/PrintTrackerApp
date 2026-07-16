using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PrintTrackerApp.Models;

namespace PrintTrackerApp.Services
{
    public static class GoogleSheetsSyncHelper
    {
        public static async Task SyncPrintJobsToGoogleSheetsAsync(IEnumerable<PrintJobInfo> jobs, string dateStr)
        {
            if (jobs == null) return;

            try
            {
                var settings = SettingsManager.LoadSettings();
                string spreadsheetId = !string.IsNullOrWhiteSpace(settings.PrintLogSpreadsheetId)
                    ? settings.PrintLogSpreadsheetId
                    : settings.GoogleSpreadsheetId;
                if (string.IsNullOrWhiteSpace(spreadsheetId))
                {
                    System.Diagnostics.Debug.WriteLine("Google Sheets Sync: Print Log Spreadsheet ID not configured.");
                    return;
                }

                string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintTrackerApp");
                string credentialsPath = Path.Combine(appDataFolder, "google_credentials.json");
                if (!File.Exists(credentialsPath))
                {
                    System.Diagnostics.Debug.WriteLine("Google Sheets Sync: google_credentials.json not found.");
                    return;
                }

                var service = new GoogleSheetsService(spreadsheetId, credentialsPath);
                string sheetName = $"PrintLog_{dateStr}";

                // Ensure the sheet exists
                await service.EnsureSheetExistsAsync(sheetName);

                // Clear the sheet first to avoid leftover rows
                await service.ClearSheetAsync(sheetName);

                // Discover dynamic keys from the jobs list (just like CsvLogger does)
                var jobsList = jobs.Reverse().ToList();
                var dynamicKeys = new List<string>();
                foreach (var job in jobsList)
                {
                    if (job.DynamicProperties != null)
                    {
                        foreach (var key in job.DynamicProperties.Keys)
                        {
                            if (!dynamicKeys.Contains(key))
                            {
                                dynamicKeys.Add(key);
                            }
                        }
                    }
                }

                // Build header row
                var headerRow = new List<object>
                {
                    "Time", "Document Name", "Hold Print Name", "User ID", "Pages", "Copies", "User", "Printer Name", "Status", "WebJobId"
                };
                foreach (var key in dynamicKeys)
                {
                    headerRow.Add(key);
                }

                var sheetData = new List<IList<object>> { headerRow };

                // Build data rows
                foreach (var job in jobsList)
                {
                    var row = new List<object>
                    {
                        job.Timestamp ?? "",
                        job.DocumentName ?? "",
                        job.WebFileName ?? "",
                        job.RicohUserId ?? "",
                        job.TotalPages,
                        job.Copies,
                        job.Owner ?? "",
                        job.PrinterName ?? "",
                        job.Status ?? "",
                        job.WebJobId
                    };

                    foreach (var key in dynamicKeys)
                    {
                        string val = job.DynamicProperties != null && job.DynamicProperties.ContainsKey(key) ? job.DynamicProperties[key] : "";
                        row.Add(val);
                    }

                    sheetData.Add(row);
                }

                // Write data to Google Sheets starting at A1
                await service.WriteDataAsync(sheetName, sheetData);
                System.Diagnostics.Debug.WriteLine($"Google Sheets Sync: Successfully synced {jobsList.Count} jobs to '{sheetName}'.");

                // Clean up old print log sheet tabs older than 7 days (1 week)
                await service.CleanupOldPrintLogsAsync(retentionDays: 7);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Google Sheets Sync Error: " + ex.Message);
            }
        }

        public static async Task SyncBotConfigToGoogleSheetsAsync()
        {
            try
            {
                var settings = SettingsManager.LoadSettings();
                string spreadsheetId = !string.IsNullOrWhiteSpace(settings.PrintLogSpreadsheetId)
                    ? settings.PrintLogSpreadsheetId
                    : settings.GoogleSpreadsheetId;
                if (string.IsNullOrWhiteSpace(spreadsheetId)) return;

                string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintTrackerApp");
                string credentialsPath = Path.Combine(appDataFolder, "google_credentials.json");
                if (!File.Exists(credentialsPath)) return;

                var service = new GoogleSheetsService(spreadsheetId, credentialsPath);
                string sheetName = "BotConfig";

                await service.EnsureSheetExistsAsync(sheetName);
                await service.ClearSheetAsync(sheetName);

                var data = new List<IList<object>>
                {
                    new List<object> { "Key", "Value" },
                    new List<object> { "TelegramBotToken", settings.TelegramBotToken ?? "" },
                    new List<object> { "TelegramTrackingBotToken", settings.TelegramTrackingBotToken ?? "" },
                    new List<object> { "TelegramChatId", settings.TelegramChatId ?? "" },
                    new List<object> { "TeacherName", settings.TeacherName ?? "" },
                    new List<object> { "LastUpdated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
                };


                await service.WriteDataAsync(sheetName, data);
                System.Diagnostics.Debug.WriteLine("Google Sheets Sync: Successfully synced BotConfig.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Google Sheets BotConfig Sync Error: " + ex.Message);
            }
        }
    }
}
