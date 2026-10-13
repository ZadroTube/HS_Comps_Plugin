using System;
using System.IO;
using Newtonsoft.Json;

namespace HSCompsPlugin.Services
{
    public class PluginSettings
    {
        public double OverlayX { get; set; } = -1; // -1 означает по центру
        public double OverlayY { get; set; } = 15;
        public double OverlayScale { get; set; } = 1.0;
        public bool IsLockedToTop { get; set; } = false;
        public bool ShowMiniOverlay { get; set; } = true;
        public bool ShowInMainMenu { get; set; } = false;
        public bool DontHideMiniOverlayOnDetails { get; set; } = false;
        public bool IsHighPerformanceMode { get; set; } = false;

        private static string SettingsFilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HearthstoneDeckTracker", "Plugins", "HSCompsPlugin");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                return Path.Combine(dir, "hs_comps_settings.json");
            }
        }

        private static PluginSettings _instance;
        public static PluginSettings Instance => _instance ?? (_instance = Load());

        public static PluginSettings Load()
        {
            try
            {
                string path = SettingsFilePath;
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var settings = JsonConvert.DeserializeObject<PluginSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch { }
            return new PluginSettings();
        }

        public void Save()
        {
            try
            {
                string path = SettingsFilePath;
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }
}
