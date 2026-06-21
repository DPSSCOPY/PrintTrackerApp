import re

with open("MainWindow.xaml", "r", encoding="utf-8") as f:
    text = f.read()

# Remove UI elements
text = re.sub(r'<StackPanel Grid\.Row="2" Margin="0,0,0,10">[\s\S]*?</StackPanel>\s*<StackPanel Grid\.Row="3" Margin="0,0,0,10">[\s\S]*?</StackPanel>', '', text)
text = text.replace('Grid.Row="4"', 'Grid.Row="2"')

with open("MainWindow.xaml", "w", encoding="utf-8") as f:
    f.write(text)

with open("MainWindow.xaml.cs", "r", encoding="utf-8") as f:
    cs = f.read()

cs = re.sub(r'txtUserId\.Text = .*?;\s*', '', cs)
cs = re.sub(r'txtCopies\.Text = .*?;\s*', '', cs)
cs = re.sub(r'_appSettings\.HoldPrintUserId = .*?;\s*', '', cs)
cs = re.sub(r'if \(int\.TryParse\(txtCopies\.Text, out int copies\)\)\s*\{\s*_appSettings\.AutoPrintCopies = copies;\s*\}\s*', '', cs)
cs = re.sub(r'if \(string\.IsNullOrEmpty\(txtUserId\.Text\)\)\s*\{\s*System\.Windows\.MessageBox\.Show\("Please enter a User ID for Hold Print\."\);\s*return;\s*\}\s*', '', cs)
cs = cs.replace('_autoPrintService.Start(txtWatchFolder.Text, txtFoxitPath.Text, txtUserId.Text, int.Parse(txtCopies.Text));', '_autoPrintService.Start(txtWatchFolder.Text, txtFoxitPath.Text, "Default", 1);')
cs = re.sub(r'txtUserId\.IsEnabled = .*?;\s*', '', cs)
cs = re.sub(r'txtCopies\.IsEnabled = .*?;\s*', '', cs)

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

cs = re.sub(r'private void BtnTest_InputID_Click\(object sender, RoutedEventArgs e\)[\s\S]*?MessageBox\.Show\("Could not find ''Details'' window\."\);\s*\}\s*\}', replacement, cs)

with open("MainWindow.xaml.cs", "w", encoding="utf-8") as f:
    f.write(cs)
