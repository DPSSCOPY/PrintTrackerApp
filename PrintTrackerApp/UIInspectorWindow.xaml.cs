using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;

namespace PrintTrackerApp
{
    public partial class UIInspectorWindow : Window
    {
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        public UIInspectorWindow()
        {
            InitializeComponent();
        }

        private async void BtnDump_Click(object sender, RoutedEventArgs e)
        {
            txtDump.Text = "Waiting for you to focus the target window...\n";
            for (int i = 5; i > 0; i--)
            {
                txtCountdown.Text = i.ToString();
                await Task.Delay(1000);
            }
            txtCountdown.Text = "Dumping...";

            IntPtr handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                txtDump.Text += "No active window found.";
                txtCountdown.Text = "";
                return;
            }

            try
            {
                AutomationElement targetWindow = AutomationElement.FromHandle(handle);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Window Found: {targetWindow.Current.Name} (Class: {targetWindow.Current.ClassName})");
                sb.AppendLine("=========================================================");
                
                DumpElement(targetWindow, sb, 0);
                
                txtDump.Text = sb.ToString();
            }
            catch (Exception ex)
            {
                txtDump.Text += $"\nError dumping UI: {ex.Message}";
            }
            finally
            {
                txtCountdown.Text = "";
            }
        }

        private void DumpElement(AutomationElement element, StringBuilder sb, int depth)
        {
            if (depth > 10) return; // Prevent infinite loops or excessively deep trees

            string indent = new string(' ', depth * 4);
            try
            {
                string name = element.Current.Name;
                string autoId = element.Current.AutomationId;
                string controlType = element.Current.ControlType.ProgrammaticName;
                string className = element.Current.ClassName;

                if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(autoId) || controlType.Contains("Button") || controlType.Contains("ComboBox") || controlType.Contains("Edit"))
                {
                    sb.AppendLine($"{indent}- Name: '{name}', AutomationId: '{autoId}', Type: {controlType}, Class: '{className}'");
                }

                AutomationElementCollection children = element.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
                foreach (AutomationElement child in children)
                {
                    DumpElement(child, sb, depth + 1);
                }
            }
            catch
            {
                // Ignore elements that throw errors during reading
            }
        }
    }
}
