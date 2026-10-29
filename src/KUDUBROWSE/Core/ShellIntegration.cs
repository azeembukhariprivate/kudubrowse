using System;
using Microsoft.Win32;

namespace Kudubrowse.Core
{
    /// <summary>
    /// Tells Windows that KUDUBROWSE is a web browser (for the current user only), so it can be chosen in Windows'
    /// "Default apps". Windows doesn't let programs make themselves the default; the person picks it there.
    /// </summary>
    public static class ShellIntegration
    {
        public const string ProgId = "KUDUBROWSEHTML";
        private const string ClientKey = @"Software\Clients\StartMenuInternet\" + AppInfo.Name;

        public static void Register(string exe = null)
        {
            exe = exe ?? AppInfo.ExePath;
            var open = "\"" + exe + "\" \"%1\"";
            var icon = "\"" + exe + "\",0";
            try
            {
                using (var c = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
                {
                    c.SetValue(string.Empty, AppInfo.Name + " web page");
                    c.SetValue("FriendlyTypeName", AppInfo.Name + " web page");
                    using (var i = c.CreateSubKey("DefaultIcon")) i.SetValue(string.Empty, icon);
                    using (var cmd = c.CreateSubKey(@"shell\open\command")) cmd.SetValue(string.Empty, open);
                    using (var app = c.CreateSubKey("Application"))
                    {
                        app.SetValue("ApplicationName", AppInfo.Name);
                        app.SetValue("ApplicationIcon", icon);
                    }
                }
                using (var k = Registry.CurrentUser.CreateSubKey(ClientKey))
                {
                    k.SetValue(string.Empty, AppInfo.Name);
                    using (var i = k.CreateSubKey("DefaultIcon")) i.SetValue(string.Empty, icon);
                    using (var cmd = k.CreateSubKey(@"shell\open\command")) cmd.SetValue(string.Empty, "\"" + exe + "\"");
                    using (var cap = k.CreateSubKey("Capabilities"))
                    {
                        cap.SetValue("ApplicationName", AppInfo.Name);
                        cap.SetValue("ApplicationDescription", "A private everyday web browser");
                        cap.SetValue("ApplicationIcon", icon);
                        using (var urls = cap.CreateSubKey("URLAssociations"))
                        {
                            urls.SetValue("http", ProgId);
                            urls.SetValue("https", ProgId);
                        }
                        using (var files = cap.CreateSubKey("FileAssociations"))
                        {
                            foreach (var ext in new[] { ".htm", ".html", ".xhtml", ".svg", ".webp" }) files.SetValue(ext, ProgId);
                        }
                        using (var menu = cap.CreateSubKey("StartMenu")) menu.SetValue("StartMenuInternet", AppInfo.Name);
                    }
                }
                using (var r = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                    r.SetValue(AppInfo.Name, ClientKey + @"\Capabilities");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is System.IO.IOException)
            {
                // Not allowed here (a locked-down PC): the browser still works, it just can't be picked as the default.
            }
        }

        public static void Unregister()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + ProgId, false);
                Registry.CurrentUser.DeleteSubKeyTree(ClientKey, false);
                using (var r = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true))
                    r?.DeleteValue(AppInfo.Name, false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is System.IO.IOException) { }
        }

        public static bool IsRegistered
        {
            get
            {
                using (var r = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications"))
                    return r?.GetValue(AppInfo.Name) != null;
            }
        }

        /// <summary>The command Windows runs for web addresses chosen with this browser.</summary>
        public static string OpenCommand()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ProgId + @"\shell\open\command"))
                return k?.GetValue(string.Empty) as string;
        }

        /// <summary>Is KUDUBROWSE the browser Windows opens web links with?</summary>
        public static bool IsDefault
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice"))
                        return k?.GetValue("ProgId") as string == ProgId;
                }
                catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
                {
                    return false;
                }
            }
        }

        /// <summary>Opens Windows' "Default apps" at KUDUBROWSE, where the person can make it the default browser.</summary>
        public static void OpenDefaultAppsSettings()
        {
            Register();
            AppInfo.OpenUrl("ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(AppInfo.Name));
        }
    }
}
