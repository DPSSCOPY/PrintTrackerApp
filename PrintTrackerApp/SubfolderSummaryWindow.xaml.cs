using System;
using System.Windows;
using System.Windows.Input;

namespace PrintTrackerApp
{
    public partial class SubfolderSummaryWindow : Window
    {
        public SubfolderSummaryWindow(string summary)
        {
            InitializeComponent();
            txtSummary.Text = summary;
        }

        private void BorderHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BtnShowDuplicates_Click(object sender, RoutedEventArgs e)
        {
            var owner = this.Owner;
            this.Close(); // Close the summary window so it doesn't block the duplicate window

            // Open duplicate files window
            var duplicateWindow = new DuplicateFilesWindow();
            duplicateWindow.Owner = owner;
            duplicateWindow.ShowDialog();
        }
    }
}
