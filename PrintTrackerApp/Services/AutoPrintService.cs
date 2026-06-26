using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PrintTrackerApp.Services
{
    public class AutoPrintService
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _processingTask;

        public bool IsRunning { get; private set; }
        public bool IsPaused { get; set; }

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<string>? FileProcessingStarted;
        public event EventHandler? QueueEmpty;
        public event EventHandler? PauseRequested;
        public event EventHandler<(string FileName, bool Success)>? FileProcessingCompleted;
        
        public Func<string, string, string>? OnRequestUniqueUserId;

        public void Start(AppSettings settings)
        {
            if (IsRunning) return;

            IsRunning = true;
            IsPaused = false;
            _cancellationTokenSource = new CancellationTokenSource();
            
            StatusChanged?.Invoke(this, "Running");

            _processingTask = Task.Run(() => PrintWorker(settings, _cancellationTokenSource.Token));
        }

        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            _cancellationTokenSource?.Cancel();
            StatusChanged?.Invoke(this, "Stopped");
        }

        private void PrintWorker(AppSettings settings, CancellationToken token)
        {
            bool hasProcessedFiles = false;
            string watchFolder = settings.SourceFolderPath;
            int lastPriorityLevel = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (IsPaused)
                    {
                        Thread.Sleep(500);
                        continue;
                    }

                    // Always reload settings so dynamic changes to priority lists reflect without restart
                    var currentSettings = SettingsManager.LoadSettings();
                    watchFolder = currentSettings.SourceFolderPath;

                    if (!Directory.Exists(watchFolder)) 
                    {
                        Thread.Sleep(3000);
                        continue;
                    }

                    var files = Directory.GetFiles(watchFolder, "*.pdf")
                        .Where(f => !f.Contains("Complete Print", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => File.GetCreationTime(f))
                        .ToList();

                    if (files.Count > 0)
                    {
                        hasProcessedFiles = true;
                        string? fileToProcess = null;
                        int currentPriorityLevel = 4;

                        // Check Priority 1
                        if (currentSettings.EnablePriority1 && !string.IsNullOrWhiteSpace(currentSettings.Priority1Prefixes))
                        {
                            fileToProcess = GetFirstMatchingFile(files, currentSettings.Priority1Prefixes);
                            if (fileToProcess != null) currentPriorityLevel = 1;
                        }

                        // Check Priority 2
                        if (fileToProcess == null && currentSettings.EnablePriority2 && !string.IsNullOrWhiteSpace(currentSettings.Priority2Prefixes))
                        {
                            fileToProcess = GetFirstMatchingFile(files, currentSettings.Priority2Prefixes);
                            if (fileToProcess != null) currentPriorityLevel = 2;
                        }

                        // Check Priority 3
                        if (fileToProcess == null && currentSettings.EnablePriority3 && !string.IsNullOrWhiteSpace(currentSettings.Priority3Prefixes))
                        {
                            fileToProcess = GetFirstMatchingFile(files, currentSettings.Priority3Prefixes);
                            if (fileToProcess != null) currentPriorityLevel = 3;
                        }

                        // Normal (Fallback)
                        if (fileToProcess == null)
                        {
                            fileToProcess = files.First();
                            currentPriorityLevel = 4;
                        }

                        if (lastPriorityLevel == 0 && currentPriorityLevel == 4)
                        {
                            bool anyPriorityEnabled = (currentSettings.EnablePriority1 && !string.IsNullOrWhiteSpace(currentSettings.Priority1Prefixes)) ||
                                                      (currentSettings.EnablePriority2 && !string.IsNullOrWhiteSpace(currentSettings.Priority2Prefixes)) ||
                                                      (currentSettings.EnablePriority3 && !string.IsNullOrWhiteSpace(currentSettings.Priority3Prefixes));
                            
                            if (anyPriorityEnabled)
                            {
                                bool continuePrinting = false;
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    var window = new PriorityConfirmWindow(0);
                                    continuePrinting = (window.ShowDialog() == true);
                                });

                                if (!continuePrinting)
                                {
                                    IsPaused = true;
                                    StatusChanged?.Invoke(this, "Paused");
                                    PauseRequested?.Invoke(this, EventArgs.Empty);
                                    lastPriorityLevel = 0;
                                    continue;
                                }
                            }
                        }
                        else if (lastPriorityLevel != 0 && lastPriorityLevel < 4 && lastPriorityLevel < currentPriorityLevel)
                        {
                            bool continuePrinting = false;
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                var window = new PriorityConfirmWindow(lastPriorityLevel);
                                continuePrinting = (window.ShowDialog() == true);
                            });

                            if (!continuePrinting)
                            {
                                IsPaused = true;
                                StatusChanged?.Invoke(this, "Paused");
                                PauseRequested?.Invoke(this, EventArgs.Empty);
                                lastPriorityLevel = currentPriorityLevel; // Prevent asking again if they resume
                                continue;
                            }
                        }

                        lastPriorityLevel = currentPriorityLevel;

                        string fileName = Path.GetFileName(fileToProcess);

                        if (!ParseDynamicFileInfo(fileName, currentSettings.HoldPrintUserId, currentSettings.AutoPrintCopies, out string _, out string _, out int _))
                        {
                            StatusChanged?.Invoke(this, $"Skipping {fileName} (Invalid Format)");
                            MoveFileToFolder(fileToProcess, watchFolder, "Skipped - Invalid Format");
                            continue;
                        }

                        FileProcessingStarted?.Invoke(this, fileName);
                        
                        int actualUiDelay = currentSettings.EnableUiStepDelay ? currentSettings.UiStepDelayMs : 300;
                        bool success = PrintPdfWithUIAutomation(fileToProcess, currentSettings.FoxitPath, currentSettings.HoldPrintUserId, fileName, currentSettings.AutoPrintCopies, token, currentSettings.FoxitWindowStyle, actualUiDelay, currentSettings.SkipBlankPage);
                        
                        FileProcessingCompleted?.Invoke(this, (fileName, success));
                        
                        if (success && !token.IsCancellationRequested)
                        {
                            Thread.Sleep(currentSettings.DelayBetweenPrints * 1000);
                            MoveFileToFolder(fileToProcess, watchFolder, "Sent to Printer");
                        }
                        else
                        {
                            StatusChanged?.Invoke(this, "Error during printing. Pausing 5s...");
                            Thread.Sleep(5000);
                            StatusChanged?.Invoke(this, "Running");
                        }
                    }
                    else
                    {
                        if (hasProcessedFiles)
                        {
                            QueueEmpty?.Invoke(this, EventArgs.Empty);
                            hasProcessedFiles = false; // Prevent multiple empty queue invocations
                        }
                        lastPriorityLevel = 0; // Reset when queue is empty
                        Thread.Sleep(3000);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in AutoPrint processing: {ex.Message}");
                    Thread.Sleep(5000);
                }
            }
        }

        private string? GetFirstMatchingFile(System.Collections.Generic.List<string> files, string prefixesCsv)
        {
            var prefixes = prefixesCsv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(p => p.Trim())
                                      .Where(p => p.Length > 0)
                                      .ToList();

            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                foreach (var prefix in prefixes)
                {
                    if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
            }
            return null;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public static bool ParseDynamicFileInfo(string fullFileName, string defaultUserId, int defaultCopies, out string finalUserId, out string finalFileName, out int finalCopies)
        {
            finalUserId = defaultUserId;
            finalFileName = Path.GetFileNameWithoutExtension(fullFileName);
            finalCopies = defaultCopies;
            bool foundCopies = false;

            try
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fullFileName);
                var match = System.Text.RegularExpressions.Regex.Match(nameWithoutExt, @"(\d+)[\s_-]*(?:copy|copies)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    foundCopies = true;
                    if (int.TryParse(match.Groups[1].Value, out int parsedCopies))
                    {
                        finalCopies = parsedCopies;
                    }

                    string beforeCopies = nameWithoutExt.Substring(0, match.Index);
                    string afterCopies = nameWithoutExt.Substring(match.Index + match.Length);

                    string rawUserId = "Default";
                    string rawFileName = "";

                    string[] beforeParts = beforeCopies.Split(new char[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                    if (beforeParts.Length > 0)
                    {
                        rawUserId = beforeParts[0];
                        if (beforeParts.Length > 1)
                        {
                            rawFileName = string.Join("-", beforeParts.Skip(1));
                        }
                    }

                    finalUserId = new string(rawUserId.Where(c => char.IsLetterOrDigit(c)).ToArray());
                    if (finalUserId.Length > 8) finalUserId = finalUserId.Substring(0, 8);
                    if (string.IsNullOrEmpty(finalUserId)) finalUserId = defaultUserId;

                    finalFileName = new string(rawFileName.Where(c => c != '"' && c != '.').ToArray());
                    if (finalFileName.Length > 16) finalFileName = finalFileName.Substring(0, 16);
                    if (string.IsNullOrEmpty(finalFileName)) finalFileName = "Unknown";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing dynamic file info: {ex.Message}");
            }

            return foundCopies;
        }

        public static bool IsPdfLandscape(string filePath)
        {
            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                using (PdfDocument document = PdfReader.Open(filePath, PdfDocumentOpenMode.Import))
                {
                    if (document.PageCount > 0)
                    {
                        var page = document.Pages[0];
                        return page.Width > page.Height;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading PDF orientation: {ex.Message}");
            }
            return false;
        }

        private bool PrintPdfWithUIAutomation(string filePath, string foxitPath, string defaultUserId, string fileName, int defaultCopies, CancellationToken token, string windowStyle, int uiStepDelayMs, bool skipBlankPage)
        {
            int delayShort = Math.Max(100, uiStepDelayMs - 100);
            int delayNormal = uiStepDelayMs;
            int delayLong = uiStepDelayMs + 200;

            ParseDynamicFileInfo(filePath, defaultUserId, defaultCopies, out string dynamicUserId, out string dynamicFileName, out int dynamicCopies);
            
            if (OnRequestUniqueUserId != null)
            {
                dynamicUserId = OnRequestUniqueUserId(dynamicUserId, dynamicFileName);
            }
            
            Process? foxitProcess = null;
            try
            {
                // 1. Launch Foxit
                ProcessStartInfo psi = new ProcessStartInfo();
                if (!string.IsNullOrEmpty(foxitPath) && File.Exists(foxitPath))
                {
                    psi.FileName = foxitPath;
                    psi.Arguments = $"\"{filePath}\"";
                }
                else
                {
                    psi.FileName = filePath;
                    psi.UseShellExecute = true;
                }

                if (windowStyle == "Minimized") psi.WindowStyle = ProcessWindowStyle.Minimized;
                else if (windowStyle == "Maximized") psi.WindowStyle = ProcessWindowStyle.Maximized;

                foxitProcess = Process.Start(psi);
                
                // Wait for Foxit Window to open
                AutomationElement? mainWindow = null;
                for (int i = 0; i < 20; i++) // Wait up to 10 seconds
                {
                    if (token.IsCancellationRequested) return false;
                    
                    if (foxitProcess != null && !foxitProcess.HasExited && foxitProcess.MainWindowHandle != IntPtr.Zero)
                    {
                        mainWindow = AutomationElement.FromHandle(foxitProcess.MainWindowHandle);
                    }
                    else
                    {
                        // Fallback: search by class name or process name if MainWindowHandle is empty
                        var processes = Process.GetProcessesByName("FoxitPDFReader");
                        if (processes.Length == 0) processes = Process.GetProcessesByName("FoxitPDFEditor");
                        
                        if (processes.Length > 0 && processes[0].MainWindowHandle != IntPtr.Zero)
                        {
                            mainWindow = AutomationElement.FromHandle(processes[0].MainWindowHandle);
                            foxitProcess = processes[0];
                        }
                    }

                    if (mainWindow != null) break;
                    Thread.Sleep(delayLong);
                }

                if (mainWindow == null)
                {
                    Debug.WriteLine("Could not find Foxit window.");
                    return false;
                }

                // Ensure window is in foreground
                try 
                {
                    IntPtr handle = new IntPtr(mainWindow.Current.NativeWindowHandle);
                    
                    if (windowStyle == "Small")
                    {
                        ShowWindow(handle, SW_NORMAL);
                        MoveWindow(handle, 0, 0, 400, 300, true);
                        SetForegroundWindow(handle);
                    }
                    else if (windowStyle == "RightHalf")
                    {
                        ShowWindow(handle, SW_NORMAL);
                        var workArea = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                        int width = workArea.Width / 2;
                        int x = workArea.Width / 2;
                        MoveWindow(handle, x, 0, width, workArea.Height, true);
                        SetForegroundWindow(handle);
                    }
                    else if (windowStyle == "Minimized")
                    {
                        // Don't restore if user explicitly requested Minimized, 
                        // though Foxit print dialog will still pop up.
                    }
                    else
                    {
                        SetForegroundWindow(handle);
                    }
                } 
                catch {}

                mainWindow.SetFocus();
                Thread.Sleep(delayNormal); // Give Foxit brief time to render

                // 2. Send Ctrl+P using native API
                KeyboardHelper.SendCtrlP();
                
                // 3. Wait for Foxit Print dialog
                AutomationElement? printDialog = WaitForKeyWindow(mainWindow, "Print", token);
                if (printDialog == null) return false;

                // 3.1 Check and skip blank pages using PdfPig
                bool isAllPages = true;
                string printRange = "";
                
                if (skipBlankPage)
                {
                    printRange = PdfPageHelper.GetNonBlankPagesString(filePath, out isAllPages, out int actualPageCount);
                    if (string.IsNullOrEmpty(printRange))
                    {
                        Debug.WriteLine($"File {fileName} is completely blank or only contains headers. Skipping printing.");
                        if (foxitProcess != null && !foxitProcess.HasExited)
                        {
                            try { foxitProcess.Kill(); } catch { }
                        }
                        return true;
                    }
                }
                
                // If there are blank pages and skip blank page is enabled, we set the specific range. Otherwise, let Foxit use "All pages" (default).
                if (skipBlankPage && !isAllPages)
                {
                    // Set Pages Radio Button (AutomationId: '10433')
                    AutomationElement pagesRadioButton = printDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "10433"));
                    if (pagesRadioButton != null)
                    {
                        if (pagesRadioButton.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object selPattern))
                        {
                            ((SelectionItemPattern)selPattern).Select();
                        }
                        else
                        {
                            InvokeElement(pagesRadioButton);
                        }
                        Thread.Sleep(delayNormal);
                    }

                    // Set Pages Textbox (AutomationId: '10415')
                    AutomationElement pagesTextBox = printDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "10415"));
                    if (pagesTextBox != null)
                    {
                        SetTextElement(pagesTextBox, printRange);
                        Thread.Sleep(delayNormal);
                    }
                }

                // 3.5. Auto-select Flip on short/long edge based on orientation
                bool isLandscape = IsPdfLandscape(filePath);
                if (isLandscape)
                {
                    AutomationElement shortEdgeBtn = printDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Flip on short edge"));
                    if (shortEdgeBtn != null)
                    {
                        if (shortEdgeBtn.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object selPattern))
                        {
                            ((SelectionItemPattern)selPattern).Select();
                        }
                        else
                        {
                            InvokeElement(shortEdgeBtn);
                        }
                        Thread.Sleep(delayNormal);
                    }
                }


                // 4. Set Copies
                if (dynamicCopies > 1)
                {
                    try { printDialog.SetFocus(); } catch { }
                    Thread.Sleep(delayNormal);
                    System.Windows.Forms.SendKeys.SendWait("%c");
                    Thread.Sleep(delayNormal);
                    System.Windows.Forms.SendKeys.SendWait(dynamicCopies.ToString());
                    Thread.Sleep(delayNormal);
                }

                // 5. Click 'Properties' (AutomationId: '10380')
                AutomationElement propertiesBtn = printDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "10380"));
                if (propertiesBtn != null)
                {
                    Thread.Sleep(delayNormal);
                    InvokeElement(propertiesBtn);
                }
                else
                {
                    // Fallback to Alt+R which is usually Properties
                    System.Windows.Forms.SendKeys.SendWait("%r");
                }

                // 6. Wait for SAVIN Properties window
                AutomationElement? savinProps = WaitForKeyWindow(printDialog, "Properties", token, true); // contains Properties
                if (savinProps == null) return false;

                // 8. Click 'Details...' (AutomationId: '1018')
                AutomationElement detailsBtn = savinProps.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1018"));
                if (detailsBtn != null)
                {
                    Thread.Sleep(delayNormal);
                    InvokeElement(detailsBtn);
                    
                    // 9. Job Type Details Window
                    AutomationElement? detailsWindow = WaitForKeyWindow(savinProps, "Details", token, true);
                    if (detailsWindow != null)
                    {
                        // Set User ID (AutomationId: '1004')
                        AutomationElement userIdEdit = detailsWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1004"));
                        if (userIdEdit != null)
                        {
                            SetTextElement(userIdEdit, dynamicUserId);
                        }

                        // Set File Name (AutomationId: '1007')
                        AutomationElement fileNameEdit = detailsWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1007"));
                        if (fileNameEdit != null)
                        {
                            SetTextElement(fileNameEdit, dynamicFileName);
                        }
                        
                        // Click OK on Details Window (AutomationId: '1')
                        AutomationElement okDetailsBtn = detailsWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1"));
                        if (okDetailsBtn != null)
                        {
                            Thread.Sleep(delayNormal);
                            InvokeElement(okDetailsBtn);
                        }
                        else
                        {
                            Thread.Sleep(delayNormal);
                            System.Windows.Forms.SendKeys.SendWait("{ENTER}");
                        }
                        Thread.Sleep(delayLong);
                    }
                }

                // 10. Click 'OK' on Properties (AutomationId: '1')
                AutomationElement okPropsBtn = savinProps.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1"));
                if (okPropsBtn != null)
                {
                    Thread.Sleep(delayNormal);
                    InvokeElement(okPropsBtn);
                }
                else
                {
                    Thread.Sleep(delayNormal);
                    System.Windows.Forms.SendKeys.SendWait("{ENTER}");
                }
                Thread.Sleep(delayLong);

                // 11. Click 'OK' on Foxit Print window (AutomationId: '1')
                AutomationElement okPrintBtn = printDialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1"));
                if (okPrintBtn != null)
                {
                    Thread.Sleep(delayNormal);
                    InvokeElement(okPrintBtn);
                }
                else
                {
                    Thread.Sleep(delayNormal);
                    System.Windows.Forms.SendKeys.SendWait("{ENTER}");
                }

                // Wait for print spooling
                // We do a minimal 200ms sleep to let Foxit submit to Windows Spooler,
                // then we actively check the WMI queue to see when it finishes spooling.
                Thread.Sleep(200);

                // Wait for print spooling by checking Foxit's windows
                // When Foxit is printing, it shows the Print dialog, and then a "Printing..." progress dialog.
                // We wait until Foxit has only 1 window left (the main window), meaning all dialogs have closed.
                try
                {
                    AutomationElement root = AutomationElement.RootElement;
                    for (int s = 0; s < 150; s++) // Max 30 seconds wait
                    {
                        if (token.IsCancellationRequested) return false;

                        AutomationElementCollection windows = root.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, foxitProcess.Id));
                        
                        // If Foxit has 1 or 0 windows, it means no modal dialogs (Print or Printing...) are open.
                        if (windows.Count <= 1)
                        {
                            // Double check that the Print dialog is actually gone
                            bool printDialogClosed = false;
                            try 
                            { 
                                if (printDialog.Current.IsOffscreen) printDialogClosed = true; 
                            } 
                            catch { printDialogClosed = true; } // ElementNotAvailableException

                            if (printDialogClosed)
                            {
                                // Add a tiny 200ms buffer to ensure Foxit fully flushed the spool data
                                Thread.Sleep(200);
                                break;
                            }
                        }

                        Thread.Sleep(200);
                    }
                }
                catch { Thread.Sleep(3000); }
                
                // 12. Close Foxit (Ctrl+W or Alt+F4)
                if (!foxitProcess.HasExited)
                {
                    try { foxitProcess.Kill(); } catch { }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UI Automation Error: {ex.Message}");
                if (foxitProcess != null && !foxitProcess.HasExited)
                {
                    try { foxitProcess.Kill(); } catch { }
                }
                return false;
            }
        }

        public static AutomationElement? WaitForKeyWindow(AutomationElement parent, string titleSubstring, CancellationToken token, bool contains = false)
        {
            for (int i = 0; i < 50; i++) // 10 seconds timeout
            {
                if (token.IsCancellationRequested) return null;

                AutomationElementCollection windows = parent.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
                foreach (AutomationElement window in windows)
                {
                    if (contains && window.Current.Name.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
                        return window;
                    else if (!contains && window.Current.Name.Equals(titleSubstring, StringComparison.OrdinalIgnoreCase))
                        return window;
                }
                Thread.Sleep(200);
            }
            return null;
        }

        public static void InvokeElement(AutomationElement element)
        {
            try
            {
                InvokePattern invokePattern = (InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern);
                // Run on a background thread to prevent blocking the UI thread when opening modal dialogs
                System.Threading.Tasks.Task.Run(() => {
                    try { invokePattern.Invoke(); } catch { }
                });
            }
            catch
            {
                // Fallback if InvokePattern is not supported (e.g. some custom buttons)
                // We can set focus and press Space
                element.SetFocus();
                Thread.Sleep(100);
                System.Windows.Forms.SendKeys.SendWait(" ");
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
        private const uint WM_SETTEXT = 0x000C;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_MAXIMIZE = 3;
        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;
        private const int SW_NORMAL = 1;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        internal static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        public static void SetTextElement(AutomationElement element, string text)
        {
            try
            {
                // Most robust way for Win32 Edit controls: SendMessage WM_SETTEXT
                int hwnd = element.Current.NativeWindowHandle;
                if (hwnd != 0)
                {
                    SendMessage((IntPtr)hwnd, WM_SETTEXT, IntPtr.Zero, text);
                    return;
                }

                // Try using ValuePattern
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
                {
                    ((ValuePattern)pattern).SetValue(text);
                }
                else
                {
                    // Fallback to sending keys
                    element.SetFocus();
                    Thread.Sleep(100);
                    System.Windows.Forms.SendKeys.SendWait("^{HOME}^+{END}{BACKSPACE}"); 
                    System.Windows.Forms.SendKeys.SendWait(text);
                }
            }
            catch
            {
                try 
                {
                    element.SetFocus();
                    Thread.Sleep(100);
                    System.Windows.Forms.SendKeys.SendWait(text);
                } 
                catch {}
            }
        }

        public static void MoveFileToFolder(string sourceFile, string watchFolder, string targetSubFolder)
        {
            try
            {
                string targetFolder = Path.Combine(watchFolder, targetSubFolder);
                if (!Directory.Exists(targetFolder))
                    Directory.CreateDirectory(targetFolder);

                string destFile = Path.Combine(targetFolder, Path.GetFileName(sourceFile));
                
                if (File.Exists(destFile))
                {
                    string name = Path.GetFileNameWithoutExtension(sourceFile);
                    string ext = Path.GetExtension(sourceFile);
                    destFile = Path.Combine(targetFolder, $"{name}_{DateTime.Now.ToString("HHmmss")}{ext}");
                }

                File.Move(sourceFile, destFile);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to move file: {ex.Message}");
            }
        }
    }

    public static class KeyboardHelper
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const int KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_P = 0x50;

        public static void SendCtrlP()
        {
            // Press Ctrl
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            // Press P
            keybd_event(VK_P, 0, 0, UIntPtr.Zero);
            
            // Release P
            keybd_event(VK_P, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            // Release Ctrl
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }
}
