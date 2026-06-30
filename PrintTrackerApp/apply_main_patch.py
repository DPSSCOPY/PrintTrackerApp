import sys

file_path = r"e:\Code\Tracking_Print\PrintTrackerApp\MainWindow.xaml.cs"
with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

injection_code = """
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
                }

                if (errorMessages.Count > 0)
                {
                    System.Windows.MessageBox.Show($"Some errors occurred:\\n{string.Join("\\n", errorMessages)}", "Warnings", MessageBoxButton.OK, MessageBoxImage.Warning);
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
"""

# Modify CellEditEnding and PreviewKeyDown to only export if not looking at historical
edit_saving_code_find = """                            // Use Dispatcher.InvokeAsync to update CSV after property is actually updated
                            Dispatcher.InvokeAsync(() =>
                            {
                                CsvLogger.ExportJobsToCsv(_printJobs, _appSettings.CsvExportPath);
                            }, System.Windows.Threading.DispatcherPriority.Background);"""
edit_saving_code_replace = """                            // Use Dispatcher.InvokeAsync to update CSV after property is actually updated
                            Dispatcher.InvokeAsync(() =>
                            {
                                if (_historicalPrintJobs == null)
                                {
                                    CsvLogger.ExportJobsToCsv(_printJobs, _appSettings.CsvExportPath);
                                }
                            }, System.Windows.Threading.DispatcherPriority.Background);"""

undo_export_find = """                            _redoStack.Push(action);
                            CsvLogger.ExportJobsToCsv(_printJobs, _appSettings.CsvExportPath);"""
undo_export_replace = """                            _redoStack.Push(action);
                            if (_historicalPrintJobs == null) CsvLogger.ExportJobsToCsv(_printJobs, _appSettings.CsvExportPath);"""

redo_export_find = """                            _undoStack.Push(action);
                            CsvLogger.ExportJobsToCsv(_printJobs, _appSettings.CsvExportPath);"""
redo_export_replace = """                            _undoStack.Push(action);
                            if (_historicalPrintJobs == null) CsvLogger.ExportJobsToCsv(_printJobs, _appSettings.CsvExportPath);"""

if "BtnResetLiveLog_Click" not in content:
    # Inject before the end of the class
    last_brace = content.rfind("    }")
    if last_brace != -1:
        content = content[:last_brace] + injection_code + "\n" + content[last_brace:]

    content = content.replace(edit_saving_code_find, edit_saving_code_replace)
    content = content.replace(undo_export_find, undo_export_replace)
    content = content.replace(redo_export_find, redo_export_replace)

    with open(file_path, "w", encoding="utf-8") as f:
        f.write(content)
    print("Injected UI handlers successfully.")
else:
    print("Already injected!")
