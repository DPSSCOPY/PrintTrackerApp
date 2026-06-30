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
        private List<TeacherIdentifier> _teachers;
        private bool _isUpdating = false;

        public class TeacherIdentifier
        {
            public string Name { get; set; }
            public string Level { get; set; }
            public string Category { get; set; }
        }

        public TeacherScheduleWindow(List<TeacherIdentifier> teachers)
        {
            InitializeComponent();
            _teachers = teachers;
            _manager = TeacherScheduleManager.Load();
            
            LoadCustomDateFilters();
            
            // Default to current week (Monday to Friday)
            int diff = (7 + (DateTime.Now.DayOfWeek - DayOfWeek.Monday)) % 7;
            dpStart.SelectedDate = DateTime.Now.AddDays(-1 * diff).Date;
            dpEnd.SelectedDate = dpStart.SelectedDate.Value.AddDays(4); // Friday
            
            BuildTable();
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
            _scheduleTable.Columns.Add("Teacher Name", typeof(string));
            _scheduleTable.Columns.Add("Level", typeof(string));
            _scheduleTable.Columns.Add("Category", typeof(string));

            dgSchedule.Columns.Clear();
            dgSchedule.Columns.Add(new DataGridTextColumn { Header = "Teacher Name", Binding = new System.Windows.Data.Binding("Teacher Name"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            dgSchedule.Columns.Add(new DataGridTextColumn { Header = "Level", Binding = new System.Windows.Data.Binding("Level"), IsReadOnly = true, Width = new DataGridLength(100) });

            // Generate date columns
            var statuses = new List<string> { "teach", "no teach", "exam" };
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

            PopulateData(start, end);
        }

        private void PopulateData(DateTime start, DateTime end)
        {
            _scheduleTable.Rows.Clear();
            string searchText = txtSearch.Text?.ToLower()?.Trim() ?? "";

            foreach (var teacher in _teachers.OrderBy(t => t.Name).ThenBy(t => t.Level))
            {
                if (!string.IsNullOrEmpty(searchText))
                {
                    if (!teacher.Name.ToLower().Contains(searchText) && !teacher.Level.ToLower().Contains(searchText))
                        continue;
                }

                DataRow row = _scheduleTable.NewRow();
                row["Teacher Name"] = teacher.Name;
                row["Level"] = teacher.Level;
                row["Category"] = teacher.Category ?? "Unknown";
                string key = $"{teacher.Name}_{teacher.Level}";

                bool hasSchedule = _manager.Schedules.ContainsKey(key);

                for (DateTime date = start; date <= end; date = date.AddDays(1))
                {
                    string dateStr = date.ToString("yyyy-MM-dd");
                    string status = "teach";

                    if (hasSchedule && _manager.Schedules[key].ContainsKey(dateStr))
                    {
                        status = _manager.Schedules[key][dateStr];
                    }

                    row[dateStr] = status;
                }

                _scheduleTable.Rows.Add(row);
            }

            ApplyFilters();
            txtStatus.Text = "";
        }

        private void TabCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (_scheduleTable == null) return;
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
                string searchFilter = $"([Teacher Name] LIKE '%{searchText}%' OR Level LIKE '%{searchText}%')";
                if (string.IsNullOrEmpty(finalFilter))
                    finalFilter = searchFilter;
                else
                    finalFilter += $" AND {searchFilter}";
            }
            
            _scheduleTable.DefaultView.RowFilter = finalFilter;
            dgSchedule.ItemsSource = _scheduleTable.DefaultView;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            DateTime start = dpStart.SelectedDate.Value;
            DateTime end = dpEnd.SelectedDate.Value;

            foreach (DataRow row in _scheduleTable.Rows)
            {
                string tName = row["Teacher Name"].ToString();
                string tLevel = row["Level"].ToString();
                string key = $"{tName}_{tLevel}";

                if (!_manager.Schedules.ContainsKey(key))
                {
                    _manager.Schedules[key] = new Dictionary<string, string>();
                }

                for (DateTime date = start; date <= end; date = date.AddDays(1))
                {
                    string dateStr = date.ToString("yyyy-MM-dd");
                    string status = row[dateStr]?.ToString() ?? "teach";
                    _manager.Schedules[key][dateStr] = status;
                }
            }

            _manager.Save();
            txtStatus.Text = "Saved successfully at " + DateTime.Now.ToString("HH:mm:ss");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
