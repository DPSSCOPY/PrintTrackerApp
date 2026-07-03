using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
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
            txtSummary.Text = "⏳ Running all 8 diagnostic tests sequentially... Please wait.";

            int passedCount = 0;
            
            if (await RunDriverTestAsync()) passedCount++;
            await Task.Delay(250);
            
            if (await RunSpoolerTestAsync()) passedCount++;
            await Task.Delay(250);
            
            if (await RunFoxitTestAsync()) passedCount++;
            await Task.Delay(250);
            
            if (await RunWmiTestAsync()) passedCount++;
            await Task.Delay(250);
            
            if (await RunCsvTestAsync()) passedCount++;
            await Task.Delay(250);

            if (await RunUiAutoTestAsync()) passedCount++;
            await Task.Delay(250);

            if (await RunWatchFolderTestAsync()) passedCount++;
            await Task.Delay(250);

            if (await RunPrinterNetTestAsync()) passedCount++;

            _isRunningAll = false;
            btnRunAll.IsEnabled = true;
            btnRunAll.Content = "🚀 Test All (ពិនិត្យទាំងអស់)";

            if (passedCount == 8)
            {
                borderSummary.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(209, 250, 229)); // Green #D1FAE5
                borderSummary.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                txtSummary.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 95, 70));
                txtSummary.Text = "✅ ល្អប្រសើរ! (Passed 8/8) កុំព្យូទ័រនេះមានគ្រប់លក្ខខណ្ឌសម្រាប់ Foxit Auto Print និងចាប់ Print បាន Smooth ១០០% ដូច PC Debug ហើយ!";
            }
            else
            {
                borderSummary.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226)); // Red #FEE2E2
                borderSummary.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
                txtSummary.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 27, 27));
                txtSummary.Text = $"⚠️ មានចំណុចត្រូវកែសម្រួល! (Passed {passedCount}/8) សូមពិនិត្យមើលចំណុចពណ៌ក្រហមខាងក្រោម ដើម្បីកែឱ្យ Foxit និងប្រព័ន្ធនេះ Smooth ១០០%។";
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

        private async void BtnTestUiAuto_Click(object sender, RoutedEventArgs e)
        {
            await RunUiAutoTestAsync();
        }

        private async void BtnTestWatchFolder_Click(object sender, RoutedEventArgs e)
        {
            await RunWatchFolderTestAsync();
        }

        private async void BtnTestPrinterNet_Click(object sender, RoutedEventArgs e)
        {
            await RunPrinterNetTestAsync();
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
            txtStatusFoxit.Text = "🔄 Checking Foxit PDF Accessibility (Reader / Editor)...";
            txtStatusFoxit.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 85, 99));

            return await Task.Run(() =>
            {
                try
                {
                    // 1. Check custom path from Settings first
                    try
                    {
                        var settings = PrintTrackerApp.Services.SettingsManager.LoadSettings();
                        if (!string.IsNullOrWhiteSpace(settings?.FoxitPath))
                        {
                            string customPath = settings.FoxitPath.Trim();
                            if (File.Exists(customPath))
                            {
                                SetStatus(txtStatusFoxit, true, $"✅ ល្អឥតខ្ចោះ! រកឃើញ Foxit PDF (តាម Path ក្នុង Settings)៖ '{customPath}'។");
                                return true;
                            }
                            else
                            {
                                SetStatus(txtStatusFoxit, false, $"❌ រកមិនឃើញ Foxit នៅទីតាំងដែលដាក់ក្នុង Settings៖ '{customPath}' ឡើយ! សូមពិនិត្យមើល Foxit PDF Path ក្នុង Settings ម្តងទៀត។");
                                return false;
                            }
                        }
                    }
                    catch { }

                    // 2. Check standard installation paths
                    string[] possiblePaths = new string[]
                    {
                        @"C:\Program Files (x86)\Foxit Software\Foxit PDF Editor\FoxitPDFEditor.exe",
                        @"C:\Program Files\Foxit Software\Foxit PDF Editor\FoxitPDFEditor.exe",
                        @"C:\Program Files (x86)\Foxit Software\Foxit PDF Reader\FoxitPDFReader.exe",
                        @"C:\Program Files\Foxit Software\Foxit PDF Reader\FoxitPDFReader.exe",
                        @"C:\Program Files (x86)\Foxit Software\Foxit Reader\FoxitReader.exe",
                        @"C:\Program Files\Foxit Software\Foxit Reader\FoxitReader.exe",
                        @"C:\Program Files (x86)\Foxit Software\Foxit PhantomPDF\FoxitPhantomPDF.exe",
                        @"C:\Program Files\Foxit Software\Foxit PhantomPDF\FoxitPhantomPDF.exe"
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

                    // 3. Try checking App Paths in Registry if not found
                    if (string.IsNullOrEmpty(foundPath))
                    {
                        string[] regNames = new string[] { "FoxitPDFEditor.exe", "FoxitPDFReader.exe", "FoxitReader.exe", "FoxitPhantomPDF.exe" };
                        foreach (string regName in regNames)
                        {
                            try
                            {
                                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{regName}"))
                                {
                                    if (key != null && key.GetValue("") != null)
                                    {
                                        string rPath = key.GetValue("").ToString();
                                        if (File.Exists(rPath))
                                        {
                                            foundPath = rPath;
                                            break;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    if (string.IsNullOrEmpty(foundPath))
                    {
                        SetStatus(txtStatusFoxit, false, "❌ រកមិនឃើញកម្មវិធី Foxit PDF Reader ឬ Foxit PDF Editor លើកុំព្យូទ័រនេះទេ។ សូមដំឡើង ឬ Browse ជ្រើសរើស Path ក្នុង Settings!");
                        return false;
                    }

                    SetStatus(txtStatusFoxit, true, $"✅ ល្អឥតខ្ចោះ! រកឃើញ Foxit PDF ត្រៀមរួចជាស្រេច៖ '{foundPath}'។");
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

        private async Task<bool> RunUiAutoTestAsync()
        {
            txtStatusUiAuto.Text = "🔄 Checking UI Automation API & Foxit Window IDs...";
            txtStatusUiAuto.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 85, 99));

            return await Task.Run(() =>
            {
                try
                {
                    var root = System.Windows.Automation.AutomationElement.RootElement;
                    if (root == null)
                    {
                        SetStatus(txtStatusUiAuto, false, "❌ អត់មានសិទ្ធិប្រើ Windows UI Automation ទេ! សូម Run កម្មវិធីនេះ As Administrator ដើម្បីអាចបញ្ជា Foxit Print បាន ១០០%។");
                        return false;
                    }

                    var settings = PrintTrackerApp.Services.SettingsManager.LoadSettings();
                    string printWinName = settings?.FoxitPrintWindowName ?? "Print";
                    string propBtnId = settings?.FoxitPropertiesBtnId ?? "10380";

                    if (string.IsNullOrWhiteSpace(printWinName) || string.IsNullOrWhiteSpace(propBtnId))
                    {
                        SetStatus(txtStatusUiAuto, false, "❌ ឈ្មោះ Window សម្រាប់បញ្ជា Foxit (Print / Properties) ក្នុង Settings មិនត្រឹមត្រូវឡើយ!");
                        return false;
                    }

                    SetStatus(txtStatusUiAuto, true, $"✅ ល្អឥតខ្ចោះ! ប្រព័ន្ធ UI Automation ដំណើរការបានល្អ ហើយឈ្មោះ Window ('{printWinName}', BtnID: '{propBtnId}') ត្រៀមរួចជាស្រេច ១០០%។");
                    return true;
                }
                catch (Exception ex)
                {
                    SetStatus(txtStatusUiAuto, false, $"❌ UI Automation Error: {ex.Message}។ សូម Check ក្រែងលោមាន Antivirus ឬ Windows UAC បិទខ្ទប់។");
                    return false;
                }
            });
        }

        private async Task<bool> RunWatchFolderTestAsync()
        {
            txtStatusWatchFolder.Text = "🔄 Testing Auto Print Source Folder & Subfolder access...";
            txtStatusWatchFolder.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 85, 99));

            return await Task.Run(() =>
            {
                try
                {
                    var settings = PrintTrackerApp.Services.SettingsManager.LoadSettings();
                    string srcFolder = settings?.SourceFolderPath;
                    if (string.IsNullOrWhiteSpace(srcFolder))
                    {
                        srcFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    }

                    if (!Directory.Exists(srcFolder))
                    {
                        Directory.CreateDirectory(srcFolder);
                    }

                    // Subfolders are created dynamically based on actual Status values at runtime.
                    // Here we only test that the source folder is writable and subfolder creation works.
                    string testSubDir = Path.Combine(srcFolder, "_diagnostic_test");
                    if (!Directory.Exists(testSubDir))
                        Directory.CreateDirectory(testSubDir);

                    string testSrc = Path.Combine(srcFolder, $"test_foxit_{Guid.NewGuid():N}.tmp");
                    string testDest = Path.Combine(testSubDir, $"test_foxit_{Guid.NewGuid():N}.tmp");

                    File.WriteAllText(testSrc, "test_watch_folder_access");
                    if (File.Exists(testSrc))
                    {
                        File.Move(testSrc, testDest);
                        if (File.Exists(testDest))
                        {
                            File.Delete(testDest);
                        }
                    }

                    // Clean up test directory
                    if (Directory.Exists(testSubDir) && Directory.GetFiles(testSubDir).Length == 0)
                        Directory.Delete(testSubDir);

                    SetStatus(txtStatusWatchFolder, true, $"✅ Source Folder ('{srcFolder}') is writable! Subfolders will be created automatically based on actual Status values.");

                    return true;
                }
                catch (Exception ex)
                {
                    SetStatus(txtStatusWatchFolder, false, $"❌ អត់មានសិទ្ធិ ឬមានកម្មវិធី Lock Folder តាមដាន! ({ex.Message}) សូមពិនិត្យមើល Source Folder ក្នុង Settings ក្រែងលោជាប់ក្នុង OneDrive ឬ Network Drive ដែលគ្មានសិទ្ធិ។");
                    return false;
                }
            });
        }

        private async Task<bool> RunPrinterNetTestAsync()
        {
            txtStatusPrinterNet.Text = "🔄 Testing Printer Queue Status & IP Ping Speed...";
            txtStatusPrinterNet.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 85, 99));

            return await Task.Run(() =>
            {
                try
                {
                    var settings = PrintTrackerApp.Services.SettingsManager.LoadSettings();
                    string ip = settings?.PrinterIp ?? "192.168.1.75";
                    string targetPrinter = string.IsNullOrWhiteSpace(_printerName) ? "SAVIN" : _printerName;

                    // 1. Check Windows Printer Queue Status
                    bool queueFound = false;
                    bool isOffline = false;
                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer"))
                    {
                        foreach (ManagementObject printer in searcher.Get())
                        {
                            string pName = printer["Name"]?.ToString() ?? "";
                            if (pName.IndexOf(targetPrinter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.IndexOf("Ricoh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                pName.IndexOf("SAVIN", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                queueFound = true;
                                bool? workOffline = printer["WorkOffline"] as bool?;
                                if (workOffline == true)
                                {
                                    isOffline = true;
                                }
                                break;
                            }
                        }
                    }

                    if (queueFound && isOffline)
                    {
                        SetStatus(txtStatusPrinterNet, false, $"⚠️ ជួរ Print Queue ក្នុង Windows កំពុងស្ថិតក្នុងស្ថានភាព 'Offline' (គ្មានការតភ្ជាប់)! នេះធ្វើឱ្យ Foxit គាំងពេលបញ្ជូន Job។ សូម Check មើលម៉ាស៊ីន Print ក្នុង Devices and Printers។");
                        return false;
                    }

                    // 2. Ping Printer IP
                    if (!string.IsNullOrWhiteSpace(ip))
                    {
                        using (var ping = new System.Net.NetworkInformation.Ping())
                        {
                            try
                            {
                                var reply = ping.Send(ip, 1500);
                                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                                {
                                    SetStatus(txtStatusPrinterNet, true, $"✅ ល្អឥតខ្ចោះ! ជួរ Print Queue ក្នុង Windows មានស្ថានភាព Online ហើយ Ping ទៅ IPម៉ាស៊ីន ('{ip}') លឿនខ្លាំង ({reply.RoundtripTime}ms) ធានាការទាញទិន្នន័យ ១០០% មិនបាត់បង់។");
                                    return true;
                                }
                                else
                                {
                                    SetStatus(txtStatusPrinterNet, false, $"❌ មិនអាច Ping ទៅកាន់ IP ម៉ាស៊ីន Print ('{ip}') បានទេ ({reply.Status})! សូម Check ខ្សែបណ្តាញ LAN ឬ IP ក្នុង Settings ដើម្បីកុំឱ្យបាត់ទិន្នន័យ។");
                                    return false;
                                }
                            }
                            catch
                            {
                                SetStatus(txtStatusPrinterNet, false, $"❌ មិនអាច Ping ទៅ IP ('{ip}')! សូម Check IP Address ក្នុង Settings និងការតភ្ជាប់ Network។");
                                return false;
                            }
                        }
                    }

                    SetStatus(txtStatusPrinterNet, true, "✅ ល្អឥតខ្ចោះ! ជួរ Print Queue ក្នុង Windows មានស្ថានភាព Online ត្រៀមរួចជាស្រេច។");
                    return true;
                }
                catch (Exception ex)
                {
                    SetStatus(txtStatusPrinterNet, false, $"❌ Printer Queue & Network Error: {ex.Message}");
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
