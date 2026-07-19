using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace PrintTrackerApp
{
    public class ExportPeriodItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string Name { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public partial class ExportPeriodSelectionWindow : Window
    {
        public ObservableCollection<ExportPeriodItem> Periods { get; set; }
        public List<string> SelectedPeriods { get; private set; } = new List<string>();
        private bool _isUpdatingAll = false;

        public ExportPeriodSelectionWindow(List<string> periodNames)
        {
            InitializeComponent();
            Periods = new ObservableCollection<ExportPeriodItem>(
                periodNames.Select(name => new ExportPeriodItem { Name = name, IsSelected = true })
            );

            foreach (var item in Periods)
            {
                item.PropertyChanged += Item_PropertyChanged;
            }

            lstPeriods.ItemsSource = Periods;
            UpdateSelectAllState();
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ExportPeriodItem.IsSelected))
            {
                UpdateSelectAllState();
            }
        }

        private void UpdateSelectAllState()
        {
            if (_isUpdatingAll) return;

            chkSelectAll.Checked -= ChkSelectAll_Checked;
            chkSelectAll.Unchecked -= ChkSelectAll_Unchecked;

            bool allSelected = Periods.All(p => p.IsSelected);
            bool noneSelected = Periods.All(p => !p.IsSelected);

            if (allSelected)
            {
                chkSelectAll.IsChecked = true;
            }
            else if (noneSelected)
            {
                chkSelectAll.IsChecked = false;
            }
            else
            {
                chkSelectAll.IsChecked = null; // Indeterminate
            }

            chkSelectAll.Checked += ChkSelectAll_Checked;
            chkSelectAll.Unchecked += ChkSelectAll_Unchecked;
        }

        private void ChkSelectAll_Checked(object sender, RoutedEventArgs e)
        {
            _isUpdatingAll = true;
            foreach (var p in Periods)
            {
                p.IsSelected = true;
            }
            _isUpdatingAll = false;
        }

        private void ChkSelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            _isUpdatingAll = true;
            foreach (var p in Periods)
            {
                p.IsSelected = false;
            }
            _isUpdatingAll = false;
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            SelectedPeriods = Periods.Where(p => p.IsSelected).Select(p => p.Name).ToList();
            if (SelectedPeriods.Count == 0)
            {
                System.Windows.MessageBox.Show("សូមជ្រើសរើសយ៉ាងហោចណាស់សប្ដាហ៍មួយដើម្បី Export។\nPlease select at least one period to export.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
