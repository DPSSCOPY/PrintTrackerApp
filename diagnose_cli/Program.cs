using System;
using System.IO;
using System.Data;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ExcelDataReader;

namespace diagnose_cli
{
    class Program
    {
        static void Main(string[] args)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            string printTrackerBaseDir = @"E:\Code\Tracking_Print\PrintTrackerApp\bin\Debug\net8.0-windows";
            string excelPathFile = Path.Combine(printTrackerBaseDir, "last_excel_path.txt");
            string excelPath = File.ReadAllText(excelPathFile).Trim();
            Console.WriteLine($"Loading Excel file: {excelPath}\n");

            // Step 1: Excel Roster
            Console.WriteLine("=== STEP 1: Excel Roster Entries for Seavchhinh ===");
            using (var stream = File.Open(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                var result = reader.AsDataSet();
                foreach (DataTable table in result.Tables)
                    for (int r = 0; r < table.Rows.Count; r++)
                    {
                        string col2 = table.Rows[r][1]?.ToString() ?? "";
                        if (col2.Contains("Seavchhinh", StringComparison.OrdinalIgnoreCase) ||
                            col2.Contains("Seavchhing", StringComparison.OrdinalIgnoreCase))
                        {
                            string col1 = table.Rows[r][0]?.ToString() ?? "";
                            string col3 = table.Columns.Count > 2 ? (table.Rows[r][2]?.ToString() ?? "") : "";
                            string col4 = table.Columns.Count > 3 ? (table.Rows[r][3]?.ToString() ?? "") : "";
                            Console.WriteLine($"  Sheet={table.TableName} Row {r}: No='{col1}' Name='{col2}' Level='{col3}' Session='{col4}'");
                        }
                    }
            }

            // Step 2: Print log entries
            Console.WriteLine("\n=== STEP 2: Print Log entries with CHHINH ===");
            foreach (var csvFile in Directory.GetFiles(@"E:\Print Log", "PrintLog_*.csv").OrderByDescending(f => f))
                foreach (var line in File.ReadAllLines(csvFile).Skip(1))
                    if (line.Contains("CHHINH", StringComparison.OrdinalIgnoreCase))
                        Console.WriteLine($"  [{Path.GetFileName(csvFile)}] {line.Trim()}");

            // Step 3: Parse document name
            Console.WriteLine("\n=== STEP 3: ParseDocumentName results ===");
            foreach (var doc in new[] { "SMC4-CHHINH-6 copies-22.06.2026.pdf", "SMC4-CHHINH-6 copies-26.06.2026..pdf" })
            {
                ParseDocumentName(doc, out string lv, out string te, out string se);
                Console.WriteLine($"  '{doc}' -> Level='{lv}' Teacher='{te}' Session='{se}'");
            }

            // Step 4: Name match scores
            Console.WriteLine("\n=== STEP 4: ScoreNameMatch ===");
            Console.WriteLine($"  ScoreNameMatch('Sros Seavchhinh','CHHINH') = {ScoreNameMatch("Sros Seavchhinh", "CHHINH")}");
            Console.WriteLine($"  ScoreNameMatch('Sros Seavchhinh','seavchhinh') = {ScoreNameMatch("Sros Seavchhinh", "seavchhinh")}");
            Console.WriteLine($"  'seavchhinh'.EndsWith('chhinh') = {"seavchhinh".EndsWith("chhinh")}");
        }

        static void ParseDocumentName(string docName, out string level, out string teacher, out string session)
        {
            level = ""; teacher = ""; session = "";
            if (docName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                docName = docName.Substring(0, docName.Length - 4);
            docName = Regex.Replace(docName, @"(?i)\bPre-([0-9][a-zA-Z0-9]*)\b", "Pre$1");
            var parts = docName.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            int copiesIndex = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].ToLower();
                int op = p.IndexOf('('); if (op >= 0) p = p.Substring(0, op).Trim();
                if (Regex.IsMatch(p, @"^\d+[-_]?c[o0]?[o0]?p[a-z]*$") ||
                    Regex.IsMatch(p, @"^\d+[-_]?cps$") ||
                    Regex.IsMatch(p, @"^c[o0]?[o0]?p[ieys]+$") || p == "cps" || p == "cop")
                { copiesIndex = i; break; }
            }
            if (copiesIndex == -1)
            {
                if (parts.Length >= 3) { level = parts[0]; teacher = parts[1]; session = parts[2]; }
                else if (parts.Length == 2) { level = parts[0]; teacher = parts[1]; }
                else if (parts.Length == 1) { teacher = parts[0]; }
                return;
            }
            if (copiesIndex >= 3) { level = parts[0]; teacher = parts[1]; session = parts[2]; }
            else if (copiesIndex == 2) { level = parts[0]; teacher = parts[1]; }
            else if (copiesIndex == 1) { teacher = parts[0]; }
        }

        static int ScoreNameMatch(string excelName, string dictName)
        {
            if (string.IsNullOrWhiteSpace(excelName) || string.IsNullOrWhiteSpace(dictName)) return 0;
            excelName = excelName.ToLower().Trim();
            dictName  = dictName.ToLower().Trim();
            if (excelName == dictName) return 1000;
            var excelWords = excelName.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var dictWords  = dictName.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (excelWords.Length == 0) return 0;
            string targetWord = excelWords[excelWords.Length - 1];
            int best = 0;
            foreach (var dWord in dictWords)
            {
                if (targetWord == dWord) { best = Math.Max(best, 900); continue; }
                if (dWord.Length >= 3 && targetWord.EndsWith(dWord)) best = Math.Max(best, 700);
                if (targetWord.Length >= 3 && dWord.EndsWith(targetWord)) best = Math.Max(best, 700);
                if (dWord.Length >= 4 && targetWord.StartsWith(dWord)) best = Math.Max(best, 500);
                if (targetWord.Length >= 4 && dWord.StartsWith(targetWord)) best = Math.Max(best, 500);
            }
            return best;
        }
    }
}
