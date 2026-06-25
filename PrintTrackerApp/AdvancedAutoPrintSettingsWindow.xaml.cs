using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;
using PrintTrackerApp.Services;

namespace PrintTrackerApp
{
    public partial class AdvancedAutoPrintSettingsWindow : Window
    {
        private AppSettings _appSettings;

        public AdvancedAutoPrintSettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();
            _appSettings = currentSettings;

            // Load settings into UI
            chkSkipBlankPage.IsChecked = _appSettings.SkipBlankPage;
            chkEnableBatchPrinting.IsChecked = _appSettings.EnableBatchPrinting;
            txtBatchSize.Text = _appSettings.BatchSize.ToString();
            
            chkEnableUiStepDelay.IsChecked = _appSettings.EnableUiStepDelay;
            txtUiStepDelay.Text = _appSettings.UiStepDelayMs.ToString();
            txtUiStepDelay.IsEnabled = _appSettings.EnableUiStepDelay;

            txtDelayBetweenPrints.Text = _appSettings.DelayBetweenPrints.ToString();
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void ChkEnableUiStepDelay_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (txtUiStepDelay != null)
            {
                txtUiStepDelay.IsEnabled = chkEnableUiStepDelay.IsChecked ?? false;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _appSettings.SkipBlankPage = chkSkipBlankPage.IsChecked ?? false;
            _appSettings.EnableBatchPrinting = chkEnableBatchPrinting.IsChecked ?? false;
            
            if (int.TryParse(txtBatchSize.Text, out int batchSize))
            {
                // Ensure batch size is at least 1
                _appSettings.BatchSize = batchSize > 0 ? batchSize : 1;
            }

            _appSettings.EnableUiStepDelay = chkEnableUiStepDelay.IsChecked ?? false;

            if (int.TryParse(txtUiStepDelay.Text, out int uiStepDelay))
            {
                // Ensure reasonable delay to prevent freezing (min 50ms)
                _appSettings.UiStepDelayMs = uiStepDelay < 50 ? 50 : uiStepDelay;
            }

            if (int.TryParse(txtDelayBetweenPrints.Text, out int delayPrints))
            {
                _appSettings.DelayBetweenPrints = delayPrints < 0 ? 0 : delayPrints;
            }

            SettingsManager.SaveSettings(_appSettings);
            
            this.DialogResult = true;
            this.Close();
        }
    }
}
