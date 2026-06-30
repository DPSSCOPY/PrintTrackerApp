using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PrintTrackerApp.Services
{
    public class TeacherScheduleManager
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "teacher_schedules.json");

        // Dictionary structure: Key = TeacherName_Level, Value = Dictionary<DateString (yyyy-MM-dd), Status (Teach/No Teach/Exam)>
        public Dictionary<string, Dictionary<string, string>> Schedules { get; set; } = new Dictionary<string, Dictionary<string, string>>();

        public static TeacherScheduleManager Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                    return new TeacherScheduleManager { Schedules = data ?? new Dictionary<string, Dictionary<string, string>>() };
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
                string json = JsonSerializer.Serialize(Schedules, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving teacher schedules: {ex.Message}");
            }
        }
    }
}
