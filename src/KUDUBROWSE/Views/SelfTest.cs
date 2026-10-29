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
            Core.Blocking.BlockLists.DownloadFolder = Path.Combine(_temp, "lists");
            var savedAllowed = Settings.AllowedSites;
            var savedBlocking = (Settings.BlockAds, Settings.HideCookieBanners, Settings.BlockPopups);
            Settings.AllowedSites = new List<string>();
            Settings.BlockAds = Settings.HideCookieBanners = true;
            // The early steps open windows with clicks made by a script, which pop-up blocking (rightly) stops;
            // it's switched on for the blocking step.
            Settings.BlockPopups = false;
            Blocker.SettingsChanged();
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

            await Step("The block lists that come with the app load", async () =>
            {
                await Task.Run(Blocker.Load);
                Note($"{Blocker.RuleCount} blocking rules, {Blocker.HidingCount} hiding rules");
                Expect(Blocker.RuleCount > 20000, "the real lists (found " + Blocker.RuleCount + " rules)");
                Expect(Blocker.ShouldBlock("https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js", "news.example.com",
                    Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.Script), "a well-known ad script is blocked");
                Expect(!Blocker.ShouldBlock("https://news.example.com/app.js", "news.example.com",
                    Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.Script), "the site's own script is not");
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
                await Step("Ads, trackers and pop-ups are blocked", async () =>
                {
                    var keep = main.Tabs.ToList();
                    try
                    {
                        Settings.BlockPopups = true;
                        Blocker.SettingsChanged();
                        Blocker.UseRules("||ads.kudutest.invalid^\n||tracker.kudutest.invalid^\n##.ad-box\nkudutest.example##.promo");
                        Page("ads.html", "Page with ads",
                            "<img id='ad' src='https://ads.kudutest.invalid/banner.png'><script src='https://tracker.kudutest.invalid/t.js'></script>" +
                            "<div class='ad-box'>An ad</div><div class='promo'>A promotion</div><div class='content'>The article</div>" +
                            "<script>setTimeout(() => window.open('https://kudutest.example/second.html'), 100)</script>");
                        var tab = main.NewTab(select: true);
                        await tab.EnsureView();
                        tab.View.CoreWebView2.SetVirtualHostNameToFolderMapping("kudutest.example", _temp, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                        var before = main.Tabs.Count;
                        main.Go("https://kudutest.example/ads.html");
                        await Wait(() => tab.Title == "Page with ads" && !tab.IsLoading, 20, "the page with ads");
                        await Task.Delay(800);
                        Expect(tab.Blocked >= 2, "the ad and the tracker blocked (blocked: " + tab.Blocked + ")");
                        var probe = await tab.View.CoreWebView2.ExecuteScriptAsync(
                            "JSON.stringify({ guarded: window.open.toString().indexOf('lastInput') >= 0, active: navigator.userActivation && navigator.userActivation.isActive, webview: !!(window.chrome && window.chrome.webview) })");
                        Note("pop-up check: " + probe + $", blocked {tab.PopupsBlocked}, tabs {main.Tabs.Count} (before {before})");
                        Expect(tab.PopupsBlocked == 1 && main.Tabs.Count == before, $"the pop-up blocked (counted {tab.PopupsBlocked}, tabs {main.Tabs.Count} instead of {before}, page: {probe})");
                        Expect(await Display(tab, ".ad-box") == "none", "the general hiding rule");
                        Expect(await Display(tab, ".promo") == "none", "the site's own hiding rule");
                        Expect(await Display(tab, ".content") != "none", "the article still shown");
                        await Capture(main, "ads blocked");
                        var shield = main.ShowShield();
                        Pump();
                        Screenshots.All("Shield");
                        shield.IsOpen = false;

                        // Allow the site: nothing is blocked after reloading.
                        Blocker.SetAllowed(tab.PageHost, true);
                        Expect(Settings.AllowedSites.Contains("kudutest.example"), "the site is in the allowed list");
                        await tab.RefreshBlocking();
                        tab.Reload();
                        await Wait(() => !tab.IsLoading && main.Tabs.Count == before + 1, 20, "the reload, and the pop-up opening this time");
                        await Task.Delay(500);
                        Expect(tab.Blocked == 0, "nothing blocked on an allowed site");
                        Expect(await Display(tab, ".ad-box") != "none", "nothing hidden on an allowed site");
                    }
                    finally
                    {
                        Blocker.SetAllowed("kudutest.example", false);
                        foreach (var t in main.Tabs.Where(t => !keep.Contains(t)).ToList()) main.CloseTab(t);
                        main.Select(keep.Last());
                    }
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
                    await Wait(() => again.Tabs[0].View?.CoreWebView2 != null && !again.Tabs[0].IsLoading && again.Tabs[0].Url.EndsWith("first.html"), 20,
                        "the other tab to load when clicked");
                    // Closing a window while a page is still starting mustn't cause an error.
                    var starting = again.NewTab(Session.Load().Tabs[1].Url, true);
                    again.Close();
                    await Task.Delay(1500);
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
            Settings.AllowedSites = savedAllowed;
            (Settings.BlockAds, Settings.HideCookieBanners, Settings.BlockPopups) = savedBlocking;
            _log.AppendLine(Errors.Count == 0 ? "SELF-TEST PASSED" : $"SELF-TEST FAILED ({Errors.Count})");
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(AppInfo.ExePath) ?? ".", "self-test.log"), _log.ToString()); }
            catch { /* read-only folder */ }
            await Task.Delay(500); // let the engine let go of the profile folder
            try { Directory.Delete(_temp, true); }
            catch { /* ignore */ }
            return Errors.Count == 0 ? 0 : 1;
        }

        private static async Task<string> Display(BrowserTab tab, string selector)
        {
            var json = await tab.View.CoreWebView2.ExecuteScriptAsync($"getComputedStyle(document.querySelector('{selector}')).display");
            return json.Trim('"');
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
