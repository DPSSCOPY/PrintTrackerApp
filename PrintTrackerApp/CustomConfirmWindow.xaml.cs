using System.Windows;
using System.Windows.Media;

namespace PrintTrackerApp
{
    public partial class CustomConfirmWindow : Window
    {
        public bool Result { get; private set; } = false;

        public CustomConfirmWindow(string title, string message, AlertType type)
        {
            InitializeComponent();
            
            txtTitle.Text = title;
            txtMessage.Text = message;

            switch (type)
            {
                case AlertType.Information:
                    borderHeader.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B82F6")); // Blue
                    txtIcon.Text = "ℹ️";
                    break;
                case AlertType.Warning:
                    borderHeader.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B")); // Amber
                    txtIcon.Text = "⚠️";
                    break;
                case AlertType.Error:
                    borderHeader.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444")); // Red
                    txtIcon.Text = "❌";
                    break;
            }
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            this.DialogResult = true;
            this.Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            this.DialogResult = false;
            this.Close();
        }

        public static bool ShowConfirm(string title, string message, AlertType type = AlertType.Warning)
        {
            bool result = false;
            if (System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                var window = new CustomConfirmWindow(title, message, type);
                window.ShowDialog();
                result = window.Result;
            }
            else
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var window = new CustomConfirmWindow(title, message, type);
                    window.ShowDialog();
                    result = window.Result;
                });
            }
            return result;
        }
    }
}
