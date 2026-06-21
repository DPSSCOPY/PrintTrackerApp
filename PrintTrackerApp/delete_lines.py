with open("MainWindow.xaml.cs", "r", encoding="utf-8") as f:
    lines = f.readlines()

del lines[620:670]

with open("MainWindow.xaml.cs", "w", encoding="utf-8") as f:
    f.writelines(lines)
