using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ExcelDataReader;
using PrintTrackerApp.Models;

namespace PrintTrackerApp.Services
{
    public static class CsvLogger
    {
        private static readonly Dictionary<string, (DateTime LastWriteTime, List<PrintJobInfo> Jobs)> _csvFileCache = new Dictionary<string, (DateTime, List<PrintJobInfo>)>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _cacheLock = new object();
        public static void ExportJobsToCsv(IEnumerable<PrintJobInfo> jobs, string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            try
            {
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
                string filePath = Path.Combine(folderPath, $"PrintLog_{dateStr}.csv");
                ExportJobsToSpecificCsvFile(jobs, filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error logging to CSV folder: " + ex.Message);
            }
        }

        public static void ExportJobsToSpecificCsvFile(IEnumerable<PrintJobInfo> jobs, string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return;

                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                SafeFileHelper.WriteSafe(filePath, writer =>
                {
                    var jobsList = jobs.Reverse().ToList();
                    
                    // Discover dynamic keys
                    var dynamicKeys = new List<string>();
                    foreach (var job in jobsList)
                    {
                        if (job.DynamicProperties != null)
                        {
                            foreach (var key in job.DynamicProperties.Keys)
                            {
                                if (!dynamicKeys.Contains(key)) dynamicKeys.Add(key);
                            }
                        }
                    }

                    // Write header
                    string header = "Time,Document Name,Hold Print Name,User ID,Pages,Copies,User,Printer Name,Status,WebJobId";
                    if (dynamicKeys.Count > 0)
                    {
                        header += "," + string.Join(",", dynamicKeys.Select(k => EscapeCsv(k)));
                    }
                    writer.WriteLine(header);

                    foreach (var job in jobsList)
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

                        string line = $"{time},{docName},{webFileName},{userId},{pages},{copies},{user},{printer},{status},{webJobId}";
                        
                        foreach (var key in dynamicKeys)
                        {
                            string val = job.DynamicProperties != null && job.DynamicProperties.ContainsKey(key) ? job.DynamicProperties[key] : "";
                            line += $",{EscapeCsv(val)}";
                        }

                        writer.WriteLine(line);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error logging to CSV file: " + ex.Message);
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

                SafeFileHelper.WriteSafe(filePath, writer =>
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
                });
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

                using (var reader = new StreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8))
                {
                    string header = reader.ReadLine();
                    var headerParts = ParseCsvLine(header);
                    var dynamicHeaders = new List<string>();
                    for (int i = 10; i < headerParts.Count; i++)
                    {
                        dynamicHeaders.Add(headerParts[i]);
                    }
                    
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        var parts = ParseCsvLine(line);
                        if (parts.Count >= 9)
                        {
                            var job = new PrintJobInfo
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
                                WebJobId = parts.Count >= 10 && int.TryParse(parts[9], out int wid) ? wid : -1,
                                SourceFilePath = filePath
                            };
                            
                            for (int i = 10; i < parts.Count && (i - 10) < dynamicHeaders.Count; i++)
                            {
                                job.DynamicProperties[dynamicHeaders[i - 10]] = parts[i];
                            }
                            
                            job.CleanDownlevelNames();
                            jobs.Add(job);
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
                    bool parsedDate = DateTime.TryParse(datePart, out DateTime fileDate);
                    if (!parsedDate) parsedDate = DateTime.TryParseExact(datePart, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out fileDate);
                    if (!parsedDate) parsedDate = DateTime.TryParse(datePart, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out fileDate);
                    
                    if (parsedDate)
                    {
                        if (fileDate.Date >= startDate.Date && fileDate.Date <= endDate.Date)
                        {
                            List<PrintJobInfo> fileJobs = null;
                            DateTime currentWriteTime = File.GetLastWriteTime(filePath);

                            lock (_cacheLock)
                            {
                                if (_csvFileCache.TryGetValue(filePath, out var cachedInfo) && cachedInfo.LastWriteTime == currentWriteTime)
                                {
                                    fileJobs = cachedInfo.Jobs;
                                }
                            }

                            if (fileJobs == null)
                            {
                                fileJobs = new List<PrintJobInfo>();
                                using (var reader = new StreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8))
                                {
                                    string header = reader.ReadLine();
                                    if (header != null)
                                    {
                                        var headerParts = ParseCsvLine(header);
                                        var dynamicHeaders = new List<string>();
                                        for (int i = 10; i < headerParts.Count; i++)
                                        {
                                            dynamicHeaders.Add(headerParts[i]);
                                        }

                                        while (!reader.EndOfStream)
                                        {
                                            var line = reader.ReadLine();
                                            if (string.IsNullOrWhiteSpace(line)) continue;

                                            var parts = ParseCsvLine(line);
                                            if (parts.Count >= 9)
                                            {
                                                var job = new PrintJobInfo
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
                                                    WebJobId = parts.Count >= 10 && int.TryParse(parts[9], out int wid) ? wid : -1,
                                                    SourceFilePath = filePath
                                                };
                                                
                                                for (int i = 10; i < parts.Count && (i - 10) < dynamicHeaders.Count; i++)
                                                {
                                                    job.DynamicProperties[dynamicHeaders[i - 10]] = parts[i];
                                                }
                                                
                                                job.CleanDownlevelNames();
                                                fileJobs.Add(job);
                                            }
                                        }
                                    }
                                }

                                lock (_cacheLock)
                                {
                                    _csvFileCache[filePath] = (currentWriteTime, fileJobs);
                                }
                            }

                            allJobs.AddRange(fileJobs);
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

                using (var reader = new StreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8))
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

                if (filePath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) || 
                    filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                    filePath.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase))
                {
                    using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream))
                        {
                            var result = reader.AsDataSet(new ExcelDataReader.ExcelDataSetConfiguration()
                            {
                                ConfigureDataTable = (dataReader) => new ExcelDataReader.ExcelDataTableConfiguration()
                                {
                                    UseHeaderRow = true
                                }
                            });
                            
                            if (result.Tables.Count > 0)
                            {
                                var table = result.Tables[0];
                                
                                int timeIdx = -1, userIdx = -1, pagesIdx = -1, copiesIdx = -1, printerIdx = -1, docNameIdx = -1, statusIdx = -1, ricohIdx = -1;
                                
                                for (int i = 0; i < table.Columns.Count; i++)
                                {
                                    string h = table.Columns[i].ColumnName.Trim();
                                    if (h.Equals("Time", StringComparison.OrdinalIgnoreCase) || h.Equals("Timestamp", StringComparison.OrdinalIgnoreCase)) timeIdx = i;
                                    else if (h.Equals("User", StringComparison.OrdinalIgnoreCase) || h.Equals("Owner", StringComparison.OrdinalIgnoreCase)) userIdx = i;
                                    else if (h.Equals("Pages", StringComparison.OrdinalIgnoreCase) || h.Equals("TotalPages", StringComparison.OrdinalIgnoreCase)) pagesIdx = i;
                                    else if (h.Equals("Copies", StringComparison.OrdinalIgnoreCase)) copiesIdx = i;
                                    else if (h.Equals("Printer Name", StringComparison.OrdinalIgnoreCase) || h.Equals("Printer", StringComparison.OrdinalIgnoreCase)) printerIdx = i;
                                    else if (h.Equals("Document Name", StringComparison.OrdinalIgnoreCase)) docNameIdx = i;
                                    else if (h.Equals("Status", StringComparison.OrdinalIgnoreCase)) statusIdx = i;
                                    else if (h.Equals("User ID (Hold)", StringComparison.OrdinalIgnoreCase) || h.Equals("User ID", StringComparison.OrdinalIgnoreCase) || h.Equals("RicohUserId", StringComparison.OrdinalIgnoreCase)) ricohIdx = i;
                                }

                                foreach (System.Data.DataRow row in table.Rows)
                                {
                                    string time = timeIdx >= 0 ? row[timeIdx]?.ToString() ?? "" : "";
                                    if (string.IsNullOrWhiteSpace(time)) continue; // skip empty rows

                                    string user = userIdx >= 0 ? row[userIdx]?.ToString() ?? "" : "";
                                    string docName = docNameIdx >= 0 ? row[docNameIdx]?.ToString() ?? "" : "";
                                    string printer = printerIdx >= 0 ? row[printerIdx]?.ToString() ?? "" : "";
                                    string status = statusIdx >= 0 ? row[statusIdx]?.ToString() ?? "Unknown" : "Unknown";
                                    string ricohId = ricohIdx >= 0 ? row[ricohIdx]?.ToString() ?? "" : "";
                                    
                                    int pages = 1;
                                    if (pagesIdx >= 0 && int.TryParse(row[pagesIdx]?.ToString(), out int p)) pages = p;
                                    
                                    int copies = 1;
                                    if (copiesIdx >= 0 && int.TryParse(row[copiesIdx]?.ToString(), out int c)) copies = c;

                                    jobs.Add(new PrintJobInfo
                                    {
                                        Timestamp = time,
                                        DocumentName = docName,
                                        RicohUserId = ricohId,
                                        TotalPages = pages,
                                        Copies = copies,
                                        Owner = user,
                                        PrinterName = printer,
                                        Status = status,
                                        JobId = Guid.NewGuid().ToString(),
                                        SourceFilePath = filePath
                                    });
                                }
                            }
                        }
                    }
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
                    int timeIdx = headerParts.FindIndex(h => h.Trim().Equals("Time", StringComparison.OrdinalIgnoreCase) || h.Trim().Equals("Timestamp", StringComparison.OrdinalIgnoreCase));
                    int userIdx = headerParts.FindIndex(h => h.Trim().Equals("User", StringComparison.OrdinalIgnoreCase) || h.Trim().Equals("Owner", StringComparison.OrdinalIgnoreCase));
                    int pagesIdx = headerParts.FindIndex(h => h.Trim().Equals("Pages", StringComparison.OrdinalIgnoreCase) || h.Trim().Equals("TotalPages", StringComparison.OrdinalIgnoreCase));
                    int copiesIdx = headerParts.FindIndex(h => h.Trim().Equals("Copies", StringComparison.OrdinalIgnoreCase));
                    int printerIdx = headerParts.FindIndex(h => h.Trim().Equals("Printer Name", StringComparison.OrdinalIgnoreCase) || h.Trim().Equals("Printer", StringComparison.OrdinalIgnoreCase));
                    int docNameIdx = headerParts.FindIndex(h => h.Trim().Equals("Document Name", StringComparison.OrdinalIgnoreCase));
                    int holdNameIdx = headerParts.FindIndex(h => h.Trim().Equals("Hold Print Name", StringComparison.OrdinalIgnoreCase) || h.Trim().Equals("WebFileName", StringComparison.OrdinalIgnoreCase));
                    int ricohIdIdx = headerParts.FindIndex(h => h.Trim().Equals("User ID (Hold)", StringComparison.OrdinalIgnoreCase) || h.Trim().Equals("User ID", StringComparison.OrdinalIgnoreCase) || h.Trim().Equals("RicohUserId", StringComparison.OrdinalIgnoreCase));
                    int statusIdx = headerParts.FindIndex(h => h.Trim().Equals("Status", StringComparison.OrdinalIgnoreCase));

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

                        var newJob = new PrintJobInfo
                        {
                            Timestamp = GetPart(timeIdx),
                            Owner = GetPart(userIdx),
                            TotalPages = int.TryParse(GetPart(pagesIdx), out int pages) ? pages : 1,
                            Copies = int.TryParse(GetPart(copiesIdx), out int copies) ? copies : 1,
                            PrinterName = GetPart(printerIdx),
                            DocumentName = GetPart(docNameIdx),
                            WebFileName = holdNameIdx >= 0 ? GetPart(holdNameIdx) : GetPart(docNameIdx),
                            RicohUserId = ricohIdIdx >= 0 ? GetPart(ricohIdIdx) : "",
                            Status = statusIdx >= 0 ? GetPart(statusIdx) : "Unknown",
                            JobId = Guid.NewGuid().ToString(),
                            SourceFilePath = filePath
                        };
                        newJob.CleanDownlevelNames();
                        jobs.Add(newJob);
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
