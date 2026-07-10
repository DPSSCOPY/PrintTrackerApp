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
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

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

        private bool _isExternalDataMode = false;
        public event EventHandler OnResetDataClicked;

        // Cached last calculation result for re-sorting without recalculating
        private DashboardCalculationResult _lastResult = null;

        // ── Performance: Pre-compiled Regex fields (avoids re-compilation per call) ──
        // ParseDocumentName patterns
        private static readonly Regex _rxPreDash  = new Regex(@"(?i)\bPre-([0-9][a-zA-Z0-9]*)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _rxCopies1  = new Regex(@"^\d+[-_]?c[o0]?[o0]?p[a-z]*$",   RegexOptions.Compiled);
        private static readonly Regex _rxCopies2  = new Regex(@"^\d+[-_]?cps$",                   RegexOptions.Compiled);
        private static readonly Regex _rxCopies3  = new Regex(@"^c[o0]?[o0]?p[ieys]+$",           RegexOptions.Compiled);
        // NormalizeLevel patterns
        private static readonly Regex _rxNormSep     = new Regex(@"[-_,/]",    RegexOptions.Compiled);
        private static readonly Regex _rxNormSpecial = new Regex(@"[^a-z0-9]", RegexOptions.Compiled);
        private static readonly (Regex Word, Regex Prefix)[] _rxNormWords = BuildNormWordPatterns();

        private static (Regex Word, Regex Prefix)[] BuildNormWordPatterns()
        {
            string[] words = { "level", "class", "grade", "room", "sec", "section", "group",
                               "ft", "pt", "kh", "morning", "afternoon", "am", "pm",
                               "term", "sem", "semester" };
            return words.Select(w => (
                new Regex(@"\b" + w + @"\b",        RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"^"  + w + @"(?=[0-9a-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase)
            )).ToArray();
        }

        // NormalizeLevel result cache (thread-safe)
        private static readonly ConcurrentDictionary<string, string> _normalizedLevelCache =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Category sets – only rebuilt once per UI-triggered refresh
        private bool _categorySetsLoaded = false;
        private HashSet<string> _ftSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _ptSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _khSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Search debounce timer (300 ms delay)
        private System.Windows.Threading.DispatcherTimer _searchDebounceTimer;

        private static readonly Dictionary<string, DateTime> _parsedTimestampCache = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly object _timestampLock = new object();

        private static bool TryGetParsedTimestamp(string timestampStr, out DateTime dt)
        {
            if (string.IsNullOrWhiteSpace(timestampStr))
            {
                dt = default;
                return false;
            }

            lock (_timestampLock)
            {
                if (_parsedTimestampCache.TryGetValue(timestampStr, out dt))
                {
                    return true;
                }
            }

            bool parsed = DateTime.TryParse(timestampStr, out dt);
            if (!parsed) parsed = DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt);
            if (!parsed) parsed = DateTime.TryParse(timestampStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt);

            if (parsed)
            {
                lock (_timestampLock)
                {
                    _parsedTimestampCache[timestampStr] = dt;
                }
            }
            return parsed;
        }

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

            _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            
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
            _originalJobs = printJobs;
            if (_isExternalDataMode)
            {
                return;
            }
            _currentJobs = printJobs;
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

        /// <summary>
        /// Load historical (old log) data into the dashboard.
        /// Auto-detects the date range from the jobs so all records are visible,
        /// and switches the filter to "Custom Range" to cover the full period.
        /// </summary>
        public void LoadHistoricalData(IEnumerable<PrintJobInfo> printJobs, string csvExportPath, string sourceName = null)
        {
            _isExternalDataMode = true;
            _currentJobs = printJobs;
            _csvExportPath = csvExportPath;

            string sourceText = string.IsNullOrEmpty(sourceName) ? "External Log" : sourceName;
            if (txtDataSource != null)
            {
                txtDataSource.Text = "Data Source: " + sourceText;
            }
            if (btnResetData != null)
            {
                btnResetData.Visibility = System.Windows.Visibility.Visible;
            }

            // Auto-detect min/max date from the jobs
            DateTime minDate = DateTime.Now.Date;
            DateTime maxDate = DateTime.Now.Date;
            bool anyParsed = false;

            foreach (var job in printJobs)
            {
                DateTime dt;
                bool parsed = DateTime.TryParse(job.Timestamp, out dt);
                if (!parsed) parsed = DateTime.TryParseExact(job.Timestamp, "yyyy-MM-dd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out dt);
                if (!parsed) parsed = DateTime.TryParse(job.Timestamp,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out dt);

                if (parsed)
                {
                    if (!anyParsed)
                    {
                        minDate = dt.Date;
                        maxDate = dt.Date;
                        anyParsed = true;
                    }
                    else
                    {
                        if (dt.Date < minDate) minDate = dt.Date;
                        if (dt.Date > maxDate) maxDate = dt.Date;
                    }
                }
            }

            // Switch filter to "Custom Range" and set dates to cover all loaded records
            dpStartDate.SelectedDate = minDate;
            dpEndDate.SelectedDate = maxDate;
            spCustomDate.Visibility = System.Windows.Visibility.Visible;

            // Set combobox to "Custom Range" without triggering the selection-changed reload twice
            foreach (System.Windows.Controls.ComboBoxItem ci in cmbDateFilter.Items)
            {
                if (ci.Content?.ToString() == "Custom Range")
                {
                    cmbDateFilter.SelectedItem = ci;
                    break;
                }
            }

            // Always reload with detected range
            ReloadFilteredData(minDate, maxDate);
        }

        public void ResetToDefaultData()
        {
            _isExternalDataMode = false;
            if (_originalJobs != null)
            {
                _currentJobs = _originalJobs;
            }
            if (txtDataSource != null)
            {
                txtDataSource.Text = "Data Source: App Log";
            }
            if (btnResetData != null)
            {
                btnResetData.Visibility = System.Windows.Visibility.Collapsed;
            }

            if (cmbDateFilter != null)
            {
                foreach (System.Windows.Controls.ComboBoxItem ci in cmbDateFilter.Items)
                {
                    if (ci.Content?.ToString() == "Today")
                    {
                        cmbDateFilter.SelectedItem = ci;
                        break;
                    }
                }
                CmbDateFilter_SelectionChanged(this, null);
            }
        }

        public void RefreshDashboard()
        {
            if (_currentJobs == null) return;
            if (_currentStart != DateTime.MinValue && _currentEnd != DateTime.MinValue)
            {
                ReloadFilteredData(_currentStart, _currentEnd);
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

        private class DashboardCalculationResult
        {
            public List<PrintJobInfo> AllJobs { get; set; } = new List<PrintJobInfo>();
            public Dictionary<string, TeacherPrintStat> StatsDict { get; set; } = new Dictionary<string, TeacherPrintStat>();
            public List<PrintJobInfo> UnmatchedJobs { get; set; } = new List<PrintJobInfo>();
            public List<TeacherExcelRecord> FtRecords { get; set; } = new List<TeacherExcelRecord>();
            public List<TeacherExcelRecord> PtRecords { get; set; } = new List<TeacherExcelRecord>();
            public List<TeacherExcelRecord> KhRecords { get; set; } = new List<TeacherExcelRecord>();
        }

        private async void ReloadFilteredData(DateTime start, DateTime end)
        {
            if (_currentJobs == null) return;

            // Reset category sets loaded flag for this UI refresh cycle
            _categorySetsLoaded = false;

            var currentJobsCopy = _currentJobs.ToList();
            string searchText = txtSearch?.Text?.ToLower()?.Trim() ?? "";

            var result = await Task.Run(() =>
            {
                var levelCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var nameCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var res = new DashboardCalculationResult();
                var filteredJobs = new List<PrintJobInfo>();

                // 1. Get from memory
                foreach (var job in currentJobsCopy)
                {
                    if (TryGetParsedTimestamp(job.Timestamp, out DateTime dt) &&
                        dt.Date >= start.Date && dt.Date <= end.Date)
                    {
                        filteredJobs.Add(job);
                    }
                }

                if (!_isExternalDataMode)
                {
                    // 2. Load from CSV history
                    var historyJobs = Services.CsvLogger.LoadJobsFromCsvForDateRange(_csvExportPath, start, end);

                    // 3. Merge avoiding duplicates
                    var memoryKeys = new HashSet<string>(filteredJobs.Select(j => $"{j.Timestamp}_{j.DocumentName}_{j.Owner}"));
                    foreach (var hJob in historyJobs)
                    {
                        string key = $"{hJob.Timestamp}_{hJob.DocumentName}_{hJob.Owner}";
                        if (!memoryKeys.Contains(key))
                        {
                            filteredJobs.Add(hJob);
                        }
                    }
                }

                res.AllJobs = filteredJobs;

                // 4. Populate StatsDict
                var processedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var tooltipLinesPerKey = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var job in res.AllJobs)
                {
                    if (string.IsNullOrWhiteSpace(job.DocumentName))
                        continue;

                    ParseDocumentName(job.DocumentName, out string level, out string teacher, out string session);

                    // If file is missing level or teacher, it's considered malformed/unmatched and is NOT assigned to anyone
                    if (string.IsNullOrWhiteSpace(level) || string.IsNullOrWhiteSpace(teacher))
                    {
                        res.UnmatchedJobs.Add(job);
                        continue;
                    }

                    string jobDate = "";
                    if (TryGetParsedTimestamp(job.Timestamp, out DateTime dt))
                    {
                        jobDate = dt.Date.ToString("yyyy-MM-dd");
                    }

                    // Split by regex or fallback to '&' and spaces
                    var levels = SplitLevels(level).ToArray();
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

                                if (!res.StatsDict.ContainsKey(key))
                                {
                                    res.StatsDict[key] = new TeacherPrintStat
                                    {
                                        TeacherName = tName,
                                        Level = lName,
                                        Session = sName
                                    };
                                }

                                string dupKey = $"{key}|{jobDate ?? ""}|{job.DocumentName.Trim().ToLower()}";
                                bool isDuplicateDailyPrint = !processedKeys.Add(dupKey);

                                if (!isDuplicateDailyPrint)
                                {
                                    res.StatsDict[key].JobCount++;
                                    res.StatsDict[key].TotalPages += job.TotalPages;
                                    res.StatsDict[key].TotalPageCopies += baseCopies + (count == 0 ? remainder : 0);
                                    
                                    if (!string.IsNullOrEmpty(jobDate))
                                    {
                                        res.StatsDict[key].PrintDays.Add(jobDate);
                                        if (!res.StatsDict[key].DailyPages.ContainsKey(jobDate))
                                        {
                                            res.StatsDict[key].DailyPages[jobDate] = 0;
                                        }
                                        res.StatsDict[key].DailyPages[jobDate] += job.TotalPages;
                                    }
                                }
                                
                                res.StatsDict[key].Jobs.Add(job);

                                // O(1) tooltip line deduplication
                                string jobInfo = $"- {job.DocumentName} ({jobDate}, {job.TotalPages} pages)";
                                if (!tooltipLinesPerKey.TryGetValue(key, out var tipSet))
                                {
                                    tipSet = new HashSet<string>(StringComparer.Ordinal);
                                    tooltipLinesPerKey[key] = tipSet;
                                }
                                tipSet.Add(jobInfo);

                                count++;
                            }
                        }
                    }
                }

                // Build JobsTooltip from deduplicated HashSets
                foreach (var kv in tooltipLinesPerKey)
                {
                    if (res.StatsDict.TryGetValue(kv.Key, out var statEntry))
                    {
                        statEntry.JobsTooltip = string.Join("\n", kv.Value) + "\n";
                    }
                }

                // 5. Apply Exemptions
                var manager = PrintTrackerApp.Services.TeacherScheduleManager.Load();
                ApplyExemptionsFromManagerInternal(res.StatsDict, start, end, manager);

                // 6. Calculate Grades
                int duration = (end.Date - start.Date).Days + 1;
                int weeks = (int)Math.Ceiling(duration / 7.0);
                if (weeks == 0) weeks = 1;

                var excelTeachers = new List<TeacherScheduleWindow.TeacherIdentifier>();
                ExtractTeachersFromTable(_ftTable, "FT", excelTeachers);
                ExtractTeachersFromTable(_ptTable, "PT", excelTeachers);
                ExtractTeachersFromTable(_khTable, "KH", excelTeachers);

                foreach (var stat in res.StatsDict.Values)
                {
                    int activeDays = stat.PrintDays.Union(stat.ExemptedDates).Count();
                    
                    if (stat.ExemptedDates.Count >= duration)
                    {
                        string statTab = GetCategoryOfStat(stat);
                        TeacherScheduleWindow.TeacherIdentifier matchingExcel = null;
                        if (statTab == "PT")
                        {
                            string targetLvlSes = string.IsNullOrEmpty(stat.Session) ? stat.Level : $"{stat.Level}-{stat.Session}";
                            matchingExcel = excelTeachers.FirstOrDefault(et => 
                                et.Category == "PT" &&
                                IsNameMatch(et.Name, stat.TeacherName, strict: true) &&
                                IsLevelMatch(et.Level, targetLvlSes));
                            if (matchingExcel == null)
                            {
                                matchingExcel = excelTeachers.FirstOrDefault(et => 
                                    et.Category == "PT" &&
                                    IsNameMatch(et.Name, stat.TeacherName, strict: false) &&
                                    IsLevelMatch(et.Level, targetLvlSes));
                            }
                        }
                        else
                        {
                            matchingExcel = excelTeachers.FirstOrDefault(et => 
                                et.Category != "PT" &&
                                IsNameMatch(et.Name, stat.TeacherName, strict: true));
                            if (matchingExcel == null)
                            {
                                matchingExcel = excelTeachers.FirstOrDefault(et => 
                                    et.Category != "PT" &&
                                    IsNameMatch(et.Name, stat.TeacherName, strict: false));
                            }
                        }

                        string key = (matchingExcel != null && statTab == "PT") 
                            ? $"{matchingExcel.Name}_{matchingExcel.Level}" 
                            : (matchingExcel != null ? $"{matchingExcel.Name}_{stat.Level}" : $"{stat.TeacherName}_{stat.Level}");
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

                // 7. Get Excel Records FT, PT, KH
                res.FtRecords = GetRecordsForTabInternal(_ftTable, "FT", searchText, res.StatsDict, manager, start, end, levelCache, nameCache);
                res.PtRecords = GetRecordsForTabInternal(_ptTable, "PT", searchText, res.StatsDict, manager, start, end, levelCache, nameCache);
                res.KhRecords = GetRecordsForTabInternal(_khTable, "KH", searchText, res.StatsDict, manager, start, end, levelCache, nameCache);

                return res;
            });

            int durationDays = (end.Date - start.Date).Days + 1;
            _lastResult = result;
            BindDashboardData(result, start, end, durationDays, searchText);
        }

        private void BindDashboardData(DashboardCalculationResult res, DateTime start, DateTime end, int totalDurationDays, string searchText)
        {
            _currentStart = start;
            _currentEnd = end;
            _currentDurationDays = totalDurationDays;

            GenerateDynamicColumns(start, end);

            _statsDict = res.StatsDict;
            _unmatchedJobs = res.UnmatchedJobs;

            // Determine active sort key
            string sortKey = GetCurrentSortKey();

            // All Data tab
            var allStats = string.IsNullOrEmpty(searchText)
                ? _statsDict.Values.ToList()
                : _statsDict.Values
                    .Where(s => s.TeacherName.ToLower().Contains(searchText) || s.Level.ToLower().Contains(searchText))
                    .ToList();

            var manager = PrintTrackerApp.Services.TeacherScheduleManager.Load();
            var excelTeachers = new List<TeacherScheduleWindow.TeacherIdentifier>();
            ExtractTeachersFromTable(_ftTable, "FT", excelTeachers);
            ExtractTeachersFromTable(_ptTable, "PT", excelTeachers);
            ExtractTeachersFromTable(_khTable, "KH", excelTeachers);

            var filteredStats = new List<TeacherPrintStat>();
            var levelCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var nameCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var stat in allStats)
            {
                string statTab = GetCategoryOfStat(stat);
                TeacherScheduleWindow.TeacherIdentifier matchingExcel = null;
                if (statTab == "PT")
                {
                    string targetLvlSes = string.IsNullOrEmpty(stat.Session) ? stat.Level : $"{stat.Level}-{stat.Session}";
                    matchingExcel = excelTeachers.FirstOrDefault(et => 
                        et.Category == "PT" &&
                        GetIsNameMatch(et.Name, stat.TeacherName, true, nameCache) &&
                        GetIsLevelMatch(et.Level, targetLvlSes, levelCache));
                    if (matchingExcel == null)
                    {
                        matchingExcel = excelTeachers.FirstOrDefault(et => 
                            et.Category == "PT" &&
                            GetIsNameMatch(et.Name, stat.TeacherName, false, nameCache) &&
                            GetIsLevelMatch(et.Level, targetLvlSes, levelCache));
                    }
                }
                else
                {
                    matchingExcel = excelTeachers.FirstOrDefault(et => 
                        et.Category != "PT" &&
                        GetIsNameMatch(et.Name, stat.TeacherName, true, nameCache));
                    if (matchingExcel == null)
                    {
                        matchingExcel = excelTeachers.FirstOrDefault(et => 
                            et.Category != "PT" &&
                            GetIsNameMatch(et.Name, stat.TeacherName, false, nameCache));
                    }
                }
                
                if (matchingExcel != null)
                {
                    if (manager.HiddenTeachers.Contains(matchingExcel.RawName) || manager.HiddenTeachers.Contains(matchingExcel.Name))
                    {
                        continue;
                    }
                }

                string scheduleLevelKey = (statTab == "PT")
                    ? (string.IsNullOrEmpty(stat.Session) ? stat.Level : $"{stat.Level}-{stat.Session}")
                    : stat.Level;
                string hiddenCheckKey = $"{stat.TeacherName}_{scheduleLevelKey}";
                string dateKey = $"{hiddenCheckKey}_{start.ToString("yyyy-MM-dd")}_{end.ToString("yyyy-MM-dd")}";
                if (manager.HiddenTeachers.Contains(hiddenCheckKey) || manager.HiddenTeachers.Contains(dateKey))
                {
                    continue;
                }

                filteredStats.Add(stat);
            }

            dgStats.ItemsSource = ApplySortToStats(filteredStats, sortKey);

            // FT / PT / KH tabs
            PopulateExcelGridDirect(gridFT, ApplySortToRecords(res.FtRecords, sortKey), "FT");
            PopulateExcelGridDirect(gridPT, ApplySortToRecords(res.PtRecords, sortKey), "PT");
            PopulateExcelGridDirect(gridKH, ApplySortToRecords(res.KhRecords, sortKey), "KH");

            UpdateUnmatchedJobsUI();
        }

        // Returns the sort key from the current cmbSort selection
        private string GetCurrentSortKey()
        {
            if (cmbSort?.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag)
                return tag;
            return "default";
        }

        // Sort a list of TeacherPrintStat (All Data tab)
        private static readonly Dictionary<string, int> _gradeOrder =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            { {"A",1},{"B",2},{"C",3},{"D",4},{"E",5},{"Exam",6},{"No Teach",7},{"Exam/No Teach",8} };

        private static int GradeRank(string g) =>
            _gradeOrder.TryGetValue(g ?? "", out int r) ? r : 99;

        private List<TeacherPrintStat> ApplySortToStats(List<TeacherPrintStat> list, string sortKey)
        {
            switch (sortKey)
            {
                case "name_asc":   return list.OrderBy(s => s.TeacherName).ThenBy(s => s.Level).ToList();
                case "name_desc":  return list.OrderByDescending(s => s.TeacherName).ThenBy(s => s.Level).ToList();
                case "level_asc":  return list.OrderBy(s => s.Level).ThenBy(s => s.TeacherName).ToList();
                case "level_desc": return list.OrderByDescending(s => s.Level).ThenBy(s => s.TeacherName).ToList();
                case "grade_asc":  return list.OrderBy(s => GradeRank(s.Grade)).ThenBy(s => s.TeacherName).ToList();
                case "grade_desc": return list.OrderByDescending(s => GradeRank(s.Grade)).ThenBy(s => s.TeacherName).ToList();
                default:           return list.OrderBy(s => s.TeacherName).ThenBy(s => s.Level).ToList();
            }
        }

        // Sort a list of TeacherExcelRecord (FT/PT/KH tabs)
        private List<TeacherExcelRecord> ApplySortToRecords(List<TeacherExcelRecord> list, string sortKey)
        {
            switch (sortKey)
            {
                case "name_asc":   return list.OrderBy(r => r.TeacherName).ThenBy(r => r.Level).ToList();
                case "name_desc":  return list.OrderByDescending(r => r.TeacherName).ThenBy(r => r.Level).ToList();
                case "level_asc":  return list.OrderBy(r => r.Level).ThenBy(r => r.TeacherName).ToList();
                case "level_desc": return list.OrderByDescending(r => r.Level).ThenBy(r => r.TeacherName).ToList();
                case "grade_asc":  return list.OrderBy(r => GradeRank(r.Grade)).ThenBy(r => r.TeacherName).ToList();
                case "grade_desc": return list.OrderByDescending(r => GradeRank(r.Grade)).ThenBy(r => r.TeacherName).ToList();
                default:           return list; // keep original Excel/data order
            }
        }

        // Restore sort order from saved settings
        private void RestoreSavedSortOrder()
        {
            try
            {
                var settings = PrintTrackerApp.Services.SettingsManager.LoadSettings();
                string saved = settings.DashboardSortOrder ?? "default";
                foreach (System.Windows.Controls.ComboBoxItem item in cmbSort.Items)
                {
                    if (item.Tag?.ToString() == saved)
                    {
                        cmbSort.SelectedItem = item;
                        return;
                    }
                }
                cmbSort.SelectedIndex = 0;
            }
            catch { cmbSort.SelectedIndex = 0; }
        }

        private void PopulateExcelGridDirect(Grid container, List<TeacherExcelRecord> records, string tabType)
        {
            int columns = 4;
            int itemsPerColumn = (int)Math.Ceiling(records.Count / (double)columns);

            if (container.Children.Count != columns || !(container.Children[0] is DataGrid))
            {
                container.Children.Clear();
                container.ColumnDefinitions.Clear();

                for (int i = 0; i < columns; i++)
                {
                    container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var grid = new DataGrid
                    {
                        AutoGenerateColumns = false,
                        CanUserAddRows = false,
                        CanUserSortColumns = false,
                        Margin = new Thickness(2),
                        HeadersVisibility = DataGridHeadersVisibility.Column,
                        RowHeight = 25
                    };
                    grid.MouseDoubleClick += DataGrid_MouseDoubleClick_AutoFit;

                    var textStyle = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
                    textStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.ToolTipProperty, new System.Windows.Data.Binding("JobsTooltip")));
                    
                    grid.Columns.Add(new DataGridTextColumn { Header = "Teacher", Binding = new System.Windows.Data.Binding("TeacherName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = textStyle });
                    
                    // Level is shown in all tabs (including PT)
                    grid.Columns.Add(new DataGridTextColumn { Header = "Level", Binding = new System.Windows.Data.Binding("Level"), Width = DataGridLength.Auto });
                    
                    if (tabType == "PT")
                    {
                        grid.Columns.Add(new DataGridTextColumn { Header = "Session", Binding = new System.Windows.Data.Binding("Session"), Width = DataGridLength.Auto });
                    }

                    var gradeCol = new DataGridTemplateColumn { Header = "Grade", Width = 80 };
                    gradeCol.CellTemplate = (DataTemplate)this.FindResource("GradeTemplate");
                    grid.Columns.Add(gradeCol);

                    Grid.SetColumn(grid, i);
                    container.Children.Add(grid);
                }
            }

            for (int i = 0; i < columns; i++)
            {
                if (container.Children[i] is DataGrid grid)
                {
                    var columnRecords = records.Skip(i * itemsPerColumn).Take(itemsPerColumn).ToList();
                    grid.ItemsSource = columnRecords;
                }
            }
        }

        private void ApplyExemptionsFromManagerInternal(Dictionary<string, TeacherPrintStat> statsDict, DateTime start, DateTime end, PrintTrackerApp.Services.TeacherScheduleManager manager)
        {
            if (statsDict == null) return;

            var excelTeachers = new System.Collections.Generic.List<TeacherScheduleWindow.TeacherIdentifier>();
            ExtractTeachersFromTable(_ftTable, "FT", excelTeachers);
            ExtractTeachersFromTable(_ptTable, "PT", excelTeachers);
            ExtractTeachersFromTable(_khTable, "KH", excelTeachers);

            foreach (var stat in statsDict.Values)
            {
                stat.ExemptedDates.Clear();
                
                string statTab = GetCategoryOfStat(stat);
                TeacherScheduleWindow.TeacherIdentifier matchingExcel = null;
                if (statTab == "PT")
                {
                    string targetLvlSes = string.IsNullOrEmpty(stat.Session) ? stat.Level : $"{stat.Level}-{stat.Session}";
                    matchingExcel = excelTeachers.FirstOrDefault(et => 
                        et.Category == "PT" &&
                        IsNameMatch(et.Name, stat.TeacherName, strict: true) &&
                        IsLevelMatch(et.Level, targetLvlSes));
                    if (matchingExcel == null)
                    {
                        matchingExcel = excelTeachers.FirstOrDefault(et => 
                            et.Category == "PT" &&
                            IsNameMatch(et.Name, stat.TeacherName, strict: false) &&
                            IsLevelMatch(et.Level, targetLvlSes));
                    }
                }
                else
                {
                    matchingExcel = excelTeachers.FirstOrDefault(et => 
                        et.Category != "PT" &&
                        IsNameMatch(et.Name, stat.TeacherName, strict: true));
                    if (matchingExcel == null)
                    {
                        matchingExcel = excelTeachers.FirstOrDefault(et => 
                            et.Category != "PT" &&
                            IsNameMatch(et.Name, stat.TeacherName, strict: false));
                    }
                }

                string lookupName = matchingExcel != null ? matchingExcel.Name : stat.TeacherName;
                string lookupLvl = matchingExcel != null ? matchingExcel.Level : stat.Level;
                string lookupSes = "";
                if (lookupLvl.Contains("-"))
                {
                    var parts = lookupLvl.Split('-');
                    if (parts.Length >= 2)
                    {
                        lookupLvl = parts[0].Trim();
                        lookupSes = parts[1].Trim();
                    }
                }
                else if (statTab == "PT" && string.IsNullOrEmpty(lookupSes))
                {
                    lookupSes = stat.Session;
                }

                var dict = GetScheduleDict(manager, lookupName, lookupLvl, lookupSes);
                if (dict != null)
                {
                    for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
                    {
                        string dateStr = date.ToString("yyyy-MM-dd");
                        if (dict.TryGetValue(dateStr, out string status))
                        {
                            if (status?.ToLower() == "no teach" || status?.ToLower() == "exam")
                            {
                                stat.ExemptedDates.Add(dateStr);
                            }
                        }
                    }
                }
            }
        }

        private bool GetIsLevelMatch(string printLevel, string excelLevel, Dictionary<string, bool> cache)
        {
            if (cache == null) return IsLevelMatch(printLevel, excelLevel);
            string key = $"{printLevel ?? ""}|{excelLevel ?? ""}";
            if (cache.TryGetValue(key, out bool res)) return res;
            res = IsLevelMatch(printLevel, excelLevel);
            cache[key] = res;
            return res;
        }

        private bool GetIsNameMatch(string excelName, string dictName, bool strict, Dictionary<string, bool> cache)
        {
            if (cache == null) return IsNameMatch(excelName, dictName, strict);
            string key = $"{excelName ?? ""}|{dictName ?? ""}|{strict}";
            if (cache.TryGetValue(key, out bool res)) return res;
            res = IsNameMatch(excelName, dictName, strict);
            cache[key] = res;
            return res;
        }

        private List<TeacherExcelRecord> GetRecordsForTabInternal(
            System.Data.DataTable table, 
            string tabType, 
            string searchText, 
            Dictionary<string, TeacherPrintStat> statsDict, 
            PrintTrackerApp.Services.TeacherScheduleManager manager, 
            DateTime start, 
            DateTime end,
            Dictionary<string, bool> levelCache,
            Dictionary<string, bool> nameCache)
        {
            InitializeCategorySets();
            var records = new List<TeacherExcelRecord>();
            if (table == null) return records;
            int startRow = 0;
            for (int r = 0; r < table.Rows.Count; r++)
            {
                var noStr = table.Rows[r][0]?.ToString();
                if (!string.IsNullOrWhiteSpace(noStr) && int.TryParse(noStr, out _))
                {
                    startRow = r;
                    break;
                }
            }

            // Pre-filter stats to only those belonging to the current tab type
            var tabStats = statsDict.Values.Where(stat => GetCategoryOfStat(stat) == tabType).ToList();

            // Pre-group schedules by teacher name (portion before the first '_')
            var schedulesByTeacher = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in manager.Schedules.Keys)
            {
                int underscoreIndex = key.IndexOf('_');
                if (underscoreIndex >= 0)
                {
                    string tName = key.Substring(0, underscoreIndex);
                    if (!schedulesByTeacher.ContainsKey(tName))
                    {
                        schedulesByTeacher[tName] = new List<string>();
                    }
                    schedulesByTeacher[tName].Add(key);
                }
            }

            var addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ── Disambiguation pre-pass ────────────────────────────────────────────
            // For each parsed teacher-token that appears in tabStats, find which
            // roster teacher has the HIGHEST ScoreNameMatch.  This ensures that
            // ambiguous tokens (e.g. "Sopha" matching both "Chhen Sopha" score=900
            // and "Prak Sophal" score=500) are awarded to only ONE teacher.
            var statTokenBestOwner = new Dictionary<string, (string Name, int Score)>(
                StringComparer.OrdinalIgnoreCase);
            for (int ri = startRow; ri < table.Rows.Count; ri++)
            {
                System.Data.DataRow rrow = table.Rows[ri];
                if (string.IsNullOrWhiteSpace(rrow[0]?.ToString())) continue;

                string rRaw = rrow[1]?.ToString() ?? "";
                if (IsInvalidTeacherName(rRaw)) continue;
                string rName = rRaw.Trim();
                if (rName.Contains("-"))
                    rName = rName.Split('-')[0].Trim();

                // Get teacher's allowed levels for this row
                var teacherLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string rowLvl = table.Columns.Count > 2 ? (rrow[2]?.ToString() ?? "") : "";
                string cleanRowLvl = rowLvl.Trim();
                if (cleanRowLvl.Contains("-"))
                {
                    cleanRowLvl = cleanRowLvl.Split('-')[0].Trim();
                }
                if (!string.IsNullOrEmpty(cleanRowLvl))
                {
                    teacherLevels.Add(cleanRowLvl);
                }
                
                if (tabType == "PT")
                {
                    var parts = rRaw.Split('-');
                    if (parts.Length >= 3)
                    {
                        string ptLvl = parts[1].Trim();
                        teacherLevels.Add(ptLvl);
                    }
                }
                
                if (schedulesByTeacher.TryGetValue(rName, out var schedKeys))
                {
                    foreach (var sKey in schedKeys)
                    {
                        string levelFromSchedule = sKey.Substring(rName.Length + 1);
                        if (levelFromSchedule.Contains("-"))
                        {
                            levelFromSchedule = levelFromSchedule.Split('-')[0].Trim();
                        }
                        teacherLevels.Add(levelFromSchedule);
                    }
                }

                foreach (var sStat in tabStats)
                {
                    int sc = ScoreNameMatch(rName, sStat.TeacherName);
                    if (sc <= 0) continue;

                    // Level match boost
                    bool levelMatches = LevelMatchesTeacher(sStat.Level, teacherLevels, levelCache);
                    if (levelMatches)
                    {
                        sc += 1000;
                    }

                    string key = $"{sStat.TeacherName.ToLower()}_{sStat.Level.ToLower()}_{sStat.Session.ToLower()}";
                    if (!statTokenBestOwner.TryGetValue(key, out var cur) || sc > cur.Score)
                        statTokenBestOwner[key] = (rName, sc);
                }
            }

            for (int i = startRow; i < table.Rows.Count; i++)
            {
                System.Data.DataRow row = table.Rows[i];
                var no = row[0]?.ToString();
                if (string.IsNullOrWhiteSpace(no)) continue;

                string rawTeacher = row[1]?.ToString() ?? "";
                if (IsInvalidTeacherName(rawTeacher)) continue;
                string teacherName = rawTeacher.Trim();
                if (teacherName.Contains("-"))
                {
                    var parts = teacherName.Split('-');
                    if (parts.Length >= 2)
                    {
                        teacherName = parts[0].Trim();
                    }
                }

                if (manager.HiddenTeachers.Contains(teacherName) || manager.HiddenTeachers.Contains(rawTeacher.Trim()))
                {
                    continue;
                }

                // If this teacher has printed in another category/tab during this period,
                // and has NOT printed in the current tab, exclude them from this tab to avoid false "E" grades.
                if (statsDict != null)
                {
                    bool printedInCurrentTab = tabStats.Any(stat => 
                        GetIsNameMatch(teacherName, stat.TeacherName, false, nameCache) && 
                        stat.PrintDays.Count > 0);
                        
                    if (!printedInCurrentTab)
                    {
                        bool printedInOtherTab = statsDict.Values.Any(stat => 
                            GetCategoryOfStat(stat) != tabType &&
                            GetIsNameMatch(teacherName, stat.TeacherName, false, nameCache) && 
                            stat.PrintDays.Count > 0);
                            
                        if (printedInOtherTab)
                        {
                            continue;
                        }
                    }
                }

                // Get teacher's allowed levels
                var teacherLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string rowLvl = table.Columns.Count > 2 ? (row[2]?.ToString() ?? "") : "";
                string cleanRowLvl = rowLvl.Trim();
                if (cleanRowLvl.Contains("-"))
                {
                    cleanRowLvl = cleanRowLvl.Split('-')[0].Trim();
                }
                if (!string.IsNullOrEmpty(cleanRowLvl))
                {
                    teacherLevels.Add(cleanRowLvl);
                }
                
                if (tabType == "PT")
                {
                    var parts = rawTeacher.Split('-');
                    if (parts.Length >= 3)
                    {
                        string ptLvl = parts[1].Trim();
                        teacherLevels.Add(ptLvl);
                    }
                }
                
                if (schedulesByTeacher.TryGetValue(teacherName, out var schedKeys))
                {
                    foreach (var sKey in schedKeys)
                    {
                        string levelFromSchedule = sKey.Substring(teacherName.Length + 1);
                        if (levelFromSchedule.Contains("-"))
                        {
                            levelFromSchedule = levelFromSchedule.Split('-')[0].Trim();
                        }
                        teacherLevels.Add(levelFromSchedule);
                    }
                }

                if (!string.IsNullOrEmpty(searchText))
                {
                    bool matchFound = teacherName.ToLower().Contains(searchText);
                    if (!matchFound)
                    {
                        var matchingStats = tabStats.Where(stat => 
                            GetIsNameMatch(teacherName, stat.TeacherName, false, nameCache) &&
                            (HasExactWordMatch(teacherName, stat.TeacherName) || LevelMatchesTeacher(stat.Level, teacherLevels, levelCache))
                        ).ToList();
                        
                        foreach (var stat in matchingStats)
                        {
                            if (stat.Level.ToLower().Contains(searchText) || stat.Session.ToLower().Contains(searchText))
                            {
                                matchFound = true;
                                break;
                            }
                        }
                    }
                    if (!matchFound) continue;
                }

                var matches = tabStats.Where(stat =>
                    GetIsNameMatch(teacherName, stat.TeacherName, false, nameCache) &&
                    (HasExactWordMatch(teacherName, stat.TeacherName) || LevelMatchesTeacher(stat.Level, teacherLevels, levelCache)) &&
                    // Disambiguation: only claim this stat if this teacher is the best-scoring owner
                    // for the parsed token + level + session combo
                    (!statTokenBestOwner.TryGetValue($"{stat.TeacherName.ToLower()}_{stat.Level.ToLower()}_{stat.Session.ToLower()}", out var bestOwn) ||
                      string.Equals(bestOwn.Name, teacherName, StringComparison.OrdinalIgnoreCase))
                ).ToList();

                if (matches.Count > 0)
                {
                    var scheduledClasses = GetTeacherScheduledClasses(teacherName, tabType, table, startRow, schedulesByTeacher, nameCache);
                    var mergedGroups = GroupAndMergeTeacherStats(teacherName, tabType, matches, scheduledClasses);

                    foreach (var groupData in mergedGroups)
                    {
                        foreach (var stat in groupData.Stats)
                        {
                            stat.IsMatched = true;
                        }

                        string lvl = groupData.Level;
                        string ses = groupData.Session;
                        string key = $"{teacherName.ToLower()}_{lvl.ToLower()}_{ses.ToLower()}";

                        // Visibility filter check
                        string scheduleLevelKey = (tabType == "PT")
                            ? (string.IsNullOrEmpty(ses) ? lvl : $"{lvl}-{ses}")
                            : lvl;
                        string hiddenCheckKey = $"{teacherName}_{scheduleLevelKey}";
                        string dateKey = $"{hiddenCheckKey}_{start.ToString("yyyy-MM-dd")}_{end.ToString("yyyy-MM-dd")}";
                        if (manager.HiddenTeachers.Contains(hiddenCheckKey) || manager.HiddenTeachers.Contains(dateKey))
                        {
                            continue;
                        }

                        if (addedKeys.Add(key))
                        {
                            var combinedPrintDays    = groupData.PrintDays;
                            var combinedExemptedDates = groupData.ExemptedDates;
                            var combinedTooltips     = groupData.TooltipLines;

                            int duration = (end.Date - start.Date).Days + 1;

                            // ── Schedule-aware teach-day count ────────────────────────────────────
                            int scheduledTeachDays = 0;
                            for (DateTime d = start.Date; d <= end.Date; d = d.AddDays(1))
                            {
                                string dayStatus = GetScheduleStatus(manager, teacherName, lvl, ses, d.ToString("yyyy-MM-dd"));
                                if (dayStatus != null)
                                {
                                    string ds = dayStatus.ToLower();
                                    if (ds == "no teach" || ds == "exam") continue; // excluded day
                                }
                                scheduledTeachDays++;
                            }
                            if (scheduledTeachDays <= 0) scheduledTeachDays = duration;

                            int weeks = (int)Math.Ceiling(duration / 7.0);
                            if (weeks == 0) weeks = 1;

                            // Effective print days = unique days teacher actually printed
                            int printedCount = combinedPrintDays.Count;

                            string combinedGrade = "E";

                            if (combinedExemptedDates.Count >= duration)
                            {
                                bool hasExam    = false;
                                bool hasNoTeach = false;

                                for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
                                {
                                    string dateStr = date.ToString("yyyy-MM-dd");
                                    string status = GetScheduleStatus(manager, teacherName, lvl, ses, dateStr);
                                    if (status != null)
                                    {
                                        string s = status.ToLower();
                                        if (s == "exam")     hasExam    = true;
                                        if (s == "no teach") hasNoTeach = true;
                                    }
                                }
                                if (hasExam && hasNoTeach) combinedGrade = "Exam/No Teach";
                                else if (hasExam)          combinedGrade = "Exam";
                                else if (hasNoTeach)       combinedGrade = "No Teach";
                                else                       combinedGrade = "Exam/No Teach";
                            }
                            else
                            {
                                int activeDays = combinedPrintDays.Union(combinedExemptedDates).Count();
                                if      (activeDays >= 4 * weeks) combinedGrade = "A";
                                else if (activeDays >= 3 * weeks) combinedGrade = "B";
                                else if (activeDays >= 2 * weeks) combinedGrade = "C";
                                else if (activeDays >= 1 * weeks) combinedGrade = "D";
                                else                              combinedGrade = "E";
                            }

                            // ── BS (Both Sessions) expansion ───────────────────────────────
                            if (string.Equals(ses, "BS", StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (var expandedSes in new[] { "S1", "S2" })
                                {
                                    string bsKey = $"{teacherName.ToLower()}_{lvl.ToLower()}_{expandedSes.ToLower()}";
                                    if (addedKeys.Add(bsKey))
                                    {
                                        records.Add(new TeacherExcelRecord
                                        {
                                            No = no,
                                            TeacherName = teacherName,
                                            Level = lvl,
                                            Session = expandedSes,
                                            Grade = combinedGrade,
                                            JobsTooltip = string.Join("\n", combinedTooltips) + (combinedTooltips.Count > 0 ? "\n" : "")
                                        });
                                    }
                                }
                            }
                            else
                            {
                                records.Add(new TeacherExcelRecord
                                {
                                    No = no,
                                    TeacherName = teacherName,
                                    Level = lvl,
                                    Session = ses,
                                    Grade = combinedGrade,
                                    JobsTooltip = string.Join("\n", combinedTooltips) + (combinedTooltips.Count > 0 ? "\n" : "")
                                });
                            }
                        }
                    }
                }
                else
                {
                    bool addedFromSchedule = false;
                    if (schedulesByTeacher.TryGetValue(teacherName, out var matchingKeys) && matchingKeys.Count > 0)
                    {
                        var officialScheduledClasses = GetTeacherScheduledClasses(teacherName, tabType, table, startRow, schedulesByTeacher, nameCache);
                        foreach (var key in matchingKeys)
                        {
                            string levelFromSchedule = key.Substring(teacherName.Length + 1);

                            // Visibility filter check
                            string dateKey = $"{key}_{start.ToString("yyyy-MM-dd")}_{end.ToString("yyyy-MM-dd")}";
                            if (manager.HiddenTeachers.Contains(key) || manager.HiddenTeachers.Contains(dateKey))
                            {
                                continue;
                            }
                            
                            CleanLevelAndSession(levelFromSchedule, out string lvl, out string ses);

                            // Filter out schedule settings for levels not officially assigned to the teacher in the roster table (if roster defines any levels)
                            bool hasOfficialLevels = officialScheduledClasses.Any(sc => !string.IsNullOrWhiteSpace(sc.Level));
                            if (hasOfficialLevels && !officialScheduledClasses.Any(sc => sc.Level.Equals(lvl, StringComparison.OrdinalIgnoreCase) && sc.Session.Equals(ses, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            // Filter schedule levels by tabType!
                            if (string.IsNullOrWhiteSpace(lvl) || GetCategoryOfLevel(lvl, ses) != tabType)
                            {
                                continue;
                            }

                            string grade = CalculateGradeFromExemptionsOnlyInternal(teacherName, levelFromSchedule, manager, start, end);
                            string mapKey = $"{teacherName.ToLower()}_{lvl.ToLower()}_{ses.ToLower()}";

                            if (string.Equals(ses, "BS", StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (var expandedSes in new[] { "S1", "S2" })
                                {
                                    string bsKey = $"{teacherName.ToLower()}_{lvl.ToLower()}_{expandedSes.ToLower()}";
                                    if (addedKeys.Add(bsKey))
                                    {
                                        records.Add(new TeacherExcelRecord
                                        {
                                            No = no,
                                            TeacherName = teacherName,
                                            Level = lvl,
                                            Session = expandedSes,
                                            Grade = grade,
                                            JobsTooltip = ""
                                        });
                                        addedFromSchedule = true;
                                    }
                                }
                            }
                            else
                            {
                                if (addedKeys.Add(mapKey))
                                {
                                    records.Add(new TeacherExcelRecord
                                    {
                                        No = no,
                                        TeacherName = teacherName,
                                        Level = lvl,
                                        Session = ses,
                                        Grade = grade,
                                        JobsTooltip = ""
                                    });
                                    addedFromSchedule = true;
                                }
                            }
                        }
                    }

                    if (!addedFromSchedule)
                    {
                        string rawLevel = table.Columns.Count > 2 ? (row[2]?.ToString() ?? "") : "";
                        string lvl = rawLevel.Trim();
                        string ses = string.Empty;
                        if (tabType == "PT" && string.IsNullOrWhiteSpace(lvl))
                        {
                            var parts = rawTeacher.Split('-');
                            if (parts.Length >= 3)
                            {
                                lvl = parts[1].Trim();
                                ses = parts[2].Trim();
                            }
                        }
                        else if (lvl.Contains("-"))
                        {
                            var parts = lvl.Split('-');
                            if (parts.Length >= 2)
                            {
                                lvl = parts[0].Trim();
                                ses = parts[1].Trim();
                            }
                        }
                        else if (table.Columns.Count > 3)
                        {
                            ses = row[3]?.ToString()?.Trim() ?? "";
                        }

                        // If raw level doesn't match tabType, clear it so we don't show wrong level
                        if (!string.IsNullOrWhiteSpace(lvl) && GetCategoryOfLevel(lvl, ses) != tabType)
                        {
                            lvl = "";
                            ses = "";
                        }

                        if (string.IsNullOrWhiteSpace(lvl))
                        {
                            continue;
                        }

                        // Visibility filter check
                        string scheduleLevelKey = (tabType == "PT")
                            ? (string.IsNullOrEmpty(ses) ? lvl : $"{lvl}-{ses}")
                            : lvl;
                        string hiddenCheckKey = $"{teacherName}_{scheduleLevelKey}";
                        string dateKey = $"{hiddenCheckKey}_{start.ToString("yyyy-MM-dd")}_{end.ToString("yyyy-MM-dd")}";
                        if (manager.HiddenTeachers.Contains(hiddenCheckKey) || manager.HiddenTeachers.Contains(dateKey))
                        {
                            continue;
                        }

                        string grade = CalculateGradeFromExemptionsOnlyInternal(teacherName, scheduleLevelKey, manager, start, end);
                        string mapKey = $"{teacherName.ToLower()}_{lvl.ToLower()}_{ses.ToLower()}";

                        if (string.Equals(ses, "BS", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var expandedSes in new[] { "S1", "S2" })
                            {
                                string bsKey = $"{teacherName.ToLower()}_{lvl.ToLower()}_{expandedSes.ToLower()}";
                                if (addedKeys.Add(bsKey))
                                {
                                    records.Add(new TeacherExcelRecord
                                    {
                                        No = no,
                                        TeacherName = teacherName,
                                        Level = lvl,
                                        Session = expandedSes,
                                        Grade = grade,
                                        JobsTooltip = ""
                                    });
                                }
                            }
                        }
                        else
                        {
                            if (addedKeys.Add(mapKey))
                            {
                                records.Add(new TeacherExcelRecord
                                {
                                    No = no,
                                    TeacherName = teacherName,
                                    Level = lvl,
                                    Session = ses,
                                    Grade = grade,
                                    JobsTooltip = ""
                                });
                            }
                        }
                    }
                }
            }

            if (tabType == "PT")
            {
                var groups = records.GroupBy(r => new { TeacherName = (r.TeacherName ?? "").ToLower(), Level = (r.Level ?? "").ToLower() }).ToList();
                var filteredRecords = new List<TeacherExcelRecord>();
                foreach (var group in groups)
                {
                    var list = group.ToList();
                    if (list.Count > 1)
                    {
                        var hasSession = list.Any(r => !string.IsNullOrWhiteSpace(r.Session));
                        if (hasSession)
                        {
                            filteredRecords.AddRange(list.Where(r => !string.IsNullOrWhiteSpace(r.Session)));
                            continue;
                        }
                    }
                    filteredRecords.AddRange(list);
                }
                records = filteredRecords;
            }

            return FilterDuplicateTypoRecords(records);
        }

        private TeacherPrintStat FindBestMatchInternal(
            string excelName, 
            string level, 
            string session, 
            Dictionary<string, TeacherPrintStat> statsDict,
            Dictionary<string, bool> levelCache,
            Dictionary<string, bool> nameCache)
        {
            var candidates = statsDict.Values.Where(v => 
                GetIsLevelMatch(v.Level, level, levelCache) && 
                GetIsNameMatch(excelName, v.TeacherName, false, nameCache)
            ).ToList();

            if (candidates.Count == 0) return null;

            var scoredCandidates = candidates.Select(c =>
            {
                int score = 0;
                
                if (IsNameMatch(excelName, c.TeacherName, strict: true)) score += 100;
                else score += 50;

                if (!string.IsNullOrEmpty(session))
                {
                    if (string.Equals(c.Session, session, StringComparison.OrdinalIgnoreCase)) score += 50;
                    else if (string.IsNullOrWhiteSpace(c.Session)) score += 10;
                }
                
                if (string.Equals(c.Level, level, StringComparison.OrdinalIgnoreCase)) score += 100;
                else if (IsLevelMatch(c.Level, level)) score += 80;
                
                return new { Stat = c, Score = score };
            }).OrderByDescending(x => x.Score).ToList();

            if (scoredCandidates.Count > 0 && scoredCandidates.First().Score >= 50)
                return scoredCandidates.First().Stat;

            return null;
        }

        private string GetScheduleStatus(
            PrintTrackerApp.Services.TeacherScheduleManager manager,
            string teacherName,
            string level,
            string session,
            string dateStr)
        {
            var dict = GetScheduleDict(manager, teacherName, level, session);
            if (dict != null && dict.TryGetValue(dateStr, out string status))
            {
                return status;
            }

            // Fallback 1: Try "TeacherName_Level-BS"
            if (!string.IsNullOrEmpty(session) && !string.Equals(session, "BS", StringComparison.OrdinalIgnoreCase))
            {
                var dictBS = GetScheduleDict(manager, teacherName, level, "BS");
                if (dictBS != null && dictBS.TryGetValue(dateStr, out string statusBS))
                {
                    return statusBS;
                }
            }

            // Fallback 2: Try "TeacherName_Level"
            if (!string.IsNullOrEmpty(session))
            {
                var dictLvl = GetScheduleDict(manager, teacherName, level, "");
                if (dictLvl != null && dictLvl.TryGetValue(dateStr, out string statusLvl))
                {
                    return statusLvl;
                }
            }

            return null;
        }

        private string CalculateGradeFromExemptionsOnlyInternal(string teacherName, string level, PrintTrackerApp.Services.TeacherScheduleManager manager, DateTime start, DateTime end)
        {
            string lvl = level;
            string ses = "";
            if (level.Contains("-"))
            {
                var parts = level.Split('-');
                lvl = parts[0].Trim();
                ses = parts[1].Trim();
            }

            int exemptedCount = 0;
            bool hasExam = false;
            bool hasNoTeach = false;

            for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
            {
                string dateStr = date.ToString("yyyy-MM-dd");
                string status = GetScheduleStatus(manager, teacherName, lvl, ses, dateStr);
                if (status != null)
                {
                    string s = status.ToLower();
                    if (s == "no teach" || s == "exam")
                    {
                        exemptedCount++;
                        if (s == "exam") hasExam = true;
                        if (s == "no teach") hasNoTeach = true;
                    }
                }
            }

            int totalDurationDays = (end.Date - start.Date).Days + 1;
            int teachDaysCount = totalDurationDays - exemptedCount;
            if (teachDaysCount > 0)
            {
                // If they had ANY teaching days but printed 0 times, they fail.
                return "E";
            }

            if (hasExam && hasNoTeach) return "Exam/No Teach";
            if (hasExam) return "Exam";
            if (hasNoTeach) return "No Teach";
            
            return "Exam/No Teach";
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

            // Fix Pre- dash issue (e.g., Pre-5 -> Pre5, but NOT Pre-Smey)
            docName = System.Text.RegularExpressions.Regex.Replace(docName, @"(?i)\bPre-([0-9][a-zA-Z0-9]*)\b", "Pre$1");

            // Split by dash or underscore
            var parts = docName.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

            // Find the index of the part containing "copies" (or variations/typos)
            int copiesIndex = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].ToLower();
                int openParen = p.IndexOf('(');
                if (openParen >= 0)
                {
                    p = p.Substring(0, openParen).Trim();
                }

                if (System.Text.RegularExpressions.Regex.IsMatch(p, @"^\d+[-_]?c[o0]?[o0]?p[a-z]*$") || // "15copies", "15copiees", "15coopies", "15cop"
                    System.Text.RegularExpressions.Regex.IsMatch(p, @"^\d+[-_]?cps$") ||           // "15cps"
                    System.Text.RegularExpressions.Regex.IsMatch(p, @"^c[o0]?[o0]?p[ieys]+$") ||   // "copies", "copiees", "coopies", "copy"
                    p == "cps" || p == "cop") 
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

        private List<string> SplitLevels(string level)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(level)) return result;

            // Pattern for distinct levels:
            // G\d+[A-Z] (e.g. G2A, G2B, G12C)
            // KG[HML][A-Z]? (e.g. KGHA, KGMA, KGL)
            // SMC\d+ (e.g. SMC3, SMC7)
            // L\d+ (e.g. L4, L5)
            // Pre\d+[A-Z]* (e.g. Pre2Ai, Pre2Aii, Pre5, Pre8)
            // F[A-Z] (e.g. FA, FB)
            // BS
            var pattern = @"(?i)G\d+[A-Z]|KG[HML][A-Z]?|SMC\d+|L\d+|Pre\d+[A-Z]*|F[A-Z]|BS";
            var matches = System.Text.RegularExpressions.Regex.Matches(level, pattern);
            if (matches.Count > 0)
            {
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    result.Add(match.Value);
                }
            }
            else
            {
                // Fallback to splitting by '&' or space
                var parts = level.Split(new[] { '&', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    result.Add(p.Trim());
                }
            }

            return result;
        }

        private void DataGrid_MouseDoubleClick_AutoFit(object sender, MouseButtonEventArgs e)
        {
            DependencyObject dep = e.OriginalSource as DependencyObject;
            
            // First look for ColumnHeader
            var header = FindVisualParent<System.Windows.Controls.Primitives.DataGridColumnHeader>(dep);
            if (header != null)
            {
                if (header.Column != null)
                {
                    header.Column.Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Auto);
                    e.Handled = true;
                }
                return;
            }

            // Then look for Cell
            var cell = FindVisualParent<System.Windows.Controls.DataGridCell>(dep);
            if (cell != null)
            {
                string headerText = cell.Column?.Header?.ToString() ?? "";
                if (headerText.IndexOf("Teacher", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var row = FindVisualParent<System.Windows.Controls.DataGridRow>(cell);
                    if (row != null && row.Item != null)
                    {
                        string teacherName = "";
                        string level = "";
                        string session = "";

                        if (row.Item is PrintTrackerApp.Models.TeacherPrintStat stat)
                        {
                            teacherName = stat.TeacherName;
                            level = stat.Level;
                            session = stat.Session;
                        }
                        else if (row.Item is PrintTrackerApp.Models.TeacherExcelRecord excelRec)
                        {
                            teacherName = excelRec.TeacherName;
                            level = excelRec.Level;
                            session = excelRec.Session;
                        }

                        if (!string.IsNullOrEmpty(teacherName))
                        {
                            OpenScheduleSettings(teacherName, level, session);
                            e.Handled = true;
                            return;
                        }
                    }
                }

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
                    string sourceText = openFileDialog.FileNames.Length > 1 
                        ? $"External CSVs ({openFileDialog.FileNames.Length} files)" 
                        : $"External CSV ({System.IO.Path.GetFileName(openFileDialog.FileNames[0])})";
                    LoadHistoricalData(allExternalJobs, _csvExportPath, sourceText);
                    
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
            ResetToDefaultData();
            OnResetDataClicked?.Invoke(this, EventArgs.Empty);
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
            ReloadFilteredData(_currentStart, _currentEnd);
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ReloadFilteredData(_currentStart, _currentEnd);
        }

        private void PopulateExcelGrid(Grid container, DataTable table, string tabType)
        {
            var records = GetRecordsForTab(table, tabType, false);

            int columns = 4;
            int itemsPerColumn = (int)Math.Ceiling(records.Count / (double)columns);

            if (container.Children.Count != columns || !(container.Children[0] is DataGrid))
            {
                container.Children.Clear();
                container.ColumnDefinitions.Clear();

                for (int i = 0; i < columns; i++)
                {
                    container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var grid = new DataGrid
                    {
                        AutoGenerateColumns = false,
                        CanUserAddRows = false,
                        CanUserSortColumns = false,
                        Margin = new Thickness(2),
                        HeadersVisibility = DataGridHeadersVisibility.Column,
                        RowHeight = 25
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

            for (int i = 0; i < columns; i++)
            {
                if (container.Children[i] is DataGrid grid)
                {
                    var columnRecords = records.Skip(i * itemsPerColumn).Take(itemsPerColumn).ToList();
                    grid.ItemsSource = columnRecords;
                }
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
            OpenScheduleSettings();
        }

        private static bool IsInvalidTeacherName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string lower = name.ToLower().Trim();
            return lower == "teacher name" || lower == "teacher" || lower == "teacher and level" || lower == "no" || lower == "no." || lower == "level";
        }

        private void OpenScheduleSettings(string locateTeacherName = null, string locateLevel = null, string locateSession = null)
        {
            // ── Schedule teachers ─────────────────────────────────────────────────
            // Mirrors all teacher+level rows. Bypasses visibility filters so hidden
            // teachers do not disappear from the settings window, allowing them to be unhidden.
            var scheduleTeachers = new System.Collections.Generic.List<TeacherScheduleWindow.TeacherIdentifier>();

            var excelTeachers = new System.Collections.Generic.List<TeacherScheduleWindow.TeacherIdentifier>();
            ExtractTeachersFromTable(_ftTable, "FT", excelTeachers);
            ExtractTeachersFromTable(_ptTable, "PT", excelTeachers);
            ExtractTeachersFromTable(_khTable, "KH", excelTeachers);

            var manager = PrintTrackerApp.Services.TeacherScheduleManager.Load();

            foreach (var t in excelTeachers)
            {
                var levels = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 1. Get levels from print statistics (non-filtered raw stats)
                if (_statsDict != null)
                {
                    foreach (var stat in _statsDict.Values)
                    {
                        if (GetCategoryOfStat(stat) == t.Category && IsNameMatch(t.Name, stat.TeacherName, false))
                        {
                            string lvlKey = string.IsNullOrEmpty(stat.Session) ? stat.Level : $"{stat.Level}-{stat.Session}";
                            if (!string.IsNullOrEmpty(lvlKey))
                            {
                                levels.Add(lvlKey);
                            }
                        }
                    }
                }

                // 2. Get levels from existing schedule configurations
                if (manager?.Schedules != null)
                {
                    string prefix = $"{t.Name}_";
                    foreach (var sKey in manager.Schedules.Keys)
                    {
                        if (sKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            string rawLvl = sKey.Substring(prefix.Length);
                            if (!string.IsNullOrEmpty(rawLvl))
                            {
                                CleanLevelAndSession(rawLvl, out string cleanLvl, out string cleanSes);
                                string levelKey = string.IsNullOrEmpty(cleanSes) ? cleanLvl : $"{cleanLvl}-{cleanSes}";
                                if (!string.IsNullOrEmpty(levelKey))
                                {
                                    levels.Add(levelKey);
                                }
                            }
                        }
                    }
                }

                // 3. Add Level from Excel table if it has one
                if (!string.IsNullOrEmpty(t.Level))
                {
                    levels.Add(t.Level);
                }

                if (levels.Count > 0)
                {
                    foreach (var lvl in levels)
                    {
                        if (!scheduleTeachers.Any(x => x.Name == t.Name && x.Level == lvl && x.Category == t.Category))
                        {
                            scheduleTeachers.Add(new TeacherScheduleWindow.TeacherIdentifier
                            {
                                Name     = t.Name,
                                Level    = lvl,
                                Category = t.Category,
                                RawName  = t.RawName
                            });
                        }
                    }
                }
                else
                {
                    // Fallback for teachers with absolutely no stats or schedules
                    scheduleTeachers.Add(new TeacherScheduleWindow.TeacherIdentifier
                    {
                        Name     = t.Name,
                        Level    = "",
                        Category = t.Category,
                        RawName  = t.RawName
                    });
                }
            }

            // ── Visibility teachers ───────────────────────────────────────────────
            // All unique teacher names from the Excel roster (not filtered by print data).
            // One row per teacher; unchecking hides ALL levels of that teacher in the dashboard.
            var visibilityTeachers = new System.Collections.Generic.List<TeacherScheduleWindow.TeacherIdentifier>();
            var allExcelRaw = new System.Collections.Generic.List<TeacherScheduleWindow.TeacherIdentifier>();
            ExtractTeachersFromTable(_ftTable, "FT", allExcelRaw);
            ExtractTeachersFromTable(_ptTable, "PT", allExcelRaw);
            ExtractTeachersFromTable(_khTable, "KH", allExcelRaw);

            foreach (var t in allExcelRaw)
            {
                if (!visibilityTeachers.Any(x => x.Name == t.Name && x.Category == t.Category))
                {
                    visibilityTeachers.Add(new TeacherScheduleWindow.TeacherIdentifier
                    {
                        Name     = t.Name,
                        Level    = "",   // Visibility tab shows name only, no level
                        Category = t.Category,
                        RawName  = t.RawName
                    });
                }
            }

            var window = new TeacherScheduleWindow(scheduleTeachers, visibilityTeachers);
            window.Owner = Window.GetWindow(this);

            if (!string.IsNullOrEmpty(locateTeacherName))
            {
                string category = GetCategoryOfLevel(locateLevel, locateSession);
                window.LocateTeacher(locateTeacherName, locateLevel, category);
            }

            window.ShowDialog();
            
            // Reload data after settings close because schedules might have changed
            DateTime start = DateTime.Now.Date;
            DateTime end = DateTime.Now.Date;
            if (cmbDateFilter.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                string content = selectedItem.Content?.ToString() ?? "";
                if (content == "Custom Range")
                {
                    if (dpStartDate.SelectedDate.HasValue) start = dpStartDate.SelectedDate.Value.Date;
                    if (dpEndDate.SelectedDate.HasValue) end = dpEndDate.SelectedDate.Value.Date;
                }
                else if (content != "Today")
                {
                    if (selectedItem.Tag is PrintTrackerApp.Services.CustomDateFilter customFilter)
                    {
                        start = customFilter.StartDate.Date;
                        end = customFilter.EndDate.Date;
                    }
                }
            }
            ReloadFilteredData(start, end);
        }

        private void ExtractTeachersFromTable(DataTable table, string tabType, System.Collections.Generic.List<TeacherScheduleWindow.TeacherIdentifier> teachers)
        {
            if (table == null) return;
            for (int i = 0; i < table.Rows.Count; i++)
            {
                DataRow row = table.Rows[i];
                var no = row[0]?.ToString();
                if (string.IsNullOrWhiteSpace(no) || !int.TryParse(no, out _)) continue;

                string rawTeacher = row[1]?.ToString() ?? "";
                if (IsInvalidTeacherName(rawTeacher)) continue;

                string rawLevel = table.Columns.Count > 2 ? (row[2]?.ToString() ?? "") : "";

                string teacherName = rawTeacher.Trim();
                string level = rawLevel.Trim();
                
                if (tabType == "PT")
                {
                    var parts = rawTeacher.Split('-');
                    if (parts.Length >= 3)
                    {
                        teacherName = parts[0].Trim();
                        level = $"{parts[1].Trim()}-{parts[2].Trim()}";
                    }
                }

                var tId = new TeacherScheduleWindow.TeacherIdentifier 
                { 
                    Name = teacherName, 
                    Level = level, 
                    Category = tabType,
                    RawName = rawTeacher.Trim() 
                };
                if (!teachers.Any(t => t.Name == tId.Name && t.Level == tId.Level && t.Category == tId.Category))
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
                
                var matchingExcel = excelTeachers.FirstOrDefault(et => IsLevelMatch(et.Level, stat.Level) && IsNameMatch(et.Name, stat.TeacherName, strict: false));
                
                string lookupName = matchingExcel != null ? matchingExcel.Name : stat.TeacherName;
                string lookupLvl = matchingExcel != null ? matchingExcel.Level : stat.Level;
                string lookupSes = "";
                string statTab = GetCategoryOfStat(stat);
                
                if (lookupLvl.Contains("-"))
                {
                    var parts = lookupLvl.Split('-');
                    if (parts.Length >= 2)
                    {
                        lookupLvl = parts[0].Trim();
                        lookupSes = parts[1].Trim();
                    }
                }
                else if (statTab == "PT" && string.IsNullOrEmpty(lookupSes))
                {
                    lookupSes = stat.Session;
                }

                var dict = GetScheduleDict(manager, lookupName, lookupLvl, lookupSes);
                if (dict != null)
                {
                    for (DateTime date = _currentStart.Date; date <= _currentEnd.Date; date = date.AddDays(1))
                    {
                        string dateStr = date.ToString("yyyy-MM-dd");
                        if (dict.TryGetValue(dateStr, out string status))
                        {
                            if (status?.ToLower() == "no teach" || status?.ToLower() == "exam")
                            {
                                stat.ExemptedDates.Add(dateStr);
                            }
                        }
                    }
                }
            }
        }

        // CalculateGradeFromExemptionsOnly is deprecated and replaced by CalculateGradeFromExemptionsOnlyInternal
        private string CalculateGradeFromExemptionsOnly(string teacherName, string level)
        {
            return "E";
        }

        private void BtnUnmatchedFiles_Click(object sender, RoutedEventArgs e)
        {
            var window = new UnmatchedFilesWindow(_unmatchedJobs);
            window.Owner = Window.GetWindow(this);
            window.ShowDialog();
        }

        // FindBestMatch is deprecated and replaced by FindBestMatchInternal with caching.

        public static bool IsLevelMatch(string printLevel, string excelLevel)
        {
            if (string.IsNullOrWhiteSpace(printLevel) && string.IsNullOrWhiteSpace(excelLevel)) return true;
            if (string.IsNullOrWhiteSpace(printLevel) || string.IsNullOrWhiteSpace(excelLevel)) return false;

            string nPrint = NormalizeLevel(printLevel);
            string nExcel = NormalizeLevel(excelLevel);

            if (nPrint == nExcel) return true;

            // Check if one is a more specific sub-level of the other (e.g., "pre2a" matching "pre2a1" or "1a" matching "1a1")
            if ((nPrint.StartsWith(nExcel) && nPrint.Length == nExcel.Length + 1) ||
                (nExcel.StartsWith(nPrint) && nExcel.Length == nPrint.Length + 1))
            {
                return true;
            }

            // Safe substring match: only allow if one contains the other WITHOUT conflicting numeric/grade prefixes
            // Specifically prevent "pre1a" from matching "1a", or "11a" from matching "1a", or "2a" matching "pre2a"
            if (nPrint.Contains(nExcel))
            {
                int idx = nPrint.IndexOf(nExcel);
                if (idx == 0 || (!char.IsDigit(nPrint[idx - 1]) && !nPrint.Substring(0, idx).Contains("pre")))
                {
                    if (idx + nExcel.Length == nPrint.Length || !char.IsDigit(nPrint[idx + nExcel.Length]))
                        return true;
                }
            }
            if (nExcel.Contains(nPrint))
            {
                int idx = nExcel.IndexOf(nPrint);
                if (idx == 0 || (!char.IsDigit(nExcel[idx - 1]) && !nExcel.Substring(0, idx).Contains("pre")))
                {
                    if (idx + nPrint.Length == nExcel.Length || !char.IsDigit(nExcel[idx + nPrint.Length]))
                        return true;
                }
            }

            return false;
        }

        public static string NormalizeLevel(string level)
        {
            if (string.IsNullOrWhiteSpace(level)) return "";

            // Memoize: same level string always produces the same result
            if (_normalizedLevelCache.TryGetValue(level, out string cached)) return cached;

            string s = level.ToLower().Trim();

            // Replace common separators with space (pre-compiled)
            s = _rxNormSep.Replace(s, " ");

            // Remove common prefixes/suffixes using pre-compiled regex array
            foreach (var (Word, Prefix) in _rxNormWords)
            {
                s = Word.Replace(s, "");
                s = Prefix.Replace(s, "");
            }

            // Remove all spaces and special characters (pre-compiled)
            s = _rxNormSpecial.Replace(s, "");

            // Normalize Pre-2A letter/number endings
            // e.g. pre2all -> pre2a2, pre2aii -> pre2a2, pre2ai -> pre2a1, pre2al -> pre2a1
            if (s.StartsWith("pre"))
            {
                if (s.EndsWith("iii") || s.EndsWith("lll")) s = s.Substring(0, s.Length - 3) + "3";
                else if (s.EndsWith("ii") || s.EndsWith("ll")) s = s.Substring(0, s.Length - 2) + "2";
                else if (s.EndsWith("i") || s.EndsWith("l"))
                {
                    if (s.Length > 1 && !char.IsDigit(s[s.Length - 1]) && char.IsLetterOrDigit(s[s.Length - 2]))
                    {
                        s = s.Substring(0, s.Length - 1) + "1";
                    }
                }
            }

            _normalizedLevelCache[level] = s;
            return s;
        }

        private void InitializeCategorySets()
        {
            if (_categorySetsLoaded) return; // Already loaded this refresh cycle
            var appSettings = Services.SettingsManager.LoadSettings();
            _ftSet = new HashSet<string>((appSettings.FtLevels ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => NormalizeLevel(s)), StringComparer.OrdinalIgnoreCase);
            _ptSet = new HashSet<string>((appSettings.PtLevels ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => NormalizeLevel(s)), StringComparer.OrdinalIgnoreCase);
            _khSet = new HashSet<string>((appSettings.KhLevels ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => NormalizeLevel(s)), StringComparer.OrdinalIgnoreCase);
            _categorySetsLoaded = true;
        }

        private bool IsLevelInCategory(string level, HashSet<string> categorySet)
        {
            if (string.IsNullOrWhiteSpace(level)) return false;
            string nLvl = NormalizeLevel(level);
            if (categorySet.Contains(nLvl)) return true;
            foreach (var lvl in categorySet)
            {
                if (IsLevelMatch(level, lvl)) return true;
            }
            return false;
        }

        private string GetCategoryOfLevel(string level, string session)
        {
            if (IsLevelInCategory(level, _khSet)) return "KH";
            if (IsLevelInCategory(level, _ptSet)) return "PT";
            if (IsLevelInCategory(level, _ftSet)) return "FT";

            // Fallback based on session
            string ses = session?.Trim().ToLower() ?? "";
            if (!string.IsNullOrEmpty(ses) && (ses.StartsWith("s") || ses.Contains("s1") || ses.Contains("s2")))
            {
                return "PT";
            }

            // Fallback based on Level prefixes
            string lvl = level?.Trim().ToLower() ?? "";
            if (lvl.StartsWith("fa") || lvl.StartsWith("fb") || lvl.StartsWith("pre5") || lvl.StartsWith("pre8") || lvl.StartsWith("prea") || (lvl.StartsWith("l") && lvl.Length >= 2 && char.IsDigit(lvl[1])))
            {
                return "PT";
            }

            return "FT";
        }

        private string GetCategoryOfStat(TeacherPrintStat stat)
        {
            InitializeCategorySets();
            return GetCategoryOfLevel(stat.Level, stat.Session);
        }

        public static bool IsNameMatch(string excelName, string dictName, bool strict)
        {
            return ScoreNameMatch(excelName, dictName) > 0;
        }

        /// <summary>
        /// Returns a numeric confidence score for how well a filename teacher token (dictName)
        /// matches a roster teacher name (excelName).  Higher = better / more specific.
        ///   1000 – exact full-name match
        ///    900 – exact word-level match   (e.g. "Chanra" == "Chanra")
        ///    700 – suffix of last name      (e.g. "Smey"   ends  "Chanreaksmey")
        ///    500 – prefix of last name      (e.g. "Pich"   starts "Pichponleu", min 4 chars)
        ///      0 – no match
        /// The ScoreNameMatch result is also the gate for IsNameMatch (score > 0 = match).
        /// </summary>
        public static int ScoreNameMatch(string excelName, string dictName)
        {
            if (string.IsNullOrWhiteSpace(excelName) || string.IsNullOrWhiteSpace(dictName)) return 0;

            excelName = excelName.ToLower().Trim();
            dictName  = dictName.ToLower().Trim();

            if (excelName == dictName) return 1000;

            var excelWords = excelName.Split(new[] { ' ', '-', '_', '.' },
                                             StringSplitOptions.RemoveEmptyEntries);
            var dictWords  = dictName.Split(new[] { ' ', '-', '_', '.' },
                                            StringSplitOptions.RemoveEmptyEntries);

            if (excelWords.Length == 0) return 0;

            // Only match against the last word of the excel name (the given name in Cambodia)
            string targetWord = excelWords[excelWords.Length - 1];

            int best = 0;
            foreach (var dWord in dictWords)
            {
                // ── Exact word match (score 900) ─────────────────────────────
                if (targetWord == dWord) { best = Math.Max(best, 900); continue; }

                // ── Suffix of last name, right-to-left (score 700) ───────────
                // e.g. "smey" is a suffix of "chanreaksmey"
                // e.g. "net"  is a suffix of "vannet"
                if (dWord.Length >= 3 && targetWord.EndsWith(dWord, StringComparison.Ordinal))
                    best = Math.Max(best, 700);

                // symmetric: excelWord is a suffix of dictWord
                if (targetWord.Length >= 3 && dWord.EndsWith(targetWord, StringComparison.Ordinal))
                    best = Math.Max(best, 700);

                // ── Prefix of last name, left-to-right (score 500) ───────────
                // e.g. "pich" is a prefix of "pichponleu"  (teacher uses first syllable)
                // Minimum 4 chars to avoid accidental 3-letter collisions.
                if (dWord.Length >= 4 && targetWord.StartsWith(dWord, StringComparison.Ordinal))
                    best = Math.Max(best, 500);

                // symmetric: excelWord is a prefix of dictWord
                if (targetWord.Length >= 4 && dWord.StartsWith(targetWord, StringComparison.Ordinal))
                    best = Math.Max(best, 500);
            }
            return best;
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

        private bool AreSimilarNames(string name1, string name2)
        {
            if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2)) return false;
            name1 = name1.ToLower().Trim();
            name2 = name2.ToLower().Trim();
            if (name1 == name2) return true;

            int fullLcs = ComputeLCS(name1, name2);
            int maxFullLen = Math.Max(name1.Length, name2.Length);
            double fullRatio = (double)fullLcs / maxFullLen;

            var words1 = name1.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var words2 = name2.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (words1.Length == 0 || words2.Length == 0) return false;

            string last1 = words1[words1.Length - 1];
            string last2 = words2[words2.Length - 1];
            int lastLcs = ComputeLCS(last1, last2);
            int maxLastLen = Math.Max(last1.Length, last2.Length);
            double lastRatio = (double)lastLcs / maxLastLen;

            return fullRatio >= 0.85 && lastRatio >= 0.80;
        }

        private List<TeacherExcelRecord> FilterDuplicateTypoRecords(List<TeacherExcelRecord> records)
        {
            var toRemove = new HashSet<TeacherExcelRecord>();

            for (int i = 0; i < records.Count; i++)
            {
                var r1 = records[i];
                for (int j = i + 1; j < records.Count; j++)
                {
                    var r2 = records[j];

                    if (string.Equals(r1.Level, r2.Level, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r1.Session, r2.Session, StringComparison.OrdinalIgnoreCase))
                    {
                        if (AreSimilarNames(r1.TeacherName, r2.TeacherName))
                        {
                            bool r1HasPrints = !string.IsNullOrEmpty(r1.JobsTooltip);
                            bool r2HasPrints = !string.IsNullOrEmpty(r2.JobsTooltip);

                            if (r1HasPrints && !r2HasPrints)
                            {
                                toRemove.Add(r2);
                            }
                            else if (!r1HasPrints && r2HasPrints)
                            {
                                toRemove.Add(r1);
                            }
                        }
                    }
                }
            }

            return records.Where(r => !toRemove.Contains(r)).ToList();
        }

        private void SearchDebounceTimer_Tick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();
            ReloadFilteredData(_currentStart, _currentEnd);
        }

        private void CmbSort_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cmbSort == null) return;
            if (cmbSort.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string tag)
            {
                // Save selection to settings
                try
                {
                    var settings = PrintTrackerApp.Services.SettingsManager.LoadSettings();
                    settings.DashboardSortOrder = tag;
                    PrintTrackerApp.Services.SettingsManager.SaveSettings(settings);
                }
                catch { }
            }

            // Re-apply sort using cached _lastResult if we have it
            if (_lastResult != null)
            {
                BindDashboardData(_lastResult, _currentStart, _currentEnd, _currentDurationDays, txtSearch?.Text);
            }
        }

        private bool HasExactWordMatch(string name1, string name2)
        {
            if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2)) return false;
            var words1 = name1.ToLower().Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var words2 = name2.ToLower().Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (words1.Length == 0) return false;
            return words2.Contains(words1[words1.Length - 1]);
        }

        private bool LevelMatchesTeacher(string statLevel, HashSet<string> teacherLevels, Dictionary<string, bool> cache)
        {
            if (string.IsNullOrWhiteSpace(statLevel)) return false;
            foreach (var tLvl in teacherLevels)
            {
                if (GetIsLevelMatch(statLevel, tLvl, cache)) return true;
            }
            return false;
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
        private List<TeacherExcelRecord> GetRecordsForTab(System.Data.DataTable table, string tabType, bool ignoreSearch, bool includeHidden = false)
        {
            InitializeCategorySets();
            var records = new List<TeacherExcelRecord>();
            if (table == null) return records;
            int startRow = 0;
            for (int r = 0; r < table.Rows.Count; r++)
            {
                var noStr = table.Rows[r][0]?.ToString();
                if (!string.IsNullOrWhiteSpace(noStr) && int.TryParse(noStr, out _))
                {
                    startRow = r;
                    break;
                }
            }
            string searchText = ignoreSearch ? "" : (txtSearch?.Text?.ToLower()?.Trim() ?? "");

            var manager = PrintTrackerApp.Services.TeacherScheduleManager.Load();
            var levelCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var nameCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            // Pre-filter stats to only those belonging to the current tab type
            var tabStats = _statsDict.Values.Where(stat => GetCategoryOfStat(stat) == tabType).ToList();

            // Pre-group schedules by teacher name (portion before the first '_')
            var schedulesByTeacher = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in manager.Schedules.Keys)
            {
                int underscoreIndex = key.IndexOf('_');
                if (underscoreIndex >= 0)
                {
                    string tName = key.Substring(0, underscoreIndex);
                    if (!schedulesByTeacher.ContainsKey(tName))
                    {
                        schedulesByTeacher[tName] = new List<string>();
                    }
                    schedulesByTeacher[tName].Add(key);
                }
            }

            var addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ── Disambiguation pre-pass ────────────────────────────────────────────
            // For each parsed teacher-token that appears in tabStats, find which
            // roster teacher has the HIGHEST ScoreNameMatch.  Prevents ambiguous tokens
            // (e.g. "Sopha" / "Smey" at wrong level) being claimed by multiple teachers.
            var statTokenBestOwner = new Dictionary<string, (string Name, int Score)>(
                StringComparer.OrdinalIgnoreCase);
            for (int ri = startRow; ri < table.Rows.Count; ri++)
            {
                System.Data.DataRow rrow = table.Rows[ri];
                if (string.IsNullOrWhiteSpace(rrow[0]?.ToString())) continue;

                string rRaw = rrow[1]?.ToString() ?? "";
                if (IsInvalidTeacherName(rRaw)) continue;
                string rName = rRaw.Trim();
                if (rName.Contains("-"))
                    rName = rName.Split('-')[0].Trim();

                // Get teacher's allowed levels for this row
                var teacherLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string rowLvl = table.Columns.Count > 2 ? (rrow[2]?.ToString() ?? "") : "";
                string cleanRowLvl = rowLvl.Trim();
                if (cleanRowLvl.Contains("-"))
                {
                    cleanRowLvl = cleanRowLvl.Split('-')[0].Trim();
                }
                if (!string.IsNullOrEmpty(cleanRowLvl))
                {
                    teacherLevels.Add(cleanRowLvl);
                }
                
                if (tabType == "PT")
                {
                    var parts = rRaw.Split('-');
                    if (parts.Length >= 3)
                    {
                        string ptLvl = parts[1].Trim();
                        teacherLevels.Add(ptLvl);
                    }
                }
                
                if (schedulesByTeacher.TryGetValue(rName, out var schedKeys))
                {
                    foreach (var sKey in schedKeys)
                    {
                        string levelFromSchedule = sKey.Substring(rName.Length + 1);
                        if (levelFromSchedule.Contains("-"))
                        {
                            levelFromSchedule = levelFromSchedule.Split('-')[0].Trim();
                        }
                        teacherLevels.Add(levelFromSchedule);
                    }
                }

                foreach (var sStat in tabStats)
                {
                    int sc = ScoreNameMatch(rName, sStat.TeacherName);
                    if (sc <= 0) continue;

                    // Level match boost
                    bool levelMatches = LevelMatchesTeacher(sStat.Level, teacherLevels, levelCache);
                    if (levelMatches)
                    {
                        sc += 1000;
                    }

                    string key = $"{sStat.TeacherName.ToLower()}_{sStat.Level.ToLower()}_{sStat.Session.ToLower()}";
                    if (!statTokenBestOwner.TryGetValue(key, out var cur) || sc > cur.Score)
                        statTokenBestOwner[key] = (rName, sc);
                }
            }

            for (int i = startRow; i < table.Rows.Count; i++)
            {
                System.Data.DataRow row = table.Rows[i];
                var no = row[0]?.ToString();
                if (string.IsNullOrWhiteSpace(no)) continue;

                string rawTeacher = row[1]?.ToString() ?? "";
                if (IsInvalidTeacherName(rawTeacher)) continue;
                string teacherName = rawTeacher.Trim();
                if (teacherName.Contains("-"))
                {
                    var parts = teacherName.Split('-');
                    if (parts.Length >= 2)
                    {
                        teacherName = parts[0].Trim();
                    }
                }

                if (!includeHidden)
                {
                    if (manager.HiddenTeachers.Contains(teacherName) || manager.HiddenTeachers.Contains(rawTeacher.Trim()))
                    {
                        continue;
                    }
                }

                // If this teacher has printed in another category/tab during this period,
                // and has NOT printed in the current tab, exclude them from this tab to avoid false "E" grades.
                if (!includeHidden)
                {
                    bool printedInCurrentTab = tabStats.Any(stat => 
                        GetIsNameMatch(teacherName, stat.TeacherName, false, nameCache) && 
                        stat.PrintDays.Count > 0);
                        
                    if (!printedInCurrentTab)
                    {
                        bool printedInOtherTab = _statsDict.Values.Any(stat => 
                            GetCategoryOfStat(stat) != tabType &&
                            GetIsNameMatch(teacherName, stat.TeacherName, false, nameCache) && 
                            stat.PrintDays.Count > 0);
                            
                        if (printedInOtherTab)
                        {
                            continue;
                        }
                    }
                }

                // Get teacher's allowed levels
                var teacherLevels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string rowLvl = table.Columns.Count > 2 ? (row[2]?.ToString() ?? "") : "";
                string cleanRowLvl = rowLvl.Trim();
                if (cleanRowLvl.Contains("-"))
                {
                    cleanRowLvl = cleanRowLvl.Split('-')[0].Trim();
                }
                if (!string.IsNullOrEmpty(cleanRowLvl))
                {
                    teacherLevels.Add(cleanRowLvl);
                }
                
                if (tabType == "PT")
                {
                    var parts = rawTeacher.Split('-');
                    if (parts.Length >= 3)
                    {
                        string ptLvl = parts[1].Trim();
                        teacherLevels.Add(ptLvl);
                    }
                }
                
                if (schedulesByTeacher.TryGetValue(teacherName, out var schedKeys))
                {
                    foreach (var sKey in schedKeys)
                    {
                        string levelFromSchedule = sKey.Substring(teacherName.Length + 1);
                        if (levelFromSchedule.Contains("-"))
                        {
                            levelFromSchedule = levelFromSchedule.Split('-')[0].Trim();
                        }
                        teacherLevels.Add(levelFromSchedule);
                    }
                }

                if (!string.IsNullOrEmpty(searchText))
                {
                    bool matchFound = teacherName.ToLower().Contains(searchText);
                    if (!matchFound)
                    {
                        var matchingStats = tabStats.Where(stat => 
                            GetIsNameMatch(teacherName, stat.TeacherName, false, nameCache) &&
                            (HasExactWordMatch(teacherName, stat.TeacherName) || LevelMatchesTeacher(stat.Level, teacherLevels, levelCache))
                        ).ToList();
                        
                        foreach (var stat in matchingStats)
                        {
                            if (stat.Level.ToLower().Contains(searchText) || stat.Session.ToLower().Contains(searchText))
                            {
                                matchFound = true;
                                break;
                            }
                        }
                    }
                    if (!matchFound) continue;
                }

                var matches = tabStats.Where(stat =>
                    GetIsNameMatch(teacherName, stat.TeacherName, false, nameCache) &&
                    (HasExactWordMatch(teacherName, stat.TeacherName) || LevelMatchesTeacher(stat.Level, teacherLevels, levelCache)) &&
                    // Disambiguation: only claim this stat if this teacher is the best-scoring owner
                    // for the parsed token + level + session combo
                    (!statTokenBestOwner.TryGetValue($"{stat.TeacherName.ToLower()}_{stat.Level.ToLower()}_{stat.Session.ToLower()}", out var bestOwn) ||
                      string.Equals(bestOwn.Name, teacherName, StringComparison.OrdinalIgnoreCase))
                ).ToList();

                if (matches.Count > 0)
                {
                    var scheduledClasses = GetTeacherScheduledClasses(teacherName, tabType, table, startRow, schedulesByTeacher, nameCache);
                    var mergedGroups = GroupAndMergeTeacherStats(teacherName, tabType, matches, scheduledClasses);

                    foreach (var groupData in mergedGroups)
                    {
                        foreach (var stat in groupData.Stats)
                        {
                            stat.IsMatched = true;
                        }

                        string lvl = groupData.Level;
                        string ses = groupData.Session;
                        string key = $"{teacherName.ToLower()}_{lvl.ToLower()}_{ses.ToLower()}";

                        // Visibility filter check
                        string scheduleLevelKey = (tabType == "PT")
                            ? (string.IsNullOrEmpty(ses) ? lvl : $"{lvl}-{ses}")
                            : lvl;
                        string hiddenCheckKey = $"{teacherName}_{scheduleLevelKey}";
                        string dateKey = $"{hiddenCheckKey}_{_currentStart.ToString("yyyy-MM-dd")}_{_currentEnd.ToString("yyyy-MM-dd")}";
                        if (!includeHidden)
                        {
                            if (manager.HiddenTeachers.Contains(hiddenCheckKey) || manager.HiddenTeachers.Contains(dateKey))
                            {
                                continue;
                            }
                        }

                        if (addedKeys.Add(key))
                        {
                            var combinedPrintDays    = groupData.PrintDays;
                            var combinedExemptedDates = groupData.ExemptedDates;
                            var combinedTooltips     = groupData.TooltipLines;

                            int duration = (_currentEnd.Date - _currentStart.Date).Days + 1;

                            // Schedule-aware teach-day count (excludes No Teach / Exam days)
                            int scheduledTeachDays2 = 0;
                            for (DateTime d = _currentStart.Date; d <= _currentEnd.Date; d = d.AddDays(1))
                            {
                                string dayStatus = GetScheduleStatus(manager, teacherName, lvl, ses, d.ToString("yyyy-MM-dd"));
                                if (dayStatus != null)
                                {
                                    string ds = dayStatus.ToLower();
                                    if (ds == "no teach" || ds == "exam") continue;
                                }
                                scheduledTeachDays2++;
                            }
                            if (scheduledTeachDays2 <= 0) scheduledTeachDays2 = duration;

                            int weeks = (int)Math.Ceiling(duration / 7.0);
                            if (weeks == 0) weeks = 1;

                            int printedCount2 = combinedPrintDays.Count;
                            string combinedGrade = "E";

                            if (combinedExemptedDates.Count >= duration)
                            {
                                bool hasExam    = false;
                                bool hasNoTeach = false;

                                for (DateTime date = _currentStart.Date; date <= _currentEnd.Date; date = date.AddDays(1))
                                {
                                    string dateStr = date.ToString("yyyy-MM-dd");
                                    string status = GetScheduleStatus(manager, teacherName, lvl, ses, dateStr);
                                    if (status != null)
                                    {
                                        string s = status.ToLower();
                                        if (s == "exam")     hasExam    = true;
                                        if (s == "no teach") hasNoTeach = true;
                                    }
                                }
                                if (hasExam && hasNoTeach) combinedGrade = "Exam/No Teach";
                                else if (hasExam)          combinedGrade = "Exam";
                                else if (hasNoTeach)       combinedGrade = "No Teach";
                                else                       combinedGrade = "Exam/No Teach";
                            }
                            else
                            {
                                int activeDays = combinedPrintDays.Union(combinedExemptedDates).Count();
                                if      (activeDays >= 4 * weeks) combinedGrade = "A";
                                else if (activeDays >= 3 * weeks) combinedGrade = "B";
                                else if (activeDays >= 2 * weeks) combinedGrade = "C";
                                else if (activeDays >= 1 * weeks) combinedGrade = "D";
                                else                              combinedGrade = "E";
                            }

                            // ── BS (Both Sessions) expansion ───────────────────────────────
                            if (string.Equals(ses, "BS", StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (var expandedSes in new[] { "S1", "S2" })
                                {
                                    string bsKey = $"{teacherName.ToLower()}_{lvl.ToLower()}_{expandedSes.ToLower()}";
                                    if (addedKeys.Add(bsKey))
                                    {
                                        records.Add(new TeacherExcelRecord
                                        {
                                            No = no,
                                            TeacherName = teacherName,
                                            Level = lvl,
                                            Session = expandedSes,
                                            Grade = combinedGrade,
                                            JobsTooltip = string.Join("\n", combinedTooltips) + (combinedTooltips.Count > 0 ? "\n" : "")
                                        });
                                    }
                                }
                            }
                            else
                            {
                                records.Add(new TeacherExcelRecord
                                {
                                    No = no,
                                    TeacherName = teacherName,
                                    Level = lvl,
                                    Session = ses,
                                    Grade = combinedGrade,
                                    JobsTooltip = string.Join("\n", combinedTooltips) + (combinedTooltips.Count > 0 ? "\n" : "")
                                });
                            }
                        }
                    }
                }
                else
                {
                    bool addedFromSchedule = false;
                    if (schedulesByTeacher.TryGetValue(teacherName, out var matchingKeys) && matchingKeys.Count > 0)
                    {
                        var officialScheduledClasses = GetTeacherScheduledClasses(teacherName, tabType, table, startRow, schedulesByTeacher, nameCache);
                        foreach (var key in matchingKeys)
                        {
                            string levelFromSchedule = key.Substring(teacherName.Length + 1);

                            // Visibility filter check
                            string dateKey = $"{key}_{_currentStart.ToString("yyyy-MM-dd")}_{_currentEnd.ToString("yyyy-MM-dd")}";
                            if (!includeHidden)
                            {
                                if (manager.HiddenTeachers.Contains(key) || manager.HiddenTeachers.Contains(dateKey))
                                {
                                    continue;
                                }
                            }
                            
                            string lvl = levelFromSchedule;
                            string ses = string.Empty;
                            if (levelFromSchedule.Contains("-"))
                            {
                                var parts = levelFromSchedule.Split('-');
                                if (parts.Length >= 2)
                                {
                                    lvl = parts[0].Trim();
                                    ses = parts[1].Trim();
                                }
                            }

                            // Filter out schedule settings for levels not officially assigned to the teacher in the roster table (if roster defines any levels)
                            bool hasOfficialLevels = officialScheduledClasses.Any(sc => !string.IsNullOrWhiteSpace(sc.Level));
                            if (hasOfficialLevels && !officialScheduledClasses.Any(sc => sc.Level.Equals(lvl, StringComparison.OrdinalIgnoreCase) && sc.Session.Equals(ses, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            // Filter schedule levels by tabType!
                            if (string.IsNullOrWhiteSpace(lvl) || GetCategoryOfLevel(lvl, ses) != tabType)
                            {
                                continue;
                            }

                            string grade = CalculateGradeFromExemptionsOnlyInternal(teacherName, levelFromSchedule, manager, _currentStart, _currentEnd);
                            string mapKey = $"{teacherName.ToLower()}_{lvl.ToLower()}_{ses.ToLower()}";

                            if (string.Equals(ses, "BS", StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (var expandedSes in new[] { "S1", "S2" })
                                {
                                    string bsKey = $"{teacherName.ToLower()}_{lvl.ToLower()}_{expandedSes.ToLower()}";
                                    if (addedKeys.Add(bsKey))
                                    {
                                        records.Add(new TeacherExcelRecord
                                        {
                                            No = no,
                                            TeacherName = teacherName,
                                            Level = lvl,
                                            Session = expandedSes,
                                            Grade = grade,
                                            JobsTooltip = ""
                                        });
                                        addedFromSchedule = true;
                                    }
                                }
                            }
                            else
                            {
                                if (addedKeys.Add(mapKey))
                                {
                                    records.Add(new TeacherExcelRecord
                                    {
                                        No = no,
                                        TeacherName = teacherName,
                                        Level = lvl,
                                        Session = ses,
                                        Grade = grade,
                                        JobsTooltip = ""
                                    });
                                    addedFromSchedule = true;
                                }
                            }
                        }
                    }

                    if (!addedFromSchedule)
                    {
                        string rawLevel = table.Columns.Count > 2 ? (row[2]?.ToString() ?? "") : "";
                        string lvl = rawLevel.Trim();
                        string ses = string.Empty;
                        if (tabType == "PT" && string.IsNullOrWhiteSpace(lvl))
                        {
                            var parts = rawTeacher.Split('-');
                            if (parts.Length >= 3)
                            {
                                lvl = parts[1].Trim();
                                ses = parts[2].Trim();
                            }
                        }
                        else if (lvl.Contains("-"))
                        {
                            var parts = lvl.Split('-');
                            if (parts.Length >= 2)
                            {
                                lvl = parts[0].Trim();
                                ses = parts[1].Trim();
                            }
                        }
                        else if (table.Columns.Count > 3)
                        {
                            ses = row[3]?.ToString()?.Trim() ?? "";
                        }

                        // If raw level doesn't match tabType, clear it so we don't show wrong level
                        if (!string.IsNullOrWhiteSpace(lvl) && GetCategoryOfLevel(lvl, ses) != tabType)
                        {
                            lvl = "";
                            ses = "";
                        }

                        if (string.IsNullOrWhiteSpace(lvl))
                        {
                            continue;
                        }

                        // Visibility filter check
                        string scheduleLevelKey = (tabType == "PT")
                            ? (string.IsNullOrEmpty(ses) ? lvl : $"{lvl}-{ses}")
                            : lvl;
                        string hiddenCheckKey = $"{teacherName}_{scheduleLevelKey}";
                        string dateKey = $"{hiddenCheckKey}_{_currentStart.ToString("yyyy-MM-dd")}_{_currentEnd.ToString("yyyy-MM-dd")}";
                        if (!includeHidden)
                        {
                            if (manager.HiddenTeachers.Contains(hiddenCheckKey) || manager.HiddenTeachers.Contains(dateKey))
                            {
                                continue;
                            }
                        }

                        string grade = CalculateGradeFromExemptionsOnlyInternal(teacherName, scheduleLevelKey, manager, _currentStart, _currentEnd);
                        string mapKey = $"{teacherName.ToLower()}_{lvl.ToLower()}_{ses.ToLower()}";

                        if (string.Equals(ses, "BS", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var expandedSes in new[] { "S1", "S2" })
                            {
                                string bsKey = $"{teacherName.ToLower()}_{lvl.ToLower()}_{expandedSes.ToLower()}";
                                if (addedKeys.Add(bsKey))
                                {
                                    records.Add(new TeacherExcelRecord
                                    {
                                        No = no,
                                        TeacherName = teacherName,
                                        Level = lvl,
                                        Session = expandedSes,
                                        Grade = grade,
                                        JobsTooltip = ""
                                    });
                                }
                            }
                        }
                        else
                        {
                            if (addedKeys.Add(mapKey))
                            {
                                records.Add(new TeacherExcelRecord
                                {
                                    No = no,
                                    TeacherName = teacherName,
                                    Level = lvl,
                                    Session = ses,
                                    Grade = grade,
                                    JobsTooltip = ""
                                });
                            }
                        }
                    }
                }
            }

            if (tabType == "PT")
            {
                var groups = records.GroupBy(r => new { TeacherName = (r.TeacherName ?? "").ToLower(), Level = (r.Level ?? "").ToLower() }).ToList();
                var filteredRecords = new List<TeacherExcelRecord>();
                foreach (var group in groups)
                {
                    var list = group.ToList();
                    if (list.Count > 1)
                    {
                        var hasSession = list.Any(r => !string.IsNullOrWhiteSpace(r.Session));
                        if (hasSession)
                        {
                            filteredRecords.AddRange(list.Where(r => !string.IsNullOrWhiteSpace(r.Session)));
                            continue;
                        }
                    }
                    filteredRecords.AddRange(list);
                }
                records = filteredRecords;
            }

            return FilterDuplicateTypoRecords(records);
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

        private class PrintGroupData
        {
            public string Level { get; set; } = string.Empty;
            public string Session { get; set; } = string.Empty;
            public List<TeacherPrintStat> Stats { get; set; } = new List<TeacherPrintStat>();
            public HashSet<string> PrintDays { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ExemptedDates { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public List<string> TooltipLines { get; set; } = new List<string>();
            public bool IsScheduled { get; set; }
        }

        private List<(string Level, string Session)> GetTeacherScheduledClasses(
            string teacherName, 
            string tabType, 
            System.Data.DataTable table, 
            int startRow, 
            Dictionary<string, List<string>> schedulesByTeacher, 
            Dictionary<string, bool> nameCache)
        {
            var teacherScheduledClasses = new List<(string Level, string Session)>();
            if (table == null) return teacherScheduledClasses;

            for (int r = startRow; r < table.Rows.Count; r++)
            {
                string rTeacher = table.Rows[r][1]?.ToString() ?? "";
                string tName = rTeacher.Trim();
                string rLvl = "";
                string rSes = "";
                if (tabType == "PT")
                {
                    var parts = rTeacher.Split('-');
                    if (parts.Length >= 3)
                    {
                        tName = parts[0].Trim();
                        rLvl = parts[1].Trim();
                        rSes = parts[2].Trim();
                    }
                    else
                    {
                        string col2 = table.Columns.Count > 2 ? (table.Rows[r][2]?.ToString() ?? "") : "";
                        if (col2.Contains("-"))
                        {
                            var p = col2.Split('-');
                            rLvl = p[0].Trim();
                            rSes = p[1].Trim();
                        }
                        else
                        {
                            rLvl = col2.Trim();
                            if (table.Columns.Count > 3)
                            {
                                rSes = table.Rows[r][3]?.ToString()?.Trim() ?? "";
                            }
                        }
                    }
                }
                else
                {
                    if (tName.Contains("-"))
                    {
                        var parts = tName.Split('-');
                        if (parts.Length >= 2)
                        {
                            tName = parts[0].Trim();
                        }
                    }
                    string col2 = table.Columns.Count > 2 ? (table.Rows[r][2]?.ToString() ?? "") : "";
                    rLvl = col2.Trim();
                    if (rLvl.Contains("-"))
                    {
                        rLvl = rLvl.Split('-')[0].Trim();
                    }
                }

                if (GetIsNameMatch(teacherName, tName, false, nameCache))
                {
                    if (string.Equals(rSes, "BS", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!teacherScheduledClasses.Any(sc => sc.Level.Equals(rLvl, StringComparison.OrdinalIgnoreCase) && sc.Session.Equals("S1", StringComparison.OrdinalIgnoreCase)))
                            teacherScheduledClasses.Add((rLvl, "S1"));
                        if (!teacherScheduledClasses.Any(sc => sc.Level.Equals(rLvl, StringComparison.OrdinalIgnoreCase) && sc.Session.Equals("S2", StringComparison.OrdinalIgnoreCase)))
                            teacherScheduledClasses.Add((rLvl, "S2"));
                    }
                    else
                    {
                        if (!teacherScheduledClasses.Any(sc => sc.Level.Equals(rLvl, StringComparison.OrdinalIgnoreCase) && sc.Session.Equals(rSes, StringComparison.OrdinalIgnoreCase)))
                        {
                            teacherScheduledClasses.Add((rLvl, rSes));
                        }
                    }
                }
            }

            // Also integrate configured schedules from manager if they exist for this teacher
            if (schedulesByTeacher != null && schedulesByTeacher.TryGetValue(teacherName, out var schedKeys))
            {
                foreach (var sKey in schedKeys)
                {
                    if (sKey.Length > teacherName.Length + 1)
                    {
                        string levelPart = sKey.Substring(teacherName.Length + 1);
                        CleanLevelAndSession(levelPart, out string lvl, out string ses);
                        if (!string.IsNullOrWhiteSpace(lvl))
                        {
                            if (!teacherScheduledClasses.Any(sc => sc.Level.Equals(lvl, StringComparison.OrdinalIgnoreCase) && sc.Session.Equals(ses, StringComparison.OrdinalIgnoreCase)))
                            {
                                teacherScheduledClasses.Add((lvl, ses));
                            }
                        }
                    }
                }
            }

            return teacherScheduledClasses;
        }

        private List<PrintGroupData> GroupAndMergeTeacherStats(
            string teacherName, 
            string tabType, 
            List<TeacherPrintStat> matches, 
            List<(string Level, string Session)> teacherScheduledClasses)
        {
            var groupedMatches = matches.GroupBy(stat => {
                string lvl = stat.Level;
                string ses = (tabType == "PT") ? stat.Session : string.Empty;
                return (Level: lvl.ToLower(), Session: ses.ToLower());
            });

            var finalGroups = new List<PrintGroupData>();
            foreach (var group in groupedMatches)
            {
                var gd = new PrintGroupData
                {
                    Level = group.First().Level,
                    Session = (tabType == "PT") ? group.First().Session : string.Empty,
                    IsScheduled = false
                };
                foreach (var stat in group)
                {
                    gd.Stats.Add(stat);
                    foreach (var day in stat.PrintDays) gd.PrintDays.Add(day);
                    foreach (var day in stat.ExemptedDates) gd.ExemptedDates.Add(day);
                    if (!string.IsNullOrEmpty(stat.JobsTooltip))
                    {
                        var lines = stat.JobsTooltip.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            string trimmed = line.Trim();
                            if (!gd.TooltipLines.Contains(trimmed))
                            {
                                gd.TooltipLines.Add(trimmed);
                            }
                        }
                    }
                }
                
                foreach (var sc in teacherScheduledClasses)
                {
                    bool levelOk = sc.Level.Equals(gd.Level, StringComparison.OrdinalIgnoreCase);
                    bool sessionOk = true;
                    if (tabType == "PT")
                    {
                        sessionOk = string.IsNullOrEmpty(sc.Session) || sc.Session.Equals(gd.Session, StringComparison.OrdinalIgnoreCase);
                    }
                    if (levelOk && sessionOk)
                    {
                        gd.IsScheduled = true;
                        break;
                    }
                }
                
                finalGroups.Add(gd);
            }

            var groupsToProcess = finalGroups.ToList();
            foreach (var gd in groupsToProcess)
            {
                if (!gd.IsScheduled)
                {
                    // Any unscheduled group (wrong level in filename, missing level digit, etc.)
                    // is redirected to the closest scheduled class.  We no longer require
                    // PrintDays.Count <= 1 because a teacher may accidentally use the wrong
                    // level name for an entire week (e.g. "Pre" instead of "Pre5").
                    {
                        (string Level, string Session)? closestClass = null;
                        int bestScore = -1;
                        foreach (var sc in teacherScheduledClasses)
                        {
                            // Skip empty/whitespace scheduled levels to prevent matching empty strings
                            if (string.IsNullOrWhiteSpace(sc.Level))
                            {
                                continue;
                            }

                            int score = 0;
                            if (tabType == "PT")
                            {
                                if (sc.Session.Equals(gd.Session, StringComparison.OrdinalIgnoreCase))
                                {
                                    score += 50;
                                }
                            }
                            if (sc.Level.Equals(gd.Level, StringComparison.OrdinalIgnoreCase))
                            {
                                score += 100;
                            }
                            else if (IsLevelMatch(gd.Level, sc.Level))
                            {
                                score += 80;
                            }
                            else if (gd.Level.StartsWith(sc.Level, StringComparison.OrdinalIgnoreCase) || 
                                     sc.Level.StartsWith(gd.Level, StringComparison.OrdinalIgnoreCase))
                            {
                                score += 30;
                            }
                            
                            if (score >= 30 && score > bestScore)
                            {
                                bestScore = score;
                                closestClass = sc;
                            }
                        }
                        
                        if (closestClass != null)
                        {
                            string targetLevel = closestClass.Value.Level;
                            string targetSession = closestClass.Value.Session;
                            if (string.IsNullOrEmpty(targetSession) && !string.IsNullOrEmpty(gd.Session))
                            {
                                targetSession = gd.Session;
                            }

                            var targetGroup = finalGroups.FirstOrDefault(g => 
                                g.Level.Equals(targetLevel, StringComparison.OrdinalIgnoreCase) && 
                                g.Session.Equals(targetSession, StringComparison.OrdinalIgnoreCase));
                            
                            if (targetGroup == null)
                            {
                                targetGroup = new PrintGroupData
                                {
                                    Level = targetLevel,
                                    Session = targetSession,
                                    IsScheduled = true
                                };
                                finalGroups.Add(targetGroup);
                            }
                            
                            foreach (var stat in gd.Stats)
                            {
                                targetGroup.Stats.Add(stat);
                            }
                            foreach (var day in gd.PrintDays)
                            {
                                targetGroup.PrintDays.Add(day);
                            }
                            foreach (var day in gd.ExemptedDates)
                            {
                                targetGroup.ExemptedDates.Add(day);
                            }
                            foreach (var line in gd.TooltipLines)
                            {
                                if (!targetGroup.TooltipLines.Contains(line))
                                {
                                    targetGroup.TooltipLines.Add(line);
                                }
                            }
                            
                            finalGroups.Remove(gd);
                        }
                    }
                }
            }

            return finalGroups;
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            if (child == null) return null;
            var parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            if (parent != null) return parent;
            return FindVisualParent<T>(parentObject);
        }

        private static void CleanLevelAndSession(string levelPart, out string lvl, out string ses)
        {
            lvl = levelPart;
            ses = string.Empty;
            if (levelPart.Contains("-"))
            {
                var parts = levelPart.Split('-');
                if (parts.Length >= 2)
                {
                    string first = parts[0].Trim();
                    string second = parts[1].Trim();
                    string secondUpper = second.ToUpper();
                    if (secondUpper == "S1" || secondUpper == "S2" || secondUpper == "BS")
                    {
                        lvl = first;
                        ses = second;
                    }
                    else
                    {
                        lvl = first;
                        ses = string.Empty;
                    }
                }
            }
        }

        private Dictionary<string, string> GetScheduleDict(
            PrintTrackerApp.Services.TeacherScheduleManager manager,
            string teacherName,
            string level,
            string session)
        {
            if (manager?.Schedules == null) return null;
            
            // 1. Direct match
            string key = string.IsNullOrEmpty(session) ? $"{teacherName}_{level}" : $"{teacherName}_{level}-{session}";
            if (manager.Schedules.TryGetValue(key, out var dict))
            {
                return dict;
            }

            // 2. Cleaned key fallback
            string prefix = $"{teacherName}_";
            foreach (var pair in manager.Schedules)
            {
                if (pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string rawLvl = pair.Key.Substring(prefix.Length);
                    CleanLevelAndSession(rawLvl, out string cleanLvl, out string cleanSes);
                    if (string.Equals(cleanLvl, level, StringComparison.OrdinalIgnoreCase) && 
                        string.Equals(cleanSes, session, StringComparison.OrdinalIgnoreCase))
                    {
                        return pair.Value;
                    }
                }
            }

            return null;
        }
    }
}
