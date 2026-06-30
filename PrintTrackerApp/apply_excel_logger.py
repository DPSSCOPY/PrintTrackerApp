import sys

file_path = r"e:\Code\Tracking_Print\PrintTrackerApp\Services\CsvLogger.cs"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

injection_code = """
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
                                        JobId = Guid.NewGuid().ToString()
                                    });
                                }
                            }
                        }
                    }
                    return jobs;
                }

"""

target = """                if (!File.Exists(filePath)) 
                {
                    errorMessage = "File not found.";
                    return jobs;
                }"""

new_target = target + "\n" + injection_code

if target in content:
    content = content.replace(target, new_target)
    with open(file_path, "w", encoding="utf-8") as f:
        f.write(content)
    print("Injected Excel support successfully.")
else:
    print("Could not find target to inject!")
