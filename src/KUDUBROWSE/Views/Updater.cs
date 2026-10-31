using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Kudubrowse.Core;
using Microsoft.Win32;

namespace Kudubrowse.Views
{
    /// <summary>
    /// Keeps the browser up to date by itself (Settings, "Update automatically"): soon after it starts and then once a
    /// day it asks GitHub for the latest release; a newer installer is downloaded quietly and checked against GitHub's
    /// SHA-256 checksum, and installed (without any windows) when the browser is closed. The next start says
    /// "Updated to ...". A portable copy isn't changed: it's only told a new version is out.
    /// </summary>
    public static class Updater
    {
        private const string AppId = "{893DD256-DB89-414A-96D4-2D2DB03B0F9F}";
        private static DispatcherTimer _timer;
        private static bool _busy;

        public static string Folder => Path.Combine(Session.DataFolder, "Update");
        public static string DownloadPrefix => $"https://github.com/{AppInfo.RepoOwner}/{AppInfo.RepoName}/releases/download/";

        /// <summary>What the About window says about updates.</summary>
        public static string Status { get; private set; } = string.Empty;
        public static event Action StatusChanged;

        /// <summary>Installed with the installer (not run from the portable zip), so it can update itself.</summary>
        public static bool Installed
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId + "_is1"))
                    {
                        var at = (k?.GetValue("InstallLocation") as string ?? string.Empty).TrimEnd('\\');
                        var here = (Path.GetDirectoryName(AppInfo.ExePath) ?? string.Empty).TrimEnd('\\');
                        return at.Length > 0 && string.Equals(Path.GetFullPath(at), Path.GetFullPath(here), StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException || ex is ArgumentException)
                {
                    return false;
                }
            }
        }

        /// <summary>A checked installer waiting to be installed: (version, file, checksum), or null.</summary>
        public static (Version Version, string File, string Sha)? Ready
        {
            get
            {
                var parts = Settings.UpdateReady.Split('|');
                if (parts.Length != 3 || !Version.TryParse(parts[0], out var v) || v <= AppInfo.Version || !File.Exists(parts[1])) return null;
                return (v, parts[1], parts[2]);
            }
        }

        /// <summary>Starts the checks (soon after start-up, then every day) when updating by itself is switched on.</summary>
        public static void Start()
        {
            SetStatus(Ready is var r && r != null ? $"{AppInfo.Name} {r.Value.Version.ToString(3)} will be installed when you close {AppInfo.Name}." : string.Empty);
            if (_timer != null) return;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _timer.Tick += async (s, e) =>
            {
                _timer.Interval = TimeSpan.FromHours(24);
                if (Settings.CheckForUpdates) await CheckNow();
            };
            _timer.Start();
        }

        /// <summary>Asks GitHub, and downloads and checks a newer installer. Returns what it found, in plain words.</summary>
        public static async Task<string> CheckNow()
        {
            if (_busy) return Status;
            _busy = true;
            try
            {
                SetStatus("Looking for a new version…");
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.Name + "/" + AppInfo.VersionText);
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                    var json = await http.GetStringAsync(AutoUpdate.LatestUrl(AppInfo.RepoOwner, AppInfo.RepoName));
                    var release = AutoUpdate.Parse(json, AppInfo.Name, AppInfo.Version, DownloadPrefix);
                    if (release == null) return SetStatus("You have the latest version.");
                    var version = release.Version.ToString(3);
                    if (Ready is var r && r != null && r.Value.Version == release.Version && AutoUpdate.Matches(r.Value.File, r.Value.Sha))
                        return SetStatus($"{AppInfo.Name} {version} will be installed when you close {AppInfo.Name}.");
                    if (!Installed) return SetStatus($"{AppInfo.Name} {version} is out. This copy wasn't installed, so it isn't updated by itself: get it from the download page.");
                    SetStatus($"Downloading {AppInfo.Name} {version}…");
                    var file = await AutoUpdate.Download(http, release, Folder, CancellationToken.None);
                    if (file == null) return SetStatus($"{AppInfo.Name} {version} is out, but its download didn't check out. It'll be tried again later.");
                    Settings.UpdateReady = version + "|" + file + "|" + release.Sha256;
                    return SetStatus($"{AppInfo.Name} {version} will be installed when you close {AppInfo.Name}.");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return SetStatus("Couldn't look for a new version (no internet?). It'll be tried again later.");
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// When the browser has closed: installs a waiting update quietly (checked once more first). The installer closes
        /// nothing that's still open and doesn't start the browser; the next start is the new version.
        /// </summary>
        public static void InstallIfReady()
        {
            if (!Settings.CheckForUpdates || !Installed || !(Ready is var r) || r == null) return;
            if (!AutoUpdate.Matches(r.Value.File, r.Value.Sha))
            {
                Settings.UpdateReady = string.Empty;
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(r.Value.File, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /SP-") { UseShellExecute = false });
                Settings.UpdateReady = string.Empty;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is IOException) { }
        }

        /// <summary>The first start after an update: the version it was updated to (once), and the installer is tidied away.</summary>
        public static string UpdatedTo()
        {
            var last = Settings.LastRunVersion;
            var now = AppInfo.Version.ToString(3);
            Settings.LastRunVersion = now;
            if (last.Length == 0 || last == now || !Version.TryParse(last, out var was) || was > AppInfo.Version) return null;
            try { if (Directory.Exists(Folder)) Directory.Delete(Folder, true); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            return now;
        }

        private static string SetStatus(string text)
        {
            Status = text;
            StatusChanged?.Invoke();
            return text;
        }
    }
}
