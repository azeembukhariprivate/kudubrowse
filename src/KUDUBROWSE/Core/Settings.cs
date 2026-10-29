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

        /// <summary>Block ads and trackers (EasyList and EasyPrivacy).</summary>
        public static bool BlockAds
        {
            get => GetBool("BlockAds", true);
            set => SetBool("BlockAds", value);
        }

        /// <summary>Hide cookie banners (EasyList Cookie List).</summary>
        public static bool HideCookieBanners
        {
            get => GetBool("HideCookieBanners", true);
            set => SetBool("HideCookieBanners", value);
        }

        /// <summary>Block new tabs and windows that pages open by themselves (not after a click).</summary>
        public static bool BlockPopups
        {
            get => GetBool("BlockPopups", true);
            set => SetBool("BlockPopups", value);
        }

        /// <summary>Sites where nothing is blocked (their own domain, such as example.co.uk).</summary>
        public static List<string> AllowedSites
        {
            get => Get("AllowedSites", string.Empty).Split('\n').Where(p => p.Length > 0).ToList();
            set => Set("AllowedSites", string.Join("\n", value.Distinct().OrderBy(d => d)));
        }

        /// <summary>Offer to save passwords typed into login forms.</summary>
        public static bool OfferToSavePasswords
        {
            get => GetBool("OfferToSavePasswords", true);
            set => SetBool("OfferToSavePasswords", value);
        }

        /// <summary>Put tabs that haven't been looked at for a while to sleep.</summary>
        public static bool SleepTabs
        {
            get => GetBool("SleepTabs", true);
            set => SetBool("SleepTabs", value);
        }

        /// <summary>Show the bookmarks bar under the address bar.</summary>
        public static bool ShowBookmarksBar
        {
            get => GetBool("ShowBookmarksBar", true);
            set => SetBool("ShowBookmarksBar", value);
        }

        /// <summary>The welcome window (bring things from Chrome, default browser) has been shown.</summary>
        public static bool WelcomeShown
        {
            get => GetBool("WelcomeShown", false);
            set => SetBool("WelcomeShown", value);
        }

        /// <summary>Open the tabs from last time when KUDUBROWSE starts.</summary>
        public static bool RestoreTabs
        {
            get => GetBool("RestoreTabs", true);
            set => SetBool("RestoreTabs", value);
        }
    }
}
