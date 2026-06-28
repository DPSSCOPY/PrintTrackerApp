using System.Collections.Generic;
using System.Windows;
using PrintTrackerApp.Models;

namespace PrintTrackerApp
{
    public partial class UnmatchedFilesWindow : Window
    {
        public UnmatchedFilesWindow(List<PrintJobInfo> unmatchedJobs)
        {
            InitializeComponent();
            dgUnmatched.ItemsSource = unmatchedJobs;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
