using System;
using System.Collections.ObjectModel;
using System.Windows;
using PrintTrackerApp.Services;

namespace PrintTrackerApp
{
    public partial class CustomDateFiltersWindow : Window
    {
        public ObservableCollection<CustomDateFilter> Filters { get; set; }

        public CustomDateFiltersWindow(System.Collections.Generic.List<CustomDateFilter> existingFilters)
        {
            InitializeComponent();
            Filters = new ObservableCollection<CustomDateFilter>();
            Filters.CollectionChanged += Filters_CollectionChanged;
            if (existingFilters != null)
            {
                foreach(var f in existingFilters) 
                {
                    Filters.Add(new CustomDateFilter { Name = f.Name, StartDate = f.StartDate, EndDate = f.EndDate });
                }
            }
            dgFilters.ItemsSource = Filters;
            
            // Default auto-start date to the nearest Monday
            DateTime today = DateTime.Today;
            int offset = today.DayOfWeek == DayOfWeek.Sunday ? -6 : (int)DayOfWeek.Monday - (int)today.DayOfWeek;
            dpAutoStart.SelectedDate = today.AddDays(offset);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnAutoGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (dpAutoStart.SelectedDate.HasValue)
            {
                DateTime start = dpAutoStart.SelectedDate.Value.Date;
                Filters.Clear();
                
                Filters.Add(new CustomDateFilter { Name = "Week 1", StartDate = start, EndDate = start.AddDays(4) });
                Filters.Add(new CustomDateFilter { Name = "Week 2", StartDate = start.AddDays(7), EndDate = start.AddDays(11) });
                Filters.Add(new CustomDateFilter { Name = "Week 3", StartDate = start.AddDays(14), EndDate = start.AddDays(18) });
                Filters.Add(new CustomDateFilter { Name = "Week 4", StartDate = start.AddDays(21), EndDate = start.AddDays(25) });
                Filters.Add(new CustomDateFilter { Name = "Week 5", StartDate = start.AddDays(28), EndDate = start.AddDays(32) });
                Filters.Add(new CustomDateFilter { Name = "Monthly", StartDate = start, EndDate = start.AddDays(32) }); // Covers 5 weeks Mon-Fri
            }
            else
            {
                System.Windows.MessageBox.Show("Please select a start date first.", "Information", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }

        private void Filters_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.InvokeAsync(() => {
                UpdateMonthlyDateRange();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateMonthlyDateRange()
        {
            var monthlyFilter = System.Linq.Enumerable.FirstOrDefault(Filters, f => f.Name == "Monthly");
            if (monthlyFilter != null)
            {
                var weekFilters = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(Filters, f => f.Name != null && f.Name.StartsWith("Week", StringComparison.OrdinalIgnoreCase)));
                if (weekFilters.Count > 0)
                {
                    DateTime minDate = System.Linq.Enumerable.Min(weekFilters, f => f.StartDate);
                    DateTime maxDate = System.Linq.Enumerable.Max(weekFilters, f => f.EndDate);
                    if (monthlyFilter.StartDate != minDate || monthlyFilter.EndDate != maxDate)
                    {
                        monthlyFilter.StartDate = minDate;
                        monthlyFilter.EndDate = maxDate;
                        dgFilters.Items.Refresh();
                    }
                }
            }
        }

        private void dgFilters_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
        {
            Dispatcher.InvokeAsync(() => {
                UpdateMonthlyDateRange();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
