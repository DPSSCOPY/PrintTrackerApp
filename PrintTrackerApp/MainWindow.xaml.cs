using System.IO;
using System.Windows.Automation;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
using PrintTrackerApp.Models;
using PrintTrackerApp.Services;

namespace PrintTrackerApp
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<PrintJobInfo> _printJobs;
        private readonly SnmpTracker _snmpTracker;
        private readonly PrintSpoolerMonitor _spoolerMonitor;
        private readonly DispatcherTimer _statusTimer;
        private readonly WebMonitorWindow _webMonitorWindow;
        private int _lastPageCount = -1;
        private PrintJobInfo? _currentActiveJob = null;
        private AppSettings _appSettings;
        private string _currentDate = DateTime.Now.ToString("yyyy-MM-dd");
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private bool _hasStartedPrinting = false;

        private readonly AutoPrintService _autoPrintService;
        
        private DispatcherTimer _runTimer;
        private Stopwatch _runStopwatch;

        public MainWindow()
        {
            _autoPrintService = new AutoPrintService();
            _autoPrintService.StatusChanged += AutoPrintService_StatusChanged;
            _autoPrintService.FileProcessingStarted += AutoPrintService_FileProcessingStarted;
            _autoPrintService.QueueEmpty += AutoPrintService_QueueEmpty;
            _autoPrintService.OnRequestUniqueUserId = GenerateUniqueUserId;
            
            InitializeComponent();
            _appSettings = SettingsManager.LoadSettings();

            _runTimer = new DispatcherTimer();
            _runTimer.Interval = TimeSpan.FromSeconds(1);
            _runTimer.Tick += RunTimer_Tick;
            _runStopwatch = new Stopwatch();

            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            _notifyIcon.Visible = true;

            _printJobs = new ObservableCollection<PrintJobInfo>();
            dgPrintJobs.ItemsSource = _printJobs;

            _snmpTracker = new SnmpTracker(_appSettings.PrinterIp);
            _spoolerMonitor = new PrintSpoolerMonitor(_appSettings.PrinterName);

            // Timer to poll printer status every 2 seconds
            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5) // Increased from 2 to 5 seconds to prevent timeout overlap
            };
            _statusTimer.Tick += StatusTimer_Tick;

            // Initialize background Web Monitor
            _webMonitorWindow = new WebMonitorWindow(_appSettings.PrinterIp, _appSettings.RefreshIntervalSeconds);
            _webMonitorWindow.OnScrapedStatusReceived += WebMonitor_OnScrapedStatusReceived;
        }

        private string GenerateUniqueUserId(string baseUserId, string holdName)
        {
            string uniqueId = baseUserId;
            int counter = 1;
            
            while (true)
            {
                bool isUsed = false;
                Dispatcher.Invoke(() => 
                {
                    isUsed = _printJobs.Any(j => 
                        string.Equals(j.RicohUserId, uniqueId, StringComparison.OrdinalIgnoreCase) && 
                        string.Equals(j.WebFileName, holdName, StringComparison.OrdinalIgnoreCase));
                });

                if (!isUsed)
                    break;
                
                string suffix = $"{counter}";
                if (baseUserId.Length + suffix.Length > 8)
                {
                    int allowedBaseLen = 8 - suffix.Length;
                    uniqueId = baseUserId.Substring(0, allowedBaseLen) + suffix;
                }
                else
                {
                    uniqueId = baseUserId + suffix;
                }
                counter++;
            }
            
            return uniqueId;
        }

        private void ShowNotification(string title, string message)
        {
            Dispatcher.Invoke(() => 
            {
                _notifyIcon.BalloonTipTitle = title;
                _notifyIcon.BalloonTipText = message;
                _notifyIcon.ShowBalloonTip(3000);
            });
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Load today's history from CSV
            var loadedJobs = CsvLogger.LoadJobsFromCsv(_appSettings.CsvExportPath);
            foreach (var job in loadedJobs)
            {
                _printJobs.Add(job);
            }

            txtFolderPath.Text = string.IsNullOrWhiteSpace(_appSettings.SourceFolderPath) ? "Not configured" : _appSettings.SourceFolderPath;
            UpdateVerificationPanel();

            txtWatchFolder.Text = _appSettings.SourceFolderPath;
            txtFoxitPath.Text = _appSettings.FoxitPath ?? "";
            UpdateStatusUI();
        
            
            if (_appSettings.Notifications.Any())
            {
                elNewNotification.Visibility = Visibility.Visible;
            }

            // Get initial page count
            Task.Run(() =>
            {
                int count = _snmpTracker.GetTotalPageCount();
                if (count > 0) _lastPageCount = count;
                
                Dispatcher.Invoke(() => 
                {
                    txtPageCount.Text = _lastPageCount > 0 ? _lastPageCount.ToString() : "Error";
                });
            });

            // Start Spooler Monitor
            _spoolerMonitor.OnJobCreated += SpoolerMonitor_OnJobCreated;
            _spoolerMonitor.OnJobDeleted += SpoolerMonitor_OnJobDeleted;
            _spoolerMonitor.OnJobPagesUpdated += SpoolerMonitor_OnJobPagesUpdated;
            _spoolerMonitor.Start();

            // Start SNMP Polling
            _statusTimer.Start();
        }

        private void SpoolerMonitor_OnJobCreated(object? sender, PrintJobInfo job)
        {
            Dispatcher.Invoke(() =>
            {
                _printJobs.Insert(0, job);
                job.Status = "Sent to Printer";
                
                if (_currentActiveJob == null)
                {
                    _currentActiveJob = job;
                }
                
                CsvLogger.ExportJobsToCsv(_printJobs, _appSettings.CsvExportPath);
            });
        }

        private void SpoolerMonitor_OnJobDeleted(object? sender, string jobId)
        {
            // The job was deleted from the PC's print spooler.
            // Per user request, DO NOT update the status to "Successfully Printed" based on the PC queue.
            // We strictly rely on the Web Monitor to provide the "Print Complete" status.
        }

        private void SpoolerMonitor_OnJobPagesUpdated(object? sender, (string JobId, int TotalPages) e)
        {
            Dispatcher.Invoke(() =>
            {
                var job = _printJobs.FirstOrDefault(j => j.JobId == e.JobId);
                if (job != null) 
                {
                    if (e.TotalPages > job.TotalPages)
                    {
                        job.TotalPages = e.TotalPages;
                        CsvLogger.ExportJobsToCsv(_printJobs, _appSettings.CsvExportPath);
                    }
                }
            });
        }

        private async void StatusTimer_Tick(object? sender, EventArgs e)
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (_currentDate != today)
            {
                _currentDate = today;
                _printJobs.Clear();
                _maxScrapedId = -1; // Reset web scraper tracking
            }

            await UpdatePrinterStatusAsync();
            
            // Continuously sweep for any completed jobs that might have failed to move (e.g. file was locked by PDF reader)
            // This runs every few seconds so it will automatically move them as soon as the user closes the PDF reader.
            var activeJobs = _printJobs.ToList();
            foreach (var job in activeJobs)
            {
                if (!string.IsNullOrEmpty(job.Status))
                {
                    UpdateFileStatusLocation(job, job.Status);
                }
            }

            UpdateVerificationPanel();
        
        }

        private void UpdateVerificationPanel()
        {
            Dispatcher.Invoke(() =>
            {
                int completedToday = _printJobs.Count(j => j.Status.Contains("Complete", StringComparison.OrdinalIgnoreCase) || j.Status.Contains("Printed", StringComparison.OrdinalIgnoreCase));
                txtCompletedCount.Text = completedToday.ToString();

                if (string.IsNullOrWhiteSpace(_appSettings.SourceFolderPath) || !System.IO.Directory.Exists(_appSettings.SourceFolderPath))
                {
                    txtPendingCount.Text = "0";
                    return;
                }

                try
                {
                    // Count all files in the source folder, excluding the Complete Print subfolder
                    var files = System.IO.Directory.GetFiles(_appSettings.SourceFolderPath);
                    int currentCount = files.Length;
                    txtPendingCount.Text = currentCount.ToString();

                    if (currentCount > 0)
                    {
                        _hasStartedPrinting = true;
                    }
                    else if (currentCount == 0 && _hasStartedPrinting)
                    {
                        // Transitioned from >0 to 0
                        _hasStartedPrinting = false;
                        
                        // Add notification history
                        var notification = new AppNotification
                        {
                            Title = "All Files Printed",
                            Message = "All files in your folder have been successfully printed and moved to Complete Print."
                        };
                        _appSettings.Notifications.Insert(0, notification);
                        SettingsManager.SaveSettings(_appSettings);
                        elNewNotification.Visibility = Visibility.Visible;

                        // Show Popup
                        var popup = new NotificationPopupWindow(notification.Message);
                        popup.Show();
                    }
                }
                catch
                {
                    txtPendingCount.Text = "Err";
                }
            });
        }

        private void BtnNotifications_Click(object sender, RoutedEventArgs e)
        {
            elNewNotification.Visibility = Visibility.Collapsed;
            var historyWindow = new NotificationsHistoryWindow(_appSettings);
            historyWindow.Owner = this;
            historyWindow.ShowDialog();
            
            // Recheck if empty
            if (_appSettings.Notifications.Any())
                elNewNotification.Visibility = Visibility.Visible;
        }

        private void BtnOpenSourceFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_appSettings.SourceFolderPath) && System.IO.Directory.Exists(_appSettings.SourceFolderPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = _appSettings.SourceFolderPath,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            txtPrinterStatus.Text = "Refreshing...";
            txtPrinterStatus.Foreground = System.Windows.Media.Brushes.Orange;
            await UpdatePrinterStatusAsync();
            
            // Also refresh page count manually
            int newCount = await Task.Run(() => _snmpTracker.GetTotalPageCount());
            if (newCount > 0)
            {
                _lastPageCount = newCount;
                txtPageCount.Text = newCount.ToString();
            }
            else
            {
                txtPageCount.Text = "Error/Offline";
            }

            // Also run a sweep for any completed jobs that might have failed to move (e.g. file was locked by PDF reader)
            foreach (var job in _printJobs.Where(j => j.Status.Contains("Complete", StringComparison.OrdinalIgnoreCase) && !j.Status.Contains("Storing", StringComparison.OrdinalIgnoreCase)))
            {
                // MovePrintedFile removed
            }
        }

        private async Task UpdatePrinterStatusAsync()
        {
            // Get Status in background from PC Print Queue instead of SNMP
            string status = await Task.Run(() => _spoolerMonitor.GetPrinterStatus());
            txtPrinterStatus.Text = status;

            // Adjust color based on status
            txtPrinterStatus.Foreground = status switch
            {
                "Idle" => System.Windows.Media.Brushes.Green,
                "Printing" => System.Windows.Media.Brushes.Blue,
                "Offline" => System.Windows.Media.Brushes.Gray,
                _ => System.Windows.Media.Brushes.Red
            };

            // We no longer rely on status == "Idle" to update job status.
            // SpoolerMonitor_OnJobDeleted will handle updating the job status precisely when it finishes.
        }
        private int _maxScrapedId = -1;

        private void WebMonitor_OnScrapedStatusReceived(object? sender, string data)
        {
            Dispatcher.Invoke(() => 
            {
                // data contains "ID|FileName|Status;ID|FileName|Status;..."
                string[] jobs = data.Split(';', StringSplitOptions.RemoveEmptyEntries);
                
                // Initialize _maxScrapedId on first scrape, but DO NOT return. We want to process it!
                if (_maxScrapedId == -1 && jobs.Length > 0)
                {
                    foreach (var jobStr in jobs)
                    {
                        string[] parts = jobStr.Split('|');
                        if (parts.Length >= 3 && int.TryParse(parts[0], out int id))
                        {
                            if (id > _maxScrapedId) _maxScrapedId = id;
                        }
                    }
                }

                // Reverse so we process oldest jobs first (from bottom of the top-10 list to top)
                Array.Reverse(jobs);
                
                foreach (var jobStr in jobs)
                {
                    string[] parts = jobStr.Split('|');
                    if (parts.Length >= 5 && int.TryParse(parts[0], out int jobId))
                    {
                        string webFileName = parts[1];
                        string status = parts[2];
                        string userId = parts[3];
                        int.TryParse(parts[4], out int webPages);
                        string createdAt = parts.Length >= 6 ? parts[5] : "";

                        // 1. Check if we ALREADY linked this exact Web Monitor Job ID to an internal print job
                        var matchedJob = _printJobs.FirstOrDefault(j => j.WebJobId == jobId);
                        
                        // 2. If not found by exact Job ID, but it's a "Complete" job, it might be a Hold Print that just got printed!
                        // When a Hold Print is printed, the printer creates a NEW Job ID. We must find the original "Storing Complete" job.
                        if (matchedJob == null && (status.Contains("Complete") || status.Contains("Printed")))
                        {
                            // Prioritize finding an UNCLAIMED job first
                            matchedJob = _printJobs.LastOrDefault(j => 
                                j.WebJobId == -1 &&
                                string.Equals(j.RicohUserId?.Trim(), userId?.Trim(), StringComparison.OrdinalIgnoreCase) && 
                                string.Equals(j.WebFileName?.Trim(), webFileName?.Trim(), StringComparison.OrdinalIgnoreCase) && 
                                j.TotalPages == webPages &&
                                IsTimeMatch(j.Timestamp, createdAt));
                                
                            // If no unclaimed job, find an already claimed one that hasn't finished printing yet
                            if (matchedJob == null)
                            {
                                matchedJob = _printJobs.LastOrDefault(j => 
                                    string.Equals(j.RicohUserId?.Trim(), userId?.Trim(), StringComparison.OrdinalIgnoreCase) && 
                                    (string.Equals(j.WebFileName?.Trim(), webFileName?.Trim(), StringComparison.OrdinalIgnoreCase) || IsFileNameMatch(j.WebFileName ?? "", webFileName) || IsFileNameMatch(j.DocumentName ?? "", webFileName)) && 
                                    j.Status != "Print Complete" && j.Status != "Successfully Printed" &&
                                    IsTimeMatch(j.Timestamp, createdAt));
                            }
                        }
                        
                        // 3. If STILL not found, and it's a completely NEW job ID OR we are in the first batch processing old loaded jobs
                        if (matchedJob == null)
                        {
                            // 1. Strong Exact Match using the parsed WebFileName and UserId from PC!
                            matchedJob = _printJobs.FirstOrDefault(j => 
                                j.WebJobId == -1 && 
                                string.Equals(j.WebFileName?.Trim(), webFileName?.Trim(), StringComparison.OrdinalIgnoreCase) && 
                                string.Equals(j.RicohUserId?.Trim(), userId?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                                IsTimeMatch(j.Timestamp, createdAt));

                            // 2. Try to find an unlinked job by fuzzy File Name Match (if strong match fails)
                            if (matchedJob == null)
                            {
                                matchedJob = _printJobs.FirstOrDefault(j => j.WebJobId == -1 && IsFileNameMatch(j.DocumentName, webFileName) && IsTimeMatch(j.Timestamp, createdAt));
                            }
                            
                            // 3. Fallback: Take the OLDEST unlinked job (FIFO)
                            // ONLY DO THIS IF IT'S A NEW JOB (jobId > _maxScrapedId), to prevent linking an old unrelated PC job during first batch
                            if (matchedJob == null && jobId > _maxScrapedId) 
                            {
                                matchedJob = _printJobs.LastOrDefault(j => j.WebJobId == -1);
                            }
                            
                            // 4. If we STILL couldn't find a matching local job, ignore it per user request.
                            if (matchedJob == null)
                            {
                                if (jobId > _maxScrapedId) _maxScrapedId = jobId;
                                continue;
                            }
                        }

                        // If we matched an internal job, link the Web Monitor ID and update status!
                        if (matchedJob != null)
                        {
                                matchedJob.WebJobId = jobId; // VERY IMPORTANT: Link it so it doesn't get matched again as unclaimed!
                                
                                string oldStatus = matchedJob.Status;
                                matchedJob.Status = status;
                                
                                if (oldStatus != status)
                                {
                                    if ((status.Contains("Complete", StringComparison.OrdinalIgnoreCase) || status.Contains("Printed", StringComparison.OrdinalIgnoreCase)) && !status.Contains("Storing", StringComparison.OrdinalIgnoreCase))
                                    {
                                        ShowNotification("Print Complete", $"File: {matchedJob.WebFileName}\nUser: {matchedJob.Owner}");
                                    }
                                    UpdateFileStatusLocation(matchedJob, status);
                                }
                                
                                if (jobId > _maxScrapedId) _maxScrapedId = jobId; // Update the max seen
                                
                                if (status.Contains("Complete") || status.Contains("Printed"))
                                {
                                    // Update Page Count
                                    Task.Run(() => 
                                    {
                                        int count = _snmpTracker.GetTotalPageCount();
                                        if (count > 0)
                                        {
                                            Dispatcher.Invoke(() => { txtPageCount.Text = count.ToString(); _lastPageCount = count; });
                                        }
                                    });
                                }
                                
                                CsvLogger.ExportJobsToCsv(_printJobs, _appSettings.CsvExportPath);
                            }
                        }
                    }
            });
        }

        private bool IsTimeMatch(string pcTimestamp, string webTimestamp)
        {
            if (string.IsNullOrWhiteSpace(pcTimestamp) || string.IsNullOrWhiteSpace(webTimestamp))
                return true;

            bool parsedPc = DateTime.TryParseExact(pcTimestamp, "yyyy-MM-dd HH:mm:ss", 
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime pcTime);
            
            if (!parsedPc)
                parsedPc = DateTime.TryParse(pcTimestamp, out pcTime);

            bool parsedWeb = DateTime.TryParse(webTimestamp, out DateTime webTime);
            if (!parsedWeb)
                parsedWeb = DateTime.TryParse(webTimestamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out webTime);

            if (parsedPc && parsedWeb)
            {
                TimeSpan diff = webTime - pcTime;
                if (diff.TotalMinutes < -5)
                    return false;
                    
                return true;
            }

            if (parsedPc && !parsedWeb)
            {
                var match = System.Text.RegularExpressions.Regex.Match(webTimestamp, @"(\d{1,2}):(\d{2})(?::(\d{2}))?\s*(AM|PM|am|pm)?");
                if (match.Success)
                {
                    if (DateTime.TryParse(match.Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime extractedTime))
                    {
                        TimeSpan diff = extractedTime.TimeOfDay - pcTime.TimeOfDay;
                        
                        if (diff.TotalHours < -12) diff += TimeSpan.FromHours(24);
                        else if (diff.TotalHours > 12) diff -= TimeSpan.FromHours(24);
                        
                        if (diff.TotalMinutes < -5)
                            return false;
                    }
                }
            }

            return true;
        }

        private bool IsFileNameMatch(string pcDocName, string webFileName)
        {
            if (string.Equals(pcDocName, webFileName, StringComparison.OrdinalIgnoreCase)) return true;

            // Remove all '?' (which Ricoh uses for unicode) and check if remaining parts match
            string cleanWebName = webFileName.Replace("?", "").Trim();
            if (cleanWebName.Length >= 3 && pcDocName.IndexOf(cleanWebName, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            // Remove extensions and check prefix
            string pcNameNoExt = System.IO.Path.GetFileNameWithoutExtension(pcDocName);
            string webNameNoExt = System.IO.Path.GetFileNameWithoutExtension(webFileName);
            
            int matchCount = 0;
            for(int i=0; i<Math.Min(pcNameNoExt.Length, webNameNoExt.Length); i++)
            {
                if (webNameNoExt[i] == '?') continue;
                if (char.ToLower(pcNameNoExt[i]) == char.ToLower(webNameNoExt[i])) matchCount++;
                else break;
            }
            if (matchCount >= 4) return true;

            return false;
        }

        private void BtnOpenWebMonitor_Click(object sender, RoutedEventArgs e)
        {
            _webMonitorWindow.Show();
            _webMonitorWindow.Activate();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var sw = new SettingsWindow(_appSettings);
            if (sw.ShowDialog() == true)
            {
                // If CSV path changed, maybe we should export existing to new path? 
                // Let's just save settings and prompt restart.
                bool needRestart = _appSettings.PrinterIp != sw.CurrentSettings.PrinterIp || 
                                   _appSettings.PrinterName != sw.CurrentSettings.PrinterName ||
                                   _appSettings.RefreshIntervalSeconds != sw.CurrentSettings.RefreshIntervalSeconds;
                
                string oldPath = _appSettings.CsvExportPath;
                _appSettings = sw.CurrentSettings;
                SettingsManager.SaveSettings(_appSettings);

                txtFolderPath.Text = string.IsNullOrWhiteSpace(_appSettings.SourceFolderPath) ? "Not configured" : _appSettings.SourceFolderPath;
                UpdateVerificationPanel();
        

                if (oldPath != _appSettings.CsvExportPath)
                {
                    CsvLogger.ExportJobsToCsv(_printJobs, _appSettings.CsvExportPath);
                }

                if (needRestart)
                {
                    System.Windows.MessageBox.Show("Settings saved. Please restart the application for the IP/Printer Name changes to take effect.", "Restart Required", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        
        private void UpdateFileStatusLocation(PrintJobInfo job, string status)
        {
            if (string.IsNullOrWhiteSpace(_appSettings.SourceFolderPath) || !System.IO.Directory.Exists(_appSettings.SourceFolderPath))
                return;

            string targetSubFolder;
            if (status.Contains("Complete", StringComparison.OrdinalIgnoreCase) && !status.Contains("Sorting", StringComparison.OrdinalIgnoreCase) && !status.Contains("Storing", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Print Complete";
            }
            else if (status.Contains("Sorting Complete", StringComparison.OrdinalIgnoreCase) || status.Contains("Storing Complete", StringComparison.OrdinalIgnoreCase))
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
                // Prevent moving file over itself and appending time repeatedly
                string currentFolder = System.IO.Path.GetDirectoryName(fileToMove);
                if (string.Equals(currentFolder, targetFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

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
                System.IO.Path.Combine(_appSettings.SourceFolderPath, "Storing Complete"),
                System.IO.Path.Combine(_appSettings.SourceFolderPath, "Printing")
            };

            // Only use exact match by document name to prevent moving unrelated files
            foreach (var folder in searchFolders)
            {
                if (!System.IO.Directory.Exists(folder)) continue;
                foreach (var file in System.IO.Directory.GetFiles(folder, "*.pdf"))
                {
                    string fileName = System.IO.Path.GetFileName(file);
                    string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(file);

                    if (string.Equals(fileName, job.DocumentName, StringComparison.OrdinalIgnoreCase) ||
                        job.DocumentName.IndexOf(fileNameWithoutExt, StringComparison.OrdinalIgnoreCase) >= 0)
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
                    System.IO.Path.Combine(_appSettings.SourceFolderPath, "Storing Complete"),
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
        private void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            // For testing the UI
            var testJob = new PrintJobInfo
            {
                DocumentName = "Test Document.docx",
                Owner = "One Gears",
                TotalPages = 2,
                Status = "Sent to Printer"
            };
            _printJobs.Insert(0, testJob);
            if (_currentActiveJob == null) _currentActiveJob = testJob;
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void BtnBrowseWatchFolder_Click(object sender, RoutedEventArgs e)
        {
            // Use standard Windows Forms FolderBrowserDialog
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select Folder to Watch for PDFs";
                dialog.SelectedPath = txtWatchFolder.Text;
                
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtWatchFolder.Text = dialog.SelectedPath;
                }
            }
        }

        private void BtnBrowseFoxit_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
            openFileDialog.Title = "Select Foxit PDF Reader Executable";
            
            if (!string.IsNullOrEmpty(txtFoxitPath.Text) && File.Exists(txtFoxitPath.Text))
            {
                openFileDialog.InitialDirectory = Path.GetDirectoryName(txtFoxitPath.Text);
            }
            else
            {
                openFileDialog.InitialDirectory = @"C:\Program Files (x86)\Foxit Software\Foxit PDF Reader";
            }

            if (openFileDialog.ShowDialog() == true)
            {
                txtFoxitPath.Text = openFileDialog.FileName;
            }
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _appSettings.SourceFolderPath = txtWatchFolder.Text;
            _appSettings.FoxitPath = txtFoxitPath.Text;
            SettingsManager.SaveSettings(_appSettings);
            System.Windows.MessageBox.Show("Settings saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnStartAutoPrint_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtWatchFolder.Text) || !Directory.Exists(txtWatchFolder.Text))
            {
                System.Windows.MessageBox.Show("Please select a valid folder to watch.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            

            // Ensure settings are saved before starting
            BtnSaveSettings_Click(null, null);

            _autoPrintService.Start(txtWatchFolder.Text, _appSettings.FoxitPath, _appSettings.HoldPrintUserId, _appSettings.AutoPrintCopies, _appSettings.FoxitWindowStyle, _appSettings.DelayBetweenPrints);
            
            _runStopwatch.Restart();
            _runTimer.Start();
            borderRunTimer.Visibility = Visibility.Visible;
            
            UpdateStatusUI();
        }

        private void RunTimer_Tick(object? sender, EventArgs e)
        {
            txtRunTimer.Text = _runStopwatch.Elapsed.ToString(@"hh\:mm\:ss");
        }

        private void BtnPauseAutoPrint_Click(object sender, RoutedEventArgs e)
        {
            if (_autoPrintService == null || !_autoPrintService.IsRunning) return;

            _autoPrintService.IsPaused = !_autoPrintService.IsPaused;
            UpdateStatusUI();
            
            if (_autoPrintService.IsPaused)
            {
                _runStopwatch.Stop();
                AutoPrintService_StatusChanged(this, "Paused");
            }
            else
            {
                _runStopwatch.Start();
                AutoPrintService_StatusChanged(this, "Running");
            }
        }

        private void BtnStopAutoPrint_Click(object sender, RoutedEventArgs e)
        {
            _runStopwatch.Stop();
            _runTimer.Stop();
            _autoPrintService.Stop();
            UpdateStatusUI();
        }

        private void AutoPrintService_QueueEmpty(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                _runStopwatch.Stop();
                _runTimer.Stop();
                _autoPrintService.Stop();
                UpdateStatusUI();
                System.Windows.MessageBox.Show("Auto Print has finished processing all files in the folder and is now stopped.", "Auto Stop", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        private void AutoPrintService_StatusChanged(object sender, string status)
        {
            Dispatcher.Invoke(() =>
            {
                txtAutoPrintStatus.Text = status;
                if (status == "Running")
                {
                    txtAutoPrintStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981")); // Green
                }
                else
                {
                    txtAutoPrintStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444")); // Red
                    txtCurrentFile.Text = "None";
                }
                UpdateStatusUI();
            });
        }

        private void AutoPrintService_FileProcessingStarted(object sender, string fileName)
        {
            Dispatcher.Invoke(() =>
            {
                txtCurrentFile.Text = fileName;
            });
        }

        private void UpdateStatusUI()
        {
            bool isRunning = _autoPrintService != null && _autoPrintService.IsRunning;
            bool isPaused = _autoPrintService != null && _autoPrintService.IsPaused;
            
            btnStartAutoPrint.IsEnabled = !isRunning;
            btnStopAutoPrint.IsEnabled = isRunning;
            btnPauseAutoPrint.IsEnabled = isRunning;
            
            if (isPaused)
            {
                btnPauseAutoPrint.Content = "RESUME";
                btnPauseAutoPrint.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B")); // Amber
                btnPauseAutoPrint.Foreground = System.Windows.Media.Brushes.White;
            }
            else
            {
                btnPauseAutoPrint.Content = "PAUSE";
                btnPauseAutoPrint.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F3F4F6")); // Default secondary
                btnPauseAutoPrint.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1F2937"));
            }
            
            txtWatchFolder.IsEnabled = !isRunning;
            txtFoxitPath.IsEnabled = !isRunning;
            
            if (!isRunning)
            {
                txtAutoPrintStatus.Text = "Stopped";
                txtAutoPrintStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444"));
            }
        }
        
        
private void BtnInspectUI_Click(object sender, RoutedEventArgs e)
        {
            var inspector = new UIInspectorWindow();
            inspector.Owner = this;
            inspector.Show();
        }

        // --- Manual Testing Methods ---

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private void BtnTest_CtrlP_Click(object sender, RoutedEventArgs e)
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("FoxitPDFReader");
            if (processes.Length == 0)
            {
                processes = System.Diagnostics.Process.GetProcessesByName("FoxitPDFEditor");
            }
            
            if (processes.Length > 0 && processes[0].MainWindowHandle != IntPtr.Zero)
            {
                IntPtr handle = processes[0].MainWindowHandle;
                SetForegroundWindow(handle);
                Thread.Sleep(500);
                KeyboardHelper.SendCtrlP();
            }
            else
            {
                System.Windows.MessageBox.Show("Could not find open Foxit window.");
            }
        }

        private string GetFoxitFileName()
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("FoxitPDFReader");
            if (processes.Length == 0) processes = System.Diagnostics.Process.GetProcessesByName("FoxitPDFEditor");
            if (processes.Length == 0) return "";
            
            var process = processes[0];
            string title = process.MainWindowTitle;
            if (title.Contains(" - Foxit")) return title;
            
            var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, 
                new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id), 
                                 new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)));
            
            foreach (AutomationElement window in windows) {
                if (window.Current.Name.Contains(" - Foxit")) return window.Current.Name;
            }
            return title; // fallback
        }

        private void BtnTest_SetCopies_Click(object sender, RoutedEventArgs e)
        {
            AutomationElement foxitWindow = GetFoxitWindow();
            if (foxitWindow == null) {
                System.Windows.MessageBox.Show("Could not find Foxit window.");
                return;
            }

            string windowTitle = GetFoxitFileName();
            string rawFileName = windowTitle;
            if (rawFileName.Contains(" - Foxit")) {
                rawFileName = rawFileName.Substring(0, rawFileName.IndexOf(" - Foxit"));
            }
            if (rawFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) {
                rawFileName = rawFileName.Substring(0, rawFileName.Length - 4);
            }

            AutoPrintService.ParseDynamicFileInfo(rawFileName, "Default", 1, out string dynamicUserId, out string dynamicFileName, out int dynamicCopies);

            if (dynamicCopies > 1)
            {
                AutomationElement printDialog = FindWindowDescendant(foxitWindow, "Print", false);
                if (printDialog != null)
                {
                    try { printDialog.SetFocus(); } catch { }
                    Thread.Sleep(300);
                    System.Windows.Forms.SendKeys.SendWait("%c");
                    Thread.Sleep(200);
                    System.Windows.Forms.SendKeys.SendWait(dynamicCopies.ToString());
                    Thread.Sleep(200);
                    System.Windows.MessageBox.Show($"Copies set to {dynamicCopies} via SendKeys.");
                }
                else
                {
                    System.Windows.MessageBox.Show("Could not find Foxit 'Print' dialog.");
                }
            }
            else
            {
                System.Windows.MessageBox.Show("File name does not contain a copy count (e.g., '...-2 copies.pdf').");
            }
        }

        private void BtnTest_OrientationEdge_Click(object sender, RoutedEventArgs e)
        {
            AutomationElement foxitWindow = GetFoxitWindow();
            AutomationElement printDialog = FindWindowDescendant(foxitWindow, "Print", false);
            if (printDialog == null)
            {
                System.Windows.MessageBox.Show("Could not find Foxit 'Print' dialog. Please open it first (Ctrl+P).");
                return;
            }

            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*";
            openFileDialog.Title = "Select the PDF file you are testing to detect its orientation";
            
            if (openFileDialog.ShowDialog() == true)
            {
                bool isLandscape = AutoPrintService.IsPdfLandscape(openFileDialog.FileName);
                string targetName = isLandscape ? "Flip on short edge" : "Flip on long edge";
                
                AutomationElement edgeBtn = printDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, targetName));
                if (edgeBtn != null)
                {
                    if (edgeBtn.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object selPattern))
                    {
                        ((SelectionItemPattern)selPattern).Select();
                    }
                    else
                    {
                        AutoPrintService.InvokeElement(edgeBtn);
                    }
                    System.Windows.MessageBox.Show($"Detected as {(isLandscape ? "Landscape" : "Portrait")}. Clicked: {targetName}");
                }
                else
                {
                    System.Windows.MessageBox.Show($"Detected as {(isLandscape ? "Landscape" : "Portrait")}, but could not find '{targetName}' radio button.");
                }
            }
        }

        private AutomationElement GetFoxitWindow()
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("FoxitPDFReader");
            if (processes.Length == 0)
            {
                processes = System.Diagnostics.Process.GetProcessesByName("FoxitPDFEditor");
            }
            if (processes.Length > 0 && processes[0].MainWindowHandle != IntPtr.Zero)
            {
                return AutomationElement.FromHandle(processes[0].MainWindowHandle);
            }
            return null;
        }

        private AutomationElement FindWindowDescendant(AutomationElement parent, string titleSubstring, bool contains)
        {
            if (parent == null) return null;
            AutomationElementCollection windows = parent.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
            foreach (AutomationElement window in windows)
            {
                if (contains && window.Current.Name.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
                    return window;
                else if (!contains && window.Current.Name.Equals(titleSubstring, StringComparison.OrdinalIgnoreCase))
                    return window;
            }
            return null;
        }

        private void BtnTest_Properties_Click(object sender, RoutedEventArgs e)
        {
            AutomationElement foxitWindow = GetFoxitWindow();
            AutomationElement printDialog = FindWindowDescendant(foxitWindow, "Print", false);
            if (printDialog != null)
            {
                AutomationElement propertiesBtn = printDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "10380"));
                if (propertiesBtn != null)
                {
                    AutoPrintService.InvokeElement(propertiesBtn);
                }
                else
                {
                    System.Windows.MessageBox.Show("Could not find Properties button (ID: 10380)");
                }
            }
            else
            {
                System.Windows.MessageBox.Show("Could not find Foxit 'Print' dialog. Did you press Ctrl+P?");
            }
        }

        private void BtnTest_ClickDetails_Click(object sender, RoutedEventArgs e)
        {
            AutomationElement foxitWindow = GetFoxitWindow();
            AutomationElement savinProps = FindWindowDescendant(foxitWindow, "Properties", true);
            if (savinProps != null)
            {
                AutomationElement detailsBtn = savinProps.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1018"));
                if (detailsBtn != null)
                {
                    AutoPrintService.InvokeElement(detailsBtn);
                }
                else
                {
                    System.Windows.MessageBox.Show("Could not find Details button (ID: 1018).");
                }
            }
            else
            {
                System.Windows.MessageBox.Show("Could not find 'SAVIN Properties' window.");
            }
        }

        private void BtnTest_InputID_Click(object sender, RoutedEventArgs e)
        {
            AutomationElement foxitWindow = GetFoxitWindow();
            if (foxitWindow == null) {
                System.Windows.MessageBox.Show("Could not find Foxit window.");
                return;
            }

            string windowTitle = GetFoxitFileName();
            string rawFileName = windowTitle;
            if (rawFileName.Contains(" - Foxit")) {
                rawFileName = rawFileName.Substring(0, rawFileName.IndexOf(" - Foxit"));
            }
            if (rawFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) {
                rawFileName = rawFileName.Substring(0, rawFileName.Length - 4);
            }

            AutoPrintService.ParseDynamicFileInfo(rawFileName, "Default", 1, out string dynamicUserId, out string dynamicFileName, out int dynamicCopies);

            AutomationElement detailsWindow = FindWindowDescendant(foxitWindow, "Details", true);
            if (detailsWindow != null)
            {
                try { SetForegroundWindow(new IntPtr(detailsWindow.Current.NativeWindowHandle)); } catch {}
                
                AutomationElement userIdEdit = detailsWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1004"));
                if (userIdEdit != null)
                {
                    AutoPrintService.SetTextElement(userIdEdit, dynamicUserId);
                }

                AutomationElement fileNameEdit = detailsWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1007"));
                if (fileNameEdit != null)
                {
                    AutoPrintService.SetTextElement(fileNameEdit, dynamicFileName);
                }
            }
            else
            {
                System.Windows.MessageBox.Show("Could not find 'Details' window.");
            }
        }




        private void BtnTest_OKJobDetails_Click(object sender, RoutedEventArgs e)
        {
            AutomationElement foxitWindow = GetFoxitWindow();
            AutomationElement detailsWindow = FindWindowDescendant(foxitWindow, "Details", true);
            if (detailsWindow != null)
            {
                AutomationElement okBtn = detailsWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1"));
                if (okBtn != null) AutoPrintService.InvokeElement(okBtn);
            }
            else
            {
                System.Windows.MessageBox.Show("Could not find 'Details' window.");
            }
        }

        private void BtnTest_OKProperties_Click(object sender, RoutedEventArgs e)
        {
            AutomationElement foxitWindow = GetFoxitWindow();
            AutomationElement savinProps = FindWindowDescendant(foxitWindow, "Properties", true);
            if (savinProps != null)
            {
                AutomationElement okBtn = savinProps.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1"));
                if (okBtn != null) AutoPrintService.InvokeElement(okBtn);
            }
            else
            {
                System.Windows.MessageBox.Show("Could not find 'SAVIN Properties' window.");
            }
        }

        private void BtnTest_OKPrint_Click(object sender, RoutedEventArgs e)
        {
            AutomationElement foxitWindow = GetFoxitWindow();
            AutomationElement printDialog = FindWindowDescendant(foxitWindow, "Print", false);
            if (printDialog != null)
            {
                AutomationElement okBtn = printDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1"));
                if (okBtn != null) AutoPrintService.InvokeElement(okBtn);
            }
            else
            {
                System.Windows.MessageBox.Show("Could not find 'Print' window.");
            }
        }
    
        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn == null) return;

            if (borderUpdateBadge.Visibility == Visibility.Visible)
            {
                System.Windows.MessageBox.Show("Downloading new version... (Mock)", "Update Available", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            btn.Content = "✨ Checking...";
            btn.IsEnabled = false;

            await System.Threading.Tasks.Task.Delay(1500);

            // Mock update check (random 50% chance to find update for demo purposes)
            bool hasUpdate = new Random().Next(0, 2) == 0;

            if (hasUpdate)
            {
                borderUpdateBadge.Visibility = Visibility.Visible;
                txtUpdateBadge.Text = "1";
                btn.Content = "✨ Update Available";
            }
            else
            {
                borderUpdateBadge.Visibility = Visibility.Collapsed;
                btn.Content = "✨ Check Update";
                System.Windows.MessageBox.Show("Application is up to date.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            btn.IsEnabled = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            _spoolerMonitor.Stop();
            _statusTimer.Stop();
            _webMonitorWindow.Hide(); // Actually, we should fully close it
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            System.Windows.Application.Current.Shutdown();
            if (_autoPrintService != null && _autoPrintService.IsRunning) { _autoPrintService.Stop(); }
            base.OnClosed(e);
        }
    }
}
