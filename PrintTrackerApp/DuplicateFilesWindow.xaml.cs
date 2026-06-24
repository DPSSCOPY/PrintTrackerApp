using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using PrintTrackerApp.Services;

namespace PrintTrackerApp
{
    public class DuplicateFileItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected 
        { 
            get => _isSelected; 
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); } 
        }
        public string FileName { get; set; } = "";
        public string Subfolder { get; set; } = "";
        public string FullPath { get; set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class DuplicateFilesWindow : Window
    {
        public ObservableCollection<DuplicateFileItem> DuplicateFiles { get; set; }

        public DuplicateFilesWindow()
        {
            InitializeComponent();
            DuplicateFiles = new ObservableCollection<DuplicateFileItem>();
            dgDuplicates.ItemsSource = DuplicateFiles;
            LoadDuplicateFiles();
        }

        private void LoadDuplicateFiles()
        {
            DuplicateFiles.Clear();
            var settings = SettingsManager.LoadSettings();
            if (string.IsNullOrWhiteSpace(settings.SourceFolderPath) || !Directory.Exists(settings.SourceFolderPath))
                return;

            var subdirs = Directory.GetDirectories(settings.SourceFolderPath);
            foreach (var dir in subdirs)
            {
                try
                {
                    string dirName = Path.GetFileName(dir);
                    var pdfFiles = Directory.GetFiles(dir, "*.pdf");
                    foreach (var f in pdfFiles)
                    {
                        string name = Path.GetFileNameWithoutExtension(f);
                        if (Regex.IsMatch(name, @"_(\d{6})$"))
                        {
                            DuplicateFiles.Add(new DuplicateFileItem
                            {
                                IsSelected = false,
                                FileName = Path.GetFileName(f),
                                Subfolder = dirName,
                                FullPath = f
                            });
                        }
                    }
                }
                catch { }
            }
        }

        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as System.Windows.Controls.CheckBox;
            bool isChecked = checkBox?.IsChecked ?? false;
            foreach (var item in DuplicateFiles)
            {
                item.IsSelected = isChecked;
            }
        }

        private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = DuplicateFiles.Where(x => x.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                System.Windows.MessageBox.Show("Please select at least one duplicate file to delete.", "Delete", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = System.Windows.MessageBox.Show($"Are you sure you want to permanently delete {selectedItems.Count} file(s)?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                int deletedCount = 0;
                foreach (var item in selectedItems)
                {
                    try
                    {
                        if (File.Exists(item.FullPath))
                        {
                            File.Delete(item.FullPath);
                            deletedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Failed to delete {item.FileName}:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                System.Windows.MessageBox.Show($"Successfully deleted {deletedCount} duplicate file(s).", "Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadDuplicateFiles(); // Refresh list
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
