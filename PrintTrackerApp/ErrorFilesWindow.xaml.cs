using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace PrintTrackerApp
{
    public class ErrorFileInfo : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get { return _isSelected; }
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string StatusFolder { get; set; } = "";
        public DateTime FailedTime { get; set; }

        private string _durationText = "";
        public string DurationText
        {
            get { return _durationText; }
            set { _durationText = value; OnPropertyChanged(nameof(DurationText)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void UpdateDuration()
        {
            TimeSpan duration = DateTime.Now - FailedTime;
            int minutes = (int)duration.TotalMinutes;
            int seconds = duration.Seconds;
            
            if (minutes > 0)
                DurationText = $"({minutes}min{seconds}sec)";
            else
                DurationText = $"({seconds}sec)";
        }
    }

    public enum ErrorWindowMode
    {
        ErrorFiles,
        SentToPrinter,
        PrintComplete,
        Processing
    }

    public partial class ErrorFilesWindow : Window
    {
        public System.Collections.Generic.List<string> MovedFiles { get; private set; } = new System.Collections.Generic.List<string>();
        private ObservableCollection<ErrorFileInfo> _errorFiles = new ObservableCollection<ErrorFileInfo>();
        private string _sourceFolderPath;
        private DispatcherTimer _timer;
        private ErrorWindowMode _mode;
        private Action<System.Collections.Generic.List<string>> _onFilesMoving;

        public ErrorFilesWindow(string sourceFolderPath, ErrorWindowMode mode = ErrorWindowMode.ErrorFiles, Action<System.Collections.Generic.List<string>> onFilesMoving = null)
        {
            InitializeComponent();
            _sourceFolderPath = sourceFolderPath;
            _mode = mode;
            _onFilesMoving = onFilesMoving;

            if (_mode == ErrorWindowMode.SentToPrinter)
            {
                this.Title = "Sent to Printer Files Manager";
                txtTitle.Text = "Manage Sent to Printer Files";
            }
            else if (_mode == ErrorWindowMode.PrintComplete)
            {
                this.Title = "Print Complete Files Manager";
                txtTitle.Text = "Manage Print Complete Files";
                btnMoveSelected.Visibility = Visibility.Collapsed;
                if (colSelect != null) colSelect.Visibility = Visibility.Collapsed;
            }
            else if (_mode == ErrorWindowMode.Processing)
            {
                this.Title = "Processing Files Manager";
                txtTitle.Text = "Manage Processing Files";
            }

            dataGridFiles.ItemsSource = _errorFiles;

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;

            LoadFiles();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _timer.Start();
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            foreach (var item in _errorFiles)
            {
                item.UpdateDuration();
            }
        }

        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox chk)
            {
                bool isChecked = chk.IsChecked ?? false;
                foreach (var file in _errorFiles)
                {
                    file.IsSelected = isChecked;
                }
            }
        }

        private void LoadFiles()
        {
            _errorFiles.Clear();
            if (chkSelectAll != null)
            {
                chkSelectAll.IsChecked = false;
            }

            if (string.IsNullOrEmpty(_sourceFolderPath) || !Directory.Exists(_sourceFolderPath))
                return;

            if (_mode == ErrorWindowMode.SentToPrinter || _mode == ErrorWindowMode.PrintComplete || _mode == ErrorWindowMode.Processing)
            {
                string targetSubfolder = _mode == ErrorWindowMode.SentToPrinter ? "Sent to Printer" : (_mode == ErrorWindowMode.Processing ? "Processing" : "Print Complete");
                string folderPath = Path.Combine(_sourceFolderPath, targetSubfolder);
                if (Directory.Exists(folderPath))
                {
                    var files = Directory.GetFiles(folderPath, "*.pdf");
                    foreach (var file in files)
                    {
                        var info = new ErrorFileInfo
                        {
                            FileName = Path.GetFileName(file),
                            FilePath = file,
                            StatusFolder = targetSubfolder,
                            FailedTime = File.GetLastWriteTime(file)
                        };
                        info.UpdateDuration();
                        _errorFiles.Add(info);
                    }
                }
            }
            else
            {
                string[] activeOrSuccessFolders = { 
                    "Storing Complete", 
                    "Print Complete",
                    "Processing", 
                    "Sent to Printer", 
                    "Printing", 
                    "Storing" 
                };

                var subdirs = Directory.GetDirectories(_sourceFolderPath);
                foreach (var folderPath in subdirs)
                {
                    string folderName = Path.GetFileName(folderPath);
                    
                    bool isExcluded = false;
                    foreach (var exclude in activeOrSuccessFolders)
                    {
                        if (folderName.Equals(exclude, StringComparison.OrdinalIgnoreCase))
                        {
                            isExcluded = true;
                            break;
                        }
                    }

                    if (!isExcluded && Directory.Exists(folderPath))
                    {
                        var files = Directory.GetFiles(folderPath, "*.pdf");
                        foreach (var file in files)
                        {
                            var info = new ErrorFileInfo
                            {
                                FileName = Path.GetFileName(file),
                                FilePath = file,
                                StatusFolder = folderName,
                                FailedTime = File.GetLastWriteTime(file)
                            };
                            info.UpdateDuration();
                            _errorFiles.Add(info);
                        }
                    }
                }
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadFiles();
        }

        private void BtnMoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = _errorFiles.Where(f => f.IsSelected).ToList();
            if (selectedFiles.Count == 0)
            {
                System.Windows.MessageBox.Show("Please select at least one file to move.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Notify MainWindow to remove history before the files are actually moved
            var fileNamesToMove = selectedFiles.Select(f => f.FileName).ToList();
            _onFilesMoving?.Invoke(fileNamesToMove);

            int successCount = 0;
            foreach (var fileInfo in selectedFiles)
            {
                try
                {
                    if (File.Exists(fileInfo.FilePath))
                    {
                        string destPath = Path.Combine(_sourceFolderPath, fileInfo.FileName);
                        
                        // If file already exists in root, append timestamp
                        if (File.Exists(destPath))
                        {
                            string name = Path.GetFileNameWithoutExtension(fileInfo.FileName);
                            string ext = Path.GetExtension(fileInfo.FileName);
                            destPath = Path.Combine(_sourceFolderPath, $"{name}_{DateTime.Now.ToString("HHmmss")}{ext}");
                        }

                        File.Move(fileInfo.FilePath, destPath);
                        MovedFiles.Add(Path.GetFileName(destPath));
                        successCount++;
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to move {fileInfo.FileName}:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            if (successCount > 0)
            {
                System.Windows.MessageBox.Show($"Successfully moved {successCount} file(s) to the main folder for reprinting.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadFiles();
            }
        }
    }
}
