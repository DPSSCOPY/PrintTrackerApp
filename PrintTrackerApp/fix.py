import re

with open("MainWindow.xaml.cs", "r", encoding="utf-8") as f:
    text = f.read()

# Add System.IO
if "using System.IO;" not in text:
    text = "using System.IO;\n" + text

# Remove all injected blocks:
injected_block = r"""\s*_autoPrintService = new AutoPrintService\(\);
\s*_autoPrintService\.StatusChanged \+= AutoPrintService_StatusChanged;
\s*_autoPrintService\.FileProcessingStarted \+= AutoPrintService_FileProcessingStarted;

\s*// Load settings
\s*_appSettings = SettingsManager\.LoadSettings\(\);
\s*txtWatchFolder\.Text = _appSettings\.SourceFolderPath;
\s*txtFoxitPath\.Text = _appSettings\.FoxitPath \?\? "";
\s*txtUserId\.Text = _appSettings\.HoldPrintUserId \?\? "";
\s*txtCopies\.Text = _appSettings\.AutoPrintCopies > 0 \? _appSettings\.AutoPrintCopies\.ToString\(\) : "1";
\s*UpdateStatusUI\(\);"""

text = re.sub(injected_block, "", text)

# Also fix the one without "UpdateStatusUI" if any
text = re.sub(r"\s*_autoPrintService = new AutoPrintService\(\);\s*_autoPrintService\.StatusChanged \+= AutoPrintService_StatusChanged;\s*_autoPrintService\.FileProcessingStarted \+= AutoPrintService_FileProcessingStarted;\s*", "", text)

# Now, add it only inside MainWindow() constructor
text = text.replace("public MainWindow()\n        {", "public MainWindow()\n        {\n            _autoPrintService = new AutoPrintService();\n            _autoPrintService.StatusChanged += AutoPrintService_StatusChanged;\n            _autoPrintService.FileProcessingStarted += AutoPrintService_FileProcessingStarted;")

# Add UI bindings back to Window_Loaded
text = text.replace("UpdateVerificationPanel();", "UpdateVerificationPanel();\n\n            txtWatchFolder.Text = _appSettings.SourceFolderPath;\n            txtFoxitPath.Text = _appSettings.FoxitPath ?? \"\";\n            txtUserId.Text = _appSettings.HoldPrintUserId ?? \"\";\n            txtCopies.Text = _appSettings.AutoPrintCopies > 0 ? _appSettings.AutoPrintCopies.ToString() : \"1\";\n            UpdateStatusUI();", 1)

# Remove BtnOpenAutoPrint_Click
text = re.sub(r"\s*private void BtnOpenAutoPrint_Click[\s\S]*?\}", "", text)

with open("MainWindow.xaml.cs", "w", encoding="utf-8") as f:
    f.write(text)
