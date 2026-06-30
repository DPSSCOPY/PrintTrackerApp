using System.Windows;
using System.Windows.Input;

namespace PrintTrackerApp
{
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; } = string.Empty;

        public InputDialog(string title = "Input")
        {
            InitializeComponent();
            this.Title = title;
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            Confirm();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }

        private void txtInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Confirm();
            }
        }

        private void Confirm()
        {
            InputText = txtInput.Text.Trim();
            if (!string.IsNullOrEmpty(InputText))
            {
                this.DialogResult = true;
            }
            else
            {
                System.Windows.MessageBox.Show(this, "Please enter a valid name.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void txtInput_Loaded(object sender, RoutedEventArgs e)
        {
            txtInput.Focus();
        }
    }
}
