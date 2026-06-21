import re

with open("MainWindow.xaml.cs", "r", encoding="utf-8") as f:
    text = f.read()

text = re.sub(r"\s*private void BtnTest_SelectHoldPrint_Click\([\s\S]*?System\.Windows\.MessageBox\.Show\(\"Could not find 'SAVIN Properties' window\.\"\);\s*\}\s*\}", "", text)

with open("MainWindow.xaml.cs", "w", encoding="utf-8") as f:
    f.write(text)
