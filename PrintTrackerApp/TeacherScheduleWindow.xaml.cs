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

        private UndoManager _undoManager = new UndoManager();
        private UndoBatch _currentUndoBatch;
        private System.Windows.Threading.DispatcherTimer _autoScrollTimer;
        private ScrollViewer _scrollViewer;

        private bool _isDraggingFill = false;
        private DataGridCellInfo _fillStartCellInfo;
        private object _fillSourceValue;
        private int _fillStartRowIdx = -1;
        private int _fillStartColIdx = -1;

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

        public TeacherScheduleWindow(List<TeacherIdentifier> teachers)
        {
            InitializeComponent();
            _teachers = teachers;
            _manager = TeacherScheduleManager.Load();
            
            LoadCustomDateFilters();
            
            int diff = (7 + (DateTime.Now.DayOfWeek - DayOfWeek.Monday)) % 7;
            dpStart.SelectedDate = DateTime.Now.AddDays(-1 * diff).Date;
            dpEnd.SelectedDate = dpStart.SelectedDate.Value.AddDays(4);
            
            _autoScrollTimer = new System.Windows.Threading.DispatcherTimer();
            _autoScrollTimer.Interval = TimeSpan.FromMilliseconds(50);
            _autoScrollTimer.Tick += AutoScrollTimer_Tick;

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
            
            _scheduleTable.Columns.Add("Teacher Name", typeof(string));
            _scheduleTable.Columns.Add("Level", typeof(string));
            _scheduleTable.Columns.Add("Category", typeof(string));

            dgSchedule.Columns.Clear();
            dgSchedule.Columns.Add(new DataGridTextColumn { Header = "Teacher Name", Binding = new System.Windows.Data.Binding("Teacher Name"), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
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
                    string status = row[dateStr]?.ToString() ?? "Teach";
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
