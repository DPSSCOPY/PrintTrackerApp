using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.IO;
using System.Data;
using Microsoft.Win32;
using ExcelDataReader;
using PrintTrackerApp.Models;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace PrintTrackerApp
{
    public partial class TeacherDashboardControl : System.Windows.Controls.UserControl
    {
        private string _csvExportPath = "";
        private IEnumerable<PrintJobInfo> _currentJobs;
        private IEnumerable<PrintJobInfo> _originalJobs;
        private List<PrintJobInfo> _unmatchedJobs = new List<PrintJobInfo>();
        private Dictionary<string, TeacherPrintStat> _statsDict = new Dictionary<string, TeacherPrintStat>();
        private DataTable _ftTable;
        private DataTable _ptTable;
        private DataTable _khTable;
        private string _currentExcelPath;
        private DateTime _lastExcelWriteTime;
        private System.Windows.Threading.DispatcherTimer _excelCheckTimer;
        
        private DateTime? _lastGoogleSheetsWriteTime;
        private System.Windows.Threading.DispatcherTimer _googleSheetsCheckTimer;
        
        private DateTime _currentStart;
        private DateTime _currentEnd;
        private int _currentDurationDays;

        public TeacherDashboardControl()
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            InitializeComponent();
            
            _excelCheckTimer = new System.Windows.Threading.DispatcherTimer();
            _excelCheckTimer.Interval = TimeSpan.FromSeconds(2);
            _excelCheckTimer.Tick += ExcelCheckTimer_Tick;

            _googleSheetsCheckTimer = new System.Windows.Threading.DispatcherTimer();
            _googleSheetsCheckTimer.Interval = TimeSpan.FromSeconds(30);
            _googleSheetsCheckTimer.Tick += GoogleSheetsCheckTimer_Tick;
            
            dpStartDate.SelectedDate = DateTime.Now;
            dpEndDate.SelectedDate = DateTime.Now;
            
            LoadCustomDateFilters();

            try
            {
                string configPath = GetConfigFilePath();
                if (File.Exists(configPath))
                {
                    string savedPath = File.ReadAllText(configPath).Trim();
                    if (savedPath == "GoogleSheets")
                    {
                        cmbDataSource.SelectedIndex = 1;
                        ImportGoogleSheetsData(null);
                    }
                    else if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
                    {
                        cmbDataSource.SelectedIndex = 0;
                        LoadExcelData(savedPath, false);
                    }
                }
            }
            catch { }
        }

        public void InitializeData(IEnumerable<PrintJobInfo> printJobs, string csvExportPath)
        {
            _currentJobs = printJobs;
            _originalJobs = printJobs;
            _csvExportPath = csvExportPath;

            if (cmbDateFilter.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Content != null)
            {
                CmbDateFilter_SelectionChanged(this, null);
            }
            else
            {
                ReloadFilteredData(DateTime.Now.Date, DateTime.Now.Date);
            }
        }

        private string GetConfigFilePath()
        {
            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_excel_path.txt");
        }

        private void LoadCustomDateFilters()
        {
            if (cmbDateFilter == null) return;
            // Remove existing custom items first, in case of reload
            for (int i = cmbDateFilter.Items.Count - 1; i >= 0; i--)
            {
                if (cmbDateFilter.Items[i] is System.Windows.Controls.ComboBoxItem item && item.Tag is PrintTrackerApp.Services.CustomDateFilter)
                {
                    cmbDateFilter.Items.RemoveAt(i);
                }
            }

            var settings = PrintTrackerApp.Services.SettingsManager.LoadSettings();
            if (settings.CustomDateFilters != null && settings.CustomDateFilters.Count > 0)
            {
                int insertIndex = cmbDateFilter.Items.Count - 1;
                foreach(var filter in settings.CustomDateFilters)
                {
                    var item = new System.Windows.Controls.ComboBoxItem();
                    item.Content = filter.Name;
                    item.Tag = filter;
                    cmbDateFilter.Items.Insert(insertIndex++, item);
                }
            }
        }

        private void BtnManageCustomRanges_Click(object sender, RoutedEventArgs e)
        {
            var settings = PrintTrackerApp.Services.SettingsManager.LoadSettings();
            var window = new CustomDateFiltersWindow(settings.CustomDateFilters);
            window.Owner = Window.GetWindow(this);
            if (window.ShowDialog() == true)
            {
                settings.CustomDateFilters = new System.Collections.Generic.List<PrintTrackerApp.Services.CustomDateFilter>(window.Filters);
                PrintTrackerApp.Services.SettingsManager.SaveSettings(settings);
                LoadCustomDateFilters();
                
                if (cmbDateFilter.SelectedIndex == -1) cmbDateFilter.SelectedIndex = 0;
            }
        }

        private void CmbDateFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cmbDateFilter == null || spCustomDate == null) return;
            
            DateTime start = DateTime.Now.Date;
            DateTime end = DateTime.Now.Date;
            bool custom = false;

            if (cmbDateFilter.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                string content = selectedItem.Content?.ToString() ?? "";
                
                if (content == "Today")
                {
                    // Default is today
                }
                else if (content == "Custom Range")
                {
                    custom = true;
                    spCustomDate.Visibility = Visibility.Visible;
                    if (dpStartDate.SelectedDate.HasValue) start = dpStartDate.SelectedDate.Value.Date;
                    if (dpEndDate.SelectedDate.HasValue) end = dpEndDate.SelectedDate.Value.Date;
                }
                else
                {
                    // Custom Week/Filter
                    if (selectedItem.Tag is PrintTrackerApp.Services.CustomDateFilter customFilter)
                    {
                        start = customFilter.StartDate.Date;
                        end = customFilter.EndDate.Date;
                    }
                }
            }

            if (!custom)
            {
                spCustomDate.Visibility = Visibility.Collapsed;
                ReloadFilteredData(start, end);
            }
        }

        private void DpDate_SelectedDateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cmbDateFilter != null && cmbDateFilter.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem && selectedItem.Content?.ToString() == "Custom Range")
            {
                DateTime start = dpStartDate.SelectedDate ?? DateTime.Now.Date;
                DateTime end = dpEndDate.SelectedDate ?? DateTime.Now.Date;
                ReloadFilteredData(start, end);
            }
        }

        private async void ReloadFilteredData(DateTime start, DateTime end)
        {
            if (_currentJobs == null) return;

            var allJobs = new List<PrintJobInfo>();

            // 1. Get from memory
            foreach (var job in _currentJobs)
            {
                if (DateTime.TryParse(job.Timestamp, out DateTime dt) && dt.Date >= start.Date && dt.Date <= end.Date)
                {
                    allJobs.Add(job);
                }
            }

            // 2. Load from CSV history (Offload to background thread to prevent UI freezing)
            var historyJobs = await Task.Run(() => Services.CsvLogger.LoadJobsFromCsvForDateRange(_csvExportPath, start, end));

            // 3. Merge avoiding duplicates
            var memoryKeys = new HashSet<string>(allJobs.Select(j => $"{j.Timestamp}_{j.DocumentName}_{j.Owner}"));
            foreach (var hJob in historyJobs)
            {
                string key = $"{hJob.Timestamp}_{hJob.DocumentName}_{hJob.Owner}";
                if (!memoryKeys.Contains(key))
                {
                    allJobs.Add(hJob);
                }
            }

            int duration = (end.Date - start.Date).Days + 1;
            LoadData(allJobs, duration, start, end);
        }

        private void LoadData(IEnumerable<PrintJobInfo> printJobs, int totalDurationDays = 1, DateTime? filterStart = null, DateTime? filterEnd = null)
        {
            DateTime start = filterStart ?? DateTime.Now.Date;
            DateTime end = filterEnd ?? DateTime.Now.Date;
            _currentStart = start;
            _currentEnd = end;
            _currentDurationDays = totalDurationDays;

            GenerateDynamicColumns(start, end);

            _statsDict.Clear();
            _unmatchedJobs.Clear();

            foreach (var job in printJobs)
            {
                if (string.IsNullOrWhiteSpace(job.DocumentName))
                    continue;

                ParseDocumentName(job.DocumentName, out string level, out string teacher, out string session);

                // If file is missing level or teacher, it's considered malformed/unmatched and is NOT assigned to anyone
                if (string.IsNullOrWhiteSpace(level) || string.IsNullOrWhiteSpace(teacher))
                {
                    _unmatchedJobs.Add(job);
                    continue;
                }

                string jobDate = "";
                if (DateTime.TryParse(job.Timestamp, out DateTime dt))
                {
                    jobDate = dt.Date.ToString("yyyy-MM-dd");
                }

                // Split by '&'
                var levels = level.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
                var teachers = teacher.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
                var sessions = session.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);

                if (levels.Length == 0) levels = new string[] { level };
                if (teachers.Length == 0) teachers = new string[] { teacher };
                if (sessions.Length == 0) sessions = new string[] { session };

                int splitCount = levels.Length * teachers.Length * sessions.Length;
                int totalCopies = job.TotalPages * job.Copies;
                int baseCopies = totalCopies / splitCount;
                int remainder = totalCopies % splitCount;

                int count = 0;
                foreach (var currentTeacher in teachers)
                {
                    foreach (var currentLevel in levels)
                    {
                        foreach (var currentSession in sessions)
                        {
                            string tName = currentTeacher.Trim();
                            string lName = currentLevel.Trim();
                            string sName = currentSession.Trim();
                            
                            string key = $"{lName}_{tName}_{sName}";

                            if (!_statsDict.ContainsKey(key))
                            {
                                _statsDict[key] = new TeacherPrintStat
                                {
                                    TeacherName = tName,
                                    Level = lName,
                                    Session = sName
                                };
                            }

                            _statsDict[key].JobCount++;
                            _statsDict[key].TotalPages += job.TotalPages;
                            _statsDict[key].TotalPageCopies += baseCopies + (count == 0 ? remainder : 0);
                            
                            if (!string.IsNullOrEmpty(jobDate))
                            {
                                _statsDict[key].PrintDays.Add(jobDate);
                                if (!_statsDict[key].DailyPages.ContainsKey(jobDate))
                                {
                                    _statsDict[key].DailyPages[jobDate] = 0;
                                }
                                _statsDict[key].DailyPages[jobDate] += job.TotalPages;
                            }
                            
                            _statsDict[key].Jobs.Add(job);

                            string jobInfo = $"- {job.DocumentName} ({jobDate}, {job.TotalPages} pages)\n";
                            if (!_statsDict[key].JobsTooltip.Contains(jobInfo))
                            {
                                _statsDict[key].JobsTooltip += jobInfo;
                            }

                            count++;
                        }
                    }
                }
            }

            // Apply exemptions from Schedule Manager
            ApplyExemptionsFromManager(start, end);

            // Calculate Grades dynamically based on number of weeks (minimum 1 week)
            int weeks = (int)Math.Ceiling(totalDurationDays / 7.0);
            if (weeks == 0) weeks = 1;
            
            foreach (var stat in _statsDict.Values)
            {
                int activeDays = stat.PrintDays.Union(stat.ExemptedDates).Count();
                
                if (stat.ExemptedDates.Count >= totalDurationDays)
                {
                    var manager = PrintTrackerApp.Services.TeacherScheduleManager.Load();
                    string key = $"{stat.TeacherName}_{stat.Level}";
                    bool hasExam = false;
                    bool hasNoTeach = false;

                    if (manager.Schedules.ContainsKey(key))
                    {
                        for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
                        {
                            string dateStr = date.ToString("yyyy-MM-dd");
                            if (manager.Schedules[key].ContainsKey(dateStr))
                            {
                                string s = manager.Schedules[key][dateStr]?.ToLower();
                                if (s == "exam") hasExam = true;
                                if (s == "no teach") hasNoTeach = true;
                            }
                        }
                    }
                    if (hasExam && hasNoTeach) stat.Grade = "Exam/No Teach";
                    else if (hasExam) stat.Grade = "Exam";
                    else if (hasNoTeach) stat.Grade = "No Teach";
                    else stat.Grade = "Exam/No Teach";
                }
                else if (activeDays >= 4 * weeks) stat.Grade = "A";
                else if (activeDays >= 3 * weeks) stat.Grade = "B";
                else if (activeDays >= 2 * weeks) stat.Grade = "C";
                else if (activeDays >= 1 * weeks) stat.Grade = "D";
                else stat.Grade = "E";
            }

            dgStats.ItemsSource = _statsDict.Values.OrderBy(s => s.TeacherName).ThenBy(s => s.Level).ToList();
            RefreshExcelTabs();
            UpdateUnmatchedJobsUI();
        }

        private void GenerateDynamicColumns(DateTime start, DateTime end)
        {
            if (dgStats == null || dgStats.Columns == null) return;

            int totalPagesIndex = -1;
            int jobsIndex = -1;
            for (int i = 0; i < dgStats.Columns.Count; i++)
            {
                var header = dgStats.Columns[i].Header?.ToString();
                if (header == "Total Pages") totalPagesIndex = i;
                if (header == "Jobs") jobsIndex = i;
            }

            if (totalPagesIndex == -1 || jobsIndex == -1) return;

            // Remove existing dynamic columns between Jobs and Total Pages
            while (totalPagesIndex > jobsIndex + 1)
            {
                dgStats.Columns.RemoveAt(jobsIndex + 1);
                totalPagesIndex--;
            }

            // Insert new dynamic columns
            int insertIndex = jobsIndex + 1;
            for (DateTime dt = start; dt <= end; dt = dt.AddDays(1))
            {
                // Skip weekends to keep table clean (optional, but requested by teachers usually)
                // Let's include all days to be safe since they might teach on weekends
                string dateStr = dt.ToString("yyyy-MM-dd");
                string headerStr = dt.ToString("dd-MMM");
                
                var style = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
                style.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.TextAlignmentProperty, System.Windows.TextAlignment.Center));
                style.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center));
                style.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.PaddingProperty, new System.Windows.Thickness(5, 0, 5, 0)));

                var column = new System.Windows.Controls.DataGridTextColumn
                {
                    Header = headerStr,
                    Binding = new System.Windows.Data.Binding($"DailyPages[{dateStr}]")
                    {
                        TargetNullValue = ""
                    },
                    Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Auto),
                    MinWidth = 60,
                    IsReadOnly = true,
                    ElementStyle = style
                };
                
                dgStats.Columns.Insert(insertIndex, column);
                insertIndex++;
            }
        }

        private void ParseDocumentName(string docName, out string level, out string teacher, out string session)
        {
            level = "";
            teacher = "";
            session = "";

            // Remove extension
            if (docName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                docName = docName.Substring(0, docName.Length - 4);
            }

            // Fix Pre- dash issue (e.g., Pre-5 -> Pre5)
            docName = System.Text.RegularExpressions.Regex.Replace(docName, @"(?i)\bPre-([a-zA-Z0-9]+)\b", "Pre$1");

            // Split by dash or underscore
            var parts = docName.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

            // Find the index of the part containing "copies"
            int copiesIndex = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].IndexOf("copies", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    copiesIndex = i;
                    break;
                }
            }

            if (copiesIndex == -1)
            {
                // If "copies" not found, try to assume first 3 parts are Level-Teacher-Session
                if (parts.Length >= 3)
                {
                    level = parts[0].Trim();
                    teacher = parts[1].Trim();
                    session = parts[2].Trim();
                }
                else if (parts.Length == 2)
                {
                    level = parts[0].Trim();
                    teacher = parts[1].Trim();
                }
                else if (parts.Length == 1)
                {
                    teacher = parts[0].Trim();
                }
                return;
            }

            // Based on copiesIndex, determine the parts before it
            if (copiesIndex >= 3)
            {
                // Assume [Level]-[Teacher]-[Session]-[...]-[Copies]
                level = parts[0].Trim();
                teacher = parts[1].Trim();
                session = parts[2].Trim();
            }
            else if (copiesIndex == 2)
            {
                // Assume [Level]-[Teacher]-[Copies]
                level = parts[0].Trim();
                teacher = parts[1].Trim();
            }
            else if (copiesIndex == 1)
            {
                // Assume [Teacher]-[Copies]
                teacher = parts[0].Trim();
            }

            if (session.Equals("BS", StringComparison.OrdinalIgnoreCase))
            {
                session = "S1&S2";
            }
        }

        private void DataGrid_MouseDoubleClick_AutoFit(object sender, MouseButtonEventArgs e)
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
        private void ExcelCheckTimer_Tick(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentExcelPath) && File.Exists(_currentExcelPath))
            {
                var currentWriteTime = File.GetLastWriteTime(_currentExcelPath);
                if (currentWriteTime > _lastExcelWriteTime)
                {
                    elExcelChangedIndicator.Visibility = Visibility.Visible;
                    btnRefreshExcel.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Red);
                }
            }
        }

        private void BtnLoadData_Click(object sender, RoutedEventArgs e)
        {
            if (cmbDataSource.SelectedIndex == 1)
            {
                ImportGoogleSheetsData(sender);
            }
            else
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog();
                openFileDialog.Filter = "Excel Files|*.xls;*.xlsx;*.xlsm";
                if (openFileDialog.ShowDialog() == true)
                {
                    LoadExcelData(openFileDialog.FileName, true);
                }
            }
        }

        private void BtnUploadCsv_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "CSV Files|*.csv";
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
                    _currentJobs = allExternalJobs;
                    string sourceText = openFileDialog.FileNames.Length > 1 
                        ? $"External CSVs ({openFileDialog.FileNames.Length} files)" 
                        : $"External CSV ({System.IO.Path.GetFileName(openFileDialog.FileNames[0])})";
                    txtDataSource.Text = "Data Source: " + sourceText;
                    btnResetData.Visibility = Visibility.Visible;
                    
                    // Re-trigger current filter
                    if (cmbDateFilter.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Content != null)
                    {
                        CmbDateFilter_SelectionChanged(this, null);
                    }
                    else
                    {
                        ReloadFilteredData(dpStartDate.SelectedDate ?? DateTime.Now.Date, dpEndDate.SelectedDate ?? DateTime.Now.Date);
                    }
                    
                    string extra = errorMessages.Count > 0 ? $"\n(Some errors occurred:\n{string.Join("\n", errorMessages)})" : "";
                    System.Windows.MessageBox.Show($"Loaded {allExternalJobs.Count} records from {openFileDialog.FileNames.Length} CSV(s).{extra}", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    string extra = errorMessages.Count > 0 ? $"\nDetails:\n{string.Join("\n", errorMessages)}" : "";
                    System.Windows.MessageBox.Show($"No data found or invalid format.{extra}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private void BtnResetData_Click(object sender, RoutedEventArgs e)
        {
            _currentJobs = _originalJobs;
            txtDataSource.Text = "Data Source: App Log";
            btnResetData.Visibility = Visibility.Collapsed;
            
            if (cmbDateFilter.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Content != null)
            {
                CmbDateFilter_SelectionChanged(this, null);
            }
            else
            {
                ReloadFilteredData(dpStartDate.SelectedDate ?? DateTime.Now.Date, dpEndDate.SelectedDate ?? DateTime.Now.Date);
            }
        }

        private void BtnRefreshExcel_Click(object sender, RoutedEventArgs e)
        {
            if (cmbDataSource.SelectedIndex == 1)
            {
                ImportGoogleSheetsData(sender);
            }
            else
            {
                if (!string.IsNullOrEmpty(_currentExcelPath) && File.Exists(_currentExcelPath))
                {
                    LoadExcelData(_currentExcelPath, false);
                }
            }
        }

        private async void GoogleSheetsCheckTimer_Tick(object sender, EventArgs e)
        {
            var settings = PrintTrackerApp.Services.SettingsManager.LoadSettings();
            string spreadsheetId = settings.TeacherDataSpreadsheetId;
            if (string.IsNullOrWhiteSpace(spreadsheetId)) return;

            string appDataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintTrackerApp");
            string credentialsPath = System.IO.Path.Combine(appDataFolder, "google_credentials.json");
            if (!System.IO.File.Exists(credentialsPath)) return;

            var service = new PrintTrackerApp.Services.GoogleSheetsService(spreadsheetId, credentialsPath);
            var modifiedTime = await service.GetSpreadsheetModifiedTimeAsync();
            
            if (modifiedTime.HasValue && _lastGoogleSheetsWriteTime.HasValue)
            {
                if (modifiedTime.Value > _lastGoogleSheetsWriteTime.Value)
                {
                    btnRefreshExcel.Visibility = Visibility.Visible;
                    btnRefreshExcel.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Red);
                    elExcelChangedIndicator.Visibility = Visibility.Visible;
                }
            }
        }

        private void LoadExcelData(string path, bool showSuccessMessage)
        {
            try
            {
                using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (var reader = ExcelReaderFactory.CreateReader(stream))
                    {
                        var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                        {
                            ConfigureDataTable = (dataReader) => new ExcelDataTableConfiguration()
                            {
                                UseHeaderRow = false
                            }
                        });

                        _ftTable = result.Tables.Contains("FT") ? result.Tables["FT"] : (result.Tables.Count > 0 ? result.Tables[0] : null);
                        _ptTable = result.Tables.Contains("PT") ? result.Tables["PT"] : (result.Tables.Count > 1 ? result.Tables[1] : null);
                        _khTable = result.Tables.Contains("KH") ? result.Tables["KH"] : (result.Tables.Count > 2 ? result.Tables[2] : null);

                        RefreshExcelTabs();
                        
                        _currentExcelPath = path;
                        _lastExcelWriteTime = File.GetLastWriteTime(path);
                        
                        try { File.WriteAllText(GetConfigFilePath(), path); } catch { }
                        
                        btnRefreshExcel.Visibility = Visibility.Visible;
                        btnRefreshExcel.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D4"));
                        elExcelChangedIndicator.Visibility = Visibility.Collapsed;
                        
                        if (_googleSheetsCheckTimer.IsEnabled) _googleSheetsCheckTimer.Stop();
                        if (!_excelCheckTimer.IsEnabled) _excelCheckTimer.Start();
                        
                        if (showSuccessMessage)
                        {
                            System.Windows.MessageBox.Show("Excel data loaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error reading Excel file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshExcelTabs()
        {
            if (_ftTable != null) PopulateExcelGrid(gridFT, _ftTable, "FT");
            if (_ptTable != null) PopulateExcelGrid(gridPT, _ptTable, "PT");
            if (_khTable != null) PopulateExcelGrid(gridKH, _khTable, "KH");
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_statsDict == null || dgStats == null || txtSearch == null) return;
            string searchText = txtSearch.Text.ToLower().Trim();
            
            if (string.IsNullOrEmpty(searchText))
            {
                dgStats.ItemsSource = _statsDict.Values.OrderBy(s => s.TeacherName).ThenBy(s => s.Level).ToList();
            }
            else
            {
                dgStats.ItemsSource = _statsDict.Values
                    .Where(s => s.TeacherName.ToLower().Contains(searchText) || s.Level.ToLower().Contains(searchText))
                    .OrderBy(s => s.TeacherName).ThenBy(s => s.Level).ToList();
            }
            RefreshExcelTabs();
        }

        private void PopulateExcelGrid(Grid container, DataTable table, string tabType)
        {
            var records = GetRecordsForTab(table, tabType, false);

            container.Children.Clear();
            container.ColumnDefinitions.Clear();

            int columns = 4;
            int itemsPerColumn = (int)Math.Ceiling(records.Count / (double)columns);

            for (int i = 0; i < columns; i++)
            {
                container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var columnRecords = records.Skip(i * itemsPerColumn).Take(itemsPerColumn).ToList();

                var grid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    CanUserSortColumns = false,
                    Margin = new Thickness(2),
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    RowHeight = 25,
                    ItemsSource = columnRecords
                };

                var textStyle = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
                textStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.ToolTipProperty, new System.Windows.Data.Binding("JobsTooltip")));
                
                grid.Columns.Add(new DataGridTextColumn { Header = "Teacher", Binding = new System.Windows.Data.Binding("TeacherName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = textStyle });
                if (tabType != "PT")
                {
                    grid.Columns.Add(new DataGridTextColumn { Header = "Level", Binding = new System.Windows.Data.Binding("Level"), Width = DataGridLength.Auto });
                }

                var gradeCol = new DataGridTemplateColumn { Header = "Grade", Width = 80 };
                gradeCol.CellTemplate = (DataTemplate)this.FindResource("GradeTemplate");
                grid.Columns.Add(gradeCol);

                Grid.SetColumn(grid, i);
                container.Children.Add(grid);
            }
        }

        private void UpdateUnmatchedJobsUI()
        {
            if (btnUnmatchedFiles != null)
            {
                btnUnmatchedFiles.Content = $"Unmatched Files ({_unmatchedJobs.Count})";
                btnUnmatchedFiles.Visibility = _unmatchedJobs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void BtnScheduleSettings_Click(object sender, RoutedEventArgs e)
        {
            var teachers = new System.Collections.Generic.List<TeacherScheduleWindow.TeacherIdentifier>();
            ExtractTeachersFromTable(_ftTable, "FT", teachers);
            ExtractTeachersFromTable(_ptTable, "PT", teachers);
            ExtractTeachersFromTable(_khTable, "KH", teachers);

            var window = new TeacherScheduleWindow(teachers);
            window.Owner = Window.GetWindow(this);
            window.ShowDialog();
            
            // Reload data after settings close because schedules might have changed
            CmbDateFilter_SelectionChanged(this, null);
        }

        private void ExtractTeachersFromTable(DataTable table, string tabType, System.Collections.Generic.List<TeacherScheduleWindow.TeacherIdentifier> teachers)
        {
            if (table == null) return;
            int startRow = (tabType == "KH") ? 2 : 1;
            for (int i = startRow; i < table.Rows.Count; i++)
            {
                DataRow row = table.Rows[i];
                var no = row[0]?.ToString();
                if (string.IsNullOrWhiteSpace(no)) continue;

                string rawTeacher = row[1]?.ToString() ?? "";
                string rawLevel = table.Columns.Count > 2 ? (row[2]?.ToString() ?? "") : "";

                string teacherName = rawTeacher.Trim();
                string level = rawLevel.Trim();
                
                if (tabType == "PT")
                {
                    var parts = rawTeacher.Split('-');
                    if (parts.Length >= 3)
                    {
                        teacherName = parts[0].Trim();
                        level = parts[1].Trim();
                    }
                }

                var tId = new TeacherScheduleWindow.TeacherIdentifier { Name = teacherName, Level = level, Category = tabType };
                if (!teachers.Any(t => t.Name == tId.Name && t.Level == tId.Level))
                {
                    teachers.Add(tId);
                }
            }
        }

        private void ApplyExemptionsFromManager(DateTime start, DateTime end)
        {
            if (_statsDict == null) return;

            var manager = PrintTrackerApp.Services.TeacherScheduleManager.Load();
            
            var excelTeachers = new System.Collections.Generic.List<TeacherScheduleWindow.TeacherIdentifier>();
            ExtractTeachersFromTable(_ftTable, "FT", excelTeachers);
            ExtractTeachersFromTable(_ptTable, "PT", excelTeachers);
            ExtractTeachersFromTable(_khTable, "KH", excelTeachers);

            foreach (var stat in _statsDict.Values)
            {
                stat.ExemptedDates.Clear();
                
                var matchingExcel = excelTeachers.FirstOrDefault(et => et.Level == stat.Level && IsNameMatch(et.Name, stat.TeacherName, strict: false));
                string key = "";
                
                if (matchingExcel != null)
                {
                    key = $"{matchingExcel.Name}_{matchingExcel.Level}";
                }
                else
                {
                    key = $"{stat.TeacherName}_{stat.Level}";
                }

                if (manager.Schedules.ContainsKey(key))
                {
                    for (DateTime date = _currentStart.Date; date <= _currentEnd.Date; date = date.AddDays(1))
                    {
                        string dateStr = date.ToString("yyyy-MM-dd");
                        if (manager.Schedules[key].ContainsKey(dateStr))
                        {
                            string status = manager.Schedules[key][dateStr];
                            if (status?.ToLower() == "no teach" || status?.ToLower() == "exam")
                            {
                                stat.ExemptedDates.Add(dateStr);
                            }
                        }
                    }
                }
            }
        }

        private string CalculateGradeFromExemptionsOnly(string teacherName, string level)
        {
            var manager = PrintTrackerApp.Services.TeacherScheduleManager.Load();
            string key = $"{teacherName}_{level}";
            int exemptedCount = 0;
            bool hasExam = false;
            bool hasNoTeach = false;

            if (manager.Schedules.ContainsKey(key))
            {
                for (DateTime date = _currentStart.Date; date <= _currentEnd.Date; date = date.AddDays(1))
                {
                    string dateStr = date.ToString("yyyy-MM-dd");
                    if (manager.Schedules[key].ContainsKey(dateStr))
                    {
                        string status = manager.Schedules[key][dateStr];
                        if (status?.ToLower() == "no teach" || status?.ToLower() == "exam")
                        {
                            exemptedCount++;
                            if (status?.ToLower() == "exam") hasExam = true;
                            if (status?.ToLower() == "no teach") hasNoTeach = true;
                        }
                    }
                }
            }

            int teachDaysCount = _currentDurationDays - exemptedCount;
            if (teachDaysCount > 0)
            {
                // If they had ANY teaching days but printed 0 times, they fail.
                return "E";
            }
            
            // All days were exempted
            if (hasExam && hasNoTeach) return "Exam/No Teach";
            if (hasExam) return "Exam";
            if (hasNoTeach) return "No Teach";
            
            return "Exam/No Teach";
        }

        private void BtnUnmatchedFiles_Click(object sender, RoutedEventArgs e)
        {
            var window = new UnmatchedFilesWindow(_unmatchedJobs);
            window.Owner = Window.GetWindow(this);
            window.ShowDialog();
        }

        private TeacherPrintStat FindBestMatch(string excelName, string level, string session)
        {
            // Only consider jobs that match the Excel level
            var levelMatches = _statsDict.Values.Where(v => 
                string.Equals(v.Level, level, StringComparison.OrdinalIgnoreCase)).ToList();
            
            if (!string.IsNullOrEmpty(session))
            {
                var sessionMatches = levelMatches.Where(v => 
                    string.Equals(v.Session, session, StringComparison.OrdinalIgnoreCase) || 
                    string.IsNullOrWhiteSpace(v.Session)).ToList();
                    
                if (sessionMatches.Count > 0) levelMatches = sessionMatches;
            }

            // 1. Try exact or strong name match within the valid candidates
            var strongMatch = levelMatches.FirstOrDefault(v => IsNameMatch(excelName, v.TeacherName, strict: true));
            if (strongMatch != null) return strongMatch;

            // 2. Try loose name match within the valid candidates
            var looseMatch = levelMatches.FirstOrDefault(v => IsNameMatch(excelName, v.TeacherName, strict: false));
            if (looseMatch != null) return looseMatch;
            
            return null;
        }

        private bool IsNameMatch(string excelName, string dictName, bool strict)
        {
            if (string.IsNullOrWhiteSpace(excelName) || string.IsNullOrWhiteSpace(dictName)) return false;
            
            excelName = excelName.ToLower().Trim();
            dictName = dictName.ToLower().Trim();
            
            if (excelName == dictName || excelName.Contains(dictName) || dictName.Contains(excelName)) return true;
            
            var excelWords = excelName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var dictWords = dictName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var eWord in excelWords)
            {
                foreach (var dWord in dictWords)
                {
                    if (eWord == dWord || eWord.Contains(dWord) || dWord.Contains(eWord))
                        return true;
                        
                    if (!strict && eWord.Length >= 4 && dWord.Length >= 4)
                    {
                        int lcs = ComputeLCS(eWord, dWord);
                        int minLen = Math.Min(eWord.Length, dWord.Length);
                        if (lcs >= minLen - 1 && lcs >= 4)
                        {
                            return true;
                        }
                    }
                }
            }
            
            return false;
        }

        private int ComputeLCS(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] dp = new int[n + 1, m + 1];

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    if (s[i - 1] == t[j - 1])
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }
            return dp[n, m];
        }

        private void TabControl_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                if (e.Delta > 0)
                {
                    uiScale.ScaleX += 0.1;
                    uiScale.ScaleY += 0.1;
                }
                else if (e.Delta < 0)
                {
                    uiScale.ScaleX -= 0.1;
                    uiScale.ScaleY -= 0.1;
                }

                if (uiScale.ScaleX < 0.5) uiScale.ScaleX = 0.5;
                if (uiScale.ScaleY < 0.5) uiScale.ScaleY = 0.5;
                if (uiScale.ScaleX > 3.0) uiScale.ScaleX = 3.0;
                if (uiScale.ScaleY > 3.0) uiScale.ScaleY = 3.0;

                e.Handled = true;
            }
        }
        private List<TeacherExcelRecord> GetRecordsForTab(System.Data.DataTable table, string tabType, bool ignoreSearch)
        {
            var records = new List<TeacherExcelRecord>();
            int startRow = (tabType == "KH") ? 2 : 1; 
            string searchText = ignoreSearch ? "" : (txtSearch?.Text?.ToLower()?.Trim() ?? "");

            for (int i = startRow; i < table.Rows.Count; i++)
            {
                System.Data.DataRow row = table.Rows[i];
                var no = row[0]?.ToString();
                if (string.IsNullOrWhiteSpace(no)) continue;

                string rawTeacher = row[1]?.ToString() ?? "";
                string rawLevel = table.Columns.Count > 2 ? (row[2]?.ToString() ?? "") : "";

                if (!string.IsNullOrEmpty(searchText))
                {
                    if (!rawTeacher.ToLower().Contains(searchText) && !rawLevel.ToLower().Contains(searchText))
                    {
                        continue;
                    }
                }

                string teacherName = rawTeacher;
                string level = rawLevel;
                string grade = "";

                if (tabType == "PT")
                {
                    var parts = rawTeacher.Split('-');
                    if (parts.Length >= 3)
                    {
                        teacherName = parts[0].Trim();
                        level = parts[1].Trim();
                        string session = parts[2].Trim();
                        
                        var bestMatch = FindBestMatch(teacherName, level, session);
                        if (bestMatch != null) 
                        {
                            grade = bestMatch.Grade;
                            bestMatch.IsMatched = true;
                        }
                        else
                        {
                            grade = CalculateGradeFromExemptionsOnly(teacherName, level);
                        }
                    }
                }
                else
                {
                    teacherName = rawTeacher.Trim();
                    level = rawLevel.Trim();

                    var bestMatch = FindBestMatch(teacherName, level, null);
                    if (bestMatch != null) 
                    {
                        grade = bestMatch.Grade;
                        bestMatch.IsMatched = true;
                    }
                    else
                    {
                        grade = CalculateGradeFromExemptionsOnly(teacherName, level);
                    }
                }

                records.Add(new TeacherExcelRecord
                {
                    No = no,
                    TeacherName = rawTeacher,
                    Level = rawLevel,
                    Grade = string.IsNullOrEmpty(grade) ? "E" : grade,
                    JobsTooltip = (tabType == "PT" ? FindBestMatch(teacherName, level, rawTeacher.Split('-').Length >= 3 ? rawTeacher.Split('-')[2].Trim() : null)?.JobsTooltip : FindBestMatch(teacherName, level, null)?.JobsTooltip) ?? ""
                });
            }
            return records;
        }

        private async void BtnExportGoogleSheets_Click(object sender, RoutedEventArgs e)
        {
            var settings = PrintTrackerApp.Services.SettingsManager.LoadSettings();
            string spreadsheetId = settings.GoogleSpreadsheetId;
            if (string.IsNullOrWhiteSpace(spreadsheetId))
            {
                System.Windows.MessageBox.Show("Please configure the Google Spreadsheet ID in Settings.", "Configuration Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string appDataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintTrackerApp");
            string credentialsPath = System.IO.Path.Combine(appDataFolder, "google_credentials.json");
            if (!System.IO.File.Exists(credentialsPath))
            {
                System.Windows.MessageBox.Show($"Could not find 'google_credentials.json' at {credentialsPath}.", "Credentials Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var btn = sender as System.Windows.Controls.Button;
                if (btn != null) btn.IsEnabled = false;

                var service = new PrintTrackerApp.Services.GoogleSheetsService(spreadsheetId, credentialsPath);

                var tabs = new[] { ("FT", _ftTable), ("PT", _ptTable), ("KH", _khTable) };
                
                foreach (var (tabName, table) in tabs)
                {
                    if (table == null) continue;
                    var records = GetRecordsForTab(table, tabName, true); // ignore UI search
                    
                    int columns = 4;
                    int itemsPerColumn = (int)Math.Ceiling(records.Count / (double)columns);
                    var sheetData = new List<IList<object>>();
                    var sheetNotes = new List<IList<string>>();

                    // Build Header Row (Row 0)
                    var headerRow = new List<object>();
                    var headerNotes = new List<string>();
                    for (int c = 0; c < columns; c++)
                    {
                        if (tabName == "PT")
                        {
                            headerRow.Add("Teacher"); headerNotes.Add("");
                            headerRow.Add("Grade"); headerNotes.Add("");
                        }
                        else
                        {
                            headerRow.Add("Teacher"); headerNotes.Add("");
                            headerRow.Add("Level / Class"); headerNotes.Add("");
                            headerRow.Add("Grade"); headerNotes.Add("");
                        }
                        if (c < columns - 1)
                        {
                            headerRow.Add(""); // spacer column
                            headerNotes.Add("");
                        }
                    }
                    sheetData.Add(headerRow);
                    sheetNotes.Add(headerNotes);

                    // Build Data Rows
                    for (int r = 0; r < itemsPerColumn; r++)
                    {
                        var row = new List<object>();
                        var noteRow = new List<string>();
                        for (int c = 0; c < columns; c++)
                        {
                            int recordIndex = c * itemsPerColumn + r;
                            if (recordIndex < records.Count)
                            {
                                var rec = records[recordIndex];
                                string teacherNote = !string.IsNullOrWhiteSpace(rec.JobsTooltip) ? rec.JobsTooltip.TrimEnd('\r', '\n') : "";
                                if (tabName == "PT")
                                {
                                    row.Add(rec.TeacherName); noteRow.Add(teacherNote);
                                    row.Add(rec.Grade); noteRow.Add("");
                                }
                                else
                                {
                                    row.Add(rec.TeacherName); noteRow.Add(teacherNote);
                                    row.Add(rec.Level); noteRow.Add("");
                                    row.Add(rec.Grade); noteRow.Add("");
                                }
                            }
                            else
                            {
                                if (tabName == "PT")
                                {
                                    row.Add(""); noteRow.Add("");
                                    row.Add(""); noteRow.Add("");
                                }
                                else
                                {
                                    row.Add(""); noteRow.Add("");
                                    row.Add(""); noteRow.Add("");
                                    row.Add(""); noteRow.Add("");
                                }
                            }
                            if (c < columns - 1)
                            {
                                row.Add(""); // spacer column
                                noteRow.Add("");
                            }
                        }
                        sheetData.Add(row);
                        sheetNotes.Add(noteRow);
                    }

                    await service.ClearSheetAsync(tabName);
                    await service.WriteAndFormatDashboardDataAsync(tabName, sheetData, sheetNotes);
                }

                System.Windows.MessageBox.Show("Successfully exported to Google Sheets!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error exporting to Google Sheets: {ex.Message}", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                var btn = sender as System.Windows.Controls.Button;
                if (btn != null) btn.IsEnabled = true;
            }
        }

        private async void ImportGoogleSheetsData(object sender)
        {
            var settings = PrintTrackerApp.Services.SettingsManager.LoadSettings();
            string spreadsheetId = settings.TeacherDataSpreadsheetId;
            if (string.IsNullOrWhiteSpace(spreadsheetId))
            {
                System.Windows.MessageBox.Show("Please configure the Teacher Data Spreadsheet ID in Settings.", "Configuration Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string appDataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrintTrackerApp");
            string credentialsPath = System.IO.Path.Combine(appDataFolder, "google_credentials.json");
            if (!System.IO.File.Exists(credentialsPath))
            {
                System.Windows.MessageBox.Show($"Could not find 'google_credentials.json' at {credentialsPath}.", "Credentials Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var btn = sender as System.Windows.Controls.Button;
                if (btn != null) btn.IsEnabled = false;

                var service = new PrintTrackerApp.Services.GoogleSheetsService(spreadsheetId, credentialsPath);
                
                var sheetNames = await service.GetSheetNamesAsync();
                
                string ftName = sheetNames.Contains("FT") ? "FT" : (sheetNames.Count > 0 ? sheetNames[0] : null);
                string ptName = sheetNames.Contains("PT") ? "PT" : (sheetNames.Count > 1 ? sheetNames[1] : null);
                string khName = sheetNames.Contains("KH") ? "KH" : (sheetNames.Count > 2 ? sheetNames[2] : null);

                if (ftName != null) _ftTable = await service.ReadSheetAsDataTableAsync(ftName);
                if (ptName != null) _ptTable = await service.ReadSheetAsDataTableAsync(ptName);
                if (khName != null) _khTable = await service.ReadSheetAsDataTableAsync(khName);

                RefreshExcelTabs();
                
                _currentExcelPath = null;
                try { File.WriteAllText(GetConfigFilePath(), "GoogleSheets"); } catch { }
                if (_excelCheckTimer.IsEnabled) _excelCheckTimer.Stop();
                
                _lastGoogleSheetsWriteTime = await service.GetSpreadsheetModifiedTimeAsync();
                if (!_googleSheetsCheckTimer.IsEnabled) _googleSheetsCheckTimer.Start();

                btnRefreshExcel.Visibility = Visibility.Visible;
                btnRefreshExcel.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D4"));
                elExcelChangedIndicator.Visibility = Visibility.Collapsed;
                
                System.Windows.MessageBox.Show("Google Sheets data loaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error reading from Google Sheets: {ex.Message}", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                var btn = sender as System.Windows.Controls.Button;
                if (btn != null) btn.IsEnabled = true;
            }
        }
    }
}
