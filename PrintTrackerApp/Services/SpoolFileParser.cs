using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PrintTrackerApp.Services
{
    public class SpoolJobDetails
    {
        public string UserId { get; set; } = string.Empty;
        public string DocumentName { get; set; } = string.Empty;
        public int Copies { get; set; } = 1;
    }

    public static class SpoolFileParser
    {
        public static SpoolJobDetails? Parse(string jobIdStr)
        {
            if (!int.TryParse(jobIdStr, out int jobId))
                return null;

            try
            {
                string spoolDir = @"C:\Windows\System32\spool\PRINTERS";
                string splFilePath = Path.Combine(spoolDir, $"{jobId:D5}.SPL");

                // Wait a bit for the spool file to be written or unlocked
                for (int i = 0; i < 15; i++)
                {
                if (File.Exists(splFilePath))
                {
                    try
                    {
                        // Open with ReadWrite share so we don't lock the print spooler
                        using var fs = new FileStream(splFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(fs, Encoding.ASCII);
                        
                        char[] buffer = new char[8192]; // Read first 8KB which should contain PJL
                        int bytesRead = reader.Read(buffer, 0, buffer.Length);
                        
                        if (bytesRead == 0)
                        {
                            throw new IOException("File is currently empty, still spooling.");
                        }

                        string content = new string(buffer, 0, bytesRead);

                        // DUMP THE CONTENT FOR DEBUGGING
                        System.IO.File.WriteAllText("spl_dump.txt", content);

                        var details = new SpoolJobDetails();
                        
                        // Parse Ricoh PJL 
                        // Example: @PJL SET USERID="6o"
                        var matchUser = Regex.Match(content, @"@PJL\s+SET\s+USERID\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                        if (matchUser.Success) details.UserId = matchUser.Groups[1].Value;

                        // Ricoh actually uses JOBID for the custom File Name (Hold Print Name)!
                        var matchJobId = Regex.Match(content, @"@PJL\s+SET\s+JOBID\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                        if (matchJobId.Success) 
                        {
                            details.DocumentName = matchJobId.Groups[1].Value;
                        }
                        else
                        {
                            // Example: @PJL SET DOCUMENTNAME="harry"
                            var matchDoc = Regex.Match(content, @"@PJL\s+SET\s+DOCUMENTNAME\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                            if (matchDoc.Success) 
                            {
                                details.DocumentName = matchDoc.Groups[1].Value;
                            }
                            else
                            {
                                // Fallback PJL: @PJL JOB NAME="harry"
                                var matchJobName = Regex.Match(content, @"@PJL\s+JOB\s+NAME\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
                                if (matchJobName.Success) details.DocumentName = matchJobName.Groups[1].Value;
                            }
                        }

                        // Extract Copies (Ricoh uses either COPIES or QTY, sometimes both where QTY is the actual collated copies)
                        int finalCopies = 1;
                        
                        var matchCopies = Regex.Match(content, @"@PJL\s+SET\s+COPIES\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                        if (matchCopies.Success && int.TryParse(matchCopies.Groups[1].Value, out int copies))
                        {
                            finalCopies = copies;
                        }

                        var matchQty = Regex.Match(content, @"@PJL\s+SET\s+QTY\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                        if (matchQty.Success && int.TryParse(matchQty.Groups[1].Value, out int qty))
                        {
                            if (qty > finalCopies) finalCopies = qty;
                        }
                        
                        details.Copies = finalCopies;

                        // If we got either piece of info, return it
                        if (!string.IsNullOrEmpty(details.UserId) || !string.IsNullOrEmpty(details.DocumentName))
                            return details;
                            
                        return null;
                    }
                    catch (IOException)
                    {
                        // File locked by spooler, wait and retry
                    }
                    catch (UnauthorizedAccessException)
                    {
                        System.Diagnostics.Debug.WriteLine("Admin rights required to read SPL file.");
                        System.IO.File.AppendAllText("wmi_debug.log", $"[{DateTime.Now}] Admin rights required to read SPL file {splFilePath}\n");
                        return null;
                    }
                }
                Thread.Sleep(200); // 200ms delay per retry
            }

            System.IO.File.AppendAllText("wmi_debug.log", $"[{DateTime.Now}] Exhausted retries or file not found for {splFilePath}\n");
            return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SpoolFileParser: {ex.Message}");
                return null;
            }
        }
    }
}
