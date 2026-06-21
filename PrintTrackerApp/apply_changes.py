import re

with open("MainWindow.xaml.cs", "r", encoding="utf-8") as f:
    text = f.read()

# 1. Replace MovePrintedFile method with UpdateFileStatusLocation and FindPhysicalFileForJob and CheckIfAllJobsComplete
new_methods = """
        private void UpdateFileStatusLocation(PrintJobInfo job, string status)
        {
            if (string.IsNullOrWhiteSpace(_appSettings.SourceFolderPath) || !System.IO.Directory.Exists(_appSettings.SourceFolderPath))
                return;

            string targetSubFolder;
            if (status.Contains("Complete", StringComparison.OrdinalIgnoreCase) && !status.Contains("Sorting", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Print Complete";
            }
            else if (status.Contains("Sorting Complete", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Sorting Complete";
            }
            else if (status.Contains("Sent to Printer", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Sent to Printer";
            }
            else
            {
                // Create safe folder name
                targetSubFolder = string.Join("_", status.Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
            }

            string targetFolder = System.IO.Path.Combine(_appSettings.SourceFolderPath, targetSubFolder);
            if (!System.IO.Directory.Exists(targetFolder))
                System.IO.Directory.CreateDirectory(targetFolder);

            string fileToMove = FindPhysicalFileForJob(job);
            if (fileToMove != null)
            {
                string destFile = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(fileToMove));
                if (System.IO.File.Exists(destFile))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(fileToMove);
                    string ext = System.IO.Path.GetExtension(fileToMove);
                    destFile = System.IO.Path.Combine(targetFolder, $"{name}_{DateTime.Now.ToString("HHmmss")}{ext}");
                }

                try
                {
                    System.IO.File.Move(fileToMove, destFile);
                    if (targetSubFolder == "Print Complete")
                    {
                        CheckIfAllJobsComplete();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to move file to {targetSubFolder}: {ex.Message}");
                }
            }
            else if (targetSubFolder == "Print Complete")
            {
                 // Check anyway if they all finished
                 CheckIfAllJobsComplete();
            }
        }

        private string? FindPhysicalFileForJob(PrintJobInfo job)
        {
            string[] searchFolders = { 
                _appSettings.SourceFolderPath,
                System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sent to Printer"),
                System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sorting Complete"),
                System.IO.Path.Combine(_appSettings.SourceFolderPath, "Printing")
            };

            foreach (var folder in searchFolders)
            {
                if (!System.IO.Directory.Exists(folder)) continue;
                foreach (var file in System.IO.Directory.GetFiles(folder, "*.pdf"))
                {
                    AutoPrintService.ParseDynamicFileInfo(System.IO.Path.GetFileName(file), "Default", 1, out string fId, out string fName, out int fCopies);
                    if (string.Equals(fId, job.RicohUserId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(fName, job.WebFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
            }
            return null;
        }

        private void CheckIfAllJobsComplete()
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrWhiteSpace(_appSettings.SourceFolderPath) || !System.IO.Directory.Exists(_appSettings.SourceFolderPath))
                    return;

                string[] activeFolders = { 
                    _appSettings.SourceFolderPath,
                    System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sent to Printer"),
                    System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sorting Complete"),
                    System.IO.Path.Combine(_appSettings.SourceFolderPath, "Printing")
                };

                int activeFilesCount = 0;
                foreach (var folder in activeFolders)
                {
                    if (System.IO.Directory.Exists(folder))
                    {
                        // In root folder, only count pdfs
                        activeFilesCount += System.IO.Directory.GetFiles(folder, "*.pdf").Length;
                    }
                }

                if (activeFilesCount == 0 && _printJobs.Count > 0)
                {
                    // Everything is complete!
                    System.Windows.MessageBox.Show("All print jobs are completed!", "Print Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Clear the print jobs so it doesn't pop up again until new jobs arrive
                    _printJobs.Clear();
                }
            });
        }
"""
# Replace MovePrintedFile
text = re.sub(r'private void MovePrintedFile.*?        }', new_methods, text, flags=re.DOTALL)


# 2. Replace the old MovePrintedFile call in WebMonitor_OnScrapedStatusReceived
old_web_monitor_block = """                                if (oldStatus != status && (status.Contains("Complete", StringComparison.OrdinalIgnoreCase) || status.Contains("Printed", StringComparison.OrdinalIgnoreCase)) && !status.Contains("Storing", StringComparison.OrdinalIgnoreCase))
                                {
                                    ShowNotification("Print Complete", $"File: {matchedJob.WebFileName}\\nUser: {matchedJob.Owner}");
                                    MovePrintedFile(matchedJob.DocumentName, matchedJob.WebFileName);
                                }"""

new_web_monitor_block = """                                if (oldStatus != status)
                                {
                                    if ((status.Contains("Complete", StringComparison.OrdinalIgnoreCase) || status.Contains("Printed", StringComparison.OrdinalIgnoreCase)) && !status.Contains("Storing", StringComparison.OrdinalIgnoreCase))
                                    {
                                        ShowNotification("Print Complete", $"File: {matchedJob.WebFileName}\\nUser: {matchedJob.Owner}");
                                    }
                                    UpdateFileStatusLocation(matchedJob, status);
                                }"""
text = text.replace(old_web_monitor_block, new_web_monitor_block)

# 3. Replace the old MovePrintedFile call in StatusTimer_Tick
old_timer_block = """            var completedJobs = _printJobs.Where(j => j.Status != null && (j.Status.Contains("Complete", StringComparison.OrdinalIgnoreCase) || j.Status.Contains("Printed", StringComparison.OrdinalIgnoreCase)) && !j.Status.Contains("Storing", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var job in completedJobs)
            {
                MovePrintedFile(job.DocumentName, job.WebFileName);
            }"""

new_timer_block = """            var activeJobs = _printJobs.ToList();
            foreach (var job in activeJobs)
            {
                if (!string.IsNullOrEmpty(job.Status))
                {
                    UpdateFileStatusLocation(job, job.Status);
                }
            }"""
text = text.replace(old_timer_block, new_timer_block)


# 4. Remove duplicate MovePrintedFile near line 280 (in SpoolerMonitor_OnJobDeleted if it's there)
# It was actually:
#                MovePrintedFile(job.DocumentName, job.WebFileName);
#            }
#        }
#
#        private async Task UpdatePrinterStatusAsync()
text = re.sub(r'MovePrintedFile\(job\.DocumentName, job\.WebFileName\);', r'// MovePrintedFile removed', text)

with open("MainWindow.xaml.cs", "w", encoding="utf-8") as f:
    f.write(text)
