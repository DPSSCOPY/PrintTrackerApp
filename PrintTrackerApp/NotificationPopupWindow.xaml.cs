using System.Windows;

namespace PrintTrackerApp
{
    public partial class NotificationPopupWindow : Window
    {
        public NotificationPopupWindow(string message = "All files in your folder have been successfully printed and moved to Complete Print.")
        {
            InitializeComponent();
            txtMessage.Text = message;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
