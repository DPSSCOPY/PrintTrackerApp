import re

with open("MainWindow.xaml.cs", "r", encoding="utf-8") as f:
    text = f.read()

# 1. Fix targetSubFolder assignment
old_block1 = """            if (status.Contains("Complete", StringComparison.OrdinalIgnoreCase) && !status.Contains("Sorting", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Print Complete";
            }
            else if (status.Contains("Sorting Complete", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Sorting Complete";
            }"""

new_block1 = """            if (status.Contains("Complete", StringComparison.OrdinalIgnoreCase) && !status.Contains("Sorting", StringComparison.OrdinalIgnoreCase) && !status.Contains("Storing", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Print Complete";
            }
            else if (status.Contains("Sorting Complete", StringComparison.OrdinalIgnoreCase) || status.Contains("Storing Complete", StringComparison.OrdinalIgnoreCase))
            {
                targetSubFolder = "Sorting Complete";
            }"""

text = text.replace(old_block1, new_block1)

# 2. Fix FindPhysicalFileForJob search folders
old_block2 = """            string[] searchFolders = { 
                _appSettings.SourceFolderPath,
                System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sent to Printer"),
                System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sorting Complete"),
                System.IO.Path.Combine(_appSettings.SourceFolderPath, "Printing")
            };"""

new_block2 = """            string[] searchFolders = { 
                _appSettings.SourceFolderPath,
                System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sent to Printer"),
                System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sorting Complete"),
                System.IO.Path.Combine(_appSettings.SourceFolderPath, "Storing Complete"),
                System.IO.Path.Combine(_appSettings.SourceFolderPath, "Printing")
            };"""

text = text.replace(old_block2, new_block2)

# 3. Fix CheckIfAllJobsComplete activeFolders
old_block3 = """                string[] activeFolders = { 
                    _appSettings.SourceFolderPath,
                    System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sent to Printer"),
                    System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sorting Complete"),
                    System.IO.Path.Combine(_appSettings.SourceFolderPath, "Printing")
                };"""

new_block3 = """                string[] activeFolders = { 
                    _appSettings.SourceFolderPath,
                    System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sent to Printer"),
                    System.IO.Path.Combine(_appSettings.SourceFolderPath, "Sorting Complete"),
                    System.IO.Path.Combine(_appSettings.SourceFolderPath, "Storing Complete"),
                    System.IO.Path.Combine(_appSettings.SourceFolderPath, "Printing")
                };"""

text = text.replace(old_block3, new_block3)

with open("MainWindow.xaml.cs", "w", encoding="utf-8") as f:
    f.write(text)
