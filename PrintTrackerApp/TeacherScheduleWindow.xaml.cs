using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PrintTrackerApp.Services;

namespace PrintTrackerApp
{
    public partial class TeacherScheduleWindow : Window
    {
        private TeacherScheduleManager _manager;
        private DataTable _scheduleTable;
        private List<TeacherIdentifier> _teachers;          // schedule tab rows
        private List<TeacherIdentifier> _visibilityTeachers; // visibility tab rows (all Excel teachers)
        private bool _isUpdating = false;

        public class TeacherIdentifier
        {
            public string Name { get; set; }
            public string Level { get; set; }
            public string Category { get; set; }
            public string RawName { get; set; }
        }

        private UndoManager _undoManager = new UndoManager();
        private UndoBatch _currentUndoBatch;
        private System.Windows.Threading.DispatcherTimer _autoScrollTimer;
        private ScrollViewer _scrollViewer;

        private bool _isDraggingFill = false;
        private DataGridCellInfo _fillStartCellInfo;
        private object _fillSourceValue;
        private int _fillStartRowIdx = -1;
        private int _fillStartColIdx = -1;

        // Schedules grid fields
        private System.Windows.Controls.CheckBox _headerCheckBox = null;
        private bool _selectAllState = true;

        // Visibility grid fields
        private DataTable _visibilityTable;
        private System.Windows.Controls.CheckBox _visibilityHeaderCheckBox = null;
        private bool _visibilitySelectAllState = true;

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T)
                    return (T)child;
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            T parent = parentObject as T;
            if (parent != null) return parent;
            return FindVisualParent<T>(parentObject);
        }

        private DataGridCell GetCellFromPoint(System.Windows.Point point)
        {
            var hit = System.Windows.Media.VisualTreeHelper.HitTest(dgSchedule, point);
            if (hit == null) return null;
            return FindVisualParent<DataGridCell>(hit.VisualHit);
        }

        private DataGridRow GetRowFromItem(object item)
        {
            return (DataGridRow)dgSchedule.ItemContainerGenerator.ContainerFromItem(item);
        }

        public TeacherScheduleWindow(List<TeacherIdentifier> scheduleTeachers, List<TeacherIdentifier> visibilityTeachers)
        {
            _isUpdating = true; // Prevent BuildTable from running during initialization
            InitializeComponent();
            _teachers = scheduleTeachers;
            _visibilityTeachers = visibilityTeachers ?? scheduleTeachers;
            _manager = TeacherScheduleManager.Load();
            
            LoadCustomDateFilters();
            
            string lastFilter = _manager.LastFilterName;
            bool foundFilter = false;
            if (!string.IsNullOrEmpty(lastFilter))
            {
                for (int i = 0; i < cmbDateFilter.Items.Count; i++)
                {
                    if (cmbDateFilter.Items[i] is ComboBoxItem item && string.Equals(item.Content?.ToString(), lastFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        cmbDateFilter.SelectedIndex = i;
                        foundFilter = true;
                        break;
                    }
                }
            }

            if (!foundFilter)
            {
                cmbDateFilter.SelectedIndex = 0; // Default to Custom Range
            }

            if (cmbDateFilter.SelectedIndex == 0)
            {
                if (_manager.LastCustomStartDate.HasValue && _manager.LastCustomEndDate.HasValue)
                {
                    dpStart.SelectedDate = _manager.LastCustomStartDate.Value;
                    dpEnd.SelectedDate = _manager.LastCustomEndDate.Value;
                }
                else
                {
                    int diff = (7 + (DateTime.Now.DayOfWeek - DayOfWeek.Monday)) % 7;
                    dpStart.SelectedDate = DateTime.Now.AddDays(-1 * diff).Date;
                    dpEnd.SelectedDate = dpStart.SelectedDate.Value.AddDays(4);
                }
            }
            else
            {
                // Force populate the dates from the selected CustomDateFilter item
                if (cmbDateFilter.SelectedItem is ComboBoxItem item && item.Tag is PrintTrackerApp.Services.CustomDateFilter filter)
                {
                    dpStart.SelectedDate = filter.StartDate.Date;
                    dpEnd.SelectedDate = filter.EndDate.Date;
                }
            }
            
            _autoScrollTimer = new System.Windows.Threading.DispatcherTimer();
            _autoScrollTimer.Interval = TimeSpan.FromMilliseconds(50);
            _autoScrollTimer.Tick += AutoScrollTimer_Tick;

            _isUpdating = false; // Re-enable BuildTable
            BuildTable();
        }

        private void AutoScrollTimer_Tick(object sender, EventArgs e)
        {
            if (!_isDraggingFill || _scrollViewer == null) return;
            var point = System.Windows.Input.Mouse.GetPosition(dgSchedule);
            double zone = 30;
            if (point.Y < zone) _scrollViewer.LineUp();
            else if (point.Y > dgSchedule.ActualHeight - zone) _scrollViewer.LineDown();
            
            if (point.X < zone) _scrollViewer.LineLeft();
            else if (point.X > dgSchedule.ActualWidth - zone) _scrollViewer.LineRight();
        }

        private void ScheduleTable_ColumnChanging(object sender, DataColumnChangeEventArgs e)
        {
            if (_undoManager.IsUndoingOrRedoing) return;

            var row = e.Row;
            var col = e.Column;
            object oldVal = row[col];
            object newVal = e.ProposedValue;

            if (Equals(oldVal, newVal)) return;

            if (_currentUndoBatch != null)
            {
                _currentUndoBatch.UndoActions.Add(() => { row[col] = oldVal; });
                _currentUndoBatch.RedoActions.Add(() => { row[col] = newVal; });
            }
            else
            {
                var batch = new UndoBatch();
                batch.UndoActions.Add(() => { row[col] = oldVal; });
                batch.RedoActions.Add(() => { row[col] = newVal; });
                _undoManager.AddBatch(batch);
            }
        }

        private void LoadCustomDateFilters()
        {
            if (cmbDateFilter == null) return;
            cmbDateFilter.Items.Clear();

            var defaultItem = new ComboBoxItem { Content = "Custom Range" };
            cmbDateFilter.Items.Add(defaultItem);

            var settings = PrintTrackerApp.Services.SettingsManager.LoadSettings();
            if (settings.CustomDateFilters != null)
            {
                foreach (var filter in settings.CustomDateFilters)
                {
                    var item = new ComboBoxItem();
                    item.Content = filter.Name;
                    item.Tag = filter;
                    cmbDateFilter.Items.Add(item);
                }
            }
            
            cmbDateFilter.SelectedIndex = 0;
        }

        private void CmbDateFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbDateFilter.SelectedItem is ComboBoxItem item && item.Tag is PrintTrackerApp.Services.CustomDateFilter filter)
            {
                dpStart.SelectedDate = filter.StartDate.Date;
                dpEnd.SelectedDate = filter.EndDate.Date;
            }
        }

        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            BuildTable();
        }

        public void LocateTeacher(string name, string level, string category)
        {
            if (string.IsNullOrEmpty(name)) return;

            // Clear search filter so the teacher isn't filtered out
            if (txtSearch != null)
            {
                txtSearch.Text = "";
            }

            // Switch to Schedules tab in the main tab control
            if (mainTabControl != null)
            {
                mainTabControl.SelectedIndex = 0;
            }

            // Select category tab
            if (tabCategory != null)
            {
                if (string.Equals(category, "FT", StringComparison.OrdinalIgnoreCase))
                    tabCategory.SelectedIndex = 1;
                else if (string.Equals(category, "PT", StringComparison.OrdinalIgnoreCase))
                    tabCategory.SelectedIndex = 2;
                else if (string.Equals(category, "KH", StringComparison.OrdinalIgnoreCase))
                    tabCategory.SelectedIndex = 3;
                else
                    tabCategory.SelectedIndex = 0; // All Teachers
            }

            // Force layout/filters update to ensure dgSchedule has the correct items
            ApplyFilters();

            // Find matching row
            DataRowView bestMatch = null;
            int bestScore = -1;

            if (dgSchedule.ItemsSource != null)
            {
                foreach (var item in dgSchedule.ItemsSource)
                {
                    if (item is DataRowView rowView)
                    {
                        string rName = rowView["Teacher Name"]?.ToString() ?? "";
                        string rLevel = rowView["Level"]?.ToString() ?? "";

                        int score = 0;
                        if (string.Equals(rName, name, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 100;
                        }
                        else if (TeacherDashboardControl.IsNameMatch(rName, name, false))
                        {
                            score += 50;
                        }

                        if (string.Equals(rLevel, level, StringComparison.OrdinalIgnoreCase))
                        {
                            score += 10;
                        }
                        else if (TeacherDashboardControl.IsLevelMatch(rLevel, level))
                        {
                            score += 5;
                        }

                        if (score > bestScore && score > 0)
                        {
                            bestScore = score;
                            bestMatch = rowView;
                        }
                    }
                }
            }

            if (bestMatch != null)
            {
                dgSchedule.SelectedItem = bestMatch;
                
                // Scroll to the selected item on Dispatcher background priority
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    dgSchedule.UpdateLayout();
                    dgSchedule.ScrollIntoView(bestMatch);
                    
                    // Also select cell to highlight
                    if (dgSchedule.Columns.Count > 1)
                    {
                        var cellInfo = new DataGridCellInfo(bestMatch, dgSchedule.Columns[1]);
                        dgSchedule.CurrentCell = cellInfo;
                        dgSchedule.SelectedCells.Clear();
                        dgSchedule.SelectedCells.Add(cellInfo);
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void BuildTable()
        {
            if (_isUpdating || dpStart.SelectedDate == null || dpEnd.SelectedDate == null) return;
            
            DateTime start = dpStart.SelectedDate.Value;
            DateTime end = dpEnd.SelectedDate.Value;
            
            if (end < start) return;
            if ((end - start).TotalDays > 31)
            {
                System.Windows.MessageBox.Show("Please select a date range of 31 days or less.", "Date Range Too Large", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _scheduleTable = new DataTable();
            _scheduleTable.ColumnChanging += ScheduleTable_ColumnChanging;
            _undoManager.Clear();
            
            _scheduleTable.Columns.Add("Show", typeof(bool)); // visibility checkbox column
            _scheduleTable.Columns.Add("Display Name", typeof(string));
            _scheduleTable.Columns.Add("Teacher Name", typeof(string));
            _scheduleTable.Columns.Add("Level", typeof(string));
            _scheduleTable.Columns.Add("Category", typeof(string));

            dgSchedule.Columns.Clear();

            // ── Checkbox column (Show in Tab) ────────────────────────────────────
            _headerCheckBox = new System.Windows.Controls.CheckBox
            {
                IsChecked = true,
                IsThreeState = true,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                ToolTip = "Show / Hide all teachers in dashboard tabs",
                Margin = new Thickness(2)
            };
            _headerCheckBox.Click += ChkSelectAll_Click;

            var showCellFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.CheckBox));
            showCellFactory.SetValue(System.Windows.Controls.CheckBox.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            showCellFactory.SetValue(System.Windows.Controls.CheckBox.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            showCellFactory.SetBinding(System.Windows.Controls.CheckBox.IsCheckedProperty,
                new System.Windows.Data.Binding("[Show]") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
            showCellFactory.AddHandler(System.Windows.Controls.CheckBox.ClickEvent, new RoutedEventHandler(RowShowChk_Click));
            var showCellTemplate = new DataTemplate { VisualTree = showCellFactory };

            var showCol = new DataGridTemplateColumn
            {
                Header = _headerCheckBox,
                Width = new DataGridLength(36),
                IsReadOnly = false,
                CellTemplate = showCellTemplate
            };
            dgSchedule.Columns.Add(showCol);
            // ────────────────────────────────────────────────────────────────────

            dgSchedule.Columns.Add(new DataGridTextColumn { Header = "Teacher Name", Binding = new System.Windows.Data.Binding("Display Name"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            dgSchedule.Columns.Add(new DataGridTextColumn { Header = "Level", Binding = new System.Windows.Data.Binding("Level"), IsReadOnly = true, Width = new DataGridLength(100) });

            // Generate date columns
            var statuses = new List<string> { "Teach", "No Teach", "Exam" };
            for (DateTime date = start; date <= end; date = date.AddDays(1))
            {
                string dateStr = date.ToString("yyyy-MM-dd");
                string headerStr = date.ToString("dd-MMM");

                _scheduleTable.Columns.Add(dateStr, typeof(string));

                var comboCol = new DataGridComboBoxColumn
                {
                    Header = headerStr,
                    SelectedItemBinding = new System.Windows.Data.Binding($"[{dateStr}]") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                    ItemsSource = statuses,
                    Width = new DataGridLength(80)
                };
                dgSchedule.Columns.Add(comboCol);
            }

            BuildVisibilityTable();
            PopulateData(start, end);
        }

        private void BuildVisibilityTable()
        {
            _visibilityTable = new DataTable();
            _visibilityTable.Columns.Add("Show", typeof(bool));
            _visibilityTable.Columns.Add("Teacher Name", typeof(string));
            _visibilityTable.Columns.Add("Category", typeof(string));

            dgVisibility.Columns.Clear();

            _visibilityHeaderCheckBox = new System.Windows.Controls.CheckBox
            {
                IsChecked = true,
                IsThreeState = true,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                ToolTip = "Show / Hide all teachers in dashboard tabs",
                Margin = new Thickness(2)
            };
            _visibilityHeaderCheckBox.Click += VisibilityChkSelectAll_Click;

            var showCellFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.CheckBox));
            showCellFactory.SetValue(System.Windows.Controls.CheckBox.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            showCellFactory.SetValue(System.Windows.Controls.CheckBox.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            showCellFactory.SetBinding(System.Windows.Controls.CheckBox.IsCheckedProperty,
                new System.Windows.Data.Binding("[Show]") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
            showCellFactory.AddHandler(System.Windows.Controls.CheckBox.ClickEvent, new RoutedEventHandler(VisibilityRowShowChk_Click));
            var showCellTemplate = new DataTemplate { VisualTree = showCellFactory };

            var showCol = new DataGridTemplateColumn
            {
                Header = _visibilityHeaderCheckBox,
                Width = new DataGridLength(36),
                IsReadOnly = false,
                CellTemplate = showCellTemplate
            };
            dgVisibility.Columns.Add(showCol);

            dgVisibility.Columns.Add(new DataGridTextColumn 
            { 
                Header = "Teacher Name", 
                Binding = new System.Windows.Data.Binding("Teacher Name"), 
                IsReadOnly = true, 
                Width = new DataGridLength(1, DataGridLengthUnitType.Star) 
            });
        }

        private void PopulateData(DateTime start, DateTime end)
        {
            _scheduleTable.Rows.Clear();
            _visibilityTable.Rows.Clear();
            string searchText = txtSearch.Text?.ToLower()?.Trim() ?? "";

            // 1. Schedules Grid
            foreach (var teacher in _teachers.OrderBy(t => t.RawName).ThenBy(t => t.Level))
            {
                if (!string.IsNullOrEmpty(searchText))
                {
                    if (!teacher.RawName.ToLower().Contains(searchText) && !teacher.Level.ToLower().Contains(searchText))
                        continue;
                }

                DataRow row = _scheduleTable.NewRow();
                row["Display Name"] = teacher.RawName;
                row["Teacher Name"] = teacher.Name;
                row["Level"] = teacher.Level;
                row["Category"] = teacher.Category ?? "Unknown";

                string levelKey = string.IsNullOrEmpty(teacher.Level) ? teacher.Name : $"{teacher.Name}_{teacher.Level}";
                string dateKey = $"{levelKey}_{start.ToString("yyyy-MM-dd")}_{end.ToString("yyyy-MM-dd")}";
                row["Show"] = !_manager.HiddenTeachers.Contains(teacher.Name) && 
                              !_manager.HiddenTeachers.Contains(levelKey) && 
                              !_manager.HiddenTeachers.Contains(dateKey);

                string key = $"{teacher.Name}_{teacher.Level}";
                bool hasSchedule = _manager.Schedules.ContainsKey(key);

                for (DateTime date = start; date <= end; date = date.AddDays(1))
                {
                    string dateStr = date.ToString("yyyy-MM-dd");
                    string status = "Teach";

                    if (hasSchedule && _manager.Schedules[key].ContainsKey(dateStr))
                    {
                        status = _manager.Schedules[key][dateStr];
                        if (status == "teach") status = "Teach";
                        else if (status == "no teach") status = "No Teach";
                        else if (status == "exam") status = "Exam";
                    }

                    row[dateStr] = status;
                }

                _scheduleTable.Rows.Add(row);
            }

            // 2. Visibility Grid — unique teacher names sourced from the Excel roster.
            // One row per teacher (no level). Unchecking hides ALL levels of that teacher.
            var uniqueVisibility = _visibilityTeachers
                .GroupBy(t => new { t.Name, t.Category })
                .Select(g => g.First())
                .OrderBy(t => t.Name);

            foreach (var teacher in uniqueVisibility)
            {
                if (!string.IsNullOrEmpty(searchText))
                {
                    if (!teacher.Name.ToLower().Contains(searchText))
                        continue;
                }

                DataRow row = _visibilityTable.NewRow();
                row["Teacher Name"] = teacher.Name;
                row["Category"] = teacher.Category ?? "Unknown";
                row["Show"] = !_manager.HiddenTeachers.Contains(teacher.Name);

                _visibilityTable.Rows.Add(row);
            }

            ApplyFilters();
            txtStatus.Text = "";
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source == mainTabControl)
            {
                ApplyFilters();
            }
        }

        private void TabCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void TabCategoryVisibility_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            // Schedules Filter
            if (_scheduleTable != null)
            {
                string searchText = txtSearch.Text?.ToLower()?.Trim() ?? "";
                string categoryFilter = "";

                if (tabCategory != null && tabCategory.SelectedItem is TabItem tabItem && tabItem.Header != null)
                {
                    string header = tabItem.Header.ToString();
                    if (header != "All Teachers")
                    {
                        categoryFilter = $"Category = '{header}'";
                    }
                }

                string finalFilter = categoryFilter;

                if (!string.IsNullOrEmpty(searchText))
                {
                    string searchFilter = $"([Display Name] LIKE '%{searchText}%' OR Level LIKE '%{searchText}%')";
                    if (string.IsNullOrEmpty(finalFilter))
                        finalFilter = searchFilter;
                    else
                        finalFilter += $" AND {searchFilter}";
                }
                
                _scheduleTable.DefaultView.RowFilter = finalFilter;
                dgSchedule.ItemsSource = _scheduleTable.DefaultView;

                // Sync Header checkbox for schedules grid
                int total = _scheduleTable.DefaultView.Count;
                if (total > 0 && _headerCheckBox != null)
                {
                    int shown = _scheduleTable.DefaultView.Cast<DataRowView>()
                        .Count(r => (bool)(r["Show"] ?? true));
                    _headerCheckBox.IsChecked = shown == total ? (bool?)true
                                                        : shown == 0     ? (bool?)false
                                                        : null;
                }
            }

            // Visibility Filter
            if (_visibilityTable != null)
            {
                string searchText = txtSearch.Text?.ToLower()?.Trim() ?? "";
                string categoryFilter = "";

                if (tabCategoryVisibility != null && tabCategoryVisibility.SelectedItem is TabItem tabItemVis && tabItemVis.Header != null)
                {
                    string header = tabItemVis.Header.ToString();
                    if (header != "All Teachers")
                    {
                        categoryFilter = $"Category = '{header}'";
                    }
                }

                string finalFilter = categoryFilter;

                if (!string.IsNullOrEmpty(searchText))
                {
                    string searchFilter = $"[Teacher Name] LIKE '%{searchText}%'";
                    if (string.IsNullOrEmpty(finalFilter))
                        finalFilter = searchFilter;
                    else
                        finalFilter += $" AND {searchFilter}";
                }

                _visibilityTable.DefaultView.RowFilter = finalFilter;
                dgVisibility.ItemsSource = _visibilityTable.DefaultView;

                // Sync Header checkbox
                int total = _visibilityTable.DefaultView.Count;
                if (total > 0 && _visibilityHeaderCheckBox != null)
                {
                    int shown = _visibilityTable.DefaultView.Cast<DataRowView>()
                        .Count(r => (bool)(r["Show"] ?? true));
                    _visibilityHeaderCheckBox.IsChecked = shown == total ? (bool?)true
                                                        : shown == 0     ? (bool?)false
                                                        : null;
                }
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void VisibilityChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_visibilityHeaderCheckBox == null || _visibilityTable == null) return;
            _visibilitySelectAllState = !_visibilitySelectAllState;
            _visibilityHeaderCheckBox.IsChecked = _visibilitySelectAllState;
            foreach (DataRowView rowView in _visibilityTable.DefaultView)
                rowView["Show"] = _visibilitySelectAllState;
        }

        private void VisibilityRowShowChk_Click(object sender, RoutedEventArgs e)
        {
            if (_visibilityHeaderCheckBox == null || _visibilityTable == null) return;
            int total = _visibilityTable.DefaultView.Count;
            if (total == 0) return;
            int shown = _visibilityTable.DefaultView.Cast<DataRowView>()
                .Count(r => (bool)(r["Show"] ?? true));
            _visibilityHeaderCheckBox.IsChecked = shown == total ? (bool?)true
                                                : shown == 0     ? (bool?)false
                                                : null;
        }

        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_headerCheckBox == null || _scheduleTable == null) return;
            _selectAllState = !_selectAllState;
            _headerCheckBox.IsChecked = _selectAllState;
            foreach (DataRowView rowView in _scheduleTable.DefaultView)
                rowView["Show"] = _selectAllState;
        }

        private void RowShowChk_Click(object sender, RoutedEventArgs e)
        {
            if (_headerCheckBox == null || _scheduleTable == null) return;
            int total = _scheduleTable.DefaultView.Count;
            if (total == 0) return;
            int shown = _scheduleTable.DefaultView.Cast<DataRowView>()
                .Count(r => (bool)(r["Show"] ?? true));
            _headerCheckBox.IsChecked = shown == total ? (bool?)true
                                                : shown == 0     ? (bool?)false
                                                : null;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            dgSchedule.CommitEdit(DataGridEditingUnit.Cell, true);
            dgSchedule.CommitEdit(DataGridEditingUnit.Row, true);
            
            if (dgVisibility != null)
            {
                dgVisibility.CommitEdit(DataGridEditingUnit.Cell, true);
                dgVisibility.CommitEdit(DataGridEditingUnit.Row, true);
            }

            DateTime start = dpStart.SelectedDate.Value;
            DateTime end = dpEnd.SelectedDate.Value;

            // Save schedules
            foreach (DataRow row in _scheduleTable.Rows)
            {
                string tName = row["Teacher Name"].ToString();
                string tLevel = row["Level"].ToString();
                string key = $"{tName}_{tLevel}";

                if (!_manager.Schedules.ContainsKey(key))
                    _manager.Schedules[key] = new Dictionary<string, string>();

                for (DateTime date = start; date <= end; date = date.AddDays(1))
                {
                    string dateStr = date.ToString("yyyy-MM-dd");
                    string status = row[dateStr]?.ToString() ?? "Teach";
                    _manager.Schedules[key][dateStr] = status;
                }
            }

            // Save hidden teachers (update selectively to avoid overwriting settings for other weeks)
            if (_visibilityTable != null)
            {
                foreach (DataRow row in _visibilityTable.Rows)
                {
                    string tName = row["Teacher Name"].ToString();
                    bool isShown = row["Show"] as bool? ?? true;
                    if (isShown)
                    {
                        _manager.HiddenTeachers.Remove(tName);
                    }
                    else
                    {
                        _manager.HiddenTeachers.Add(tName);
                    }
                }
            }
            if (_scheduleTable != null)
            {
                foreach (DataRow row in _scheduleTable.Rows)
                {
                    string tName = row["Teacher Name"].ToString();
                    string tLevel = row["Level"].ToString();
                    string levelKey = string.IsNullOrEmpty(tLevel) ? tName : $"{tName}_{tLevel}";
                    string dateKey = $"{levelKey}_{start.ToString("yyyy-MM-dd")}_{end.ToString("yyyy-MM-dd")}";

                    bool isShown = row["Show"] as bool? ?? true;
                    if (isShown)
                    {
                        _manager.HiddenTeachers.Remove(levelKey);
                        _manager.HiddenTeachers.Remove(dateKey);
                    }
                    else
                    {
                        _manager.HiddenTeachers.Add(dateKey);
                    }
                }
            }

            // Save last selected filter and custom range dates
            if (cmbDateFilter.SelectedItem is ComboBoxItem selectedItem)
            {
                _manager.LastFilterName = selectedItem.Content?.ToString() ?? "";
            }
            if (cmbDateFilter.SelectedIndex == 0)
            {
                _manager.LastCustomStartDate = dpStart.SelectedDate;
                _manager.LastCustomEndDate = dpEnd.SelectedDate;
            }

            _manager.Save();
            txtStatus.Text = "Saved successfully at " + DateTime.Now.ToString("HH:mm:ss");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private void DgSchedule_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingFill)
            {
                var point = e.GetPosition(dgSchedule);
                var currentCell = GetCellFromPoint(point);
                if (currentCell != null)
                {
                    var colIdx = currentCell.Column.DisplayIndex;
                    var rowView = currentCell.DataContext as DataRowView;
                    if (rowView != null)
                    {
                        var rowIdx = dgSchedule.Items.IndexOf(rowView);
                        if (rowIdx >= 0)
                        {
                            int minRow = Math.Min(_fillStartRowIdx, rowIdx);
                            int maxRow = Math.Max(_fillStartRowIdx, rowIdx);
                            int minCol = Math.Min(_fillStartColIdx, colIdx);
                            int maxCol = Math.Max(_fillStartColIdx, colIdx);

                            dgSchedule.SelectedCells.Clear();
                            for (int r = minRow; r <= maxRow; r++)
                            {
                                var item = dgSchedule.Items[r];
                                for (int c = minCol; c <= maxCol; c++)
                                {
                                    var col = dgSchedule.Columns.FirstOrDefault(x => x.DisplayIndex == c);
                                    if (col != null)
                                    {
                                        dgSchedule.SelectedCells.Add(new DataGridCellInfo(item, col));
                                    }
                                }
                            }
                        }
                    }
                }
                return;
            }

            var cell = GetCellFromPoint(e.GetPosition(dgSchedule));
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

        private void DgSchedule_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (System.Windows.Input.Mouse.OverrideCursor == System.Windows.Input.Cursors.Cross)
            {
                var cell = GetCellFromPoint(e.GetPosition(dgSchedule));
                if (cell != null && !cell.Column.IsReadOnly)
                {
                    var rowView = cell.DataContext as DataRowView;
                    if (rowView != null)
                    {
                        _isDraggingFill = true;
                        _scrollViewer = FindVisualChild<ScrollViewer>(dgSchedule);
                        _autoScrollTimer.Start();
                        
                        _fillStartCellInfo = new DataGridCellInfo(cell);
                        _fillStartColIdx = cell.Column.DisplayIndex;
                        _fillStartRowIdx = dgSchedule.Items.IndexOf(rowView);

                        string bindingPath = (cell.Column.ClipboardContentBinding as System.Windows.Data.Binding)?.Path?.Path 
                                             ?? ((cell.Column as DataGridComboBoxColumn)?.SelectedItemBinding as System.Windows.Data.Binding)?.Path?.Path;
                        
                        if (!string.IsNullOrEmpty(bindingPath))
                        {
                            var cleanPath = bindingPath.Replace("[", "").Replace("]", "");
                            _fillSourceValue = rowView[cleanPath];
                        }

                        e.Handled = true;
                        dgSchedule.CaptureMouse();
                    }
                }
            }
        }

        private void DgSchedule_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isDraggingFill)
            {
                _isDraggingFill = false;
                _autoScrollTimer.Stop();
                dgSchedule.ReleaseMouseCapture();
                System.Windows.Input.Mouse.OverrideCursor = null;

                if (_fillSourceValue != null)
                {
                    _currentUndoBatch = new UndoBatch();
                    
                    foreach (var cellInfo in dgSchedule.SelectedCells)
                    {
                        if (cellInfo.Column.IsReadOnly) continue;
                        
                        var rowView = cellInfo.Item as DataRowView;
                        if (rowView != null)
                        {
                            string bindingPath = (cellInfo.Column.ClipboardContentBinding as System.Windows.Data.Binding)?.Path?.Path 
                                                 ?? ((cellInfo.Column as DataGridComboBoxColumn)?.SelectedItemBinding as System.Windows.Data.Binding)?.Path?.Path;
                            if (!string.IsNullOrEmpty(bindingPath))
                            {
                                var cleanPath = bindingPath.Replace("[", "").Replace("]", "");
                                rowView[cleanPath] = _fillSourceValue;
                                rowView.EndEdit();
                            }
                        }
                    }
                    _undoManager.AddBatch(_currentUndoBatch);
                    _currentUndoBatch = null;
                    
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        dgSchedule.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
                        dgSchedule.Items.Refresh();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        private void DgSchedule_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Z && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                _undoManager.Undo();
                e.Handled = true;
                return;
            }
            else if (e.Key == System.Windows.Input.Key.Y && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                _undoManager.Redo();
                e.Handled = true;
                return;
            }

            if (e.Key == System.Windows.Input.Key.V && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                string clipboardText = System.Windows.Clipboard.GetText();
                if (string.IsNullOrEmpty(clipboardText)) return;

                var cells = dgSchedule.SelectedCells;
                if (cells.Count == 0) return;

                var startCell = cells.OrderBy(c => dgSchedule.Items.IndexOf(c.Item)).ThenBy(c => c.Column.DisplayIndex).First();
                int startRowIdx = dgSchedule.Items.IndexOf(startCell.Item);
                int startColIdx = startCell.Column.DisplayIndex;

                _currentUndoBatch = new UndoBatch();

                var lines = clipboardText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                for (int r = 0; r < lines.Length; r++)
                {
                    if (string.IsNullOrEmpty(lines[r]) && r == lines.Length - 1) continue;

                    var vals = lines[r].Split('\t');
                    if (startRowIdx + r >= dgSchedule.Items.Count) break;

                    var rowView = dgSchedule.Items[startRowIdx + r] as DataRowView;
                    if (rowView == null) continue;

                    for (int c = 0; c < vals.Length; c++)
                    {
                        var col = dgSchedule.Columns.FirstOrDefault(x => x.DisplayIndex == startColIdx + c);
                        if (col != null && !col.IsReadOnly)
                        {
                            string bindingPath = (col.ClipboardContentBinding as System.Windows.Data.Binding)?.Path?.Path 
                                                 ?? ((col as DataGridComboBoxColumn)?.SelectedItemBinding as System.Windows.Data.Binding)?.Path?.Path;
                            if (!string.IsNullOrEmpty(bindingPath))
                            {
                                var cleanPath = bindingPath.Replace("[", "").Replace("]", "");
                                rowView[cleanPath] = vals[c];
                                rowView.EndEdit();
                            }
                        }
                    }
                }
                _undoManager.AddBatch(_currentUndoBatch);
                _currentUndoBatch = null;
                
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    dgSchedule.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
                    dgSchedule.Items.Refresh();
                }), System.Windows.Threading.DispatcherPriority.Background);
                
                e.Handled = true;
            }
        }
    }
}
