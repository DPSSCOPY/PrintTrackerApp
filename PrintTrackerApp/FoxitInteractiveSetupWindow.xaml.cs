using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintTrackerApp.Services;

namespace PrintTrackerApp
{
    public partial class FoxitInteractiveSetupWindow : Window
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        private AppSettings _settings;
        private ObservableCollection<PrinterProfile> _printerProfiles;
        private PrinterProfile _currentProfile;
        private bool _isUpdatingUi = false;

        public FoxitInteractiveSetupWindow()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            _settings = SettingsManager.LoadSettings();
            
            // Foxit Window Names
            txtFoxitPrintWindowName.Text = _settings.FoxitPrintWindowName;
            
            // Foxit Controls
            txtFoxitPrintOkBtnId.Text = _settings.FoxitPrintOkBtnId;
            txtFoxitPropertiesBtnId.Text = _settings.FoxitPropertiesBtnId;
            txtFoxitCopiesSpinnerId.Text = _settings.FoxitCopiesSpinnerId;
            txtFoxitPagesRadioBtnId.Text = _settings.FoxitPagesRadioBtnId;
            txtFoxitShortEdgeRadioBtnId.Text = _settings.FoxitShortEdgeRadioBtnId;
            txtFoxitPagesTextBoxId.Text = _settings.FoxitPagesTextBoxId;
            txtFoxitCopiesTextBoxId.Text = _settings.FoxitCopiesTextBoxId;

            // Load Printer Profiles
            if (_settings.PrinterProfiles == null || _settings.PrinterProfiles.Count == 0)
            {
                _settings.PrinterProfiles = new System.Collections.Generic.List<PrinterProfile> { new PrinterProfile() };
            }
            
            _printerProfiles = new ObservableCollection<PrinterProfile>(_settings.PrinterProfiles);
            cmbPrinterProfiles.ItemsSource = _printerProfiles;
            
            var active = _printerProfiles.FirstOrDefault(p => p.ProfileName == _settings.ActivePrinterProfileName);
            if (active == null) active = _printerProfiles.First();
            
            cmbPrinterProfiles.SelectedItem = active;
            // The SelectionChanged event will populate the UI
        }

        private void SaveCurrentProfileFields()
        {
            if (_currentProfile == null) return;
            
            _currentProfile.ProfileName = txtProfileName.Text;
            _currentProfile.FoxitPropertiesWindowName = txtFoxitPropertiesWindowName.Text;
            _currentProfile.FoxitJobDetailsWindowName = txtFoxitJobDetailsWindowName.Text;
            
            _currentProfile.SavinDetailsBtnId = txtSavinDetailsBtnId.Text;
            _currentProfile.SavinUserIdTextBoxId = txtSavinUserIdTextBoxId.Text;
            _currentProfile.SavinFileNameTextBoxId = txtSavinFileNameTextBoxId.Text;
            _currentProfile.SavinDetailsOkBtnId = txtSavinDetailsOkBtnId.Text;
            _currentProfile.SavinPropertiesOkBtnId = txtSavinPropertiesOkBtnId.Text;
        }

        private void CmbPrinterProfiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            
            if (cmbPrinterProfiles.SelectedItem is PrinterProfile selectedProfile)
            {
                // Save current edits before switching
                SaveCurrentProfileFields();
                
                _isUpdatingUi = true;
                _currentProfile = selectedProfile;
                
                txtProfileName.Text = _currentProfile.ProfileName;
                txtFoxitPropertiesWindowName.Text = _currentProfile.FoxitPropertiesWindowName;
                txtFoxitJobDetailsWindowName.Text = _currentProfile.FoxitJobDetailsWindowName;
                
                txtSavinDetailsBtnId.Text = _currentProfile.SavinDetailsBtnId;
                txtSavinUserIdTextBoxId.Text = _currentProfile.SavinUserIdTextBoxId;
                txtSavinFileNameTextBoxId.Text = _currentProfile.SavinFileNameTextBoxId;
                txtSavinDetailsOkBtnId.Text = _currentProfile.SavinDetailsOkBtnId;
                txtSavinPropertiesOkBtnId.Text = _currentProfile.SavinPropertiesOkBtnId;
                
                _isUpdatingUi = false;
            }
        }

        private void TxtProfileName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUi || _currentProfile == null) return;
            _currentProfile.ProfileName = txtProfileName.Text;
            cmbPrinterProfiles.Items.Refresh(); // Refresh dropdown display
        }

        private void BtnAddPrinter_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentProfileFields();
            
            var newProfile = new PrinterProfile 
            { 
                ProfileName = $"New Printer {_printerProfiles.Count + 1}"
            };
            
            _printerProfiles.Add(newProfile);
            cmbPrinterProfiles.SelectedItem = newProfile;
            txtStatus.Text = "Added new printer profile.";
            txtStatus.Foreground = System.Windows.Media.Brushes.Blue;
        }

        private void BtnDeletePrinter_Click(object sender, RoutedEventArgs e)
        {
            if (_printerProfiles.Count <= 1)
            {
                System.Windows.MessageBox.Show("You must have at least one printer profile.", "Cannot Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (_currentProfile != null)
            {
                var result = System.Windows.MessageBox.Show($"Are you sure you want to delete profile '{_currentProfile.ProfileName}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _printerProfiles.Remove(_currentProfile);
                    _currentProfile = null; // Important before selection changes automatically
                    cmbPrinterProfiles.SelectedIndex = 0;
                    txtStatus.Text = "Deleted printer profile.";
                    txtStatus.Foreground = System.Windows.Media.Brushes.Blue;
                }
            }
        }

        // --- Foxit Capture Events ---
        private async void BtnCapturePrintWindow_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtFoxitPrintWindowName, true);
        private async void BtnCapturePrintOkBtn_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtFoxitPrintOkBtnId, false);
        private async void BtnCapturePropertiesBtn_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtFoxitPropertiesBtnId, false);
        private async void BtnCaptureCopiesSpinner_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtFoxitCopiesSpinnerId, false);
        private async void BtnCapturePagesRadioBtn_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtFoxitPagesRadioBtnId, false);
        private async void BtnCaptureShortEdgeRadioBtn_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtFoxitShortEdgeRadioBtnId, false);
        private async void BtnCapturePagesTextBox_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtFoxitPagesTextBoxId, false);
        private async void BtnCaptureCopiesTextBox_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtFoxitCopiesTextBoxId, false);

        // --- Savin Capture Events (Active Profile) ---
        private async void BtnCapturePropertiesWindow_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtFoxitPropertiesWindowName, true);
        private async void BtnCaptureJobDetailsWindow_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtFoxitJobDetailsWindowName, true);
        private async void BtnCaptureSavinDetailsBtn_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtSavinDetailsBtnId, false);
        private async void BtnCaptureSavinUserIdTextBox_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtSavinUserIdTextBoxId, false);
        private async void BtnCaptureSavinFileNameTextBox_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtSavinFileNameTextBoxId, false);
        private async void BtnCaptureSavinDetailsOkBtn_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtSavinDetailsOkBtnId, false);
        private async void BtnCaptureSavinPropertiesOkBtn_Click(object sender, RoutedEventArgs e) => await CaptureElementAsync(txtSavinPropertiesOkBtnId, false);

        private async Task CaptureElementAsync(System.Windows.Controls.TextBox targetTextBox, bool captureWindowName)
        {
            txtStatus.Foreground = System.Windows.Media.Brushes.Red;
            for (int i = 5; i > 0; i--)
            {
                txtStatus.Text = $"Move mouse to target in {i} seconds...";
                await Task.Delay(1000);
            }

            txtStatus.Text = "Capturing...";
            await Task.Delay(200); // Give a tiny moment

            try
            {
                if (GetCursorPos(out POINT pt))
                {
                    AutomationElement element = AutomationElement.FromPoint(new System.Windows.Point(pt.X, pt.Y));
                    
                    if (element != null)
                    {
                        if (captureWindowName)
                        {
                            AutomationElement window = element;
                            while (window != null && window.Current.ControlType != ControlType.Window)
                            {
                                window = TreeWalker.ControlViewWalker.GetParent(window);
                            }
                            
                            if (window != null && !string.IsNullOrEmpty(window.Current.Name))
                            {
                                targetTextBox.Text = window.Current.Name;
                                txtStatus.Text = $"Successfully captured Window Name: '{window.Current.Name}'";
                            }
                            else if (!string.IsNullOrEmpty(element.Current.Name))
                            {
                                targetTextBox.Text = element.Current.Name;
                                txtStatus.Text = $"Captured Element Name: '{element.Current.Name}'";
                            }
                            else
                            {
                                txtStatus.Text = "Error: Element has no Name property.";
                            }
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(element.Current.AutomationId))
                            {
                                targetTextBox.Text = element.Current.AutomationId;
                                txtStatus.Text = $"Successfully captured Automation ID: '{element.Current.AutomationId}'";
                            }
                            else
                            {
                                txtStatus.Text = "Error: Element has no Automation ID. Try capturing its Name instead.";
                            }
                        }
                        txtStatus.Foreground = System.Windows.Media.Brushes.Green;
                    }
                    else
                    {
                        txtStatus.Text = "Failed to find UI element at cursor position.";
                    }
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Error during capture: {ex.Message}";
                txtStatus.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            txtFoxitPrintWindowName.Text = "Print";
            txtFoxitPrintOkBtnId.Text = "1";
            txtFoxitPropertiesBtnId.Text = "10380";
            txtFoxitCopiesSpinnerId.Text = "10590";
            txtFoxitPagesRadioBtnId.Text = "10433";
            txtFoxitShortEdgeRadioBtnId.Text = "10431";
            txtFoxitPagesTextBoxId.Text = "10415";
            txtFoxitCopiesTextBoxId.Text = "10408";

            txtFoxitPropertiesWindowName.Text = "Properties";
            txtFoxitJobDetailsWindowName.Text = "Job Type Details";
            txtSavinDetailsBtnId.Text = "1018";
            txtSavinUserIdTextBoxId.Text = "1004";
            txtSavinFileNameTextBoxId.Text = "1007";
            txtSavinDetailsOkBtnId.Text = "1";
            txtSavinPropertiesOkBtnId.Text = "1";

            txtStatus.Text = "Reset current values to default.";
            txtStatus.Foreground = System.Windows.Media.Brushes.Blue;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentProfileFields();
            
            _settings.FoxitPrintWindowName = txtFoxitPrintWindowName.Text;
            _settings.FoxitPrintOkBtnId = txtFoxitPrintOkBtnId.Text;
            _settings.FoxitPropertiesBtnId = txtFoxitPropertiesBtnId.Text;
            _settings.FoxitCopiesSpinnerId = txtFoxitCopiesSpinnerId.Text;
            _settings.FoxitPagesRadioBtnId = txtFoxitPagesRadioBtnId.Text;
            _settings.FoxitShortEdgeRadioBtnId = txtFoxitShortEdgeRadioBtnId.Text;
            _settings.FoxitPagesTextBoxId = txtFoxitPagesTextBoxId.Text;
            _settings.FoxitCopiesTextBoxId = txtFoxitCopiesTextBoxId.Text;

            _settings.PrinterProfiles = _printerProfiles.ToList();
            if (_currentProfile != null)
            {
                _settings.ActivePrinterProfileName = _currentProfile.ProfileName;
            }

            SettingsManager.SaveSettings(_settings);
            DialogResult = true;
            Close();
        }
    }
}
