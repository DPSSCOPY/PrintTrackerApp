using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using AutoUpdaterDotNET;

namespace PrintTrackerApp
{
    public partial class UpdateWindow : Window
    {
        private UpdateInfoEventArgs _args;
        public int DelayMinutes { get; private set; } = 0;

        public UpdateWindow(UpdateInfoEventArgs args)
        {
            InitializeComponent();
            _args = args;
            
            txtVersionInfo.Text = $"Version {args.CurrentVersion} is available. (You have {args.InstalledVersion})";
            LoadReleaseNotes();
        }

        private async void LoadReleaseNotes()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    // For now we'll try to fetch ReleaseNotes.txt from our repo, or default text
                    string notesUrl = "https://raw.githubusercontent.com/DPSSCOPY/PrintTrackerApp/main/ReleaseNotes.txt";
                    string notes = await client.GetStringAsync(notesUrl);
                    if (string.IsNullOrWhiteSpace(notes))
                    {
                        txtReleaseNotes.Text = "Bug fixes and performance improvements.";
                    }
                    else
                    {
                        txtReleaseNotes.Text = notes;
                    }
                }
            }
            catch (Exception)
            {
                txtReleaseNotes.Text = "Bug fixes and performance improvements.";
            }
        }

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AutoUpdater.DownloadUpdate(_args))
                {
                    System.Windows.Application.Current.Shutdown();
                }
            }
            catch (Exception exception)
            {
                System.Windows.MessageBox.Show(exception.Message, exception.GetType().ToString(), MessageBoxButton.OK, MessageBoxImage.Error);
                this.DialogResult = false;
                this.Close();
            }
        }

        private void BtnAskLater_Click(object sender, RoutedEventArgs e)
        {
            if (cmbRemindTime.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int mins))
            {
                DelayMinutes = mins;
            }
            else
            {
                DelayMinutes = 30; // fallback
            }

            this.DialogResult = false;
            this.Close();
        }
    }
}
