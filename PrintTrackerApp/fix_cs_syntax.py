import sys

with open("MainWindow.xaml.cs", "r", encoding="utf-8") as f:
    lines = f.readlines()

# The error starts at line 624. I'll read and print lines 621 to 673 to ensure I delete the exact right lines.
for i, line in enumerate(lines[620:675], 621):
    print(f"{i}: {line}", end="")
