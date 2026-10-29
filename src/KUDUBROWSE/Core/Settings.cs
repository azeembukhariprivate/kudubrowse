using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace Kudubrowse.Core
{
    public enum ThemeMode { System, Light, Dark }

    /// <summary>Settings are stored under HKEY_CURRENT_USER, so no administrator rights are needed.</summary>
    public static class Settings
    {
        private const string KeyPath = @"Software\KUDUBROWSE";

        private static string Get(string name, string fallback)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    return key?.GetValue(name) as string ?? fallback;
                }
            }
            catch
            {
                return fallback;
            }
        }

        private static void Set(string name, string value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    key?.SetValue(name, value ?? string.Empty);
                }
            }
            catch
            {
                // Settings are best-effort.
            }
        }

        private static bool GetBool(string name, bool fallback) => Get(name, fallback ? "1" : "0") == "1";
        private static void SetBool(string name, bool value) => Set(name, value ? "1" : "0");

        private static T GetEnum<T>(string name, T fallback) where T : struct =>
            Enum.TryParse(Get(name, fallback.ToString()), out T v) ? v : fallback;

        public static ThemeMode Theme
        {
            get => GetEnum("Theme", ThemeMode.System);
            set => Set("Theme", value.ToString());
        }

        public static bool CheckForUpdates
        {
            get => GetBool("CheckForUpdates", true);
            set => SetBool("CheckForUpdates", value);
        }

        public static string SkippedVersion
        {
            get => Get("SkippedVersion", string.Empty);
            set => Set("SkippedVersion", value);
        }

        /// <summary>The search engine for words typed in the address bar (an id from SearchEngines).</summary>
        public static string SearchEngine
        {
            get => Get("SearchEngine", SearchEngines.Default.Id);
            set => Set("SearchEngine", value);
        }

        /// <summary>Open the tabs from last time when KUDUBROWSE starts.</summary>
        public static bool RestoreTabs
        {
            get => GetBool("RestoreTabs", true);
            set => SetBool("RestoreTabs", value);
        }
    }
}
