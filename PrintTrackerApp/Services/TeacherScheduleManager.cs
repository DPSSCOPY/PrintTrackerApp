using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PrintTrackerApp.Services
{
    public class TeacherScheduleManager
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "teacher_schedules.json");

        // Dictionary structure: Key = TeacherName_Level, Value = Dictionary<DateString (yyyy-MM-dd), Status (Teach/No Teach/Exam)>
        public Dictionary<string, Dictionary<string, string>> Schedules { get; set; } = new Dictionary<string, Dictionary<string, string>>();

        // Keys of teacher+level rows hidden from FT/PT/KH dashboard tabs (key = "TeacherName_Level")
        public HashSet<string> HiddenTeachers { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Persistent filter settings
        public string LastFilterName { get; set; } = "";
        public DateTime? LastCustomStartDate { get; set; }
        public DateTime? LastCustomEndDate { get; set; }

        // Internal DTO for JSON serialization (preserves backward-compat with old plain-dict format)
        private class SaveData
        {
            public Dictionary<string, Dictionary<string, string>> Schedules { get; set; }
            public List<string> HiddenTeachers { get; set; }
            public string LastFilterName { get; set; }
            public DateTime? LastCustomStartDate { get; set; }
            public DateTime? LastCustomEndDate { get; set; }
        }

        public static TeacherScheduleManager Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);

                    // Try new format first (wrapper object with HiddenTeachers)
                    try
                    {
                        var data = JsonSerializer.Deserialize<SaveData>(json);
                        if (data?.Schedules != null)
                        {
                            return new TeacherScheduleManager
                            {
                                Schedules = data.Schedules,
                                HiddenTeachers = new HashSet<string>(
                                    data.HiddenTeachers ?? new List<string>(),
                                    StringComparer.OrdinalIgnoreCase),
                                LastFilterName = data.LastFilterName ?? "",
                                LastCustomStartDate = data.LastCustomStartDate,
                                LastCustomEndDate = data.LastCustomEndDate
                            };
                        }
                    }
                    catch { /* fall through to old format */ }

                    // Backward-compat: old format was a plain Dictionary<string, Dictionary<string, string>>
                    var oldData = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                    return new TeacherScheduleManager { Schedules = oldData ?? new Dictionary<string, Dictionary<string, string>>() };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading teacher schedules: {ex.Message}");
            }
            
            return new TeacherScheduleManager();
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var data = new SaveData
                {
                    Schedules = Schedules,
                    HiddenTeachers = HiddenTeachers.OrderBy(k => k).ToList(),
                    LastFilterName = LastFilterName,
                    LastCustomStartDate = LastCustomStartDate,
                    LastCustomEndDate = LastCustomEndDate
                };
                string json = JsonSerializer.Serialize(data, options);
                SafeFileHelper.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving teacher schedules: {ex.Message}");
            }
        }
    }
}
