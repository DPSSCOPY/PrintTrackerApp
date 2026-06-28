using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PrintTrackerApp.Models;

namespace PrintTrackerApp.Services
{
    public static class CsvLogger
    {
        public static void ExportJobsToCsv(IEnumerable<PrintJobInfo> jobs, string folderPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath))
                    return;

                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
                string filePath = Path.Combine(folderPath, $"PrintLog_{dateStr}.csv");

                // Get today's jobs only, or we can just write everything that is in memory.
                // Assuming _printJobs usually holds recent/today's jobs. Let's write them all to today's file.
                // It's safer to filter by Timestamp starting with today's date, but for simplicity we write all provided.
                
                // Overwrite the file to ensure updates (like Copies and Status) are reflected
                using (var writer = new StreamWriter(filePath, append: false, Encoding.UTF8))
                {
                    // Write header
                    writer.WriteLine("Time,Document Name,Hold Print Name,User ID,Pages,Copies,User,Printer Name,Status");

                    // Write rows (oldest first or newest first? The list is newest first, so let's reverse to append order)
                    foreach (var job in jobs.Reverse())
                    {
                        string time = EscapeCsv(job.Timestamp);
                        string docName = EscapeCsv(job.DocumentName);
                        string webFileName = EscapeCsv(job.WebFileName);
                        string userId = EscapeCsv(job.RicohUserId);
                        string pages = job.TotalPages.ToString();
                        string copies = job.Copies.ToString();
                        string user = EscapeCsv(job.Owner);
                        string printer = EscapeCsv(job.PrinterName);
                        string status = EscapeCsv(job.Status);
                        string webJobId = job.WebJobId.ToString();

                        writer.WriteLine($"{time},{docName},{webFileName},{userId},{pages},{copies},{user},{printer},{status},{webJobId}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error logging to CSV: " + ex.Message);
            }
        }

        private static string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }

        public static void ExportWebMonitorRawToCsv(IEnumerable<string[]> rawJobs, string folderPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath) || rawJobs == null || !rawJobs.Any())
                    return;

                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
                string filePath = Path.Combine(folderPath, $"WebMonitorHistory_{dateStr}.csv");

                // Overwrite mode for dynamic update
                using (var writer = new StreamWriter(filePath, append: false, Encoding.UTF8))
                {
                    writer.WriteLine("Log Time,Job ID,File Name,Status,User ID,Pages,Created At");

                    foreach (var rawJobParts in rawJobs)
                    {
                        // Check if we passed the log time as the 7th element (index 6) or 8th. 
                        // Actually, let's just assume MainWindow appends Log Time at the end (index 6).
                        string logTime = EscapeCsv(rawJobParts.Length > 6 ? rawJobParts[6] : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        string jobId = EscapeCsv(rawJobParts.Length > 0 ? rawJobParts[0] : "");
                        string fileName = EscapeCsv(rawJobParts.Length > 1 ? rawJobParts[1] : "");
                        string status = EscapeCsv(rawJobParts.Length > 2 ? rawJobParts[2] : "");
                        string userId = EscapeCsv(rawJobParts.Length > 3 ? rawJobParts[3] : "");
                        string pages = EscapeCsv(rawJobParts.Length > 4 ? rawJobParts[4] : "");
                        string createdAt = EscapeCsv(rawJobParts.Length > 5 ? rawJobParts[5] : "");

                        writer.WriteLine($"{logTime},{jobId},{fileName},{status},{userId},{pages},{createdAt}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error logging raw web monitor to CSV: " + ex.Message);
            }
        }

        public static List<PrintJobInfo> LoadJobsFromCsv(string folderPath)
        {
            var jobs = new List<PrintJobInfo>();
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath))
                    return jobs;

                string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
                string filePath = Path.Combine(folderPath, $"PrintLog_{dateStr}.csv");

                if (!File.Exists(filePath))
                    return jobs;

                using (var reader = new StreamReader(filePath, Encoding.UTF8))
                {
                    string header = reader.ReadLine(); // skip header
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        var parts = ParseCsvLine(line);
                        if (parts.Count >= 9)
                        {
                            jobs.Add(new PrintJobInfo
                            {
                                Timestamp = parts[0],
                                DocumentName = parts[1],
                                WebFileName = parts[2],
                                RicohUserId = parts[3],
                                TotalPages = int.TryParse(parts[4], out int p) ? p : 1,
                                Copies = int.TryParse(parts[5], out int c) ? c : 1,
                                Owner = parts[6],
                                PrinterName = parts[7],
                                Status = parts[8],
                                JobId = Guid.NewGuid().ToString(),
                                WebJobId = parts.Count >= 10 && int.TryParse(parts[9], out int wid) ? wid : -1
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error loading CSV: " + ex.Message);
            }
            
            // Reverse so newest jobs are at the top, like the DataGrid expects
            jobs.Reverse();
            return jobs;
        }

        public static List<PrintJobInfo> LoadJobsFromCsvForDateRange(string folderPath, DateTime startDate, DateTime endDate)
        {
            var allJobs = new List<PrintJobInfo>();
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                    return allJobs;

                // Ensure time components don't interfere
                startDate = startDate.Date;
                endDate = endDate.Date.AddDays(1).AddTicks(-1);

                var files = Directory.GetFiles(folderPath, "PrintLog_*.csv");
                foreach (var filePath in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    string datePart = fileName.Replace("PrintLog_", "");
                    if (DateTime.TryParse(datePart, out DateTime fileDate))
                    {
                        if (fileDate.Date >= startDate.Date && fileDate.Date <= endDate.Date)
                        {
                            using (var reader = new StreamReader(filePath, Encoding.UTF8))
                            {
                                string header = reader.ReadLine();
                                while (!reader.EndOfStream)
                                {
                                    var line = reader.ReadLine();
                                    if (string.IsNullOrWhiteSpace(line)) continue;

                                    var parts = ParseCsvLine(line);
                                    if (parts.Count >= 9)
                                    {
                                        allJobs.Add(new PrintJobInfo
                                        {
                                            Timestamp = parts[0],
                                            DocumentName = parts[1],
                                            WebFileName = parts[2],
                                            RicohUserId = parts[3],
                                            TotalPages = int.TryParse(parts[4], out int p) ? p : 1,
                                            Copies = int.TryParse(parts[5], out int c) ? c : 1,
                                            Owner = parts[6],
                                            PrinterName = parts[7],
                                            Status = parts[8],
                                            JobId = Guid.NewGuid().ToString(),
                                            WebJobId = parts.Count >= 10 && int.TryParse(parts[9], out int wid) ? wid : -1
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error loading CSV date range: " + ex.Message);
            }

            return allJobs.OrderByDescending(j => j.Timestamp).ToList();
        }

        public static Dictionary<int, string[]> LoadWebMonitorRawFromCsv(string folderPath)
        {
            var dict = new Dictionary<int, string[]>();
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath))
                    return dict;

                string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
                string filePath = Path.Combine(folderPath, $"WebMonitorHistory_{dateStr}.csv");

                if (!File.Exists(filePath))
                    return dict;

                using (var reader = new StreamReader(filePath, Encoding.UTF8))
                {
                    string header = reader.ReadLine(); // skip header
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        var parts = ParseCsvLine(line);
                        if (parts.Count >= 7)
                        {
                            // "Log Time,Job ID,File Name,Status,User ID,Pages,Created At"
                            if (int.TryParse(parts[1], out int jobId))
                            {
                                dict[jobId] = new string[] 
                                {
                                    parts[1], // Job ID
                                    parts[2], // File Name
                                    parts[3], // Status
                                    parts[4], // User ID
                                    parts[5], // Pages
                                    parts[6], // Created At
                                    parts[0]  // Log Time
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error loading WebMonitor CSV: " + ex.Message);
            }
            return dict;
        }

        public static List<PrintJobInfo> LoadExternalCsvLog(string filePath, out string errorMessage)
        {
            errorMessage = "";
            var jobs = new List<PrintJobInfo>();
            try
            {
                if (!File.Exists(filePath)) 
                {
                    errorMessage = "File not found.";
                    return jobs;
                }
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fs, Encoding.UTF8, true))
                {
                    string header = reader.ReadLine() ?? "";
                    string firstLineSample = header;
                    char delimiter = ',';
                    if (!header.Contains(",") && header.Contains(";")) delimiter = ';';
                    else if (!header.Contains(",") && header.Contains("\t")) delimiter = '\t';
                    
                    var headerParts = ParseCsvLine(header, delimiter);
                    int timeIdx = headerParts.FindIndex(h => h.Trim().Equals("Time", StringComparison.OrdinalIgnoreCase));
                    int userIdx = headerParts.FindIndex(h => h.Trim().Equals("User", StringComparison.OrdinalIgnoreCase));
                    int pagesIdx = headerParts.FindIndex(h => h.Trim().Equals("Pages", StringComparison.OrdinalIgnoreCase));
                    int copiesIdx = headerParts.FindIndex(h => h.Trim().Equals("Copies", StringComparison.OrdinalIgnoreCase));
                    int printerIdx = headerParts.FindIndex(h => h.Trim().Equals("Printer", StringComparison.OrdinalIgnoreCase));
                    int docNameIdx = headerParts.FindIndex(h => h.Trim().Equals("Document Name", StringComparison.OrdinalIgnoreCase));

                    // Fallback to hardcoded indices if header is missing or unrecognized
                    if (timeIdx == -1 && docNameIdx == -1)
                    {
                        if (headerParts.Count == 5)
                        {
                            timeIdx = 0; userIdx = -1; pagesIdx = 1; copiesIdx = 2; printerIdx = 3; docNameIdx = 4;
                        }
                        else
                        {
                            timeIdx = 0; userIdx = 1; pagesIdx = 2; copiesIdx = 3; printerIdx = 4; docNameIdx = 5;
                        }
                    }

                    int lineCount = 1;
                    int maxCols = headerParts.Count;
                    
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        lineCount++;
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        if (lineCount == 2) firstLineSample = line;
                        
                        var parts = ParseCsvLine(line, delimiter);
                        if (parts.Count > maxCols) maxCols = parts.Count;
                        
                        if (parts.Count < 2) continue; // Skip totally invalid lines

                        string GetPart(int idx) => (idx >= 0 && idx < parts.Count) ? parts[idx].Trim() : "";

                        jobs.Add(new PrintJobInfo
                        {
                            Timestamp = GetPart(timeIdx),
                            Owner = GetPart(userIdx),
                            TotalPages = int.TryParse(GetPart(pagesIdx), out int pages) ? pages : 1,
                            Copies = int.TryParse(GetPart(copiesIdx), out int copies) ? copies : 1,
                            PrinterName = GetPart(printerIdx),
                            DocumentName = GetPart(docNameIdx),
                            WebFileName = GetPart(docNameIdx)
                        });
                    }
                    
                    if (jobs.Count == 0)
                    {
                        errorMessage = $"Read {lineCount} lines. Max cols found: {maxCols}. Delimiter: '{delimiter}'.\nFirst line: {firstLineSample}";
                    }
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                System.Diagnostics.Debug.WriteLine("Error loading external CSV: " + ex.Message);
            }
            
            // External logs are usually chronological, so reverse to show newest first
            jobs.Reverse();
            return jobs;
        }

        private static List<string> ParseCsvLine(string line, char delimiter = ',')
        {
            var result = new List<string>();
            bool inQuotes = false;
            var currentField = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++; // skip escaped quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
                else
                {
                    currentField.Append(c);
                }
            }
            result.Add(currentField.ToString());
            return result;
        }
    }
}
