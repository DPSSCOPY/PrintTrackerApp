using System.Windows;
using PrintTrackerApp.Services;

namespace PrintTrackerApp
{
    public partial class NotificationsHistoryWindow : Window
    {
        private AppSettings _settings;

        public NotificationsHistoryWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            RefreshList();
        }

        private void RefreshList()
        {
            lvNotifications.ItemsSource = null;
            lvNotifications.ItemsSource = _settings.Notifications;
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show("Are you sure you want to clear all notification history?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _settings.Notifications.Clear();
                SettingsManager.SaveSettings(_settings);
                RefreshList();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
