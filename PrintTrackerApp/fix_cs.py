import re

with open("MainWindow.xaml.cs", "r", encoding="utf-8") as f:
    text = f.read()

# Fix the missing txtUserId checks
text = re.sub(r'if \(string\.IsNullOrEmpty\(txtUserId\.Text\)\)\s*\{\s*System\.Windows\.MessageBox\.Show\("Please enter a Hold Print User ID\.", "Error", MessageBoxButton\.OK, MessageBoxImage\.Error\);\s*return;\s*\}', '', text)

replacement = """        private void BtnTest_InputID_Click(object sender, RoutedEventArgs e)
        {
            AutomationElement foxitWindow = GetFoxitWindow();
            if (foxitWindow == null) {
                System.Windows.MessageBox.Show("Could not find Foxit window.");
                return;
            }

            string windowTitle = foxitWindow.Current.Name;
            string rawFileName = windowTitle;
            if (rawFileName.Contains(" - Foxit")) {
                rawFileName = rawFileName.Substring(0, rawFileName.IndexOf(" - Foxit"));
            }
            if (rawFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) {
                rawFileName = rawFileName.Substring(0, rawFileName.Length - 4);
            }

            AutoPrintService.ParseDynamicFileInfo(rawFileName, "Default", 1, out string dynamicUserId, out string dynamicFileName, out int dynamicCopies);

            AutomationElement detailsWindow = FindWindowDescendant(foxitWindow, "Details", true);
            if (detailsWindow != null)
            {
                try { SetForegroundWindow(new IntPtr(detailsWindow.Current.NativeWindowHandle)); } catch {}
                
                AutomationElement userIdEdit = detailsWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1004"));
                if (userIdEdit != null)
                {
                    AutoPrintService.SetTextElement(userIdEdit, dynamicUserId);
                }

                AutomationElement fileNameEdit = detailsWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1007"));
                if (fileNameEdit != null)
                {
                    AutoPrintService.SetTextElement(fileNameEdit, dynamicFileName);
                }
            }
            else
            {
                System.Windows.MessageBox.Show("Could not find 'Details' window.");
            }
        }"""

text = re.sub(r'private void BtnTest_InputID_Click\(object sender, RoutedEventArgs e\)[\s\S]*?MessageBox\.Show\("Could not find ''Details'' window\."\);\s*\}\s*\}', replacement, text)

with open("MainWindow.xaml.cs", "w", encoding="utf-8") as f:
    f.write(text)
