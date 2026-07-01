using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace PrintTrackerApp
{
    public partial class SystemDiagnosticWindow : Window
    {
        private readonly string _csvPath;
        private readonly string _printerName;
        private bool _isRunningAll = false;

        public SystemDiagnosticWindow(string csvPath, string printerName)
        {
            InitializeComponent();
            _csvPath = csvPath;
            _printerName = printerName;
        }

        private async void BtnRunAll_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunningAll) return;
            _isRunningAll = true;
            btnRunAll.IsEnabled = false;
            btnRunAll.Content = "🔄 Testing...";

            borderSummary.Visibility = Visibility.Visible;
            borderSummary.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 243, 199)); // Yellow #FEF3C7
            borderSummary.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11));
            txtSummary.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(146, 64, 14));
            txtSummary.Text = "⏳ Running all 5 diagnostic tests sequentially... Please wait.";

            int passedCount = 0;
            
            if (await RunDriverTestAsync()) passedCount++;
            await Task.Delay(300);
            
            if (await RunSpoolerTestAsync()) passedCount++;
            await Task.Delay(300);
            
            if (await RunFoxitTestAsync()) passedCount++;
            await Task.Delay(300);
            
            if (await RunWmiTestAsync()) passedCount++;
            await Task.Delay(300);
            
            if (await RunCsvTestAsync()) passedCount++;

            _isRunningAll = false;
            btnRunAll.IsEnabled = true;
            btnRunAll.Content = "🚀 Test All (ពិនិត្យទាំងអស់)";

            if (passedCount == 5)
            {
                borderSummary.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 250, 229)); // Green #D1FAE5
                borderSummary.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                txtSummary.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 95, 70));
                txtSummary.Text = "✅ ល្អប្រសើរ! (Passed 5/5) កុំព្យូទ័រនេះមានគ្រប់លក្ខខណ្ឌដំណើរការកូដបានលឿន និង Smooth ១០០% ដូច PC Debug ហើយ!";
            }
            else
            {
                borderSummary.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226)); // Red #FEE2E2
                borderSummary.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                txtSummary.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 27, 27));
                txtSummary.Text = $"⚠️ មានចំណុចត្រូវកែសម្រួល! (Passed {passedCount}/5) សូមពិនិត្យមើលចំណុចពណ៌ក្រហមខាងក្រោម ដើម្បីកែឱ្យម៉ាស៊ីននេះ Smooth ១០០%។";
            }
        }

        private async void BtnTestDriver_Click(object sender, RoutedEventArgs e)
        {
            await RunDriverTestAsync();
        }

        private async void BtnTestSpooler_Click(object sender, RoutedEventArgs e)
        {
            await RunSpoolerTestAsync();
        }

        private async void BtnTestFoxit_Click(object sender, RoutedEventArgs e)
        {
            await RunFoxitTestAsync();
        }

        private async void BtnTestWmi_Click(object sender, RoutedEventArgs e)
        {
            await RunWmiTestAsync();
        }

        private async void BtnTestCsv_Click(object sender, RoutedEventArgs e)
        {
            await RunCsvTestAsync();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async Task<bool> RunDriverTestAsync()
        {
            txtStatusDriver.Text = "🔄 Testing Ricoh Printer Driver...";
            txtStatusDriver.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 85, 99));

            return await Task.Run(() =>
            {
                try
                {
                    string targetPrinter = string.IsNullOrWhiteSpace(_printerName) ? "Ricoh" : _printerName;
                    string foundDriver = "";
                    string foundPrinter = "";

                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer"))
                    {
                        foreach (ManagementObject printer in searcher.Get())
                        {
                            string pName = printer["Name"]?.ToString() ?? "";
                            string dName = printer["DriverName"]?.ToString() ?? "";

                            if (pName.IndexOf(targetPrinter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                dName.IndexOf("Ricoh", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                foundPrinter = pName;
                                foundDriver = dName;
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(foundPrinter))
                    {
                        SetStatus(txtStatusDriver, false, $"❌ រកមិនឃើញម៉ាស៊ីន Print ឈ្មោះ '{targetPrinter}' ឬ Ricoh ឡើយ។ សូម Check ឈ្មោះក្នុង Settings។");
                        return false;
                    }

                    if (foundDriver.IndexOf("WSD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        foundDriver.IndexOf("IPP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        foundDriver.IndexOf("Class Driver", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        SetStatus(txtStatusDriver, false, $"❌ ប្រភេទ Driver មិនល្អ! [{foundPrinter}] កំពុងប្រើ Driver '{foundDriver}' (ដើរយឺត/គាំង)។ សូមដូរទៅប្រើ Ricoh PCL6 Driver ផ្លូវការវិញ!");
                        return false;
                    }

                    if (foundDriver.IndexOf("PCL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        foundDriver.IndexOf("Ricoh", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        SetStatus(txtStatusDriver, true, $"✅ ល្អឥតខ្ចោះ! [{foundPrinter}] កំពុងប្រើ PCL6 Driver ផ្លូវការ៖ '{foundDriver}'។");
                        return true;
                    }

                    SetStatus(txtStatusDriver, true, $"✅ ដំណើរការបាន! [{foundPrinter}] Driver: '{foundDriver}'។");
                    return true;
                }
                catch (Exception ex)
                {
                    SetStatus(txtStatusDriver, false, $"❌ Error checking driver: {ex.Message}");
                    return false;
                }
            });
        }

        private async Task<bool> RunSpoolerTestAsync()
        {
            txtStatusSpooler.Text = "🔄 Testing Spooler Service & Folder Access...";
            txtStatusSpooler.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 85, 99));

            return await Task.Run(() =>
            {
                try
                {
                    bool isRunning = false;
                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Service WHERE Name = 'Spooler'"))
                    {
                        foreach (ManagementObject service in searcher.Get())
                        {
                            string state = service["State"]?.ToString() ?? "";
                            if (string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase))
                            {
                                isRunning = true;
                                break;
                            }
                        }
                    }

                    if (!isRunning)
                    {
                        SetStatus(txtStatusSpooler, false, "❌ Windows Print Spooler Service អត់កំពុងដំណើរការទេ! សូម Start Service ក្នុង Services.msc។");
                        return false;
                    }

                    string spoolDir = @"C:\Windows\System32\spool\PRINTERS";
                    if (!Directory.Exists(spoolDir))
                    {
                        SetStatus(txtStatusSpooler, false, $"❌ រកមិនឃើញ Folder Spooler '{spoolDir}' ឡើយ។");
                        return false;
                    }

                    // Test writing dummy temp file
                    string testFile = Path.Combine(spoolDir, $"print_test_{Guid.NewGuid():N}.tmp");
                    File.WriteAllText(testFile, "test_spooler_access");
                    if (File.Exists(testFile))
                    {
                        File.Delete(testFile);
                    }

                    SetStatus(txtStatusSpooler, true, "✅ ល្អឥតខ្ចោះ! Print Spooler កំពុង Running ហើយសិទ្ធិអាន/សរសេរឯកសារក្នុង PRINTERS Folder លឿនល្អ (អត់មាន Antivirus Block ទេ)។");
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    SetStatus(txtStatusSpooler, false, "❌ អត់មានសិទ្ធិ (Access Denied) ចូល C:\\Windows\\System32\\spool\\PRINTERS! សូម Run As Administrator ឬ Add Folder នេះចូលក្នុង Antivirus Exclusion List!");
                    return false;
                }
                catch (Exception ex)
                {
                    SetStatus(txtStatusSpooler, false, $"❌ Spooler Test Error: {ex.Message}");
                    return false;
                }
            });
        }

        private async Task<bool> RunFoxitTestAsync()
        {
            txtStatusFoxit.Text = "🔄 Checking Foxit PDF Reader Accessibility...";
            txtStatusFoxit.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 85, 99));

            return await Task.Run(() =>
            {
                try
                {
                    string[] possiblePaths = new string[]
                    {
                        @"C:\Program Files (x86)\Foxit Software\Foxit PDF Reader\FoxitPDFReader.exe",
                        @"C:\Program Files\Foxit Software\Foxit PDF Reader\FoxitPDFReader.exe",
                        @"C:\Program Files (x86)\Foxit Software\Foxit Reader\FoxitReader.exe",
                        @"C:\Program Files\Foxit Software\Foxit Reader\FoxitReader.exe"
                    };

                    string foundPath = "";
                    foreach (string path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            foundPath = path;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(foundPath))
                    {
                        // Try checking App Paths in Registry
                        try
                        {
                            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\FoxitPDFReader.exe"))
                            {
                                if (key != null && key.GetValue("") != null)
                                {
                                    foundPath = key.GetValue("").ToString();
                                }
                            }
                        }
                        catch { }
                    }

                    if (string.IsNullOrEmpty(foundPath))
                    {
                        SetStatus(txtStatusFoxit, false, "❌ រកមិនឃើញកម្មវិធី Foxit PDF Reader លើកុំព្យូទ័រនេះទេ។ សូមដំឡើង Foxit PDF Reader ដើម្បីប្រើ Auto Print!");
                        return false;
                    }

                    SetStatus(txtStatusFoxit, true, $"✅ ល្អឥតខ្ចោះ! រកឃើញ Foxit PDF Reader ត្រៀមរួចជាស្រេច៖ '{foundPath}'។");
                    return true;
                }
                catch (Exception ex)
                {
                    SetStatus(txtStatusFoxit, false, $"❌ Foxit Test Error: {ex.Message}");
                    return false;
                }
            });
        }

        private async Task<bool> RunWmiTestAsync()
        {
            txtStatusWmi.Text = "🔄 Measuring WMI Query Speed & Health...";
            txtStatusWmi.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 85, 99));

            return await Task.Run(() =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    int count = 0;
                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PrintJob"))
                    {
                        foreach (var obj in searcher.Get())
                        {
                            count++;
                        }
                    }
                    sw.Stop();

                    long ms = sw.ElapsedMilliseconds;
                    if (ms > 1500)
                    {
                        SetStatus(txtStatusWmi, false, $"⚠️ ប្រព័ន្ធ WMI ឆ្លើយតបយឺត ({ms}ms)! នេះអាចធ្វើឱ្យការចាប់ Print យឺត។ សូម Refresh WMI ដោយ Run 'winmgmt /resetrepository' ក្នុង Admin PowerShell។");
                        return false;
                    }

                    SetStatus(txtStatusWmi, true, $"✅ ល្អឥតខ្ចោះ! ប្រព័ន្ធ WMI មានសុខភាពល្អ និងលឿនខ្លាំង (ឆ្លើយតបក្នុង {ms}ms)។");
                    return true;
                }
                catch (Exception ex)
                {
                    SetStatus(txtStatusWmi, false, $"❌ WMI Error: {ex.Message}. ប្រព័ន្ធ WMI លើម៉ាស៊ីននេះមានបញ្ហា សូម Run 'winmgmt /resetrepository'។");
                    return false;
                }
            });
        }

        private async Task<bool> RunCsvTestAsync()
        {
            txtStatusCsv.Text = "🔄 Testing CSV Storage Directory Access...";
            txtStatusCsv.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 85, 99));

            return await Task.Run(() =>
            {
                try
                {
                    string targetFolder = string.IsNullOrWhiteSpace(_csvPath) ? AppDomain.CurrentDomain.BaseDirectory : _csvPath;
                    if (!Directory.Exists(targetFolder))
                    {
                        Directory.CreateDirectory(targetFolder);
                    }

                    string testFile = Path.Combine(targetFolder, $"test_write_{Guid.NewGuid():N}.log");
                    File.WriteAllText(testFile, "csv_write_test");
                    if (File.Exists(testFile))
                    {
                        File.Delete(testFile);
                    }

                    SetStatus(txtStatusCsv, true, $"✅ ល្អឥតខ្ចោះ! ទីតាំងរក្សាទុក CSV '{targetFolder}' អាចអាន/សរសេរបានលឿន និងអត់មាន Error។");
                    return true;
                }
                catch (Exception ex)
                {
                    SetStatus(txtStatusCsv, false, $"❌ អត់មានសិទ្ធិរក្សាទុកទិន្នន័យនៅ '{_csvPath}' ទេ ({ex.Message})! សូម Check មើលក្រែងលោមានឯកសារ CSV កំពុងបើកចោលក្នុង Excel ឬអត់មានសិទ្ធិ។");
                    return false;
                }
            });
        }

        private void SetStatus(System.Windows.Controls.TextBlock txt, bool success, string message)
        {
            Dispatcher.Invoke(() =>
            {
                txt.Text = message;
                if (success)
                {
                    txt.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)); // Green #10B981
                }
                else
                {
                    txt.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // Red #EF4444
                }
            });
        }
    }
}
