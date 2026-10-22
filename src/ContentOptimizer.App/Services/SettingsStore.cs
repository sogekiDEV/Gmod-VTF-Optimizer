using System;
using System.IO;
using System.Text.Json;

namespace ContentOptimizer.App.Services
{
    public sealed class UserSettings
    {
        public int Cap { get; set; } = 1024;
        public int TriggerSide { get; set; }
        public int MinConvertSide { get; set; } = 64;
        public double QualityFloorDb { get; set; } = 27;
        public bool ConvertFormats { get; set; } = true;
        public bool DetectOpaque { get; set; } = true;
        public bool ConvertNormalMaps { get; set; } = true;
        public bool RespectNoLod { get; set; } = true;
        public bool InPlace { get; set; }
        public bool CopyEverything { get; set; } = true;
        public string? Excludes { get; set; }
        public string? Language { get; set; }
    }

    public static class SettingsStore
    {
        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VtfOptimizer", "settings.json");

        public static UserSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath)) ?? new UserSettings();
                }
            }
            catch
            {
            }

            return new UserSettings();
        }

        public static void Save(UserSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }
    }
}
