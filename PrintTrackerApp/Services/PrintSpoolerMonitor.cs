using System;
using System.Management;
using System.Printing;
using PrintTrackerApp.Models;

namespace PrintTrackerApp.Services
{
    public class PrintSpoolerMonitor
    {
        private ManagementEventWatcher? _watcherCreation;
        private ManagementEventWatcher? _watcherDeletion;
        private ManagementEventWatcher? _watcherModification;
        private readonly string _printerName;
        public event EventHandler<PrintJobInfo>? OnJobCreated;
        public event EventHandler<PrintJobInfo>? OnJobUpdated;
        public event EventHandler<(string JobId, string JobStatus, int PagesPrinted)>? OnJobDeleted;
        public event EventHandler<(string JobId, int TotalPages)>? OnJobPagesUpdated;

        public PrintSpoolerMonitor(string printerName)
        {
            _printerName = printerName;
        }

        public void Start()
        {
            try
            {
                string query = "SELECT * FROM __InstanceCreationEvent WITHIN 0.1 WHERE TargetInstance ISA 'Win32_PrintJob'";
                _watcherCreation = new ManagementEventWatcher(query);
                _watcherCreation.EventArrived += Watcher_EventArrived;
                _watcherCreation.Start();

                string delQuery = "SELECT * FROM __InstanceDeletionEvent WITHIN 0.1 WHERE TargetInstance ISA 'Win32_PrintJob'";
                _watcherDeletion = new ManagementEventWatcher(delQuery);
                _watcherDeletion.EventArrived += WatcherDeletion_EventArrived;
                _watcherDeletion.Start();

                string modQuery = "SELECT * FROM __InstanceModificationEvent WITHIN 0.1 WHERE TargetInstance ISA 'Win32_PrintJob'";
                _watcherModification = new ManagementEventWatcher(modQuery);
                _watcherModification.EventArrived += WatcherModification_EventArrived;
                _watcherModification.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WMI] Error starting PrintSpoolerMonitor: {ex.Message}");
                System.IO.File.AppendAllText("wmi_debug.log", $"[{DateTime.Now}] Start Error: {ex.Message}\n");
            }
        }

        public void Stop()
        {
            if (_watcherCreation != null)
            {
                _watcherCreation.Stop();
                _watcherCreation.Dispose();
            }
            if (_watcherDeletion != null)
            {
                _watcherDeletion.Stop();
                _watcherDeletion.Dispose();
            }
            if (_watcherModification != null)
            {
                _watcherModification.Stop();
                _watcherModification.Dispose();
            }
        }

        private void Watcher_EventArrived(object sender, EventArrivedEventArgs e)
        {
            try 
            {
                var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                string name = targetInstance["Name"]?.ToString() ?? "";
                
                System.Diagnostics.Debug.WriteLine($"[WMI] Job Created Event: Name={name}, PrinterName={_printerName}");
                
            var job = new PrintJobInfo
            {
                JobId = targetInstance["JobId"]?.ToString() ?? "",
                DocumentName = targetInstance["Document"]?.ToString() ?? "Unknown",
                Owner = targetInstance["Owner"]?.ToString() ?? "Unknown",
                MachineName = targetInstance["HostPrintQueue"]?.ToString() ?? Environment.MachineName,
                TotalPages = Convert.ToInt32(targetInstance["TotalPages"] ?? 0)
            };
            
            // Extract actual printer name from WMI Name property (usually "PrinterName, JobId")
            job.PrinterName = name.Contains(",") ? name.Split(',')[0] : _printerName;
            
            // Pre-fill Web properties from PC properties for immediate display
            // Usually Ricoh default settings match these exactly.
            job.WebFileName = job.DocumentName;
            job.RicohUserId = job.Owner;

            // Attempt immediate pre-fill if DocumentName matches dynamic format (e.g. SMC3-Vannet-S1-9copies...)
            if (AutoPrintService.ParseDynamicFileInfo(job.DocumentName, job.Owner, 1, out string parsedUserId, out string parsedFileName, out int parsedCopies))
            {
                job.RicohUserId = parsedUserId;
                job.WebFileName = parsedFileName;
                if (parsedCopies > 1) job.Copies = parsedCopies;
            }

            if (job.TotalPages <= 0)
            {
                job.TotalPages = 1; // Fallback so it doesn't show 0 initially
            }

            OnJobCreated?.Invoke(this, job);

            // Attempt to parse actual custom User ID / Document Name from Ricoh .SPL file asynchronously
            // Large files might take 10-60 seconds to unlock the .SPL file for reading.
            System.Threading.Tasks.Task.Run(() => 
            {
                for (int i = 0; i < 60; i++) // Poll for up to 60 seconds
                {
                    var customDetails = SpoolFileParser.Parse(job.JobId);
                    if (customDetails != null)
                    {
                        bool changed = false;
                        if (!string.IsNullOrEmpty(customDetails.UserId) && job.RicohUserId != customDetails.UserId)
                        {
                            job.RicohUserId = customDetails.UserId;
                            changed = true;
                        }
                            
                        if (!string.IsNullOrEmpty(customDetails.DocumentName))
                        {
                            if (job.WebFileName != customDetails.DocumentName)
                            {
                                job.WebFileName = customDetails.DocumentName;
                                changed = true;
                            }
                            if (job.DocumentName == "Local Downlevel Document" || job.DocumentName == "Print Document" || job.DocumentName == "Remote Downlevel Document" || string.IsNullOrWhiteSpace(job.DocumentName))
                            {
                                job.DocumentName = customDetails.DocumentName;
                                changed = true;
                            }
                        }
                        
                        job.CleanDownlevelNames();
                            
                        if (customDetails.Copies > 1 && job.Copies != customDetails.Copies)
                        {
                            job.Copies = customDetails.Copies;
                            changed = true;
                        }

                        if (changed)
                        {
                            OnJobUpdated?.Invoke(this, job);
                        }
                        
                        // We strictly want the custom Document Name. If it's missing (maybe file partially written), don't break yet!
                        if (!string.IsNullOrEmpty(customDetails.DocumentName))
                        {
                            break;
                        }
                    }
                    System.Threading.Thread.Sleep(1000);
                }
            });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WMI] Error in Watcher_EventArrived: {ex}");
                System.IO.File.AppendAllText("wmi_debug.log", $"[{DateTime.Now}] WMI Event Error: {ex}\n");
            }
        }

        private void WatcherDeletion_EventArrived(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                string jobId = targetInstance["JobId"]?.ToString() ?? "";
                int totalPages = Convert.ToInt32(targetInstance["TotalPages"] ?? 0);
                
                if (totalPages > 0)
                {
                    OnJobPagesUpdated?.Invoke(this, (jobId, totalPages));
                }

                string jobStatus = targetInstance["JobStatus"]?.ToString() ?? "";
                int pagesPrinted = Convert.ToInt32(targetInstance["PagesPrinted"] ?? 0);

                OnJobDeleted?.Invoke(this, (jobId, jobStatus, pagesPrinted));
            }
            catch { }
        }

        private void WatcherModification_EventArrived(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                string jobId = targetInstance["JobId"]?.ToString() ?? "";
                int totalPages = Convert.ToInt32(targetInstance["TotalPages"] ?? 0);
                
                if (totalPages > 0)
                {
                    OnJobPagesUpdated?.Invoke(this, (jobId, totalPages));
                }
            }
            catch { }
        }

        public string GetPrinterStatus()
        {
            try
            {
                using var server = new LocalPrintServer();
                using var queue = server.GetPrintQueue(_printerName);
                queue.Refresh();
                
                var status = queue.QueueStatus;
                
                if (status == PrintQueueStatus.None) return "Idle";
                if (status.HasFlag(PrintQueueStatus.Printing)) return "Printing";
                if (status.HasFlag(PrintQueueStatus.Offline)) return "Offline";
                if (status.HasFlag(PrintQueueStatus.Error)) return "Error";
                if (status.HasFlag(PrintQueueStatus.PaperJam)) return "Paper Jam";
                if (status.HasFlag(PrintQueueStatus.PaperOut)) return "Out of Paper";
                if (status.HasFlag(PrintQueueStatus.Paused)) return "Paused";
                if (status.HasFlag(PrintQueueStatus.TonerLow)) return "Toner Low";
                if (status.HasFlag(PrintQueueStatus.NoToner)) return "No Toner";
                if (status.HasFlag(PrintQueueStatus.DoorOpen)) return "Door Open";
                if (status.HasFlag(PrintQueueStatus.UserIntervention)) return "Needs Attention";
                if (status.HasFlag(PrintQueueStatus.PendingDeletion)) return "Deleting...";
                if (status.HasFlag(PrintQueueStatus.Waiting)) return "Waiting";
                if (status.HasFlag(PrintQueueStatus.Initializing)) return "Initializing";
                if (status.HasFlag(PrintQueueStatus.WarmingUp)) return "Warming Up";
                if (status.HasFlag(PrintQueueStatus.Processing)) return "Processing";
                if (status.HasFlag(PrintQueueStatus.OutputBinFull)) return "Output Bin Full";
                if (status.HasFlag(PrintQueueStatus.NotAvailable)) return "Not Available";
                if (status.HasFlag(PrintQueueStatus.ServerUnknown)) return "Unknown";

                return status.ToString();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error getting printer status: " + ex.Message);
                return "Unknown / Not Found";
            }
        }
    }
}
