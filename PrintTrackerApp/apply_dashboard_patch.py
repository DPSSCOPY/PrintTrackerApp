import sys
import re

file_path = r"e:\Code\Tracking_Print\PrintTrackerApp\MainWindow.xaml.cs"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

historical_logic = """
                if (_historicalPrintJobs != null)
                {
                    // Update stats based on historical data
                    txtPendingCount.Text = "0"; // Historical data doesn't have "Files Waiting" in a folder

                    var successStatuses = new[] { "Storing Complete", "Complete Print", "Processing", "Print Complete", "Successfully Printed" };
                    var successJobs = _historicalPrintJobs.Where(j => successStatuses.Any(s => (j.Status ?? "").Contains(s, StringComparison.OrdinalIgnoreCase))).ToList();
                    
                    int totalSubCount = successJobs.Count;
                    var uniqueBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    
                    foreach (var job in successJobs)
                    {
                        string name = job.DocumentName ?? "";
                        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                            name = name.Substring(0, name.Length - 4);
                            
                        var match = System.Text.RegularExpressions.Regex.Match(name, @"_(\\d{6})$");
                        if (match.Success) 
                        {
                            name = name.Substring(0, name.Length - 7);
                        }
                        uniqueBaseNames.Add(name);
                    }

                    int actualCount = uniqueBaseNames.Count;
                    int duplicateCount = totalSubCount - actualCount;

                    txtSubfolderCount.Text = actualCount.ToString();
                    txtDuplicateCount.Text = duplicateCount.ToString();

                    txtSentToPrinterCount.Text = _historicalPrintJobs.Count(j => (j.Status ?? "").Contains("Sent to Printer", StringComparison.OrdinalIgnoreCase)).ToString();
                    txtPrintCompleteCount.Text = _historicalPrintJobs.Count(j => (j.Status ?? "").Contains("Print Complete", StringComparison.OrdinalIgnoreCase) || (j.Status ?? "").Contains("Successfully Printed", StringComparison.OrdinalIgnoreCase)).ToString();

                    var errorJobs = _historicalPrintJobs.Where(j => 
                    {
                        var status = j.Status ?? "";
                        bool isSuccessOrActive = successStatuses.Any(s => status.Contains(s, StringComparison.OrdinalIgnoreCase)) 
                            || status.Contains("Sent to Printer", StringComparison.OrdinalIgnoreCase)
                            || status.Contains("Printing", StringComparison.OrdinalIgnoreCase)
                            || status.Contains("Storing", StringComparison.OrdinalIgnoreCase)
                            || status.Contains("Spooling", StringComparison.OrdinalIgnoreCase);
                        return !isSuccessOrActive;
                    }).ToList();

                    txtErrorFilesCount.Text = errorJobs.Count.ToString();
                    return;
                }
"""

# Insert historical_logic at the beginning of UpdateVerificationPanel's Dispatcher block (line 464)
target_find = """        private void UpdateVerificationPanel()
        {
            Dispatcher.Invoke(() =>
            {
                int completedToday = _printJobs.Count(j => j.Status.Contains("Complete", StringComparison.OrdinalIgnoreCase) || j.Status.Contains("Printed", StringComparison.OrdinalIgnoreCase));"""

target_replace = """        private void UpdateVerificationPanel()
        {
            Dispatcher.Invoke(() =>
            {
""" + historical_logic + """
                int completedToday = _printJobs.Count(j => j.Status.Contains("Complete", StringComparison.OrdinalIgnoreCase) || j.Status.Contains("Printed", StringComparison.OrdinalIgnoreCase));"""

if "if (_historicalPrintJobs != null)" not in content:
    content = content.replace(target_find, target_replace)
    with open(file_path, "w", encoding="utf-8") as f:
        f.write(content)
    print("Injected historical UpdateVerificationPanel logic successfully.")
else:
    print("Already injected!")

# ALSO we need to call UpdateVerificationPanel() inside BtnLoadHistoricalLog_Click and BtnResetLiveLog_Click
load_find = """                    // Force refresh filter
                    TxtSearch_TextChanged(null, null);"""
load_replace = """                    // Force refresh filter
                    TxtSearch_TextChanged(null, null);
                    UpdateVerificationPanel();"""

content = content.replace(load_find, load_replace)
with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
print("Added calls to UpdateVerificationPanel.")

