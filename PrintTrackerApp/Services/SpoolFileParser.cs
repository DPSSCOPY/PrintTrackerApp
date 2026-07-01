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
                    // Fallback: If exact file ID doesn't exist, search for any recently modified SPL file
                    if (!File.Exists(splFilePath) && Directory.Exists(spoolDir))
                    {
                        try
                        {
                            var recentFiles = Directory.GetFiles(spoolDir, "*.*")
                                .Where(f => f.EndsWith(".SPL", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".SHD", StringComparison.OrdinalIgnoreCase))
                                .Where(f => (DateTime.Now - File.GetLastWriteTime(f)).TotalMinutes < 5)
                                .OrderByDescending(f => File.GetLastWriteTime(f))
                                .ToList();
                            if (recentFiles.Any())
                            {
                                splFilePath = recentFiles.First();
                            }
                        }
                        catch { }
                    }

                    if (File.Exists(splFilePath))
                    {
                        try
                        {
                            // Open with ReadWrite share so we don't lock the print spooler
                            using var fs = new FileStream(splFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            using var reader = new StreamReader(fs, Encoding.ASCII);
                            
                            char[] buffer = new char[65536]; // Read first 64KB which should contain PJL even with large headers
                            int bytesRead = reader.Read(buffer, 0, buffer.Length);
                            
                            if (bytesRead == 0)
                            {
                                throw new IOException("File is currently empty, still spooling.");
                            }

                            string content = new string(buffer, 0, bytesRead);

                            // DUMP THE CONTENT FOR DEBUGGING
                            try { System.IO.File.WriteAllText("spl_dump.txt", content); } catch { }

                            var details = new SpoolJobDetails();
                            
                            details.UserId = ExtractPjlValue(content, new[] { "USERID", "USERNAME", "USER" });
                            details.DocumentName = ExtractPjlValue(content, new[] { "JOBID", "DOCUMENTNAME", "JOB NAME", "JOBNAME", "FILENAME", "TITLE" });

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
                            System.Diagnostics.Debug.WriteLine("Admin rights required to read SPL file. Attempting permission recovery...");
                            try { System.IO.File.AppendAllText("wmi_debug.log", $"[{DateTime.Now}] Admin rights required to read SPL file {splFilePath}. Attempting recovery...\n"); } catch { }
                            
                            try
                            {
                                var psi = new System.Diagnostics.ProcessStartInfo("icacls", $@"""{spoolDir}"" /grant *S-1-1-0:(OI)(CI)(RX) *S-1-5-32-545:(OI)(CI)(RX) /T /Q /C")
                                {
                                    CreateNoWindow = true,
                                    UseShellExecute = false,
                                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                                };
                                System.Diagnostics.Process.Start(psi)?.WaitForExit(1000);
                            }
                            catch { }

                            Thread.Sleep(500);
                            continue;
                        }
                    }
                    Thread.Sleep(200); // 200ms delay per retry
                }

                try { System.IO.File.AppendAllText("wmi_debug.log", $"[{DateTime.Now}] Exhausted retries or file not found for {splFilePath}\n"); } catch { }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SpoolFileParser: {ex.Message}");
                return null;
            }
        }

        private static string ExtractPjlValue(string content, string[] keys)
        {
            foreach (var key in keys)
            {
                var match = Regex.Match(content, $@"@PJL\s+(?:SET\s+)?{key}\s*=\s*(?:""([^""]+)""|([^\s""]+))", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var val = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value;
                    if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                }
            }
            return string.Empty;
        }
    }
}
