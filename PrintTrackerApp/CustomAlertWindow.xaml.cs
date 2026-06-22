using System.Windows;
using System.Windows.Media;

namespace PrintTrackerApp
{
    public enum AlertType
    {
        Information,
        Warning,
        Error,
        SentToPrinter,
        StoringCompleted,
        PrintCompleted
    }

    public partial class CustomAlertWindow : Window
    {
        public CustomAlertWindow(string title, string message, AlertType type)
        {
            InitializeComponent();
            
            txtTitle.Text = title;
            txtMessage.Text = message;

            switch (type)
            {
                case AlertType.Information:
                    borderHeader.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B82F6")); // Blue
                    btnOK.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3B82F6"));
                    txtIcon.Text = "ℹ️";
                    break;
                case AlertType.Warning:
                    borderHeader.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B")); // Amber
                    btnOK.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F59E0B"));
                    txtIcon.Text = "⚠️";
                    break;
                case AlertType.Error:
                    borderHeader.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444")); // Red
                    btnOK.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EF4444"));
                    txtIcon.Text = "❌";
                    break;
                case AlertType.SentToPrinter:
                    borderHeader.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0EA5E9")); // Cyan
                    btnOK.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0EA5E9"));
                    txtIcon.Text = "🖨️";
                    break;
                case AlertType.StoringCompleted:
                    borderHeader.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B5CF6")); // Purple
                    btnOK.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B5CF6"));
                    txtIcon.Text = "📁";
                    break;
                case AlertType.PrintCompleted:
                    borderHeader.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981")); // Green
                    btnOK.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#10B981"));
                    txtIcon.Text = "✅";
                    break;
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        public static void ShowMessage(string title, string message, AlertType type)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var window = new CustomAlertWindow(title, message, type);
                window.ShowDialog();
            });
        }
    }
}
