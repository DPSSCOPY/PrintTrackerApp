using System;

namespace PrintTrackerApp.Models
{
    public class TeacherPrintStat
    {
        public string TeacherName { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
        public string Session { get; set; } = string.Empty;
        public int TotalPages { get; set; } = 0; // Sum of job.TotalPages
        public int TotalPageCopies { get; set; } = 0; // Pages * Copies
        public int JobCount { get; set; } = 0;
        public System.Collections.Generic.HashSet<string> PrintDays { get; set; } = new System.Collections.Generic.HashSet<string>();
        public System.Collections.Generic.Dictionary<string, int> DailyPages { get; set; } = new System.Collections.Generic.Dictionary<string, int>();
        public System.Collections.Generic.HashSet<string> ExemptedDates { get; set; } = new System.Collections.Generic.HashSet<string>();
        public string Grade { get; set; } = string.Empty;
        public string JobsTooltip { get; set; } = string.Empty;
        public bool IsMatched { get; set; } = false;
        public System.Collections.Generic.List<PrintJobInfo> Jobs { get; set; } = new System.Collections.Generic.List<PrintJobInfo>();
    }
}
