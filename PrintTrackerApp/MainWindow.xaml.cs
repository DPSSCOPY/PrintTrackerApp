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
using AutoUpdaterDotNET;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PrintTrackerApp
{
    public class EditAction
    {
        public PrintJobInfo Job { get; set; }
        public string PropertyName { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
    }

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
        private bool _alertedSentToPrinter = false;
        private bool _alertedStoringCompleted = false;
        private bool _alertedPrintCompleted = false;
        private string _lastReportDate = "";

        private readonly AutoPrintService _autoPrintService;
        
        private DispatcherTimer _runTimer;
        private Stopwatch _runStopwatch;
        
        private DispatcherTimer? _timeCheckTimer;
        private ShutdownAlertWindow? _shutdownWindow;
        private string _lastShutdownDate = "";
        private System.Threading.CancellationTokenSource? _shutdownDelayCts;
        private AutoUpdaterDotNET.UpdateInfoEventArgs _pendingUpdate;
        private System.Windows.Threading.DispatcherTimer _updateReminderTimer;

        private PrintTrackerApp.Services.UndoManager _undoManager = new PrintTrackerApp.Services.UndoManager();
        private PrintTrackerApp.Services.UndoBatch _currentUndoBatch;
        private System.Windows.Threading.DispatcherTimer _autoScrollTimer;
        private System.Windows.Controls.ScrollViewer _scrollViewer;

        private bool _isDraggingFill = false;
        private System.Windows.Controls.DataGridCellInfo _fillStartCellInfo;
        private object _fillSourceValue;
        private int _fillStartRowIdx = -1;
        private int _fillStartColIdx = -1;

        private static T FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
        {
            if (parent == null) return null;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T) return (T)child;
                T childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null) return childOfChild;
            }
            return null;
        }

        private static T FindVisualParent<T>(System.Windows.DependencyObject child) where T : System.Windows.DependencyObject
        {
            var parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            if (parent != null) return parent;
            return FindVisualParent<T>(parentObject);
        }

        private System.Windows.Controls.DataGridCell GetCellFromPoint(System.Windows.Point point)
        {
            var hit = System.Windows.Media.VisualTreeHelper.HitTest(dgPrintJobs, point);
            if (hit == null) return null;
            return FindVisualParent<System.Windows.Controls.DataGridCell>(hit.VisualHit);
        }
        
        private object GetPrintJobValue(PrintJobInfo job, string bindingPath)
        {
            if (bindingPath.StartsWith("DynamicProperties["))
            {
                string key = bindingPath.Substring(18).TrimEnd(']');
                job.DynamicProperties.TryGetValue(key, out string v);
                return v;
            }
            else
            {
                var prop = typeof(PrintJobInfo).GetProperty(bindingPath);
                return prop?.GetValue(job);
            }
        }

        private void SetPrintJobValue(PrintJobInfo job, string bindingPath, object value, bool skipRefresh = false)
        {
            if (bindingPath.StartsWith("DynamicProperties["))
            {
                string key = bindingPath.Substring(18).TrimEnd(']');
                job.DynamicProperties[key] = value?.ToString() ?? "";
                if (!skipRefresh) dgPrintJobs.Items.Refresh(); 
            }
            else
            {
                var prop = typeof(PrintJobInfo).GetProperty(bindingPath);
                if (prop != null && prop.CanWrite)
                {
                    try { prop.SetValue(job, Convert.ChangeType(value, prop.PropertyType)); } catch {}
                }
            }
            
            if (bindingPath == "Status" && value != null)
            {
                _ = System.Threading.Tasks.Task.Run(() => UpdateFileStatusLocation(job, value.ToString()));
            }
        }

        // Batch Printing State
        private System.Collections.Generic.List<PrintJobInfo> _currentBatchJobs = new System.Collections.Generic.List<PrintJobInfo>();
        private int _batchCounter = 0;
        private bool _isWaitingForBatch = false;
        private DateTime _batchWaitStartTime;

        private System.IO.FileSystemWatcher _csvWatcher;
        private bool _isAppModifyingCsv = false;
        private DateTime _lastCsvChangeTime = DateTime.MinValue;

        private void SetupCsvWatcher()
        {
            if (string.IsNullOrWhiteSpace(_appSettings?.CsvExportPath) || !System.IO.Directory.Exists(_appSettings.CsvExportPath))
                return;

            if (_csvWatcher != null)
            {
                _csvWatcher.EnableRaisingEvents = false;
                _csvWatcher.Dispose();
            }

            _csvWatcher = new System.IO.FileSystemWatcher(_appSettings.CsvExportPath, "PrintLog_*.csv");
            _csvWatcher.NotifyFilter = System.IO.NotifyFilters.LastWrite;
            _csvWatcher.Changed += CsvWatcher_Changed;
            _csvWatcher.EnableRaisingEvents = true;
        }

        private async void CsvWatcher_Changed(object sender, System.IO.FileSystemEventArgs e)
        {
            if ((DateTime.Now - _lastCsvChangeTime).TotalMilliseconds < 1000) return;
            if (_isAppModifyingCsv) return;
            
            _lastCsvChangeTime = DateTime.Now;
            
            // Wait briefly for external editor to finish saving
            await System.Threading.Tasks.Task.Delay(1000);
            if (_isAppModifyingCsv) return; 

            // Read jobs in background to avoid blocking UI and allow retries
            System.Collections.Generic.List<PrintJobInfo> loadedJobs = null;
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    loadedJobs = CsvLogger.LoadJobsFromCsv(_appSettings.CsvExportPath);
                    // If it succeeded without throwing inside, break
                    if (loadedJobs != null) break;
                }
                catch
                {
                    await System.Threading.Tasks.Task.Delay(500);
                }
            }

            if (loadedJobs == null) return;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    _printJobs.Clear();
                    foreach (var job in loadedJobs)
                    {
                        _printJobs.Add(job);
                    }
                    teacherDashboard.InitializeData(_printJobs, _appSettings.CsvExportPath);
                    dgPrintJobs.Items.Refresh();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error updating UI from CSV: {ex.Message}");
                }
            });
        }

        private void ExportJobsToCsvSafely(IEnumerable<PrintJobInfo> jobs, string path)
        {
            if (jobs == null || string.IsNullOrWhiteSpace(path)) return;
            _isAppModifyingCsv = true;
            try
            {
                CsvLogger.ExportJobsToCsv(jobs, path);
            }
            finally
            {
                System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ => _isAppModifyingCsv = false);
            }
        }

        public MainWindow()
        {
            _autoPrintService = new AutoPrintService();
            _autoPrintService.StatusChanged += AutoPrintService_StatusChanged;
            _autoScrollTimer = new System.Windows.Threading.DispatcherTimer();
            _autoScrollTimer.Interval = TimeSpan.FromMilliseconds(50);
            _autoScrollTimer.Tick += AutoScrollTimer_Tick;
            _autoPrintService.FileProcessingStarted += AutoPrintService_FileProcessingStarted;
            _autoPrintService.FileProcessingCompleted += AutoPrintService_FileProcessingCompleted;
            _autoPrintService.QueueEmpty += AutoPrintService_QueueEmpty;
            _autoPrintService.PauseRequested += AutoPrintService_PauseRequested;
            _autoPrintService.OnRequestUniqueUserId = GenerateUniqueUserId;
            
            InitializeComponent();
            _appSettings = SettingsManager.LoadSettings();

            _runTimer = new DispatcherTimer();
            _runTimer.Interval = TimeSpan.FromSeconds(1);
            _runTimer.Tick += RunTimer_Tick;
            _runStopwatch = new Stopwatch();

            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            _notifyIcon.Text = "Ricoh Print Tracker (Running)";
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (s, e) => 
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            };

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("Open Print Tracker", null, (s, e) => 
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            });
            contextMenu.Items.Add("Quit", null, (s, e) => 
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                Environment.Exit(0);
            });
            _notifyIcon.ContextMenuStrip = contextMenu;

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

            // Initialize background Web Monitor Off-Screen to prevent Chromium background throttling
            _webMonitorWindow = new WebMonitorWindow(_appSettings.PrinterIp, _appSettings.RefreshIntervalSeconds);
            _webMonitorWindow.OnScrapedStatusReceived += WebMonitor_OnScrapedStatusReceived;
            _webMonitorWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            _webMonitorWindow.Left = -10000;
            _webMonitorWindow.Top = -10000;
            _webMonitorWindow.ShowInTaskbar = false;
            _webMonitorWindow.Show(); // Show it immediately so it renders off-screen

            // Auto Shutdown timer disabled (Coming Soon)
            // _timeCheckTimer = new DispatcherTimer();
            // _timeCheckTimer.Interval = TimeSpan.FromSeconds(30);
            // _timeCheckTimer.Tick += TimeCheckTimer_Tick;
            // _timeCheckTimer.Start();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            _notifyIcon.ShowBalloonTip(2000, "Print Tracker", "Application is still running in the background.", System.Windows.Forms.ToolTipIcon.Info);
        }

        private void TimeCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (_appSettings.EnableAutoShutdown && _appSettings.AutoShutdownMode == 1) // Specific Time
            {
                string currentTime = DateTime.Now.ToString("HH:mm");
                string currentDate = DateTime.Now.ToString("yyyy-MM-dd");

                if (currentTime == _appSettings.AutoShutdownTime && _lastShutdownDate != currentDate)
                {
                    _lastShutdownDate = currentDate;
                    ShowShutdownAlert("Scheduled Auto Shutdown", $"The scheduled time ({_appSettings.AutoShutdownTime}) has been reached.");
                }
            }
        }

        private void ShowShutdownAlert(string title, string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (_shutdownWindow != null && _shutdownWindow.IsLoaded)
                    return;

                _shutdownWindow = new ShutdownAlertWindow($"{title}\n{message}");
                _shutdownWindow.Show();
            });
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
            AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;
            AutoUpdater.Start("https://raw.githubusercontent.com/DPSSCOPY/PrintTrackerApp/main/AutoUpdater.xml");

            // Setup reminder timer
            _updateReminderTimer = new System.Windows.Threading.DispatcherTimer();
            _updateReminderTimer.Tick += (s, ev) => 
            {
                _updateReminderTimer.Stop();
                AutoUpdater.Start("https://raw.githubusercontent.com/DPSSCOPY/PrintTrackerApp/main/AutoUpdater.xml");
            };

            // Auto-open the web monitor window as requested by the user, but run it in the background
            StartWebMonitorInBackground();

            // Load today's history from CSV
            var loadedJobs = CsvLogger.LoadJobsFromCsv(_appSettings.CsvExportPath);
            foreach (var job in loadedJobs)
            {
                job.CleanDownlevelNames();
                _printJobs.Add(job);
            }

            teacherDashboard.InitializeData(_printJobs, _appSettings.CsvExportPath);
            SetupCsvWatcher();

            // Also load the existing web monitor history so it's not overwritten and deleted on restart
            _webMonitorHistoryJobs = CsvLogger.LoadWebMonitorRawFromCsv(_appSettings.CsvExportPath);

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
            _spoolerMonitor.OnJobUpdated += SpoolerMonitor_OnJobUpdated;
            _spoolerMonitor.OnJobDeleted += SpoolerMonitor_OnJobDeleted;
            _spoolerMonitor.OnJobPagesUpdated += SpoolerMonitor_OnJobPagesUpdated;
            _spoolerMonitor.Start();

            // Start SNMP Polling
            _statusTimer.Start();
        }

        private void SpoolerMonitor_OnJobUpdated(object? sender, PrintJobInfo updatedJob)
        {
            Dispatcher.Invoke(() =>
            {
                var existingJob = _printJobs.FirstOrDefault(j => j.JobId == updatedJob.JobId);
                if (existingJob != null)
                {
                    existingJob.RicohUserId = updatedJob.RicohUserId;
                    existingJob.WebFileName = updatedJob.WebFileName;
                    existingJob.Copies = updatedJob.Copies;
                    
                    if (!string.IsNullOrEmpty(updatedJob.DocumentName) && updatedJob.DocumentName != "Local Downlevel Document" && updatedJob.DocumentName != "Print Document" && updatedJob.DocumentName != "Remote Downlevel Document")
                    {
                        if (existingJob.DocumentName == "Local Downlevel Document" || existingJob.DocumentName == "Print Document" || existingJob.DocumentName == "Remote Downlevel Document" || string.IsNullOrWhiteSpace(existingJob.DocumentName))
                        {
                            existingJob.DocumentName = updatedJob.DocumentName;
                        }
                    }

                    existingJob.CleanDownlevelNames();

                    ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
                    dgPrintJobs.Items.Refresh();
                }
            });
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (uiScale != null)
            {
                // Base width is 850.
                // Scale down slightly to decrease text and button size (minimum 0.7 scale)
                // When scaled down, virtual width increases, allowing WrapPanels to still wrap.
                double scale = Math.Max(0.7, Math.Min(1.0, e.NewSize.Width / 850.0));
                uiScale.ScaleX = scale;
                uiScale.ScaleY = scale;
            }
        }

        private string _currentAutoPrintFile = "";

        private void SpoolerMonitor_OnJobCreated(object? sender, PrintJobInfo job)
        {
            if (job.DocumentName == "Local Downlevel Document" || job.DocumentName == "Print Document" || job.DocumentName == "Remote Downlevel Document" || string.IsNullOrWhiteSpace(job.DocumentName))
            {
                if (!string.IsNullOrEmpty(_currentAutoPrintFile) && _autoPrintService != null && _autoPrintService.IsRunning)
                {
                    job.DocumentName = _currentAutoPrintFile;
                    if (job.WebFileName == "Local Downlevel Document" || job.WebFileName == "Print Document" || job.WebFileName == "Remote Downlevel Document" || string.IsNullOrWhiteSpace(job.WebFileName))
                    {
                        job.WebFileName = _currentAutoPrintFile;
                    }
                }
                else if (!string.IsNullOrEmpty(job.WebFileName) && job.WebFileName != "Local Downlevel Document" && job.WebFileName != "Print Document" && job.WebFileName != "Remote Downlevel Document")
                {
                    job.DocumentName = job.WebFileName;
                }
            }

            // Ensure WebFileName, RicohUserId, and Copies are extracted immediately if DocumentName follows the dynamic pattern
            if (PrintTrackerApp.Services.AutoPrintService.ParseDynamicFileInfo(job.DocumentName, job.Owner, 1, out string parsedUserId, out string parsedFileName, out int parsedCopies))
            {
                job.RicohUserId = parsedUserId;
                job.WebFileName = parsedFileName;
                if (parsedCopies > 1 && job.Copies == 1) job.Copies = parsedCopies;
            }
            else if (!string.IsNullOrEmpty(job.WebFileName) && PrintTrackerApp.Services.AutoPrintService.ParseDynamicFileInfo(job.WebFileName, job.Owner, 1, out string parsedUserIdWeb, out string parsedFileNameWeb, out int parsedCopiesWeb))
            {
                job.RicohUserId = parsedUserIdWeb;
                job.WebFileName = parsedFileNameWeb;
                if (parsedCopiesWeb > 1 && job.Copies == 1) job.Copies = parsedCopiesWeb;
            }

            // 1. ATTEMPT TO GET EXACT PAGE COUNT FROM PDF FILE
            string? physicalFile = FindPhysicalFileForJob(job);
            bool parsedPdfPages = false;
            if (!string.IsNullOrEmpty(physicalFile) && System.IO.File.Exists(physicalFile))
            {
                try
                {
                    // Instead of using PdfSharp to get total raw pages, 
                    // use PdfPageHelper to get the actual filtered (non-blank) page count
                    // that Foxit will actually print.
                    PdfPageHelper.GetNonBlankPagesString(physicalFile, out bool isAllPages, out int actualPageCount);
                    if (actualPageCount > 0)
                    {
                        job.TotalPages = actualPageCount;
                        parsedPdfPages = true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not read PDF pages for {physicalFile}: {ex.Message}");
                }
            }

            Dispatcher.Invoke(() =>
            {
                // Tag it so we know we got the accurate count from the PDF itself
                if (parsedPdfPages)
                {
                    job.IsPdfPageCountAccurate = true; 
                }

                job.CleanDownlevelNames();
                _printJobs.Insert(0, job);
                job.Status = "Sent to Printer";
                
                if (_currentActiveJob == null)
                {
                    _currentActiveJob = job;
                }
                
                ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);

                if (_appSettings.EnableBatchPrinting && _autoPrintService != null && _autoPrintService.IsRunning)
                {
                    _currentBatchJobs.Add(job);
                }

                // Dynamically move the file to folder (useful for manual prints)
                UpdateFileStatusLocation(job, job.Status);
            });
        }

        private void SpoolerMonitor_OnJobDeleted(object? sender, (string JobId, string JobStatus, int PagesPrinted) e)
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
                    if (!job.IsPdfPageCountAccurate && e.TotalPages > job.TotalPages)
                    {
                        job.TotalPages = e.TotalPages;
                        ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
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
                _webMonitorHistoryJobs.Clear();
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
            if (_isWaitingForBatch)
            {
                CheckBatchWaitStatus();
            }
            CheckAndSendDailyReport();
        }

        private void CheckAndSendDailyReport()
        {
            if (string.IsNullOrWhiteSpace(_appSettings.DailyReportTime)) return;

            string nowTime = DateTime.Now.ToString("HH:mm");
            string today = DateTime.Now.ToString("yyyy-MM-dd");

            if (nowTime == _appSettings.DailyReportTime && _lastReportDate != today)
            {
                int GetPdfCount(string folder)
                {
                    try { return System.IO.Directory.Exists(folder) ? System.IO.Directory.GetFiles(folder, "*.pdf").Length : 0; }
                    catch { return 0; }
                }

                int sourceCount = GetPdfCount(_appSettings.SourceFolderPath);
                int totalCount = sourceCount;
                
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine($"📊 *Daily Print Report* ({today})");
                sb.AppendLine($"📂 Source: {sourceCount}");

                try
                {
                    var subDirs = System.IO.Directory.GetDirectories(_appSettings.SourceFolderPath);
                    foreach (var dir in subDirs)
                    {
                        string dirName = System.IO.Path.GetFileName(dir);
                        int count = GetPdfCount(dir);
                        totalCount += count;
                        
                        string icon = "📁";
                        string lowerName = dirName.ToLower();
                        if (lowerName.Contains("sent")) icon = "📤";
                        else if (lowerName.Contains("print complete")) icon = "✅";
                        else if (lowerName.Contains("print") || lowerName.Contains("process")) icon = "⚙️";
                        else if (lowerName.Contains("stor")) icon = "🗃️";
                        else if (lowerName.Contains("sort")) icon = "🗂️";
                        
                        sb.AppendLine($"{icon} {dirName}: {count}");
                    }
                }
                catch { }

                if (totalCount > 0)
                {
                    string csvPath = System.IO.Path.Combine(_appSettings.CsvExportPath, $"PrintLog_{today}.csv");
                    _ = SendTelegramMessageWithDocumentAsync(sb.ToString().TrimEnd(), csvPath);
                }
                
                _lastReportDate = today;
            }
        }

        private void UpdateVerificationPanel()
        {
            Dispatcher.Invoke(() =>
            {

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
                            
                        var match = System.Text.RegularExpressions.Regex.Match(name, @"_(\d{6})$");
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

                int completedToday = _printJobs.Count(j => j.Status.Contains("Complete", StringComparison.OrdinalIgnoreCase) || j.Status.Contains("Printed", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(_appSettings.SourceFolderPath) || !System.IO.Directory.Exists(_appSettings.SourceFolderPath))
                {
                    txtPendingCount.Text = "0";
                    txtSubfolderCount.Text = "0";
                    txtSentToPrinterCount.Text = "0";
                    txtDuplicateCount.Text = "0";
                    txtErrorFilesCount.Text = "0";
                    txtPrintCompleteCount.Text = "0";
                    return;
                }

                try
                {
                    // Count all PDF files in the source folder, excluding the Complete Print subfolder
                    var files = System.IO.Directory.GetFiles(_appSettings.SourceFolderPath, "*.pdf");
                    int currentCount = files.Length;
                    txtPendingCount.Text = currentCount.ToString();

                    // Count pdf files ONLY in specific printed folders
                    int totalSubCount = 0;
                    HashSet<string> uniqueBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    
                    string[] printedFolders = { "Storing Complete", "Complete Print", "Processing", "Print Complete", "Successfully Printed" };
                    foreach (var folder in printedFolders)
                    {
                        string dirPath = System.IO.Path.Combine(_appSettings.SourceFolderPath, folder);
                        if (System.IO.Directory.Exists(dirPath))
                        {
                            try 
                            { 
                                var pdfFiles = System.IO.Directory.GetFiles(dirPath, "*.pdf");
                                totalSubCount += pdfFiles.Length;
                                foreach(var f in pdfFiles) 
                                {
                                    string name = System.IO.Path.GetFileNameWithoutExtension(f);
                                    // Remove auto-appended time suffix (_HHmmss) to identify the true original file
                                    var match = System.Text.RegularExpressions.Regex.Match(name, @"_(\d{6})$");
                                    if (match.Success) 
                                    {
                                        name = name.Substring(0, name.Length - 7);
                                    }
                                    uniqueBaseNames.Add(name);
                                }
                            } 
                            catch { }
                        }
                    }
                    
                    int actualCount = uniqueBaseNames.Count;
                    int duplicateCount = totalSubCount - actualCount;

                    txtSubfolderCount.Text = actualCount.ToString();
                    txtDuplicateCount.Text = duplicateCount.ToString();

                    // Count Sent to Printer files
                    int sentToPrinterCount = 0;
                    string sentToPrinterDir = System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sent to Printer");
                    if (System.IO.Directory.Exists(sentToPrinterDir))
                    {
                        try
                        {
                            sentToPrinterCount = System.IO.Directory.GetFiles(sentToPrinterDir, "*.pdf").Length;
                        }
                        catch { }
                    }
                    txtSentToPrinterCount.Text = sentToPrinterCount.ToString();

                    // Count Print Complete files
                    int printCompleteCount = 0;
                    string printCompleteDir = System.IO.Path.Combine(_appSettings.SourceFolderPath, "Print Complete");
                    if (System.IO.Directory.Exists(printCompleteDir))
                    {
                        try
                        {
                            printCompleteCount = System.IO.Directory.GetFiles(printCompleteDir, "*.pdf").Length;
                        }
                        catch { }
                    }
                    txtPrintCompleteCount.Text = printCompleteCount.ToString();

                    // Count Error files: any subfolder except active states and "Storing Complete"
                    int errorCount = 0;
                    string[] activeOrSuccessFolders = { 
                        "Storing Complete", 
                        "Print Complete",
                        "Processing", 
                        "Sent to Printer", 
                        "Printing", 
                        "Storing" 
                    };
                    
                    var subdirs = System.IO.Directory.GetDirectories(_appSettings.SourceFolderPath);
                    foreach (var dir in subdirs)
                    {
                        string dirName = System.IO.Path.GetFileName(dir);
                        
                        bool isExcluded = false;
                        foreach (var exclude in activeOrSuccessFolders)
                        {
                            if (dirName.Equals(exclude, StringComparison.OrdinalIgnoreCase))
                            {
                                isExcluded = true;
                                break;
                            }
                        }

                        if (!isExcluded)
                        {
                            try
                            {
                                errorCount += System.IO.Directory.GetFiles(dir, "*.pdf").Length;
                            }
                            catch { }
                        }
                    }
                    txtErrorFilesCount.Text = errorCount.ToString();

                    if (currentCount > 0 && _autoPrintService != null && _autoPrintService.IsRunning)
                    {
                        if (!_hasStartedPrinting)
                        {
                            _hasStartedPrinting = true;
                            _alertedSentToPrinter = false;
                            _alertedStoringCompleted = false;
                            _alertedPrintCompleted = false;
                        }
                    }
                    
                    CheckBatchStatus();
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

        private void BtnShowSubfolderDetails_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_appSettings.SourceFolderPath) || !System.IO.Directory.Exists(_appSettings.SourceFolderPath))
            {
                System.Windows.MessageBox.Show("Source folder is not configured or does not exist.", "Details", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var subdirs = System.IO.Directory.GetDirectories(_appSettings.SourceFolderPath);
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("Summary of files by subfolder:");
                sb.AppendLine(new string('-', 40));

                int totalCount = 0;
                foreach (var dir in subdirs)
                {
                    string dirName = System.IO.Path.GetFileName(dir);
                    int count = 0;
                    try { count = System.IO.Directory.GetFiles(dir, "*.pdf").Length; } catch { }
                    
                    sb.AppendLine($"{dirName}: {count} file(s)");
                    totalCount += count;
                }

                sb.AppendLine(new string('-', 40));
                sb.AppendLine($"Total files in subfolders: {totalCount}");

                var window = new SubfolderSummaryWindow(sb.ToString());
                window.Owner = this;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error retrieving details: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string searchText = txtSearch.Text.ToLower();
            
            // Handle placeholder visibility
            if (txtSearchPlaceholder != null)
                txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(searchText) ? Visibility.Visible : Visibility.Collapsed;

            System.ComponentModel.ICollectionView view = System.Windows.Data.CollectionViewSource.GetDefaultView(dgPrintJobs.ItemsSource);
            if (view != null)
            {
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    view.Filter = null;
                }
                else
                {
                    view.Filter = item =>
                    {
                        var job = item as PrintJobInfo;
                        if (job == null) return false;

                        return (job.DocumentName != null && job.DocumentName.ToLower().Contains(searchText)) ||
                               (job.WebFileName != null && job.WebFileName.ToLower().Contains(searchText)) ||
                               (job.RicohUserId != null && job.RicohUserId.ToLower().Contains(searchText)) ||
                               (job.Owner != null && job.Owner.ToLower().Contains(searchText)) ||
                               (job.PrinterName != null && job.PrinterName.ToLower().Contains(searchText)) ||
                               (job.Status != null && job.Status.ToLower().Contains(searchText));
                    };
                }
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
        private Dictionary<int, string[]> _webMonitorHistoryJobs = new Dictionary<int, string[]>();

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

                        // Raw History Logging: Dynamic Update (Overwrite file if status changes or new job)
                        bool changed = false;
                        if (!_webMonitorHistoryJobs.ContainsKey(jobId))
                        {
                            var newRow = new List<string>(parts);
                            newRow.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); // Append Log Time as 7th element (index 6)
                            _webMonitorHistoryJobs[jobId] = newRow.ToArray();
                            changed = true;
                        }
                        else if (_webMonitorHistoryJobs[jobId].Length > 2 && _webMonitorHistoryJobs[jobId][2] != status)
                        {
                            // Status changed! Update it, but keep original Log Time
                            var updatedRow = new List<string>(parts);
                            string oldLogTime = _webMonitorHistoryJobs[jobId].Length > 6 ? _webMonitorHistoryJobs[jobId][6] : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            updatedRow.Add(oldLogTime);
                            _webMonitorHistoryJobs[jobId] = updatedRow.ToArray();
                            changed = true;
                        }

                        if (changed)
                        {
                            // Output jobs sorted by ID descending
                            CsvLogger.ExportWebMonitorRawToCsv(_webMonitorHistoryJobs.Values.OrderByDescending(x => int.Parse(x[0])), _appSettings.CsvExportPath);
                        }

                        // 1. Check if we ALREADY linked this exact Web Monitor Job ID to an internal print job
                        var matchedJob = _printJobs.FirstOrDefault(j => j.WebJobId == jobId);
                        
                        // 2. If not found by exact Job ID, but it's a "Complete" job, it might be a Hold Print that just got printed!
                        // When a Hold Print is printed, the printer creates a NEW Job ID. We must find the original "Storing Complete" job.
                        if (matchedJob == null && (status.Contains("Complete", StringComparison.OrdinalIgnoreCase) || status.Contains("Printed", StringComparison.OrdinalIgnoreCase) || status.Contains("Processing", StringComparison.OrdinalIgnoreCase)))
                        {
                            // Prioritize finding an UNCLAIMED job first
                            matchedJob = _printJobs.FirstOrDefault(j => 
                                j.WebJobId == -1 &&
                                string.Equals(j.RicohUserId?.Trim(), userId?.Trim(), StringComparison.OrdinalIgnoreCase) && 
                                string.Equals(j.WebFileName?.Trim(), webFileName?.Trim(), StringComparison.OrdinalIgnoreCase) && 
                                IsTimeMatch(j.Timestamp, createdAt));
                                
                            // If no unclaimed job, find an already claimed one that hasn't finished printing yet
                            if (matchedJob == null)
                            {
                                matchedJob = _printJobs.FirstOrDefault(j => 
                                    string.Equals(j.RicohUserId?.Trim(), userId?.Trim(), StringComparison.OrdinalIgnoreCase) && 
                                    (string.Equals(j.WebFileName?.Trim(), webFileName?.Trim(), StringComparison.OrdinalIgnoreCase) || IsFileNameMatch(j.WebFileName ?? "", webFileName) || IsFileNameMatch(j.DocumentName ?? "", webFileName)) && 
                                    j.Status != "Print Complete" && j.Status != "Successfully Printed" &&
                                    jobId > j.WebJobId &&
                                    IsTimeMatch(j.Timestamp, createdAt));
                                    
                                if (matchedJob != null)
                                {
                                    matchedJob.IsInPrintPhase = true;
                                }
                            }
                        }
                        
                        // 3. If STILL not found, and it's a completely NEW job ID OR we are in the first batch processing old loaded jobs
                        if (matchedJob == null)
                        {
                            // 1. Strict Match: Web Job ID (if we could somehow get it, but we can't from PC side before linking)
                            // 2. Strict Match: Hold Name (UserId) + File Name + Timestamp (As requested by user for 100% accuracy)
                            matchedJob = _printJobs.FirstOrDefault(j => 
                                j.WebJobId == -1 && 
                                IsUserIdMatch(j.RicohUserId, j.Owner, userId) &&
                                (string.Equals(j.WebFileName?.Trim(), webFileName?.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(j.DocumentName?.Trim(), webFileName?.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                                j.TotalPages == webPages &&
                                IsTimeMatch(j.Timestamp, createdAt));

                            // 3. Fallback: Fuzzy File Name Match BUT STILL REQUIRE STRICT User ID (Hold Name) Match
                            if (matchedJob == null)
                            {
                                matchedJob = _printJobs.FirstOrDefault(j => 
                                    j.WebJobId == -1 && 
                                    IsUserIdMatch(j.RicohUserId, j.Owner, userId) && 
                                    IsFileNameMatch(j.DocumentName, webFileName) && 
                                    j.TotalPages == webPages &&
                                    IsTimeMatch(j.Timestamp, createdAt));
                                    
                                // 4. Final Fallback: If still not found, try without Pages just in case Ricoh reports pages differently (e.g. duplex)
                                if (matchedJob == null)
                                {
                                    matchedJob = _printJobs.FirstOrDefault(j => 
                                        j.WebJobId == -1 && 
                                        IsUserIdMatch(j.RicohUserId, j.Owner, userId) && 
                                        IsFileNameMatch(j.DocumentName, webFileName) && 
                                        IsTimeMatch(j.Timestamp, createdAt));
                                }
                            }
                            
                            // 4. If we STILL couldn't find a matching local job, ignore it per user request to prevent wrong status updates.
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
                                
                                // Strict Rules for Storing Phase: 
                                // If already "Storing Complete", do NOT downgrade to "Storing Failed".
                                // However, it CAN transition to Error or Cancelled.
                                bool canUpdate = true;
                                if (oldStatus.Contains("Storing Complete", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (status.Contains("Storing Failed", StringComparison.OrdinalIgnoreCase))
                                    {
                                        canUpdate = false;
                                    }
                                }

                                if (canUpdate)
                                {
                                    matchedJob.Status = status;
                                    
                                    if (oldStatus != status)
                                    {
                                        if ((status.Contains("Complete", StringComparison.OrdinalIgnoreCase) || status.Contains("Printed", StringComparison.OrdinalIgnoreCase)) && !status.Contains("Storing", StringComparison.OrdinalIgnoreCase))
                                        {
                                            ShowNotification("Print Complete", $"File: {matchedJob.WebFileName}\nUser: {matchedJob.Owner}");
                                        }
                                        UpdateFileStatusLocation(matchedJob, status);
                                    }
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
                                
                                ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
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

        private bool IsUserIdMatch(string pcUserId, string pcOwner, string webUserId)
        {
            if (string.IsNullOrWhiteSpace(webUserId)) return true; // If web monitor doesn't provide it, we can't filter by it
            
            string webClean = webUserId.Trim();
            if (string.Equals(pcUserId?.Trim(), webClean, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(pcOwner?.Trim(), webClean, StringComparison.OrdinalIgnoreCase)) return true;
            
            // Sometimes Ricoh strips domain prefixes (e.g. DOMAIN\User becomes User)
            if (!string.IsNullOrEmpty(pcOwner))
            {
                var parts = pcOwner.Split('\\');
                string ownerName = parts.Length > 1 ? parts[1] : parts[0];
                if (string.Equals(ownerName.Trim(), webClean, StringComparison.OrdinalIgnoreCase)) return true;
            }
            
            // Allow partial match if PC user id contains the web user id
            if (!string.IsNullOrEmpty(pcUserId) && pcUserId.IndexOf(webClean, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            
            return false;
        }

        private bool IsFileNameMatch(string pcDocName, string webFileName)
        {
            if (string.IsNullOrWhiteSpace(pcDocName) || string.IsNullOrWhiteSpace(webFileName)) return false;

            string pcNameNoExt = System.IO.Path.GetFileNameWithoutExtension(pcDocName).Trim();
            string webNameNoExt = System.IO.Path.GetFileNameWithoutExtension(webFileName).Trim();

            // 1. 100% Exact Match
            if (string.Equals(pcNameNoExt, webNameNoExt, StringComparison.OrdinalIgnoreCase)) return true;

            // 2. Exact match considering '?' (Ricoh replaces unicode characters with '?')
            // The length MUST be exactly the same for it to be a 100% match.
            if (pcNameNoExt.Length == webNameNoExt.Length)
            {
                bool isMatch = true;
                for (int i = 0; i < pcNameNoExt.Length; i++)
                {
                    if (webNameNoExt[i] == '?') continue; // wildcard for unicode
                    if (char.ToLowerInvariant(pcNameNoExt[i]) != char.ToLowerInvariant(webNameNoExt[i]))
                    {
                        isMatch = false;
                        break;
                    }
                }
                if (isMatch) return true;
            }

            return false;
        }

        private void StartWebMonitorInBackground()
        {
            _webMonitorWindow.Left = -10000;
            _webMonitorWindow.Top = -10000;
            _webMonitorWindow.ShowInTaskbar = false;
            _webMonitorWindow.Show();
        }

        private void BtnOpenWebMonitor_Click(object sender, RoutedEventArgs e)
        {
            _webMonitorWindow.Left = SystemParameters.WorkArea.Width / 2 - _webMonitorWindow.Width / 2;
            _webMonitorWindow.Top = SystemParameters.WorkArea.Height / 2 - _webMonitorWindow.Height / 2;
            _webMonitorWindow.ShowInTaskbar = true;
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
                
                // Reload to avoid overwriting any fields modified by child windows (e.g. Foxit IDs)
                var latestSettings = SettingsManager.LoadSettings();
                
                // Manually merge fields updated in SettingsWindow
                latestSettings.PrinterIp = sw.CurrentSettings.PrinterIp;
                latestSettings.PrinterName = sw.CurrentSettings.PrinterName;
                latestSettings.CsvExportPath = sw.CurrentSettings.CsvExportPath;
                latestSettings.SourceFolderPath = sw.CurrentSettings.SourceFolderPath;
                latestSettings.EnablePriority1 = sw.CurrentSettings.EnablePriority1;
                latestSettings.Priority1Prefixes = sw.CurrentSettings.Priority1Prefixes;
                latestSettings.EnablePriority2 = sw.CurrentSettings.EnablePriority2;
                latestSettings.Priority2Prefixes = sw.CurrentSettings.Priority2Prefixes;
                latestSettings.EnablePriority3 = sw.CurrentSettings.EnablePriority3;
                latestSettings.Priority3Prefixes = sw.CurrentSettings.Priority3Prefixes;
                latestSettings.GoogleSpreadsheetId = sw.CurrentSettings.GoogleSpreadsheetId;
                latestSettings.TelegramBotUrl = sw.CurrentSettings.TelegramBotUrl;
                latestSettings.TelegramBotToken = sw.CurrentSettings.TelegramBotToken;
                latestSettings.TelegramChatId = sw.CurrentSettings.TelegramChatId;
                latestSettings.DailyReportTime = sw.CurrentSettings.DailyReportTime;
                latestSettings.NotifySentToPrinter = sw.CurrentSettings.NotifySentToPrinter;
                latestSettings.NotifyStoringCompleted = sw.CurrentSettings.NotifyStoringCompleted;
                latestSettings.NotifyPrintCompleted = sw.CurrentSettings.NotifyPrintCompleted;
                latestSettings.EnableAutoShutdown = sw.CurrentSettings.EnableAutoShutdown;
                latestSettings.AutoShutdownMode = sw.CurrentSettings.AutoShutdownMode;
                latestSettings.AutoShutdownDelayMinutes = sw.CurrentSettings.AutoShutdownDelayMinutes;
                latestSettings.AutoShutdownTime = sw.CurrentSettings.AutoShutdownTime;
                latestSettings.RefreshIntervalSeconds = sw.CurrentSettings.RefreshIntervalSeconds;
                latestSettings.FoxitWindowStyle = sw.CurrentSettings.FoxitWindowStyle;
                latestSettings.DelayBetweenPrints = sw.CurrentSettings.DelayBetweenPrints;
                
                _appSettings = latestSettings;
                SettingsManager.SaveSettings(_appSettings);
                
                // Reset daily report tracking so it can trigger immediately if the new time matches current time
                _lastReportDate = "";

                txtFolderPath.Text = string.IsNullOrWhiteSpace(_appSettings.SourceFolderPath) ? "Not configured" : _appSettings.SourceFolderPath;
                UpdateVerificationPanel();
        

                if (oldPath != _appSettings.CsvExportPath)
                {
                    ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
                }

                if (needRestart)
                {
                    CustomAlertWindow.ShowMessage("Restart Required", "Settings saved. Please restart the application for the IP/Printer Name changes to take effect.", AlertType.Information);
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
            else if (status.Contains("Sorting", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Sorting Complete";
            }
            else if (status.Contains("Storing Failed", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Storing Failed";
            }
            else if (status.Contains("Storing Complete", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Storing Complete";
            }
            else if (status.Contains("Storing", StringComparison.OrdinalIgnoreCase) || status.Contains("Hold", StringComparison.OrdinalIgnoreCase) || status.Contains("Locked", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Storing";
            }
            else if (status.Contains("Power Failed", StringComparison.OrdinalIgnoreCase) || status.Contains("Power Fail", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Power Failed";
            }
            else if (status.Contains("Processing", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Processing";
            }
            else if (status.Contains("Printing", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Printing";
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

                    CheckBatchStatus();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to move file to {targetSubFolder}: {ex.Message}");
                }
            }
            else if (targetSubFolder == "Print Complete")
            {
                 // Check anyway if they all finished
                 CheckBatchStatus();
            }
        }

        private string? FindPhysicalFileForJob(PrintJobInfo job)
        {
            if (string.IsNullOrWhiteSpace(_appSettings.SourceFolderPath) || !System.IO.Directory.Exists(_appSettings.SourceFolderPath))
                return null;

            var searchFolders = new List<string> { _appSettings.SourceFolderPath };
            try
            {
                searchFolders.AddRange(System.IO.Directory.GetDirectories(_appSettings.SourceFolderPath));
            }
            catch { }

            string jobDocName = job.DocumentName ?? "";
            string jobDocNameNoExt = System.IO.Path.GetFileNameWithoutExtension(jobDocName);

            // Pass 1: Exact Match
            foreach (var folder in searchFolders)
            {
                if (!System.IO.Directory.Exists(folder)) continue;
                string exactPath = System.IO.Path.Combine(folder, jobDocName);
                if (System.IO.File.Exists(exactPath))
                {
                    return exactPath;
                }
            }

            // Pass 2: Fallback (Time Suffixes or Web Monitor matches)
            foreach (var folder in searchFolders)
            {
                if (!System.IO.Directory.Exists(folder)) continue;
                foreach (var file in System.IO.Directory.GetFiles(folder, "*.pdf"))
                {
                    string fileName = System.IO.Path.GetFileName(file);
                    string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(file);

                    // If file was renamed with a time suffix like "_123456"
                    if (!string.IsNullOrWhiteSpace(jobDocNameNoExt) && 
                        fileNameWithoutExt.StartsWith(jobDocNameNoExt, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }

                    // Strict WebFileName match (ONLY if we couldn't find the exact file)
                    if (!string.IsNullOrWhiteSpace(job.WebFileName) && IsFileNameMatch(fileName, job.WebFileName))
                    {
                        return file;
                    }
                }
            }

            return null;
        }

        private void CheckBatchStatus()
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrWhiteSpace(_appSettings.SourceFolderPath) || !System.IO.Directory.Exists(_appSettings.SourceFolderPath))
                    return;

                int GetPdfCount(string folder)
                {
                    try { return System.IO.Directory.Exists(folder) ? System.IO.Directory.GetFiles(folder, "*.pdf").Length : 0; }
                    catch { return 0; }
                }

                int sourceCount = GetPdfCount(_appSettings.SourceFolderPath);
                int sentToPrinterCount = GetPdfCount(System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sent to Printer"));
                int sortingCount = GetPdfCount(System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sorting Complete"));
                int storingCount = GetPdfCount(System.IO.Path.Combine(_appSettings.SourceFolderPath, "Storing Complete"));
                int printingCount = GetPdfCount(System.IO.Path.Combine(_appSettings.SourceFolderPath, "Printing"));
                int processingCount = GetPdfCount(System.IO.Path.Combine(_appSettings.SourceFolderPath, "Processing"));

                if (sourceCount > 0)
                {
                    _alertedSentToPrinter = false;
                    _alertedStoringCompleted = false;
                    
                    if (_appSettings.EnableAutoShutdown)
                    {
                        AbortShutdown();
                    }
                }

                if (sourceCount == 0 && !_alertedSentToPrinter && _hasStartedPrinting)
                {
                    _alertedSentToPrinter = true;
                    CustomAlertWindow.ShowMessage("Sent to Printer", "All files have been successfully sent to the printer.", AlertType.SentToPrinter);
                    
                    var notification = new AppNotification
                    {
                        Title = "Sent to Printer",
                        Message = "All files in your folder have been successfully sent to the printer."
                    };
                    
                    var currentSettings = SettingsManager.LoadSettings();
                    currentSettings.Notifications.Insert(0, notification);
                    _appSettings = currentSettings;
                    SettingsManager.SaveSettings(_appSettings);
                    
                    elNewNotification.Visibility = Visibility.Visible;
                    
                    if (_appSettings.NotifySentToPrinter)
                    {
                        _ = SendTelegramMessageAsync("📤 *ឯកសារត្រូវបានបញ្ជូនទៅម៉ាស៊ីនព្រីនរួចរាល់!* (Sent to Printer)");
                    }
                }

                if (sourceCount == 0 && sentToPrinterCount == 0 && printingCount == 0 && processingCount == 0 && !_alertedStoringCompleted && _alertedSentToPrinter && _hasStartedPrinting)
                {
                    _alertedStoringCompleted = true;
                    CustomAlertWindow.ShowMessage("Storing Completed", "All files have been processed and stored in the printer.", AlertType.StoringCompleted);
                    
                    if (_appSettings.NotifyStoringCompleted)
                    {
                        _ = SendTelegramMessageAsync("🗃️ *ឯកសារបានដំណើរការរក្សាទុកចប់សព្វគ្រប់!* (Storing Completed)");
                    }
                }

                if (sourceCount == 0 && sentToPrinterCount == 0 && sortingCount == 0 && storingCount == 0 && printingCount == 0 && processingCount == 0 && _printJobs.Count > 0 && !_alertedPrintCompleted && _alertedSentToPrinter && _alertedStoringCompleted)
                {
                    // Everything is complete!
                    _hasStartedPrinting = false;
                    _alertedPrintCompleted = true;
                    CustomAlertWindow.ShowMessage("Print Complete", "All print jobs are completed!", AlertType.PrintCompleted);
                    
                    if (_appSettings.NotifyPrintCompleted)
                    {
                        _ = SendTelegramMessageAsync("✅ *ការព្រីនត្រូវបានបញ្ចាំងរួចរាល់!* (Print Complete)");
                    }

                    // Auto Shutdown after print complete (Specific Time mode) is disabled for now (Coming Soon)
                    // if (_appSettings.EnableAutoShutdown && _appSettings.AutoShutdownMode == 0)
                    // {
                    //     int delayMs = _appSettings.AutoShutdownDelayMinutes * 60000;
                    //     _shutdownDelayCts?.Cancel();
                    //     _shutdownDelayCts = new System.Threading.CancellationTokenSource();
                    //     var token = _shutdownDelayCts.Token;
                    //
                    //     Task.Run(async () =>
                    //     {
                    //         try
                    //         {
                    //             if (delayMs > 0)
                    //             {
                    //                 ShowNotification("Auto Shutdown Scheduled", $"PC will shut down in {_appSettings.AutoShutdownDelayMinutes} minutes.");
                    //                 await Task.Delay(delayMs, token);
                    //             }
                    //
                    //             if (!token.IsCancellationRequested)
                    //             {
                    //                 ShowShutdownAlert("Print Complete Auto Shutdown", "All print jobs have been completed.");
                    //             }
                    //         }
                    //         catch { }
                    //     });
                    // }
                    
                    _alertedSentToPrinter = false;
                    _alertedStoringCompleted = false;
                }
            });
        }

        private void CheckBatchWaitStatus()
        {
            if (!_isWaitingForBatch) return;

            bool allCompleted = true;
            bool anyFailed = false;
            bool anySentToPrinter = false;
            string failedFileName = "";

            foreach (var job in _currentBatchJobs)
            {
                string status = job.Status ?? "";
                if (status.Contains("Failed", StringComparison.OrdinalIgnoreCase) || 
                    status.Contains("Error", StringComparison.OrdinalIgnoreCase) || 
                    status.Contains("Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    anyFailed = true;
                    failedFileName = job.DocumentName;
                    break;
                }
                
                if (status.Contains("Sent to Printer", StringComparison.OrdinalIgnoreCase) ||
                    status.Contains("Spooling", StringComparison.OrdinalIgnoreCase))
                {
                    anySentToPrinter = true;
                }
                
                // Continue if "Storing Complete" or better
                if (!status.Contains("Storing Complete", StringComparison.OrdinalIgnoreCase) && 
                    !status.Contains("Print Complete", StringComparison.OrdinalIgnoreCase) && 
                    !status.Contains("Printed", StringComparison.OrdinalIgnoreCase) &&
                    !status.Contains("Printing", StringComparison.OrdinalIgnoreCase) && 
                    !status.Contains("Processing", StringComparison.OrdinalIgnoreCase))
                {
                    allCompleted = false;
                }
            }

            if (anyFailed)
            {
                _isWaitingForBatch = false;
                
                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    bool isYes = CustomConfirmWindow.ShowConfirm(
                        "Batch Print Error",
                        $"File '{failedFileName}' encountered an error or was cancelled.\nDo you want to continue printing the next batch?",
                        AlertType.Error);
                        
                    if (isYes)
                    {
                        _currentBatchJobs.Clear();
                        _batchCounter = 0;
                        _autoPrintService.IsPaused = false;
                        AutoPrintService_StatusChanged(this, "Running");
                    }
                    else
                    {
                        BtnStopAutoPrint_Click(null, null);
                    }
                });
            }
            else if (allCompleted && _currentBatchJobs.Count >= _batchCounter)
            {
                _isWaitingForBatch = false;
                _currentBatchJobs.Clear();
                _batchCounter = 0;
                _autoPrintService.IsPaused = false;
                AutoPrintService_StatusChanged(this, "Running");
            }
            else
            {
                bool isWaitingForArrival = _currentBatchJobs.Count < _batchCounter;

                if (anySentToPrinter || isWaitingForArrival)
                {
                    // Timeout check: 30 seconds only if still 'Sent to Printer'
                    if ((DateTime.Now - _batchWaitStartTime).TotalSeconds >= 30)
                    {
                        _isWaitingForBatch = false;
                        System.Windows.Application.Current.Dispatcher.Invoke(() => 
                        {
                            bool isYes = CustomConfirmWindow.ShowConfirm(
                                "Batch Print Timeout",
                                "Batch verification timed out after 30 seconds.\nSome files are still stuck (e.g. 'Sent to Printer').\nDo you want to continue printing the next batch?",
                                AlertType.Warning);
                                
                            if (isYes)
                            {
                                _currentBatchJobs.Clear();
                                _batchCounter = 0;
                                _autoPrintService.IsPaused = false;
                                AutoPrintService_StatusChanged(this, "Running");
                            }
                            else
                            {
                                BtnStopAutoPrint_Click(null, null);
                            }
                        });
                    }
                }
                else
                {
                    // Alert IMMEDIATELY if no files are "Sent to Printer" but they are not all completed (e.g. Printer Offline, Paper Out)
                    _isWaitingForBatch = false;
                    System.Windows.Application.Current.Dispatcher.Invoke(() => 
                    {
                        bool isYes = CustomConfirmWindow.ShowConfirm(
                            "Batch Print Warning",
                            "Some files are stuck in an unknown state (not 'Sent to Printer').\nDo you want to continue printing the next batch?",
                            AlertType.Warning);
                            
                        if (isYes)
                        {
                            _currentBatchJobs.Clear();
                            _batchCounter = 0;
                            _autoPrintService.IsPaused = false;
                            AutoPrintService_StatusChanged(this, "Running");
                        }
                        else
                        {
                            BtnStopAutoPrint_Click(null, null);
                        }
                    });
                }
            }
        }

        private void AbortShutdown()
        {
            _shutdownDelayCts?.Cancel();
            Dispatcher.Invoke(() =>
            {
                _shutdownWindow?.AbortExternally();
            });

            try
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo("shutdown", "-a") { CreateNoWindow = true, UseShellExecute = false });
            }
            catch { }
        }

        private async Task SendTelegramMessageAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(_appSettings.TelegramBotToken) || string.IsNullOrWhiteSpace(_appSettings.TelegramChatId))
                return;

            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    string baseUrl = string.IsNullOrWhiteSpace(_appSettings.TelegramBotUrl) ? "https://api.telegram.org/bot" : _appSettings.TelegramBotUrl;
                    string url = $"{baseUrl}{_appSettings.TelegramBotToken}/sendMessage";
                    
                    var chatIds = _appSettings.TelegramChatId.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var chatId in chatIds)
                    {
                        string trimmedId = chatId.Trim();
                        if (string.IsNullOrEmpty(trimmedId)) continue;

                        var payload = new
                        {
                            chat_id = trimmedId,
                            text = message,
                            parse_mode = "Markdown"
                        };
                        
                        string json = System.Text.Json.JsonSerializer.Serialize(payload);
                        var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                        await client.PostAsync(url, content);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to send Telegram message: {ex.Message}");
            }
        }

        private async Task SendTelegramMessageWithDocumentAsync(string message, string filePath)
        {
            if (string.IsNullOrWhiteSpace(_appSettings.TelegramBotToken) || string.IsNullOrWhiteSpace(_appSettings.TelegramChatId))
                return;

            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            {
                await SendTelegramMessageAsync(message);
                return;
            }

            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    string baseUrl = string.IsNullOrWhiteSpace(_appSettings.TelegramBotUrl) ? "https://api.telegram.org/bot" : _appSettings.TelegramBotUrl;
                    string url = $"{baseUrl}{_appSettings.TelegramBotToken}/sendDocument";
                    
                    var chatIds = _appSettings.TelegramChatId.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var chatId in chatIds)
                    {
                        string trimmedId = chatId.Trim();
                        if (string.IsNullOrEmpty(trimmedId)) continue;

                        using (var form = new System.Net.Http.MultipartFormDataContent())
                        {
                            form.Add(new System.Net.Http.StringContent(trimmedId), "chat_id");
                            form.Add(new System.Net.Http.StringContent(message), "caption");
                            form.Add(new System.Net.Http.StringContent("Markdown"), "parse_mode");

                            var fileContent = new System.Net.Http.ByteArrayContent(System.IO.File.ReadAllBytes(filePath));
                            fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/csv");
                            form.Add(fileContent, "document", System.IO.Path.GetFileName(filePath));

                            await client.PostAsync(url, form);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to send Telegram document: {ex.Message}");
                await SendTelegramMessageAsync(message);
            }
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
            var latestSettings = SettingsManager.LoadSettings();
            latestSettings.SourceFolderPath = txtWatchFolder.Text;
            latestSettings.FoxitPath = txtFoxitPath.Text;
            _appSettings = latestSettings;
            
            SettingsManager.SaveSettings(_appSettings);
            CustomAlertWindow.ShowMessage("Success", "Settings saved successfully.", AlertType.Information);
        }

        private void BtnStartAutoPrint_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtWatchFolder.Text) || !Directory.Exists(txtWatchFolder.Text))
            {
                CustomAlertWindow.ShowMessage("Error", "Please select a valid folder to watch.", AlertType.Error);
                return;
            }

            var files = System.IO.Directory.GetFiles(txtWatchFolder.Text, "*.pdf")
                                 .Where(f => !f.Contains("Complete Print", StringComparison.OrdinalIgnoreCase))
                                 .ToList();

            if (files.Count == 0)
            {
                CustomAlertWindow.ShowMessage("No Files", "មិនមានឯកសារត្រូវព្រីននៅក្នុង Folder ទេ! សូមដាក់ឯកសារជាមុនសិន។", AlertType.Warning);
                return;
            }

            

            // Ensure settings are saved before starting
            BtnSaveSettings_Click(null, null);

            _autoPrintService.Start(_appSettings);
            
            _runStopwatch.Start();
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
                if (_autoPrintService.IsPrinting)
                {
                    AutoPrintService_StatusChanged(this, "Pausing after current print...");
                }
                else
                {
                    AutoPrintService_StatusChanged(this, "Paused");
                }
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
            _isWaitingForBatch = false;
            _currentBatchJobs.Clear();
            _batchCounter = 0;
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
                CustomAlertWindow.ShowMessage("Auto Stop", "Auto Print has finished processing all files in the folder and is now stopped.", AlertType.Information);
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
            _currentAutoPrintFile = fileName;
            Dispatcher.Invoke(() =>
            {
                txtCurrentFile.Text = fileName;
            });
        }

        private void AutoPrintService_FileProcessingCompleted(object sender, (string FileName, bool Success) e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_appSettings.EnableBatchPrinting && !_isWaitingForBatch && _autoPrintService != null && _autoPrintService.IsRunning && e.Success)
                {
                    _batchCounter++;
                    if (_batchCounter >= _appSettings.BatchSize)
                    {
                        _autoPrintService.IsPaused = true;
                        _isWaitingForBatch = true;
                        _batchWaitStartTime = DateTime.Now;
                        UpdateStatusUI();
                    }
                }

                if (e.Success)
                {
                    // Fallback: Ensure job is tracked even if WMI drops the event (e.g. printing too fast)
                    Task.Run(async () =>
                    {
                        await Task.Delay(3000); // Give WMI 3s to capture the event
                        Dispatcher.Invoke(() =>
                        {
                            var exists = _printJobs.Any(j => 
                                (string.Equals(j.DocumentName, e.FileName, StringComparison.OrdinalIgnoreCase) || 
                                 string.Equals(j.WebFileName, e.FileName, StringComparison.OrdinalIgnoreCase)) &&
                                (DateTime.Now - DateTime.Parse(j.Timestamp)).TotalMinutes < 5);
                                
                            if (!exists)
                            {
                                // WMI missed it! Add it manually to ensure tracking
                                PrintTrackerApp.Services.AutoPrintService.ParseDynamicFileInfo(e.FileName, _appSettings.HoldPrintUserId, _appSettings.AutoPrintCopies, out string userId, out string fileName, out int copies);
                                
                                var fallbackJob = new PrintJobInfo
                                {
                                    JobId = Guid.NewGuid().ToString(),
                                    DocumentName = e.FileName,
                                    WebFileName = fileName,
                                    RicohUserId = userId,
                                    Copies = copies,
                                    TotalPages = 1,
                                    Owner = Environment.UserName,
                                    PrinterName = _appSettings.PrinterIp ?? "Unknown",
                                    Status = "Sent to Printer",
                                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                    WebJobId = -1
                                };
                                
                                // Attempt to parse exact PDF pages just like SpoolerMonitor does
                                string? physicalFile = FindPhysicalFileForJob(fallbackJob);
                                bool parsedPdfPages = false;
                                if (!string.IsNullOrEmpty(physicalFile) && System.IO.File.Exists(physicalFile))
                                {
                                    try
                                    {
                                        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                                        using (var document = PdfReader.Open(physicalFile, PdfDocumentOpenMode.Import))
                                        {
                                            if (document.PageCount > 0)
                                            {
                                                fallbackJob.TotalPages = document.PageCount;
                                                parsedPdfPages = true;
                                            }
                                        }
                                    }
                                    catch { }
                                }
                                
                                if (parsedPdfPages) fallbackJob.IsPdfPageCountAccurate = true;
                                
                                _printJobs.Insert(0, fallbackJob);
                                if (_currentActiveJob == null) _currentActiveJob = fallbackJob;
                                ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
                            }
                        });
                    });
                }
            });
        }

        private void AutoPrintService_PauseRequested(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_autoPrintService.IsRunning && _autoPrintService.IsPaused)
                {
                    _runStopwatch.Stop();
                    UpdateStatusUI();
                }
            });
        }

        private void UpdateStatusUI()
        {
            bool isRunning = _autoPrintService != null && _autoPrintService.IsRunning;
            bool isPaused = _autoPrintService != null && _autoPrintService.IsPaused;
            
            btnStartAutoPrint.IsEnabled = !isRunning;
            btnStopAutoPrint.IsEnabled = isRunning;
            btnPauseAutoPrint.IsEnabled = isRunning;
            
            pnlTopControls.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
            
            if (isPaused)
            {
                btnPauseAutoPrint.Content = "RESUME";
                btnPauseAutoPrint.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B")); // Amber
                btnPauseAutoPrint.Foreground = System.Windows.Media.Brushes.White;
                
                // Update top icon
                if (txtTopPauseIcon != null)
                {
                    txtTopPauseIcon.Text = "▶";
                    txtTopPauseIcon.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981")); // Green
                    txtTopPauseText.Text = "Resume";
                    txtTopPauseText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981")); // Green
                }
            }
            else
            {
                btnPauseAutoPrint.Content = "PAUSE";
                btnPauseAutoPrint.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F3F4F6")); // Default secondary
                btnPauseAutoPrint.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1F2937"));
                
                // Update top icon
                if (txtTopPauseIcon != null)
                {
                    txtTopPauseIcon.Text = "⏸";
                    txtTopPauseIcon.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D97706")); // Amber
                    txtTopPauseText.Text = "Pause";
                    txtTopPauseText.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D97706")); // Amber
                }
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
                new AndCondition(
                    new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)));

            foreach (AutomationElement window in windows)
            {
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
                    
                    AutomationElement copiesTextBox = printDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "10408"));
                    AutomationElement copiesSpinner = printDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "10590"));
                    
                    if (copiesTextBox != null)
                    {
                        AutoPrintService.SetTextElement(copiesTextBox, dynamicCopies.ToString());
                        System.Windows.MessageBox.Show($"Copies set to {dynamicCopies} via UI Automation (TextBox ID: 10408).");
                    }
                    else if (copiesSpinner != null)
                    {
                        if (copiesSpinner.TryGetCurrentPattern(System.Windows.Automation.RangeValuePattern.Pattern, out object rangePattern))
                        {
                            ((System.Windows.Automation.RangeValuePattern)rangePattern).SetValue(dynamicCopies);
                        }
                        else
                        {
                            try { copiesSpinner.SetFocus(); } catch { }
                            Thread.Sleep(200);
                            System.Windows.Forms.SendKeys.SendWait("^{HOME}^+{END}{BACKSPACE}");
                            System.Windows.Forms.SendKeys.SendWait(dynamicCopies.ToString());
                        }
                        System.Windows.MessageBox.Show($"Copies set to {dynamicCopies} via UI Automation (Spinner ID: 10590).");
                    }
                    else
                    {
                        System.Windows.Forms.SendKeys.SendWait("%c");
                        Thread.Sleep(200);
                        System.Windows.Forms.SendKeys.SendWait(dynamicCopies.ToString());
                        Thread.Sleep(200);
                        System.Windows.MessageBox.Show($"Copies set to {dynamicCopies} via SendKeys. (Could not find ID 10408 or 10590)");
                    }
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
                string targetId = isLandscape ? _appSettings.FoxitShortEdgeRadioBtnId : _appSettings.FoxitLongEdgeRadioBtnId;
                
                AutomationElement edgeBtn = null;
                if (!string.IsNullOrEmpty(targetId))
                {
                    edgeBtn = printDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, targetId));
                }
                
                if (edgeBtn == null)
                {
                    edgeBtn = printDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, targetName));
                }
                
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
    
        private void AutoUpdaterOnCheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            if (args != null && args.IsUpdateAvailable)
            {
                _pendingUpdate = args;
                
                // Show badge
                Dispatcher.Invoke(() => {
                    borderUpdateBadge.Visibility = Visibility.Visible;
                });

                // Show custom update window
                Dispatcher.Invoke(() => {
                    var updateWindow = new UpdateWindow(args);
                    updateWindow.Owner = this;
                    updateWindow.ShowDialog();

                    if (updateWindow.DelayMinutes > 0)
                    {
                        // User chose to be reminded later
                        _updateReminderTimer.Interval = TimeSpan.FromMinutes(updateWindow.DelayMinutes);
                        _updateReminderTimer.Start();
                    }
                });
            }
            else
            {
                // No update or error
                _pendingUpdate = null;
                Dispatcher.Invoke(() => {
                    borderUpdateBadge.Visibility = Visibility.Collapsed;
                });
            }
        }

        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingUpdate != null && _pendingUpdate.IsUpdateAvailable)
            {
                var updateWindow = new UpdateWindow(_pendingUpdate);
                updateWindow.Owner = this;
                updateWindow.ShowDialog();

                if (updateWindow.DelayMinutes > 0)
                {
                    _updateReminderTimer.Interval = TimeSpan.FromMinutes(updateWindow.DelayMinutes);
                    _updateReminderTimer.Start();
                }
            }
            else
            {
                // Force check again
                AutoUpdater.Start("https://raw.githubusercontent.com/DPSSCOPY/PrintTrackerApp/main/AutoUpdater.xml");
            }
        }

        private void RemoveJobsBeforeMove(System.Collections.Generic.List<string> fileNames)
        {
            if (fileNames == null || fileNames.Count == 0) return;

            var jobsToRemove = _printJobs.Where(job => 
                fileNames.Any(mf => 
                {
                    string fileName = mf;
                    string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(mf);
                    string docName = job.DocumentName;
                    string webFileName = job.WebFileName;

                    if (string.IsNullOrEmpty(docName)) return false;

                    return string.Equals(fileName, docName, StringComparison.OrdinalIgnoreCase) ||
                           docName.IndexOf(fileNameWithoutExt, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           fileNameWithoutExt.IndexOf(docName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           (!string.IsNullOrEmpty(webFileName) && fileNameWithoutExt.IndexOf(webFileName, StringComparison.OrdinalIgnoreCase) >= 0);
                })).ToList();

            foreach (var job in jobsToRemove)
            {
                _printJobs.Remove(job);
            }

            if (jobsToRemove.Count > 0)
            {
                ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
            }
        }

        private void BtnShowSentToPrinter_Click(object sender, RoutedEventArgs e)
        {
            var window = new ErrorFilesWindow(_appSettings.SourceFolderPath, ErrorWindowMode.SentToPrinter, RemoveJobsBeforeMove);
            window.Owner = this;
            window.ShowDialog();
            
            UpdateVerificationPanel();
        }

        private void BtnShowErrorFiles_Click(object sender, RoutedEventArgs e)
        {
            var errorWindow = new ErrorFilesWindow(_appSettings.SourceFolderPath, ErrorWindowMode.ErrorFiles, RemoveJobsBeforeMove);
            errorWindow.Owner = this;
            errorWindow.ShowDialog();

            UpdateVerificationPanel();
        }

        private void BtnShowPrintComplete_Click(object sender, RoutedEventArgs e)
        {
            var window = new ErrorFilesWindow(_appSettings.SourceFolderPath, ErrorWindowMode.PrintComplete, RemoveJobsBeforeMove);
            window.Owner = this;
            window.ShowDialog();
            
            UpdateVerificationPanel();
        }

        private void BtnAdvancedAutoPrintSettings_Click(object sender, RoutedEventArgs e)
        {
            // Ensure we pass the absolute latest settings (including any UI IDs updated)
            _appSettings = SettingsManager.LoadSettings();
            var advancedWindow = new AdvancedAutoPrintSettingsWindow(_appSettings);
            advancedWindow.Owner = this;
            if (advancedWindow.ShowDialog() == true)
            {
                _appSettings = SettingsManager.LoadSettings();
            }
        }

        private void MenuItemDeleteJob_Click(object sender, RoutedEventArgs e)
        {
            var itemsToRemove = new HashSet<PrintTrackerApp.Models.PrintJobInfo>();
            if (dgPrintJobs.SelectedItems != null)
            {
                foreach (var item in dgPrintJobs.SelectedItems.OfType<PrintTrackerApp.Models.PrintJobInfo>()) itemsToRemove.Add(item);
            }
            if (dgPrintJobs.SelectedCells != null)
            {
                foreach (var cell in dgPrintJobs.SelectedCells)
                {
                    if (cell.Item is PrintTrackerApp.Models.PrintJobInfo job) itemsToRemove.Add(job);
                }
            }

            if (itemsToRemove.Count > 0)
            {
                var result = System.Windows.MessageBox.Show($"Are you sure you want to delete {itemsToRemove.Count} selected job(s)?", "Confirm Delete", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    foreach (var item in itemsToRemove.ToList())
                    {
                        _printJobs.Remove(item);
                    }
                    ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
                }
            }
        }

        private void MenuItemCancelMenu_Click(object sender, RoutedEventArgs e)
        {
            var itemsToCancel = new HashSet<PrintTrackerApp.Models.PrintJobInfo>();
            if (dgPrintJobs.SelectedItems != null)
            {
                foreach (var item in dgPrintJobs.SelectedItems.OfType<PrintTrackerApp.Models.PrintJobInfo>()) itemsToCancel.Add(item);
            }
            if (dgPrintJobs.SelectedCells != null)
            {
                foreach (var cell in dgPrintJobs.SelectedCells)
                {
                    if (cell.Item is PrintTrackerApp.Models.PrintJobInfo job) itemsToCancel.Add(job);
                }
            }

            if (itemsToCancel.Count > 0)
            {
                var result = System.Windows.MessageBox.Show($"Are you sure you want to mark {itemsToCancel.Count} selected job(s) as Cancelled?", "Confirm Cancel", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    foreach (var item in itemsToCancel.ToList())
                    {
                        item.Status = "Cancelled";
                    }
                    ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
                }
            }
        }

        private void DgPrintJobs_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                e.Handled = true;
                double zoomStep = 0.1;
                if (e.Delta > 0)
                {
                    dgScale.ScaleX += zoomStep;
                    dgScale.ScaleY += zoomStep;
                }
                else
                {
                    if (dgScale.ScaleX > 0.5) // limit min zoom to 50%
                    {
                        dgScale.ScaleX -= zoomStep;
                        dgScale.ScaleY -= zoomStep;
                    }
                }
            }
        }

        private void DataGrid_MouseDoubleClick_AutoFit(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.DataGridColumnHeader header)
            {
                if (header.Column != null)
                {
                    header.Column.Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Auto);
                    e.Handled = true;
                }
            }
            else if (sender is System.Windows.Controls.DataGridCell cell)
            {
                if (cell.Column != null)
                {
                    cell.Column.Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Auto);
                    e.Handled = true;
                }
            }
        }
        
        private void AutoScrollTimer_Tick(object sender, EventArgs e)
        {
            if (!_isDraggingFill || _scrollViewer == null) return;
            var point = System.Windows.Input.Mouse.GetPosition(dgPrintJobs);
            double zone = 30;
            if (point.Y < zone) _scrollViewer.LineUp();
            else if (point.Y > dgPrintJobs.ActualHeight - zone) _scrollViewer.LineDown();
            
            if (point.X < zone) _scrollViewer.LineLeft();
            else if (point.X > dgPrintJobs.ActualWidth - zone) _scrollViewer.LineRight();
        }

        private void DgPrintJobs_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingFill)
            {
                var point = e.GetPosition(dgPrintJobs);
                var currentCell = GetCellFromPoint(point);
                if (currentCell != null)
                {
                    var colIdx = currentCell.Column.DisplayIndex;
                    var rowView = currentCell.DataContext as PrintJobInfo;
                    if (rowView != null)
                    {
                        var rowIdx = dgPrintJobs.Items.IndexOf(rowView);
                        if (rowIdx >= 0)
                        {
                            int minRow = Math.Min(_fillStartRowIdx, rowIdx);
                            int maxRow = Math.Max(_fillStartRowIdx, rowIdx);
                            int minCol = Math.Min(_fillStartColIdx, colIdx);
                            int maxCol = Math.Max(_fillStartColIdx, colIdx);

                            dgPrintJobs.SelectedCells.Clear();
                            for (int r = minRow; r <= maxRow; r++)
                            {
                                var item = dgPrintJobs.Items[r];
                                for (int c = minCol; c <= maxCol; c++)
                                {
                                    var col = dgPrintJobs.Columns.FirstOrDefault(x => x.DisplayIndex == c);
                                    if (col != null)
                                    {
                                        dgPrintJobs.SelectedCells.Add(new System.Windows.Controls.DataGridCellInfo(item, col));
                                    }
                                }
                            }
                        }
                    }
                }
                return;
            }

            var cell = GetCellFromPoint(e.GetPosition(dgPrintJobs));
            if (cell != null && cell.IsSelected && !cell.Column.IsReadOnly)
            {
                var cellPoint = e.GetPosition(cell);
                if (cellPoint.X >= cell.ActualWidth - 10 && cellPoint.Y >= cell.ActualHeight - 10)
                {
                    System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Cross;
                    return;
                }
            }
            System.Windows.Input.Mouse.OverrideCursor = null;
        }

        private void DgPrintJobs_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (System.Windows.Input.Mouse.OverrideCursor == System.Windows.Input.Cursors.Cross)
            {
                var cell = GetCellFromPoint(e.GetPosition(dgPrintJobs));
                if (cell != null && !cell.Column.IsReadOnly)
                {
                    var rowView = cell.DataContext as PrintJobInfo;
                    if (rowView != null)
                    {
                        _isDraggingFill = true;
                        _scrollViewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(dgPrintJobs);
                        _autoScrollTimer.Start();
                        
                        _fillStartCellInfo = new System.Windows.Controls.DataGridCellInfo(cell);
                        _fillStartColIdx = cell.Column.DisplayIndex;
                        _fillStartRowIdx = dgPrintJobs.Items.IndexOf(rowView);

                        string bindingPath = (cell.Column.ClipboardContentBinding as System.Windows.Data.Binding)?.Path?.Path 
                                             ?? ((cell.Column as System.Windows.Controls.DataGridTextColumn)?.Binding as System.Windows.Data.Binding)?.Path?.Path;
                        
                        if (!string.IsNullOrEmpty(bindingPath))
                        {
                            _fillSourceValue = GetPrintJobValue(rowView, bindingPath);
                        }

                        e.Handled = true;
                        dgPrintJobs.CaptureMouse();
                    }
                }
            }
        }

        private void DgPrintJobs_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDraggingFill)
            {
                _isDraggingFill = false;
                _autoScrollTimer.Stop();
                dgPrintJobs.ReleaseMouseCapture();
                System.Windows.Input.Mouse.OverrideCursor = null;

                if (_fillSourceValue != null)
                {
                    _currentUndoBatch = new PrintTrackerApp.Services.UndoBatch();
                    bool requiresRefresh = false;

                    foreach (var cellInfo in dgPrintJobs.SelectedCells)
                    {
                        if (cellInfo.Column.IsReadOnly) continue;
                        
                        var rowView = cellInfo.Item as PrintJobInfo;
                        if (rowView != null)
                        {
                            string bindingPath = (cellInfo.Column.ClipboardContentBinding as System.Windows.Data.Binding)?.Path?.Path 
                                                 ?? ((cellInfo.Column as System.Windows.Controls.DataGridTextColumn)?.Binding as System.Windows.Data.Binding)?.Path?.Path;
                            if (!string.IsNullOrEmpty(bindingPath))
                            {
                                object oldVal = GetPrintJobValue(rowView, bindingPath);
                                if (!Equals(oldVal, _fillSourceValue))
                                {
                                    if (bindingPath.StartsWith("DynamicProperties[")) requiresRefresh = true;
                                    var jobCapture = rowView;
                                    var pathCapture = bindingPath;
                                    var oldCapture = oldVal;
                                    var newCapture = _fillSourceValue;
                                    
                                    _currentUndoBatch.UndoActions.Add(() => SetPrintJobValue(jobCapture, pathCapture, oldCapture, false));
                                    _currentUndoBatch.RedoActions.Add(() => SetPrintJobValue(jobCapture, pathCapture, newCapture, false));
                                    
                                    SetPrintJobValue(rowView, bindingPath, _fillSourceValue, true);
                                }
                            }
                        }
                    }
                    
                    if (_currentUndoBatch.UndoActions.Count > 0)
                        _undoManager.AddBatch(_currentUndoBatch);
                    _currentUndoBatch = null;

                    if (requiresRefresh) dgPrintJobs.Items.Refresh();
                    if (_historicalPrintJobs == null) ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
                }
            }
        }
        
        private void DgPrintJobs_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == System.Windows.Controls.DataGridEditAction.Commit)
            {
                var job = e.Row.Item as PrintJobInfo;
                if (job == null) return;
                
                var column = e.Column as System.Windows.Controls.DataGridBoundColumn;
                if (column?.Binding is System.Windows.Data.Binding binding)
                {
                    string propertyName = binding.Path.Path;
                    object oldValue = GetPrintJobValue(job, propertyName);

                    Dispatcher.InvokeAsync(() =>
                    {
                        object newValue = GetPrintJobValue(job, propertyName);
                        if (!Equals(oldValue, newValue))
                        {
                            var batch = new PrintTrackerApp.Services.UndoBatch();
                            var jobCapture = job;
                            var pathCapture = propertyName;
                            var oldCapture = oldValue;
                            var newCapture = newValue;
                            
                            batch.UndoActions.Add(() => SetPrintJobValue(jobCapture, pathCapture, oldCapture, true));
                            batch.RedoActions.Add(() => SetPrintJobValue(jobCapture, pathCapture, newCapture, true));
                            _undoManager.AddBatch(batch);

                            if (propertyName == "Status")
                            {
                                string newStatus = newValue?.ToString() ?? "";
                                _ = System.Threading.Tasks.Task.Run(() => UpdateFileStatusLocation(job, newStatus));
                            }

                            if (_historicalPrintJobs == null)
                            {
                                ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
                            }
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        private void DgPrintJobs_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                // Let the user delete via keyboard
                MenuItemDeleteJob_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                if (e.Key == System.Windows.Input.Key.C)
                {
                    System.Windows.Input.ApplicationCommands.Copy.Execute(null, dgPrintJobs);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.V)
                {
                    string clipboardData = System.Windows.Clipboard.GetText();
                    if (!string.IsNullOrEmpty(clipboardData) && dgPrintJobs.CurrentCell.Item != null)
                    {
                        string[] rows = clipboardData.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                        int startRowIdx = dgPrintJobs.Items.IndexOf(dgPrintJobs.CurrentCell.Item);
                        int startColIdx = dgPrintJobs.CurrentCell.Column.DisplayIndex;

                        var batch = new PrintTrackerApp.Services.UndoBatch();
                        bool requiresRefresh = false;

                        for (int r = 0; r < rows.Length; r++)
                        {
                            if (startRowIdx + r >= dgPrintJobs.Items.Count) break;
                            var rowView = dgPrintJobs.Items[startRowIdx + r] as PrintJobInfo;
                            if (rowView == null) continue;

                            string[] cells = rows[r].Split('\t');
                            for (int c = 0; c < cells.Length; c++)
                            {
                                var col = dgPrintJobs.Columns.FirstOrDefault(x => x.DisplayIndex == startColIdx + c);
                                if (col == null || col.IsReadOnly) continue;

                                string bindingPath = (col.ClipboardContentBinding as System.Windows.Data.Binding)?.Path?.Path 
                                                     ?? ((col as System.Windows.Controls.DataGridTextColumn)?.Binding as System.Windows.Data.Binding)?.Path?.Path;
                                
                                if (!string.IsNullOrEmpty(bindingPath))
                                {
                                    object oldVal = GetPrintJobValue(rowView, bindingPath);
                                    if (!Equals(oldVal, cells[c]))
                                    {
                                        if (bindingPath.StartsWith("DynamicProperties[")) requiresRefresh = true;
                                        var jobCapture = rowView;
                                        var pathCapture = bindingPath;
                                        var oldCapture = oldVal;
                                        var newCapture = cells[c];
                                        
                                        batch.UndoActions.Add(() => SetPrintJobValue(jobCapture, pathCapture, oldCapture, false));
                                        batch.RedoActions.Add(() => SetPrintJobValue(jobCapture, pathCapture, newCapture, false));
                                        
                                        SetPrintJobValue(rowView, bindingPath, cells[c], true);
                                    }
                                }
                            }
                        }
                        
                        if (batch.UndoActions.Count > 0)
                        {
                            _undoManager.AddBatch(batch);
                            if (requiresRefresh) dgPrintJobs.Items.Refresh();
                            if (_historicalPrintJobs == null) ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
                        }
                    }
                    e.Handled = true;
                }
            }
        }

        private void MainTabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (e.Source is System.Windows.Controls.TabControl && tabDashboard != null && tabDashboard.IsSelected)
            {
                // Re-initialize with the latest jobs if the user switches to the Dashboard tab
                teacherDashboard.InitializeData(_printJobs, _appSettings.CsvExportPath);
            }
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

        private ObservableCollection<PrintJobInfo> _historicalPrintJobs;

        private void BtnLoadHistoricalLog_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Supported Files|*.csv;*.xlsx;*.xls|CSV Files|*.csv|Excel Files|*.xls;*.xlsx";
            openFileDialog.Multiselect = true;
            if (openFileDialog.ShowDialog() == true)
            {
                var allExternalJobs = new List<PrintJobInfo>();
                var errorMessages = new List<string>();

                foreach (var fileName in openFileDialog.FileNames)
                {
                    string errorMessage;
                    var externalJobs = PrintTrackerApp.Services.CsvLogger.LoadExternalCsvLog(fileName, out errorMessage);
                    if (externalJobs.Count > 0)
                    {
                        allExternalJobs.AddRange(externalJobs);
                    }
                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        errorMessages.Add($"{System.IO.Path.GetFileName(fileName)}: {errorMessage}");
                    }
                }

                if (allExternalJobs.Count > 0)
                {
                    // Sort by newest first, as per usual
                    allExternalJobs = allExternalJobs.OrderByDescending(j => j.Timestamp).ToList();
                    
                    _historicalPrintJobs = new ObservableCollection<PrintJobInfo>(allExternalJobs);
                    dgPrintJobs.ItemsSource = _historicalPrintJobs;
                    btnResetLiveLog.Visibility = Visibility.Visible;
                    
                    // Force refresh filter
                    TxtSearch_TextChanged(null, null);
                    UpdateVerificationPanel();
                }

                if (errorMessages.Count > 0)
                {
                    System.Windows.MessageBox.Show($"Some errors occurred:\n{string.Join("\n", errorMessages)}", "Warnings", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnResetLiveLog_Click(object sender, RoutedEventArgs e)
        {
            _historicalPrintJobs = null;
            dgPrintJobs.ItemsSource = _printJobs;
            btnResetLiveLog.Visibility = Visibility.Collapsed;
            
            // Force refresh filter
            TxtSearch_TextChanged(null, null);
        }
        private void MenuItemAddColumn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("Add Column");
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                string columnName = dialog.InputText;
                var newCol = new System.Windows.Controls.DataGridTextColumn
                {
                    Header = columnName,
                    Binding = new System.Windows.Data.Binding($"DynamicProperties[{columnName}]") 
                    { 
                        Mode = System.Windows.Data.BindingMode.TwoWay, 
                        UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged 
                    }
                };
                dgPrintJobs.Columns.Add(newCol);
            }
        }
        private void MenuItemAddBlankRow_Click(object sender, RoutedEventArgs e)
        {
            var newJob = new PrintJobInfo
            {
                JobId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                DocumentName = "",
                WebFileName = "",
                RicohUserId = "",
                TotalPages = 1,
                Copies = 1,
                Owner = "",
                PrinterName = "",
                Status = "Manual Entry"
            };
            
            if (_historicalPrintJobs != null)
            {
                _historicalPrintJobs.Insert(0, newJob);
            }
            else
            {
                _printJobs.Insert(0, newJob);
            }
            
            dgPrintJobs.SelectedIndex = 0;
            dgPrintJobs.Focus();
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                if (e.Key == System.Windows.Input.Key.Z)
                {
                    _undoManager.Undo();
                    dgPrintJobs.Items.Refresh();
                    if (_historicalPrintJobs == null) ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Y)
                {
                    _undoManager.Redo();
                    dgPrintJobs.Items.Refresh();
                    if (_historicalPrintJobs == null) ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.S)
                {
                    e.Handled = true;
                    if (_historicalPrintJobs != null)
                    {
                        ExportJobsToCsvSafely(_historicalPrintJobs, _appSettings.CsvExportPath);
                    }
                    else
                    {
                        ExportJobsToCsvSafely(_printJobs, _appSettings.CsvExportPath);
                    }
                    
                    System.Windows.MessageBox.Show("Saved successfully.", "Save", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }
}
