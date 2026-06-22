using System.Windows;

namespace PrintTrackerApp
{
    public partial class PriorityConfirmWindow : Window
    {
        public PriorityConfirmWindow(int completedPriorityLevel)
        {
            InitializeComponent();
            
            if (completedPriorityLevel > 0)
            {
                txtMessage.Text = $"Priority {completedPriorityLevel} ត្រូវបានព្រីនរួចរាល់ហើយ!\n\nតើអ្នកចង់បន្តព្រីនឯកសារដែលនៅសល់ (Priority បន្តបន្ទាប់ ឫ ឯកសារធម្មតា) ដែរឬទេ?";
            }
            else
            {
                txtMessage.Text = "មិនមានឯកសារ Priority ត្រូវព្រីនទេ!\n\nតើអ្នកចង់បន្តព្រីនឯកសារធម្មតាដែរឬទេ?";
            }
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}
