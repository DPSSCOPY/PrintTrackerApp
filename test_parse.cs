using System;
using System.Globalization;

class Program
{
    static void Main()
    {
        string[] inputs = {
            "Jun 20, 2026 10:16:32 AM",
            "Jun 20, 2026 10:16:32",
            "2026-06-20 10:32:42"
        };
        
        foreach (var s in inputs)
        {
            bool ok = DateTime.TryParse(s, out DateTime dt);
            Console.WriteLine($"{s} -> Parse Default: {ok} {dt}");
            
            bool ok2 = DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt2);
            Console.WriteLine($"{s} -> Parse Invariant: {ok2} {dt2}");
        }
    }
}
