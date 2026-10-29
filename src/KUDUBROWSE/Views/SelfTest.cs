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

        public static async Task<int> Run(string chrome)
        {
            Running = true;
            _log = new StringBuilder();
            _temp = Path.Combine(Path.GetTempPath(), "kudubrowse-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_temp);
            Web.ProfileFolder = Path.Combine(_temp, "profile");
            Session.DefaultFile = Path.Combine(_temp, "session.txt");
            Core.Blocking.BlockLists.DownloadFolder = Path.Combine(_temp, "lists");
            BrowserLibrary.Folder = Path.Combine(_temp, "library");
            BrowserLibrary.Reload();
            if (chrome != null) Core.Library.ChromeImport.UserDataFolder = Path.GetFullPath(chrome);
            Core.Library.History.KeepFor = TimeSpan.FromDays(36500); // the made-up Chrome history is old
            var wasRegistered = ShellIntegration.IsRegistered;
            PasswordStore.Reload();
            BrowserRooms.Reload();
            Downloads.Folder = Path.Combine(_temp, "downloads");
            Downloads.ListFile = Path.Combine(_temp, "downloads.json");
            Downloads.Reload();
            var savedDownloads = (Settings.AskWhereToSave, Settings.SortDownloads, Settings.FastDownloads);
            (Settings.AskWhereToSave, Settings.SortDownloads, Settings.FastDownloads) = (false, false, true);
            Verify.AutoAnswer = true;
            var savedOffer = Settings.OfferToSavePasswords;
            Settings.OfferToSavePasswords = true;
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
                await Step("History, bookmarks, the bookmarks bar and suggestions", async () =>
                {
                    var keep = main.Tabs.ToList();
                    try
                    {
                        var tab = main.NewTab(select: true);
                        await tab.EnsureView();
                        tab.View.CoreWebView2.SetVirtualHostNameToFolderMapping("kudutest.example", _temp, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                        main.Go("https://kudutest.example/second.html");
                        await Wait(() => tab.Title == "Second page" && !tab.IsLoading, 20, "the second page");
                        await Wait(() => BrowserLibrary.History.Search("kudutest.example/second").Count == 1, 10, "the page in the history");
                        Expect(BrowserLibrary.History.Search("second").First().Title == "Second page", "its title in the history");
                        var panel = main.ShowBookmarkPanel();
                        Pump();
                        Screenshots.All("Bookmark added");
                        panel.IsOpen = false;
                        Pump();
                        Expect(BrowserLibrary.Bookmarks.Contains("https://kudutest.example/second.html"), "bookmarked");
                        Expect(main.BarVisible, "the bookmarks bar shows");
                        var s = main.TypeInAddress("kudutest");
                        Pump();
                        Screenshots.All("Suggestions");
                        main.CloseSuggestions();
                        Expect(s.Count >= 2 && s[0].Url == "https://kudutest.example/second.html", "the bookmark suggested first (got " + string.Join(", ", s.Select(x => x.Url)) + ")");
                        var lib = LibraryWindow.Show(main, false);
                        OffScreen(lib);
                        Pump();
                        lib.Search("second");
                        Expect(lib.ItemCount >= 2, "the history window lists the page under its day");
                        Screenshots.One(lib, "History");
                        LibraryWindow.Show(main, true);
                        Pump();
                        Expect(lib.ShowingBookmarks && lib.ItemCount == 1, "the bookmarks list");
                        Screenshots.One(lib, "Bookmarks");
                        lib.Close();
                        BrowserLibrary.SaveAll();
                        BrowserLibrary.Reload();
                        Expect(BrowserLibrary.History.Search("second").Count == 1 && BrowserLibrary.Bookmarks.Contains("https://kudutest.example/second.html"), "both kept after closing");
                    }
                    finally
                    {
                        foreach (var t in main.Tabs.Where(t => !keep.Contains(t)).ToList()) main.CloseTab(t);
                        main.Select(keep.Last());
                    }
                });
                await Step("Bring bookmarks and history from Chrome", async () =>
                {
                    Expect(chrome != null, "a made-up Chrome profile (--chrome folder)");
                    var w = new ImportWindow(main);
                    OffScreen(w);
                    w.Show();
                    Pump();
                    Screenshots.One(w, w.Title);
                    await w.Run();
                    Note(ImportWindow.LastResult);
                    Expect(ImportWindow.LastResult.Contains("4 bookmarks added") && ImportWindow.LastResult.Contains("3001 pages added"), "everything brought over: " + ImportWindow.LastResult);
                    Expect(BrowserLibrary.Bookmarks.Bar.Children.Any(b => b.Title == "Kerala Tourism"), "Chrome's bar on our bar");
                    await w.Run();
                    Expect(ImportWindow.LastResult.Contains("0 bookmarks added") && ImportWindow.LastResult.Contains("0 pages added"), "nothing twice: " + ImportWindow.LastResult);
                    Screenshots.One(w, w.Title + " done");
                    w.Close();
                    ShowAndClose(new WelcomeWindow(main));
                });
                await Step("Passwords: offered to save after signing in, filled when picked, strong ones suggested", async () =>
                {
                    var keep = main.Tabs.ToList();
                    try
                    {
                        Page("login.html", "Sign in", "<form action='second.html'><input id='user' autocomplete='username'><input id='pass' type='password'><button id='go'>Sign in</button></form>");
                        Page("signup.html", "Sign up", "<form><input id='email' type='email'><input id='p1' type='password' autocomplete='new-password'><input id='p2' type='password' autocomplete='new-password'><button>Join</button></form>");
                        var tab = main.NewTab(select: true);
                        await tab.EnsureView();
                        tab.View.CoreWebView2.SetVirtualHostNameToFolderMapping("kudutest.example", _temp, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                        main.Go("https://kudutest.example/login.html");
                        await Wait(() => tab.Title == "Sign in" && !tab.IsLoading, 20, "the sign-in page");
                        await tab.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('user').value = 'azeem'; document.getElementById('pass').value = 'Kochi-2025!'; document.getElementById('go').click();");
                        await Wait(() => tab.PendingLogin != null, 10, "the offer to save the password");
                        await Wait(() => tab.Title == "Second page", 10, "the page after signing in");
                        Expect(main.SaveBarShown, "the save bar shows");
                        Screenshots.All("Save password");
                        main.SavePendingLogin();
                        Expect(!main.SaveBarShown, "the bar goes away");
                        PasswordStore.Reload();
                        var saved = PasswordStore.Vault.For("https://kudutest.example/login.html");
                        Expect(saved.Count == 1 && saved[0].Username == "azeem" && saved[0].Password == "Kochi-2025!", "saved, encrypted, and read back");
                        var raw = File.ReadAllText(PasswordStore.Vault.File);
                        Expect(!raw.Contains("Kochi-2025") && !raw.Contains("azeem"), "the file isn't readable as it is");

                        // Back on the sign-in page: the list next to the box, and filling once a login is picked.
                        main.Go("https://kudutest.example/login.html");
                        await Wait(() => tab.Title == "Sign in" && !tab.IsLoading, 20, "the sign-in page again");
                        Expect(await Value(tab, "pass") == "", "nothing filled before a login is picked");
                        await tab.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('user').focus()");
                        await Wait(() => main.FillChoices.Contains("login:azeem"), 10, "the saved login offered");
                        Screenshots.All("Saved login offered");
                        main.ChooseFill("login:azeem");
                        await WaitAsync(async () => await Value(tab, "pass") == "Kochi-2025!", 10, "the password filled");
                        Expect(await Value(tab, "user") == "azeem", "the name filled");

                        // A page that's not the same site gets nothing.
                        main.Go("https://kudutest.example/second.html");
                        await Wait(() => tab.Title == "Second page" && !tab.IsLoading, 20, "another page");
                        Expect(PasswordStore.Vault.For("https://other.example/").Count == 0, "no logins for another site");

                        // Signing up: a strong password is suggested and fills both boxes.
                        main.Go("https://kudutest.example/signup.html");
                        await Wait(() => tab.Title == "Sign up" && !tab.IsLoading, 20, "the sign-up page");
                        await tab.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('p1').focus()");
                        await Wait(() => main.FillChoices.Contains("generated"), 10, "a strong password suggested");
                        main.ChooseFill("generated");
                        await WaitAsync(async () => (await Value(tab, "p1")).Length == 16, 10, "the strong password filled");
                        Expect(await Value(tab, "p1") == await Value(tab, "p2"), "both boxes the same");

                        var w = PasswordsWindow.Show(main);
                        OffScreen(w);
                        Pump();
                        Expect(w.RowCount == 1, "the passwords window lists it");
                        Screenshots.One(w, "Passwords");
                        var csv = Path.Combine(_temp, "Chrome Passwords.csv");
                        File.WriteAllText(csv, "name,url,username,password,note\nshop,https://shop.example/login,me,Pa55-word!x,\nbank,https://bank.example/,me,another-Pa55,\n");
                        var imp = new ImportPasswordsWindow(w);
                        var msg = imp.Import(csv, true);
                        Note(msg);
                        Expect(msg.StartsWith("2 passwords brought over") && !File.Exists(csv), "brought over from the file, and the file deleted: " + msg);
                        Pump();
                        Expect(w.RowCount == 3, "three passwords listed");
                        Screenshots.One(w, "Passwords after bringing them from Chrome");
                        w.Close();
                    }
                    finally
                    {
                        foreach (var t in main.Tabs.Where(t => !keep.Contains(t)).ToList()) main.CloseTab(t);
                        main.Select(keep.Last());
                    }
                });
                await Step("Rooms keep logins apart; private windows keep nothing", async () =>
                {
                    var keep = main.Tabs.ToList();
                    try
                    {
                        async Task<BrowserTab> Open(MainWindow w, string room, string url)
                        {
                            var t = w.NewTab(select: true, room: room);
                            await t.EnsureView();
                            t.View.CoreWebView2.SetVirtualHostNameToFolderMapping("kudutest.example", _temp, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                            w.Go(url);
                            await Wait(() => t.Url.StartsWith(url) && !t.IsLoading, 20, "the page in room " + room);
                            return t;
                        }
                        async Task<string> Cookie(BrowserTab t) => JsonText(await t.View.CoreWebView2.ExecuteScriptAsync("document.cookie"));

                        var personal = await Open(main, Rooms.DefaultId, "https://kudutest.example/second.html");
                        await personal.View.CoreWebView2.ExecuteScriptAsync("document.cookie = 'who=personal; max-age=3600; path=/'");
                        var work = await Open(main, "room-1", "https://kudutest.example/second.html");
                        Expect(await Cookie(work) == string.Empty, "the Work room doesn't see Personal's cookie (it saw \"" + await Cookie(work) + "\")");
                        await work.View.CoreWebView2.ExecuteScriptAsync("document.cookie = 'who=work; max-age=3600; path=/'");
                        Expect(await Cookie(personal) == "who=personal", "Personal keeps its own");
                        Expect(work.Header.BorderThickness.Top > 0 && personal.Header.BorderThickness.Top == 0, "the Work tab shows its room's colour");
                        Screenshots.All("Rooms");

                        // A private window: no history, not kept for next time, and its cookies go when it closes.
                        var priv = main.NewPrivateWindow();
                        OffScreen(priv);
                        var p = await Open(priv, Rooms.DefaultId, "https://kudutest.example/second.html?private=1");
                        Expect(p.IsPrivate && priv.Title.Contains("(private)"), "a private tab");
                        await p.View.CoreWebView2.ExecuteScriptAsync("document.cookie = 'who=secret; max-age=3600; path=/'");
                        Expect(await Cookie(personal) == "who=personal", "the private window doesn't touch the normal one");
                        Screenshots.One(priv, "Private window");
                        Expect(BrowserLibrary.History.Search("private=1").Count == 0, "no history from the private window");
                        MainWindow.SaveAll();
                        Expect(!Session.Load().Tabs.Any(t => t.Url.Contains("private=1")), "private tabs aren't kept for next time");
                        priv.Close();
                        await Task.Delay(1500);
                        var priv2 = main.NewPrivateWindow();
                        OffScreen(priv2);
                        var p2 = await Open(priv2, Rooms.DefaultId, "https://kudutest.example/second.html?private=2");
                        Expect(await Cookie(p2) == string.Empty, "a new private window starts with nothing (it saw \"" + await Cookie(p2) + "\")");
                        priv2.Close();

                        // Clearing one site's data signs out of it.
                        await personal.ClearSiteData();
                        await Wait(() => !personal.IsLoading, 10, "the reload after clearing");
                        Expect(await Cookie(personal) == string.Empty, "the site's cookie cleared");
                        Expect(await Cookie(work) == "who=work", "other rooms untouched");
                    }
                    finally
                    {
                        foreach (var t in main.Tabs.Where(t => !keep.Contains(t)).ToList()) main.CloseTab(t);
                        main.Select(keep.Last());
                    }
                });
                await Step("Downloads, sleeping tabs and one running copy", async () =>
                {
                    var keep = main.Tabs.ToList();
                    try
                    {
                        File.WriteAllText(Path.Combine(_temp, "notes.txt"), "Downloaded by the self-test.");
                        Page("files.html", "Files", "<a id='dl' href='notes.txt' download>Download the notes</a>");
                        var tab = main.NewTab(select: true);
                        await tab.EnsureView();
                        tab.View.CoreWebView2.SetVirtualHostNameToFolderMapping("kudutest.example", _temp, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                        main.Go("https://kudutest.example/files.html");
                        await Wait(() => tab.Title == "Files" && !tab.IsLoading, 20, "the files page");
                        await tab.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('dl').click()");
                        await Wait(() => Downloads.Items.Any(i => i.Done && i.FileName.StartsWith("notes")), 20, "the download to finish");
                        var item = Downloads.Items.First(i => i.Done);
                        Expect(File.ReadAllText(item.Path) == "Downloaded by the self-test.", "the file arrived whole");
                        Expect(item.Path.StartsWith(Downloads.Folder), "in the downloads folder");
                        var dw = DownloadsWindow.Show(main);
                        OffScreen(dw);
                        Pump();
                        Expect(dw.ItemCount == 1, "listed");
                        Screenshots.One(dw, "Downloads");
                        dw.Close();

                        // Sleeping: a tab that isn't shown goes to sleep, and wakes when clicked.
                        BrowserTab.SleepAfter = TimeSpan.Zero;
                        var other = main.NewTab("https://kudutest.example/second.html", true);
                        main.Select(tab);
                        await Task.Delay(300);
                        await main.SleepIdleTabs();
                        Expect(keep.Concat(new[] { other }).Any(t => t.IsSleeping), "a hidden tab went to sleep");
                        var sleeper = keep.Concat(new[] { other }).First(t => t.IsSleeping);
                        main.Select(sleeper);
                        Expect(!sleeper.IsSleeping, "woke up when shown");
                        if (sleeper.View?.CoreWebView2 != null)
                            Expect(await sleeper.View.CoreWebView2.ExecuteScriptAsync("1 + 1") == "2", "the woken page works");
                        BrowserTab.SleepAfter = TimeSpan.FromMinutes(10);

                        // One running copy: a second start hands its address over and a tab opens here.
                        var pipe = "kudubrowse-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        using (var one = new SingleInstance(pipe))
                        {
                            one.Listen(a => main.Dispatcher.BeginInvoke(new Action(() => App.OpenFromOutside(a))));
                            await Task.Delay(300);
                            var before = MainWindow.Open.Sum(w => w.Tabs.Count);
                            Expect(SingleInstance.Send(pipe, new[] { "example.org" }), "the address handed over");
                            await Wait(() => MainWindow.Open.Sum(w => w.Tabs.Count) == before + 1, 10, "a new tab for it");
                            Expect(MainWindow.Open.SelectMany(w => w.Tabs).Any(t => t.Url == "https://example.org"), "opened at the address given");
                        }
                    }
                    finally
                    {
                        foreach (var w in MainWindow.Open.ToList())
                            foreach (var t in w.Tabs.Where(t => !keep.Contains(t)).ToList()) w.CloseTab(t);
                        main.Select(keep.Last());
                    }
                });
                await Step("Download manager: big files in pieces, pause and carry on, sorting, trying again, kept for next time", async () =>
                {
                    var keep = main.Tabs.ToList();
                    using (var server = new TestServer())
                    {
                        try
                        {
                            var big = TestServer.Made(26 * 1024 * 1024);
                            var bigItem = server.Add("big.bin", new TestServer.Item { Body = big, SlowMs = 20, Disposition = "attachment; filename=\"big.bin\"" });
                            var photo = TestServer.Made(200000, 4);
                            server.Add("photo.png", new TestServer.Item { Body = photo, Type = "image/png", Disposition = "attachment; filename=\"photo.png\"" });
                            var plain = TestServer.Made(3 * 1024 * 1024, 6);
                            server.Add("plain.zip", new TestServer.Item { Body = plain, Parts = false, Type = "application/zip", Disposition = "attachment; filename=\"plain.zip\"" });
                            server.Add("files.html", "<!doctype html><title>Download files</title><a id='big' href='/big.bin'>Big</a> <a id='photo' href='/photo.png'>Photo</a>");
                            var tab = main.NewTab(server.Url("files.html"), true);
                            await Wait(() => tab.Title == "Download files" && !tab.IsLoading && tab.View?.CoreWebView2 != null, 20, "the files page");
                            await tab.View.CoreWebView2.ExecuteScriptAsync("document.cookie = 'session=kudu; path=/'");

                            // A big file: the browser fetches it in pieces at once, with the site's cookies; paused and carried on.
                            await tab.View.CoreWebView2.ExecuteScriptAsync("document.getElementById('big').click()");
                            await Wait(() => Downloads.Items.Any(i => i.FileName == "big.bin"), 20, "the big download to start");
                            var item = Downloads.Items.First(i => i.FileName == "big.bin");
                            Note("big file: " + Downloads.LastChoice);
                            Expect(item.Record.Own, "fetched by the browser itself, not " + Downloads.LastChoice);
                            await Wait(() => item.Received > 2 * 1024 * 1024 || item.Done, 30, "some of it to arrive");
                            Expect(!item.Done, "still arriving when paused");
                            item.Pause();
                            await Wait(() => item.Paused, 10, "the pause");
                            Expect(item.Record.InPieces, "in pieces: " + item.Record.Pieces?.Count);
                            Expect(File.Exists(item.Record.PartFile), "what arrived is kept");
                            var dw = DownloadsWindow.Show(main);
                            OffScreen(dw);
                            Pump();
                            Screenshots.One(dw, "Downloads, paused");
                            bigItem.SlowMs = 0;
                            item.CarryOn();
                            await Wait(() => item.Done || item.Failed, 60, "the big download to finish");
                            Expect(item.Done, "finished: " + item.Record.Error);
                            Expect(File.ReadAllBytes(item.Path).SequenceEqual(big), "the file is exactly the site's");
                            Expect(AttachmentServices.HasMark(item.Path), "marked as from the internet, like the engine's downloads");
                            Expect(server.CookiesSeen.Any(c => c.StartsWith("/big.bin") && c.Contains("session=kudu")), "the site's cookies were sent");

                            // Sorted by type.
                            Settings.SortDownloads = true;
                            // Opened by its address: a second download started by a page script (not a real click) is held back
                            // by the engine's guard against pages starting many downloads.
                            main.Go(server.Url("photo.png"));
                            await Wait(() => Downloads.Items.Any(i => i.FileName == "photo.png" && i.Done), 20, "the picture");
                            var pic = Downloads.Items.First(i => i.FileName == "photo.png");
                            Expect(Path.GetDirectoryName(pic.Path) == Path.Combine(Downloads.Folder, "Pictures"), "in the Pictures folder: " + pic.Path);
                            Expect(!pic.Record.Own, "small files are left to the engine");
                            Settings.SortDownloads = false;

                            // Kept for next time, and a download that stopped when the browser closed can be tried again.
                            Downloads.SaveNow();
                            var kept = DownloadList.Load(Downloads.ListFile);
                            Expect(kept.Any(r => r.FileName == "big.bin" && r.State == DownloadState.Done), "the list is kept");
                            kept.Insert(0, new DownloadRecord { Url = server.Url("plain.zip"), File = Path.Combine(Downloads.Folder, "plain.zip"), State = DownloadState.Running });
                            DownloadList.Save(Downloads.ListFile, kept);
                            Downloads.Reload();
                            var stopped = Downloads.Items.First(i => i.FileName == "plain.zip");
                            Expect(stopped.Failed && !stopped.ThisSession, "comes back as not finished");
                            stopped.CarryOn();
                            await Wait(() => stopped.Done || (stopped.Failed && stopped.Record.Own), 30, "trying again");
                            Expect(stopped.Done, "finished when tried again: " + stopped.Record.Error);
                            Expect(File.ReadAllBytes(stopped.Path).SequenceEqual(plain), "whole");
                            dw.Close();
                            dw = DownloadsWindow.Show(main);
                            OffScreen(dw);
                            Pump();
                            Expect(dw.ItemCount >= 4, "all in the list: " + dw.ItemCount);
                            Screenshots.One(dw, "Downloads");
                            dw.Close();
                        }
                        finally
                        {
                            Settings.SortDownloads = false;
                            foreach (var t in main.Tabs.Where(t => !keep.Contains(t)).ToList()) main.CloseTab(t);
                            main.Select(keep.Last());
                        }
                    }
                });
                await Step("Tab groups and two pages side by side", async () =>
                {
                    var keep = main.Tabs.ToList();
                    try
                    {
                        var a = keep[0];
                        var b = keep[1];
                        var c = main.NewTab(second, true);
                        await Wait(() => c.Title == "Second page", 20, "a third tab");
                        var g = main.AddToGroup(a, null, "Reading");
                        main.AddToGroup(c, g);
                        Expect(main.Tabs.ToList().IndexOf(c) == main.Tabs.ToList().IndexOf(a) + 1, "grouped tabs sit together");
                        Expect(a.Group == g && c.Group == g && b.Group == null, "in the group");
                        MainWindow.SaveAll();
                        var saved = Session.Load().Tabs;
                        Expect(saved.Count(t => t.Group == "Reading" && t.GroupColor == g.Color) == 2, "the group is kept for next time");
                        main.Select(c);
                        main.ToggleGroup(g);
                        Expect(g.Collapsed && main.Current == b, "folding the group moves to a tab outside it");
                        Expect(a.Header.Visibility == Visibility.Collapsed && c.Header.Visibility == Visibility.Collapsed, "its tabs are folded away");
                        Screenshots.All("Tab group folded");
                        main.ToggleGroup(g);
                        Expect(a.Header.Visibility == Visibility.Visible, "unfolded");

                        main.Select(a);
                        await Wait(() => a.View != null, 20, "the first tab's page");
                        main.ShowSideBySide(b);
                        await Wait(() => b.View != null, 20, "the second tab's page");
                        Pump();
                        Expect(main.IsSplit && main.SplitPartner == b, "two pages side by side");
                        Expect(a.View.Visibility == Visibility.Visible && b.View.Visibility == Visibility.Visible, "both shown");
                        Expect(System.Windows.Controls.Grid.GetColumn(a.View) != System.Windows.Controls.Grid.GetColumn(b.View), "next to each other");
                        Expect(a.View.ActualWidth > 100 && b.View.ActualWidth > 100, "each has room");
                        main.Select(b);
                        Expect(main.IsSplit && main.SplitPartner == a, "picking the other half keeps both");
                        Screenshots.All("Side by side");
                        main.Select(c);
                        Expect(!main.IsSplit, "picking another tab ends it");
                        main.Select(a);
                        Expect(b.View.Visibility == Visibility.Hidden, "one page again");
                    }
                    finally
                    {
                        main.EndSideBySide();
                        foreach (var t in main.Tabs.Where(t => !keep.Contains(t)).ToList()) main.CloseTab(t);
                        foreach (var t in keep) main.RemoveFromGroup(t);
                        main.Select(keep.Last());
                    }
                });
                await Step("Reading mode, whole-page screenshot and PDF", async () =>
                {
                    var keep = main.Tabs.ToList();
                    try
                    {
                        var text = string.Concat(Enumerable.Range(1, 12).Select(i =>
                            $"<p>Paragraph {i} of the test article. The backwaters of Kerala are a chain of lagoons and lakes lying parallel to the Arabian Sea coast, " +
                            "linked by canals, rivers and inlets. Houseboats drift slowly past paddy fields, coconut groves and small villages along the water.</p>"));
                        var article = Page("article.html", "A test article", "<nav><a href='#'>Home</a> <a href='#'>News</a></nav><article><h2>On the water</h2>" + text +
                            "</article><div style='height:2600px'></div><footer>The end of the page</footer>");
                        var tab = main.NewTab(select: true);
                        main.Go(article);
                        await Wait(() => tab.Title == "A test article" && !tab.IsLoading, 20, "the article");
                        Expect(await main.ToggleReadingMode() == "open", "reading mode opens on an article");
                        Expect(await tab.View.CoreWebView2.ExecuteScriptAsync("!!document.getElementById('kudubrowse-reader')") == "true", "shown over the page");
                        await Capture(main, "reading mode");
                        Expect(await main.ToggleReadingMode() == "closed", "and closes again");
                        Expect(await tab.View.CoreWebView2.ExecuteScriptAsync("!!document.getElementById('kudubrowse-reader')") == "false", "gone from the page");

                        var png = await main.SaveScreenshot(Path.Combine(_temp, "whole page.png"));
                        Expect(png != null && File.Exists(png), "screenshot saved");
                        var bytes = File.ReadAllBytes(png);
                        Expect(bytes.Length > 24 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G', "a PNG picture");
                        var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
                        Note("screenshot height " + height);
                        Expect(height > 2600, "the whole page, not just the part on screen (" + height + " pixels tall)");
                        Directory.CreateDirectory(Screenshots.Folder);
                        File.Copy(png, Path.Combine(Screenshots.Folder, "web whole page.png"), true);

                        var pdf = await main.SavePdf(Path.Combine(_temp, "page.pdf"));
                        Expect(pdf != null && File.Exists(pdf), "PDF saved");
                        Expect(Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 5) == "%PDF-", "a PDF file");
                    }
                    finally
                    {
                        foreach (var t in main.Tabs.Where(t => !keep.Contains(t)).ToList()) main.CloseTab(t);
                        main.Select(keep.Last());
                    }
                });
                await Step("What KUDUBROWSE can do", () =>
                {
                    var f = new FeaturesWindow(main);
                    Expect(f.TryButtons >= 15, "\"Try it\" buttons, found " + f.TryButtons);
                    ShowAndClose(f);
                    return Task.CompletedTask;
                });
                await Step("Can be chosen as the default browser", () =>
                {
                    ShellIntegration.Register();
                    Expect(ShellIntegration.IsRegistered, "known to Windows as a browser");
                    Expect((ShellIntegration.OpenCommand() ?? string.Empty).Contains(Path.GetFileName(AppInfo.ExePath)) && ShellIntegration.OpenCommand().EndsWith("\"%1\""), "opens links with " + ShellIntegration.OpenCommand());
                    ShellIntegration.Unregister();
                    Expect(!ShellIntegration.IsRegistered, "removed again");
                    if (wasRegistered) ShellIntegration.Register();
                    return Task.CompletedTask;
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
                    ShowAndClose(new FeaturesWindow(main));
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

            (Settings.AskWhereToSave, Settings.SortDownloads, Settings.FastDownloads) = savedDownloads;
            Settings.Theme = savedTheme;
            Settings.RestoreTabs = savedRestore;
            Settings.OfferToSavePasswords = savedOffer;
            Verify.AutoAnswer = null;
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

        private static async Task<string> Value(BrowserTab tab, string id)
        {
            var json = await tab.View.CoreWebView2.ExecuteScriptAsync($"document.getElementById('{id}').value");
            return JsonText(json);
        }

        /// <summary>A JSON string from the page as plain text.</summary>
        private static string JsonText(string json) => new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<string>(json) ?? string.Empty;

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

        private static async Task WaitAsync(Func<Task<bool>> condition, int seconds, string what)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (!await condition())
            {
                if (DateTime.UtcNow > until) throw new Exception("timed out waiting for " + what);
                await Task.Delay(100);
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
