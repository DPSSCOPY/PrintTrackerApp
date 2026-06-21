import re

with open("MainWindow.xaml.cs", "r", encoding="utf-8") as f:
    main_code = f.read()

with open("AutoPrintWindow.xaml.cs", "r", encoding="utf-8") as f:
    auto_code = f.read()

# Extract fields
main_fields = re.search(r"public partial class MainWindow : Window\s*\{([\s\S]*?)public MainWindow", main_code).group(1)

auto_fields_match = re.search(r"public partial class AutoPrintWindow : Window\s*\{([\s\S]*?)public AutoPrintWindow", auto_code)
if auto_fields_match:
    auto_fields = auto_fields_match.group(1).replace("private AppSettings _currentSettings;", "") # MainWindow already has _appSettings
else:
    auto_fields = ""

# Extract methods from AutoPrint
auto_methods = re.search(r"private void Window_Loaded[\s\S]*?protected override void OnClosed\([^)]+\)\s*\{[\s\S]*?base\.OnClosed\([^)]+\);\s*\}", auto_code).group(0)

# Also need manual testing methods
manual_methods_match = re.search(r"private void BtnInspectUI_Click[\s\S]*?}\s*}\s*}$", auto_code)
if manual_methods_match:
    manual_methods = manual_methods_match.group(0)
    manual_methods = re.sub(r"}\s*}\s*$", "", manual_methods) # strip trailing braces
else:
    manual_methods = ""

# Clean up AutoPrint methods to use _appSettings instead of _currentSettings
auto_methods = auto_methods.replace("_currentSettings", "_appSettings")
auto_methods = auto_methods.replace("txtStatus.", "txtAutoPrintStatus.")
auto_methods = auto_methods.replace("btnStart.", "btnStartAutoPrint.")
auto_methods = auto_methods.replace("btnStop.", "btnStopAutoPrint.")
auto_methods = auto_methods.replace("BtnStart_Click", "BtnStartAutoPrint_Click")
auto_methods = auto_methods.replace("BtnStop_Click", "BtnStopAutoPrint_Click")

# Extract the body of AutoPrintWindow's Window_Loaded.
auto_loaded_body = re.search(r"private void Window_Loaded\(object sender, RoutedEventArgs e\)\s*\{([\s\S]*?)\}\s*private void NumberValidationTextBox", auto_methods).group(1)

# Remove AutoPrint Window_Loaded from auto_methods
auto_methods = re.sub(r"private void Window_Loaded\(object sender, RoutedEventArgs e\)\s*\{[\s\S]*?\}\s*private void NumberValidationTextBox", "private void NumberValidationTextBox", auto_methods)

# Merge into MainWindow Window_Loaded
main_code = main_code.replace("UpdateVerificationPanel();", "UpdateVerificationPanel();\n\n            _autoPrintService = new AutoPrintService();\n            _autoPrintService.StatusChanged += AutoPrintService_StatusChanged;\n            _autoPrintService.FileProcessingStarted += AutoPrintService_FileProcessingStarted;\n" + auto_loaded_body)

# Merge fields
main_code = main_code.replace(main_fields, main_fields + auto_fields)

# Remove OnClosed from auto_methods as MainWindow has its own OnClosed
auto_methods = re.sub(r"protected override void OnClosed\([^)]+\)\s*\{[\s\S]*?base\.OnClosed\([^)]+\);\s*\}", "", auto_methods)

# Merge OnClosed logic into MainWindow's OnClosed
main_code = main_code.replace("base.OnClosed(e);", "if (_autoPrintService != null && _autoPrintService.IsRunning) { _autoPrintService.Stop(); }\n            base.OnClosed(e);")

# Insert methods at the end of MainWindow (before protected override void OnClosed)
insertion_point = main_code.find("protected override void OnClosed")
main_code = main_code[:insertion_point] + auto_methods + "\n" + manual_methods + "\n        " + main_code[insertion_point:]

# Add namespaces
if "using System.Windows.Input;" not in main_code:
    main_code = "using System.Windows.Input;\n" + main_code
if "using System.Text.RegularExpressions;" not in main_code:
    main_code = "using System.Text.RegularExpressions;\n" + main_code
if "using System.Windows.Automation;" not in main_code:
    main_code = "using System.Windows.Automation;\n" + main_code

with open("MainWindow.xaml.cs", "w", encoding="utf-8") as f:
    f.write(main_code)
