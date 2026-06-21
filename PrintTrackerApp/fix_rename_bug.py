import re

with open("MainWindow.xaml.cs", "r", encoding="utf-8") as f:
    text = f.read()

# Add a check to prevent moving a file onto itself
old_block = """            string fileToMove = FindPhysicalFileForJob(job);
            if (fileToMove != null)
            {
                string destFile = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(fileToMove));
                if (System.IO.File.Exists(destFile))
                {"""

new_block = """            string fileToMove = FindPhysicalFileForJob(job);
            if (fileToMove != null)
            {
                // Prevent moving file over itself and appending time repeatedly
                string currentFolder = System.IO.Path.GetDirectoryName(fileToMove);
                if (string.Equals(currentFolder, targetFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string destFile = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(fileToMove));
                if (System.IO.File.Exists(destFile))
                {"""

text = text.replace(old_block, new_block)

with open("MainWindow.xaml.cs", "w", encoding="utf-8") as f:
    f.write(text)
