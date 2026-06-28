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
                DelayBetweenPrints = settings.DelayBetweenPrints,
                EnablePriority1 = settings.EnablePriority1,
                Priority1Prefixes = settings.Priority1Prefixes,
                EnablePriority2 = settings.EnablePriority2,
                Priority2Prefixes = settings.Priority2Prefixes,
                EnablePriority3 = settings.EnablePriority3,
                Priority3Prefixes = settings.Priority3Prefixes,
                TelegramBotUrl = settings.TelegramBotUrl,
                TelegramBotToken = settings.TelegramBotToken,
                TelegramChatId = settings.TelegramChatId,
                DailyReportTime = settings.DailyReportTime,
                NotifySentToPrinter = settings.NotifySentToPrinter,
                NotifyStoringCompleted = settings.NotifyStoringCompleted,
                NotifyPrintCompleted = settings.NotifyPrintCompleted,
                CustomDateFilters = settings.CustomDateFilters != null ? new System.Collections.Generic.List<Services.CustomDateFilter>(settings.CustomDateFilters) : new System.Collections.Generic.List<Services.CustomDateFilter>()
            };

            txtPrinterIp.Text = CurrentSettings.PrinterIp;
            txtPrinterName.Text = CurrentSettings.PrinterName;
            txtCsvPath.Text = CurrentSettings.CsvExportPath;
            txtSourcePath.Text = CurrentSettings.SourceFolderPath;

            chkPriority1.IsChecked = CurrentSettings.EnablePriority1;
            txtPriority1.Text = CurrentSettings.Priority1Prefixes;
            chkPriority2.IsChecked = CurrentSettings.EnablePriority2;
            txtPriority2.Text = CurrentSettings.Priority2Prefixes;
            chkPriority3.IsChecked = CurrentSettings.EnablePriority3;
            txtPriority3.Text = CurrentSettings.Priority3Prefixes;

            txtTelegramBotUrl.Text = CurrentSettings.TelegramBotUrl;
            txtTelegramToken.Text = CurrentSettings.TelegramBotToken;
            txtTelegramChatId.Text = CurrentSettings.TelegramChatId;
            txtDailyReportTime.Text = CurrentSettings.DailyReportTime;
            chkNotifySentToPrinter.IsChecked = CurrentSettings.NotifySentToPrinter;
            chkNotifyStoringCompleted.IsChecked = CurrentSettings.NotifyStoringCompleted;
            chkNotifyPrintCompleted.IsChecked = CurrentSettings.NotifyPrintCompleted;

            chkAutoShutdown.IsChecked = CurrentSettings.EnableAutoShutdown;
            rbShutdownAfterPrint.IsChecked = CurrentSettings.AutoShutdownMode == 0;
            rbShutdownAtTime.IsChecked = CurrentSettings.AutoShutdownMode == 1;
            txtShutdownDelay.Text = CurrentSettings.AutoShutdownDelayMinutes.ToString();
            txtShutdownTime.Text = CurrentSettings.AutoShutdownTime;
            pnlAutoShutdownOptions.IsEnabled = CurrentSettings.EnableAutoShutdown;

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
            
            CurrentSettings.EnablePriority1 = chkPriority1.IsChecked ?? false;
            CurrentSettings.Priority1Prefixes = txtPriority1.Text.Trim();
            CurrentSettings.EnablePriority2 = chkPriority2.IsChecked ?? false;
            CurrentSettings.Priority2Prefixes = txtPriority2.Text.Trim();
            CurrentSettings.EnablePriority3 = chkPriority3.IsChecked ?? false;
            CurrentSettings.Priority3Prefixes = txtPriority3.Text.Trim();
            
            CurrentSettings.TelegramBotUrl = txtTelegramBotUrl.Text.Trim();
            CurrentSettings.TelegramBotToken = txtTelegramToken.Text.Trim();
            CurrentSettings.TelegramChatId = txtTelegramChatId.Text.Trim();
            CurrentSettings.DailyReportTime = txtDailyReportTime.Text.Trim();
            CurrentSettings.NotifySentToPrinter = chkNotifySentToPrinter.IsChecked ?? true;
            CurrentSettings.NotifyStoringCompleted = chkNotifyStoringCompleted.IsChecked ?? true;
            CurrentSettings.NotifyPrintCompleted = chkNotifyPrintCompleted.IsChecked ?? true;
            
            CurrentSettings.EnableAutoShutdown = chkAutoShutdown.IsChecked ?? false;
            CurrentSettings.AutoShutdownMode = (rbShutdownAtTime.IsChecked == true) ? 1 : 0;
            if (int.TryParse(txtShutdownDelay.Text.Trim(), out int minutes))
            {
                CurrentSettings.AutoShutdownDelayMinutes = minutes;
            }
            CurrentSettings.AutoShutdownTime = txtShutdownTime.Text.Trim();

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
            this.Close();
        }

        private void chkAutoShutdown_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (pnlAutoShutdownOptions != null)
            {
                pnlAutoShutdownOptions.IsEnabled = chkAutoShutdown.IsChecked ?? false;
            }
        }

        private void BtnFoxitUISettings_Click(object sender, RoutedEventArgs e)
        {
            var foxitUiWindow = new FoxitInteractiveSetupWindow();
            foxitUiWindow.Owner = this;
            foxitUiWindow.ShowDialog();
            
            // Reload settings so the updated Foxit IDs are immediately available
            var updatedSettings = SettingsManager.LoadSettings();
            CurrentSettings.FoxitPrintWindowName = updatedSettings.FoxitPrintWindowName;
            CurrentSettings.FoxitPrintOkBtnId = updatedSettings.FoxitPrintOkBtnId;
            CurrentSettings.FoxitPropertiesBtnId = updatedSettings.FoxitPropertiesBtnId;
            CurrentSettings.FoxitCopiesSpinnerId = updatedSettings.FoxitCopiesSpinnerId;
            CurrentSettings.FoxitPagesRadioBtnId = updatedSettings.FoxitPagesRadioBtnId;
            CurrentSettings.FoxitShortEdgeRadioBtnId = updatedSettings.FoxitShortEdgeRadioBtnId;
            CurrentSettings.FoxitLongEdgeRadioBtnId = updatedSettings.FoxitLongEdgeRadioBtnId;
            CurrentSettings.FoxitPagesTextBoxId = updatedSettings.FoxitPagesTextBoxId;
            CurrentSettings.FoxitCopiesTextBoxId = updatedSettings.FoxitCopiesTextBoxId;

            CurrentSettings.PrinterProfiles = updatedSettings.PrinterProfiles;
            CurrentSettings.ActivePrinterProfileName = updatedSettings.ActivePrinterProfileName;
        }

        private async void BtnTestTelegram_Click(object sender, RoutedEventArgs e)
        {
            btnTestTelegram.IsEnabled = false;
            string botUrl = string.IsNullOrWhiteSpace(txtTelegramBotUrl.Text) ? "https://api.telegram.org/bot" : txtTelegramBotUrl.Text.Trim();
            string botToken = txtTelegramToken.Text.Trim();
            string chatIdsStr = txtTelegramChatId.Text.Trim();

            if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatIdsStr))
            {
                System.Windows.MessageBox.Show("Please enter both Bot Token and Chat ID.", "Missing Info", MessageBoxButton.OK, MessageBoxImage.Warning);
                btnTestTelegram.IsEnabled = true;
                return;
            }

            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    string url = $"{botUrl}{botToken}/sendMessage";
                    var chatIds = chatIdsStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    bool success = false;

                    foreach (var chatId in chatIds)
                    {
                        string trimmedId = chatId.Trim();
                        if (string.IsNullOrEmpty(trimmedId)) continue;

                        var payload = new
                        {
                            chat_id = trimmedId,
                            text = "👋 *Test Message* \nThis is a test message from Print Tracker App Settings. If you receive this, your setup is correct!",
                            parse_mode = "Markdown"
                        };
                        
                        string json = System.Text.Json.JsonSerializer.Serialize(payload);
                        var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                        var response = await client.PostAsync(url, content);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            success = true;
                        }
                    }

                    if (success)
                    {
                        System.Windows.MessageBox.Show("Test message sent successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Failed to send test message. Please verify your Bot Token and Chat ID.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error sending message: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnTestTelegram.IsEnabled = true;
            }
        }
    }
}
