using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Kudubrowse.Core;

namespace Kudubrowse.Views
{
    /// <summary>
    /// KUDUBROWSE.exe --self-test: opens the real browser window with test pages made on the spot and uses it:
    /// typing addresses, back and forward, new tabs, links that open new windows, pages that close themselves,
    /// reopening closed tabs, zoom, tabs coming back after a restart, and every window in both themes.
    /// Uses its own profile and session file, so nothing of the user's is touched. Writes self-test.log next to
    /// the exe; exits with 0 when everything passed.
    /// </summary>
    public static class SelfTest
    {
        public static bool Running { get; private set; }
        public static List<string> Errors { get; } = new List<string>();

        private static StringBuilder _log;
        private static string _temp;

        public static async Task<int> Run()
        {
            Running = true;
            _log = new StringBuilder();
            _temp = Path.Combine(Path.GetTempPath(), "kudubrowse-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_temp);
            Web.ProfileFolder = Path.Combine(_temp, "profile");
            Session.DefaultFile = Path.Combine(_temp, "session.txt");
            var savedTheme = Settings.Theme;
            var savedRestore = Settings.RestoreTabs;
            Settings.RestoreTabs = true;

            await Step("Load themes and styles", () =>
            {
                ThemeManager.Init(ThemeMode.Light);
                foreach (var key in new[] { "WindowBg", "SurfaceBg", "SurfaceAltBg", "Text", "SubtleText", "Accent", "BorderBrush", "InputBorder", "InputBg", "HoverBg",
                                            "GhostButton", "PrimaryButton", "ChipRadio", "IconGlobe", "IconHome", "IconLock", "IconArrowLeft", "IconArrowRight", "IconRefresh",
                                            "IconMore", "IconPlus", "IconClose" })
                    Application.Current.FindResource(key);
                return Task.CompletedTask;
            });
            await Step("Edge engine (WebView2) is there", () =>
            {
                var version = Web.RuntimeVersion;
                Note("WebView2 " + version);
                Expect(version != null, "WebView2 runtime");
                return Task.CompletedTask;
            });

            var first = Page("first.html", "First page", "<a id='next' href='second.html'>Second page</a> <button id='pop' onclick=\"window.open('popup.html')\">Open a window</button>");
            var second = Page("second.html", "Second page", "<p>The second page.</p>");
            Page("popup.html", "Pop-up page", "<button id='close' onclick='window.close()'>Close me</button>");

            MainWindow main = null;
            await Step("New window with a new tab", () =>
            {
                main = new MainWindow();
                OffScreen(main);
                main.Show();
                main.Start(null);
                Pump();
                Expect(main.Tabs.Count == 1 && main.Current.IsBlank, "one new tab");
                return Task.CompletedTask;
            });
            if (main != null)
            {
                await Step("Type a page's address", async () =>
                {
                    main.Go(first);
                    await Wait(() => main.Current.Title == "First page", 30, "the first page");
                    Expect(main.Title.StartsWith("First page"), "window title");
                    Expect(main.AddressText.StartsWith("file:///"), "address shown: " + main.AddressText);
                    await Capture(main, "first page");
                });
                await Step("Follow a link, back and forward", async () =>
                {
                    await main.Current.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('next').click()");
                    await Wait(() => main.Current.Title == "Second page", 20, "the second page");
                    await Wait(() => main.Current.CanGoBack, 10, "back to be possible");
                    main.Current.GoBack();
                    await Wait(() => main.Current.Title == "First page", 20, "back to the first page");
                    await Wait(() => main.Current.CanGoForward, 10, "forward to be possible");
                    main.Current.GoForward();
                    await Wait(() => main.Current.Title == "Second page", 20, "forward to the second page");
                });
                await Step("A link that opens a new window opens a tab, and the page can close it", async () =>
                {
                    main.Current.GoBack();
                    await Wait(() => main.Current.Title == "First page", 20, "the first page");
                    var opener = main.Current;
                    await opener.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('pop').click()");
                    await Wait(() => main.Tabs.Count == 2 && main.Current.Title == "Pop-up page", 20, "the pop-up tab");
                    Expect(main.Tabs[1] == main.Current, "opens next to the page that opened it");
                    await main.Current.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('close').click()");
                    await Wait(() => main.Tabs.Count == 1, 20, "the pop-up to close itself");
                    Expect(main.Current == opener, "back on the first page");
                });
                await Step("New tab page, typing in it, closing and reopening a tab", async () =>
                {
                    var tab = main.NewTab(select: true);
                    Pump();
                    Expect(main.Tabs.Count == 2 && main.Current == tab && tab.IsBlank, "a new blank tab");
                    main.Go(second);
                    await Wait(() => tab.Title == "Second page", 20, "the second page in the new tab");
                    main.CloseTab(tab);
                    Expect(main.Tabs.Count == 1, "tab closed");
                    main.ReopenClosedTab();
                    await Wait(() => main.Tabs.Count == 2 && main.Current.Title == "Second page", 20, "the closed tab to come back");
                });
                await Step("Zoom", async () =>
                {
                    main.Current.Zoom = 1.5;
                    Pump();
                    Expect(Math.Abs(main.Current.Zoom - 1.5) < 0.01, "zoomed to 150%");
                    main.Current.Zoom = 1;
                    await Task.CompletedTask;
                });
                await Step("Tabs come back next time", async () =>
                {
                    MainWindow.SaveAll();
                    var (saved, selected) = Session.Load();
                    Expect(saved.Count == 2, "two tabs saved, found " + saved.Count);
                    Expect(saved[0].Title == "First page" && saved[1].Title == "Second page", "their titles");
                    Expect(selected == 1, "the selected tab");
                    var again = new MainWindow();
                    OffScreen(again);
                    again.Show();
                    again.Start(null);
                    Pump();
                    Expect(again.Tabs.Count == 2, "two tabs back");
                    Expect(again.Tabs[0].View == null, "tabs not shown yet wait until they're clicked");
                    await Wait(() => again.Current.Title == "Second page", 20, "the selected tab to load");
                    again.Select(again.Tabs[0]);
                    await Wait(() => again.Tabs[0].Title == "First page" && again.Tabs[0].View != null, 20, "the other tab to load when clicked");
                    again.Close();
                });
                await Step("Dark theme", async () =>
                {
                    ThemeManager.Apply(ThemeMode.Dark);
                    main.NewTab(select: true);
                    Pump();
                    Screenshots.All("Dark theme");
                    ShowAndClose(new SettingsWindow(main));
                    ShowAndClose(About.Build(main));
                    ThemeManager.Apply(ThemeMode.Light);
                    await Task.CompletedTask;
                });
                await Step("Settings window", () => { ShowAndClose(new SettingsWindow(main)); return Task.CompletedTask; });
                await Step("About window", () => { ShowAndClose(About.Build(main)); return Task.CompletedTask; });
                await Step("Close", () =>
                {
                    main.Close();
                    return Task.CompletedTask;
                });
            }

            Settings.Theme = savedTheme;
            Settings.RestoreTabs = savedRestore;
            _log.AppendLine(Errors.Count == 0 ? "SELF-TEST PASSED" : $"SELF-TEST FAILED ({Errors.Count})");
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(AppInfo.ExePath) ?? ".", "self-test.log"), _log.ToString()); }
            catch { /* read-only folder */ }
            await Task.Delay(500); // let the engine let go of the profile folder
            try { Directory.Delete(_temp, true); }
            catch { /* ignore */ }
            return Errors.Count == 0 ? 0 : 1;
        }

        /// <summary>Writes a test page and returns its path.</summary>
        private static string Page(string name, string title, string body)
        {
            var path = Path.Combine(_temp, name);
            File.WriteAllText(path, $"<!doctype html><html><head><meta charset='utf-8'><title>{title}</title></head>" +
                                    $"<body style='font-family:Segoe UI;padding:40px'><h1>{title}</h1>{body}</body></html>");
            return path;
        }

        private static async Task Capture(MainWindow main, string name)
        {
            try
            {
                Directory.CreateDirectory(Screenshots.Folder);
                using (var fs = File.Create(Path.Combine(Screenshots.Folder, $"web {name}.png")))
                    await main.Current.View.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, fs);
            }
            catch (Exception ex)
            {
                Note("couldn't capture the page: " + ex.Message);
            }
        }

        private static async Task Step(string name, Func<Task> action)
        {
            var before = Errors.Count;
            var started = DateTime.UtcNow;
            try
            {
                await action();
                Pump();
                Screenshots.All(name);
                if (Errors.Count > before) throw new Exception(string.Join(" | ", Errors.Skip(before)));
                _log.AppendLine($"ok    {name} ({(DateTime.UtcNow - started).TotalSeconds:0.0}s)");
            }
            catch (Exception ex)
            {
                if (Errors.Count == before) Errors.Add(name + ": " + ex.Message);
                _log.AppendLine("FAIL  " + name + ": " + ex);
            }
        }

        private static void Note(string text) => _log.AppendLine("      " + text);

        private static void Expect(bool condition, string what)
        {
            if (!condition) throw new Exception("expected " + what);
        }

        private static async Task Wait(Func<bool> condition, int seconds, string what)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (!condition())
            {
                if (DateTime.UtcNow > until) throw new Exception("timed out waiting for " + what);
                await Task.Delay(50);
            }
        }

        private static void Pump()
        {
            foreach (Window w in Application.Current.Windows) w.UpdateLayout();
            Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }

        private static void OffScreen(Window w)
        {
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.WindowState = WindowState.Normal;
            w.Left = -20000;
            w.Top = -20000;
            w.ShowActivated = false;
            w.ShowInTaskbar = false;
        }

        private static void ShowAndClose(Window w)
        {
            OffScreen(w);
            w.Show();
            Pump();
            Screenshots.One(w, w.Title);
            w.Close();
        }
    }
}
