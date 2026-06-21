using System.Windows;
using PrintTrackerApp.Services;

namespace PrintTrackerApp
{
    public partial class SettingsWindow : Window
    {
        public AppSettings CurrentSettings { get; private set; }

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            CurrentSettings = new AppSettings 
            { 
                PrinterIp = settings.PrinterIp,
                PrinterName = settings.PrinterName,
                CsvExportPath = settings.CsvExportPath,
                RefreshIntervalSeconds = settings.RefreshIntervalSeconds,
                SourceFolderPath = settings.SourceFolderPath,
                FoxitWindowStyle = settings.FoxitWindowStyle,
                DelayBetweenPrints = settings.DelayBetweenPrints
            };

            txtPrinterIp.Text = CurrentSettings.PrinterIp;
            txtPrinterName.Text = CurrentSettings.PrinterName;
            txtCsvPath.Text = CurrentSettings.CsvExportPath;
            txtSourcePath.Text = CurrentSettings.SourceFolderPath;

            foreach (System.Windows.Controls.ComboBoxItem item in cmbRefreshInterval.Items)
            {
                if (item.Tag.ToString() == CurrentSettings.RefreshIntervalSeconds.ToString())
                {
                    cmbRefreshInterval.SelectedItem = item;
                    break;
                }
            }

            foreach (System.Windows.Controls.ComboBoxItem item in cmbFoxitWindowStyle.Items)
            {
                if (item.Tag.ToString() == CurrentSettings.FoxitWindowStyle)
                {
                    cmbFoxitWindowStyle.SelectedItem = item;
                    break;
                }
            }

            foreach (System.Windows.Controls.ComboBoxItem item in cmbDelay.Items)
            {
                if (item.Tag.ToString() == CurrentSettings.DelayBetweenPrints.ToString())
                {
                    cmbDelay.SelectedItem = item;
                    break;
                }
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.SelectedPath = CurrentSettings.CsvExportPath;
                dialog.Description = "Select a folder to save daily CSV Print Logs";
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtCsvPath.Text = dialog.SelectedPath;
                }
            }
        }

        private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.SelectedPath = CurrentSettings.SourceFolderPath;
                dialog.Description = "Select the folder containing files to print";
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtSourcePath.Text = dialog.SelectedPath;
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            CurrentSettings.PrinterIp = txtPrinterIp.Text.Trim();
            CurrentSettings.PrinterName = txtPrinterName.Text.Trim();
            CurrentSettings.CsvExportPath = txtCsvPath.Text.Trim();
            CurrentSettings.SourceFolderPath = txtSourcePath.Text.Trim();
            
            if (cmbRefreshInterval.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem && int.TryParse(selectedItem.Tag?.ToString(), out int interval))
            {
                CurrentSettings.RefreshIntervalSeconds = interval;
            }

            if (cmbFoxitWindowStyle.SelectedItem is System.Windows.Controls.ComboBoxItem styleItem)
            {
                CurrentSettings.FoxitWindowStyle = styleItem.Tag?.ToString() ?? "Normal";
            }

            if (cmbDelay.SelectedItem is System.Windows.Controls.ComboBoxItem delayItem && int.TryParse(delayItem.Tag?.ToString(), out int delay))
            {
                CurrentSettings.DelayBetweenPrints = delay;
            }

            this.DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}
