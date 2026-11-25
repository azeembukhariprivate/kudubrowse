// KUDUBROWSE's settings, kept in the registry for the current user.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace Kudubrowse.Core
{
    /// <summary>How the browser looks: like Windows, always light, or always dark.</summary>
    public enum ThemeMode { System, Light, Dark }

    /// <summary>Settings are stored under HKEY_CURRENT_USER, so no administrator rights are needed.</summary>
    public static class Settings
    {
        /// <summary>Where the settings are kept, under HKEY_CURRENT_USER.</summary>
        private const string KeyPath = @"Software\KUDUBROWSE";

        /// <summary>A setting as text, or fallback when it isn't there or can't be read.</summary>
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

        /// <summary>Keeps a setting (quietly does nothing when the registry can't be written).</summary>
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

        /// <summary>A yes/no setting (kept as "1" or "0").</summary>
        private static bool GetBool(string name, bool fallback) => Get(name, fallback ? "1" : "0") == "1";
        /// <summary>Keeps a yes/no setting.</summary>
        private static void SetBool(string name, bool value) => Set(name, value ? "1" : "0");

        /// <summary>A setting that is one of a list of choices (an enum).</summary>
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
    
        /// <summary>A checked installer of a newer version, ready to install when the browser closes ("version|file|sha256").</summary>
        public static string UpdateReady
        {
            get => Get("UpdateReady", string.Empty);
            set => Set("UpdateReady", value);
        }

        /// <summary>The version that ran last time (to say "Updated to ..." once after an update).</summary>
        public static string LastRunVersion
        {
            get => Get("LastRunVersion", string.Empty);
            set => Set("LastRunVersion", value);
        }

        /// <summary>Tabs are listed down the left side of the window instead of along the top.</summary>
        public static bool TabsOnSide
        {
            get => GetBool("TabsOnSide", false);
            set => SetBool("TabsOnSide", value);
        }

        /// <summary>Sites (registrable domains) whose tabs are always muted.</summary>
        public static List<string> MutedSites
        {
            get => Get("MutedSites", string.Empty).Split('\n').Where(p => p.Length > 0).ToList();
            set => Set("MutedSites", string.Join("\n", value.Distinct().OrderBy(d => d)));
        }

        /// <summary>How pages look: "Off" (as the site shows them), "WhenSiteHasIt" or "Always" (dark).</summary>
        public static DarkMode DarkPages
        {
            get => Core.DarkPages.Parse(Get("DarkPages", "Off"));
            set => Set("DarkPages", value.ToString());
        }

        /// <summary>Sites never darkened when pages are always dark (registrable domains).</summary>
        public static List<string> LightSites
        {
            get => Get("LightSites", string.Empty).Split('\n').Where(p => p.Length > 0).ToList();
            set => Set("LightSites", string.Join("\n", value.Distinct().OrderBy(d => d)));
        }

        /// <summary>Where downloads go (empty: Windows' Downloads folder).</summary>
        public static string DownloadFolder
        {
            get => Get("DownloadFolder", string.Empty);
            set => Set("DownloadFolder", value);
        }

        /// <summary>Ask where to save each download, instead of putting it in the downloads folder.</summary>
        public static bool AskWhereToSave
        {
            get => GetBool("AskWhereToSave", false);
            set => SetBool("AskWhereToSave", value);
        }

        /// <summary>Put downloads in folders by type (Pictures, Documents, Programs...) inside the downloads folder.</summary>
        public static bool SortDownloads
        {
            get => GetBool("SortDownloads", false);
            set => SetBool("SortDownloads", value);
        }

        /// <summary>Fetch big files in pieces at the same time, when the site allows it.</summary>
        public static bool FastDownloads
        {
            get => GetBool("FastDownloads", true);
            set => SetBool("FastDownloads", value);
        }

        /// <summary>Reading mode's text size in pixels, and its colours (light, sepia or dark).</summary>
        public static int ReadingSize
        {
            get => int.TryParse(Get("ReadingSize", "20"), out var n) && n >= 14 && n <= 32 ? n : 20;
            set => Set("ReadingSize", Math.Max(14, Math.Min(32, value)).ToString());
        }

        public static string ReadingTheme
        {
            get
            {
                var t = Get("ReadingTheme", "light");
                return t == "sepia" || t == "dark" ? t : "light";
            }
            set => Set("ReadingTheme", value);
        }

        /// <summary>Open http:// pages as https:// when the site has it, and warn before opening a site without it.</summary>
        public static bool SecureConnections
        {
            get => GetBool("SecureConnections", true);
            set => SetBool("SecureConnections", value);
        }

        /// <summary>Sites (registrable domains) the person chose to open without a secure connection.</summary>
        public static List<string> InsecureSites
        {
            get => Get("InsecureSites", string.Empty).Split('\n').Where(p => p.Length > 0).ToList();
            set => Set("InsecureSites", string.Join("\n", value.Distinct().OrderBy(d => d)));
        }

        /// <summary>When the browser closes, forget nothing, the sites on the list, or every site.</summary>
        public static ForgetMode ForgetWhenClosing
        {
            get => GetEnum("ForgetWhenClosing", ForgetMode.Off);
            set => Set("ForgetWhenClosing", value.ToString());
        }

        /// <summary>What's forgotten: cookies and site data (signs you out).</summary>
        public static bool ForgetCookies
        {
            get => GetBool("ForgetCookies", true);
            set => SetBool("ForgetCookies", value);
        }

        /// <summary>What's forgotten: the history.</summary>
        public static bool ForgetHistory
        {
            get => GetBool("ForgetHistory", false);
            set => SetBool("ForgetHistory", value);
        }

        /// <summary>The sites (registrable domains) forgotten when the browser closes.</summary>
        public static List<string> ForgetSites
        {
            get => Get("ForgetSites", string.Empty).Split('\n').Where(p => p.Length > 0).ToList();
            set => Set("ForgetSites", string.Join("\n", value.Distinct().OrderBy(d => d)));
        }

        /// <summary>Forgetting began when the browser last closed and hasn't finished (it's finished at the next start).</summary>
        public static bool ForgetPending
        {
            get => GetBool("ForgetPending", false);
            set => SetBool("ForgetPending", value);
        }
    }
}
