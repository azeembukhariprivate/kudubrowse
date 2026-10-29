using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kudubrowse.Controls;
using Kudubrowse.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Kudubrowse.Views
{
    /// <summary>
    /// One tab: its page (an Edge WebView2, made the first time the tab is shown, so tabs brought back from last
    /// time don't all load at once) and its header in the tab strip.
    /// </summary>
    public sealed class BrowserTab
    {
        private readonly Image _icon = new Image { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        private readonly Glyph _blankIcon;
        private readonly TextBlock _title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
        private Task _ready;
        private string _pending;
        private bool _closed;
        private CoreWebView2Environment _env;
        private string _mainUrl;
        private string _hidingScriptId, _popupScriptId;

        public BrowserTab(string url = null, string title = null, string room = null, bool isPrivate = false)
        {
            Url = url ?? string.Empty;
            Room = Rooms.ValidId(room) ? room : Rooms.DefaultId;
            IsPrivate = isPrivate;
            Title = string.IsNullOrEmpty(title) ? (string.IsNullOrEmpty(url) ? "New tab" : AddressBar.Display(url)) : title;

            _blankIcon = Ui.Icon("IconGlobe", 15);
            var close = Ui.Button(null, "IconClose", "GhostButton");
            close.Padding = new Thickness(0);
            close.Width = close.Height = close.MinHeight = 22;
            close.MinWidth = 22;
            close.ToolTip = "Close tab (Ctrl+W)";
            close.Click += (s, e) => CloseClicked?.Invoke(this);

            var grid = Ui.Columns(Ui.Px(22), Ui.Star(), Ui.Auto);
            grid.Children.Add(_icon.At(0, 0));
            grid.Children.Add(_blankIcon.At(0, 0));
            grid.Children.Add(_title.Margin(6, 0, 4, 0).At(0, 1));
            grid.Children.Add(close.At(0, 2));
            Grid.SetColumnSpan(_groupLine, 3);
            grid.Children.Add(_groupLine);
            Header = new Border
            {
                Child = grid,
                Height = 34,
                Padding = new Thickness(10, 0, 4, 0),
                Margin = new Thickness(0, 6, 2, 0),
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Arrow
            };
            Header.MouseLeftButtonDown += (s, e) => Selected?.Invoke(this);
            Header.MouseUp += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle) CloseClicked?.Invoke(this);
            };
            ShowRoom();
            Header.MouseEnter += (s, e) => UpdateLook();
            Header.MouseLeave += (s, e) => UpdateLook();
            Refresh();
        }

        public Border Header { get; }

        /// <summary>The room this tab is in (its logins and site data are kept apart from other rooms').</summary>
        public string Room { get; }
        /// <summary>A private tab keeps nothing: no history, and its site data goes when the private window closes.</summary>
        public bool IsPrivate { get; }

        /// <summary>The tab group it's in, if any.</summary>
        public TabGroup Group { get; set; }

        /// <summary>
        /// A coloured line on top of the tab shows its room (none for the first room; private tabs are dark purple),
        /// and one underneath shows its tab group.
        /// </summary>
        public void ShowRoom()
        {
            var room = BrowserRooms.All.Find(Room);
            var color = IsPrivate ? "#5B21B6" : room.IsDefault ? null : room.Color;
            Header.BorderThickness = new Thickness(0, color == null ? 0 : 3, 0, 0);
            Header.BorderBrush = color == null ? null : new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            _groupLine.Background = Group == null ? null : new SolidColorBrush((Color)ColorConverter.ConvertFromString(Group.Color));
            _roomName = IsPrivate ? "Private" : room.IsDefault ? null : room.Name;
            Refresh();
        }

        private string _roomName;
        private readonly Border _groupLine = new Border { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-6, 0, 0, 0), CornerRadius = new CornerRadius(1.5), IsHitTestVisible = false };
        /// <summary>The page, once the tab has been shown.</summary>
        public WebView2 View { get; private set; }

        public string Url { get; private set; }
        public string Title { get; private set; }
        public bool IsLoading { get; private set; }
        public bool IsActive { get; private set; }
        public bool IsBlank => string.IsNullOrEmpty(Url) || Url == "about:blank";
        /// <summary>The host of the page shown (or being opened).</summary>
        public string PageHost { get; private set; } = string.Empty;
        /// <summary>Blocking is switched off for this site (from the shield button).</summary>
        public bool SiteAllowed { get; private set; }
        /// <summary>Ads, trackers and cookie banners blocked on this page.</summary>
        public int Blocked { get; private set; }
        /// <summary>Pop-ups this page tried to open by itself.</summary>
        public int PopupsBlocked { get; private set; }
        public bool CanGoBack => View?.CoreWebView2?.CanGoBack == true;
        public bool CanGoForward => View?.CoreWebView2?.CanGoForward == true;

        /// <summary>Something the toolbar shows changed (address, title, loading, back/forward).</summary>
        public event Action<BrowserTab> Changed;
        public event Action<BrowserTab> Selected;
        /// <summary>The page was made; the window puts it in place.</summary>
        public event Action<BrowserTab> ViewCreated;
        public event Action<BrowserTab> CloseClicked;
        /// <summary>The page asked for a new window (a link with target=_blank, window.open).</summary>
        public event Action<BrowserTab, CoreWebView2NewWindowRequestedEventArgs> NewWindowRequested;
        /// <summary>The page asked to close itself (window.close).</summary>
        public event Action<BrowserTab> PageClosed;
        public event Action<BrowserTab, bool> FullScreenChanged;
        /// <summary>Something more was blocked on the page.</summary>
        public event Action<BrowserTab> BlockedChanged;

        // ================================================================ passwords

        /// <summary>A login box got focus: where it is on the page (in page pixels), and whether it's for a new password.</summary>
        public event Action<BrowserTab, Point, bool> LoginFieldFocused;
        public event Action<BrowserTab> LoginFieldLeft;
        /// <summary>A login was sent on the page: its address, the name and the password typed.</summary>
        public event Action<BrowserTab, string, string, string> LoginSent;

        /// <summary>A login the page sent that hasn't been saved or dismissed yet.</summary>
        public (string Url, string Username, string Password, Core.Passwords.Vault.SaveKind Kind)? PendingLogin { get; set; }

        private static readonly System.Web.Script.Serialization.JavaScriptSerializer Json = new System.Web.Script.Serialization.JavaScriptSerializer();

        private void OnPageMessage(string source, string json)
        {
            System.Collections.Generic.Dictionary<string, object> m;
            try { m = Json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json); }
            catch (ArgumentException) { return; }
            if (m == null || !m.TryGetValue("kudu", out var kind) || Core.Passwords.Vault.OriginOf(source) == null) return;
            double Num(string k) => m.TryGetValue(k, out var v) && v != null ? Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture) : 0;
            string Text(string k) => m.TryGetValue(k, out var v) ? v as string ?? string.Empty : string.Empty;
            switch (kind as string)
            {
                case "field":
                    LoginFieldFocused?.Invoke(this, new Point(Num("x"), Num("y")), m.TryGetValue("isNew", out var n) && n is bool b && b);
                    break;
                case "blur":
                    LoginFieldLeft?.Invoke(this);
                    break;
                case "login":
                    LoginSent?.Invoke(this, source, Text("username"), Text("password"));
                    break;
                case "reading":
                    // Reading mode's text size and colours, kept for next time (both are checked when stored).
                    Settings.ReadingSize = (int)Num("size");
                    Settings.ReadingTheme = Text("theme");
                    break;
            }
        }

        /// <summary>Fills the focused login box with a saved login (only called after the person picks it).</summary>
        public void Fill(string username, string password) =>
            View?.CoreWebView2?.PostWebMessageAsJson(Json.Serialize(new { kudu = "fill", username, password }));

        /// <summary>Fills the new-password boxes with a strong password.</summary>
        public void FillGenerated(string password) =>
            View?.CoreWebView2?.PostWebMessageAsJson(Json.Serialize(new { kudu = "fill", generated = true, password }));

        public void CountBlockedPopup()
        {
            PopupsBlocked++;
            BlockedChanged?.Invoke(this);
        }

        // ================================================================ sleeping

        /// <summary>Tabs not looked at for this long go to sleep (they use almost no processor and less memory).</summary>
        public static TimeSpan SleepAfter { get; set; } = TimeSpan.FromMinutes(10);
        public DateTime LastShown { get; private set; } = DateTime.UtcNow;
        public bool IsSleeping { get; private set; }

        /// <summary>Puts the page to sleep if it's been hidden long enough and isn't playing sound.</summary>
        public async Task<bool> SleepIfIdle()
        {
            var core = View?.CoreWebView2;
            if (core == null || IsActive || IsSleeping || _closed || DateTime.UtcNow - LastShown < SleepAfter) return false;
            if (core.IsDocumentPlayingAudio || IsLoading) return false;
            try
            {
                if (!await core.TrySuspendAsync()) return false;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException)
            {
                return false;
            }
            IsSleeping = true;
            Header.Opacity = 0.6;
            Refresh();
            return true;
        }

        private void Wake()
        {
            if (!IsSleeping) return;
            IsSleeping = false;
            Header.Opacity = 1;
            try { View?.CoreWebView2?.Resume(); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException) { }
            Refresh();
        }

        // ================================================================ site data

        /// <summary>Deletes this site's cookies and stored data in this tab's room (signing out of it), then reloads.</summary>
        public async Task ClearSiteData()
        {
            var core = View?.CoreWebView2;
            if (core == null || string.IsNullOrEmpty(PageHost)) return;
            var site = Core.Blocking.Domains.Registrable(PageHost);
            foreach (var c in await core.CookieManager.GetCookiesAsync(null))
            {
                var d = c.Domain.TrimStart('.').ToLowerInvariant();
                if (d == site || d.EndsWith("." + site)) core.CookieManager.DeleteCookie(c);
            }
            foreach (var origin in new[] { "https://" + PageHost, "http://" + PageHost, "https://" + site, "https://www." + site }.Distinct())
                await core.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin",
                    Json.Serialize(new { origin, storageTypes = "local_storage,indexeddb,websql,service_workers,cache_storage,file_systems,shader_cache" }));
            core.Reload();
        }

        public SavedTab ToSaved() => new SavedTab { Url = Url, Title = Title, Room = Room, Group = Group?.Name, GroupColor = Group?.Color };

        public void SetActive(bool active)
        {
            if (IsActive != active) LastShown = DateTime.UtcNow;
            IsActive = active;
            if (active) Wake();
            UpdateLook();
        }

        /// <summary>Makes the page (once). Pages opened by another page come with their own engine window already asked for.</summary>
        public Task EnsureView()
        {
            if (_ready != null) return _ready;
            View = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.White };
            // The page needs to be in the window before the engine can start in it.
            ViewCreated?.Invoke(this);
            _ready = Create();
            return _ready;
        }

        private async Task Create()
        {
            var env = _env = await Web.Environment;
            var view = View;
            try
            {
                var options = env.CreateCoreWebView2ControllerOptions();
                options.ProfileName = Room;
                options.IsInPrivateModeEnabled = IsPrivate;
                await view.EnsureCoreWebView2Async(env, options);
            }
            catch (Exception) when (_closed)
            {
                return; // The tab (or its window) was closed while the page was still starting.
            }
            if (_closed) return;
            var core = view.CoreWebView2;
            core.Settings.IsStatusBarEnabled = true;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.DocumentTitleChanged += (s, e) =>
            {
                if (!IsPrivate) BrowserLibrary.History.SetTitle(core.Source, core.DocumentTitle);
                Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? AddressBar.Display(core.Source) : core.DocumentTitle;
                Refresh();
            };
            core.SourceChanged += (s, e) =>
            {
                Url = core.Source;
                Refresh();
            };
            core.HistoryChanged += (s, e) => Refresh();
            core.NavigationStarting += (s, e) =>
            {
                IsLoading = true;
                _mainUrl = e.Uri;
                PageHost = HostOf(e.Uri);
                SiteAllowed = Blocker.IsAllowed(PageHost);
                if (!e.IsRedirected)
                {
                    Blocked = 0;
                    PopupsBlocked = 0;
                }
                Refresh();
                BlockedChanged?.Invoke(this);
            };
            // Blocking: every request is checked before it's sent; the page's hiding rules go in once it's read.
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += OnRequest;
            core.DOMContentLoaded += async (s, e) =>
            {
                var script = Blocker.SiteScript(PageHost);
                if (script == null || _closed) return;
                try { await core.ExecuteScriptAsync(script); }
                catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException) { }
            };
            core.WebMessageReceived += (s, e) =>
            {
                string message = null;
                try { message = e.TryGetWebMessageAsString(); }
                catch (ArgumentException) { }
                if (message == Blocker.PopupMessage)
                {
                    CountBlockedPopup();
                    return;
                }
                if (message == null) OnPageMessage(e.Source, e.WebMessageAsJson);
            };
            // Passwords: notices login boxes and sent logins (see PasswordScript).
            try { await core.AddScriptToExecuteOnDocumentCreatedAsync(PasswordScript.Source); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException) { }
            await UpdateHidingScript();
            Blocker.Changed += OnBlockerChanged;
            core.NavigationCompleted += (s, e) =>
            {
                // Pages opened are kept in the history (only ordinary web pages).
                if (e.IsSuccess && !IsPrivate) BrowserLibrary.History.Add(core.Source, core.DocumentTitle);
                IsLoading = false;
                Refresh();
            };
            core.FaviconChanged += async (s, e) => await LoadFavicon();
            core.DownloadStarting += (s, e) => Downloads.Start(this, core, e);
            core.NewWindowRequested += (s, e) => NewWindowRequested?.Invoke(this, e);
            core.WindowCloseRequested += (s, e) => PageClosed?.Invoke(this);
            core.ContainsFullScreenElementChanged += (s, e) => FullScreenChanged?.Invoke(this, core.ContainsFullScreenElement);
            if (_pending != null)
            {
                var url = _pending;
                _pending = null;
                core.Navigate(url);
            }
        }

        private void OnRequest(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (!Blocker.Ready || SiteAllowed) return;
            var uri = e.Request.Uri;
            // The page itself is never blocked, only what it asks for.
            if (e.ResourceContext == CoreWebView2WebResourceContext.Document && uri == _mainUrl) return;
            if (!Blocker.ShouldBlock(uri, PageHost, e.ResourceContext)) return;
            e.Response = _env.CreateWebResourceResponse(null, 403, "Blocked", string.Empty);
            Blocked++;
            BlockedChanged?.Invoke(this);
        }

        private async void OnBlockerChanged() => await RefreshBlocking();

        /// <summary>Takes in changed blocking settings (before the page is reloaded).</summary>
        public async Task RefreshBlocking()
        {
            if (_closed || View?.CoreWebView2 == null) return;
            SiteAllowed = Blocker.IsAllowed(PageHost);
            await UpdateHidingScript();
        }

        /// <summary>Puts the current general hiding rules into every page this tab opens from now on.</summary>
        private async Task UpdateHidingScript()
        {
            var core = View?.CoreWebView2;
            if (core == null) return;
            try
            {
                if (_hidingScriptId != null) core.RemoveScriptToExecuteOnDocumentCreated(_hidingScriptId);
                if (_popupScriptId != null) core.RemoveScriptToExecuteOnDocumentCreated(_popupScriptId);
                _hidingScriptId = _popupScriptId = null;
                var script = Blocker.DocumentScript();
                if (script != null) _hidingScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(script);
                var popups = Blocker.PopupScript();
                if (popups != null) _popupScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(popups);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException) { }
        }

        public static string HostOf(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https") ? u.Host.ToLowerInvariant() : string.Empty;

        private async Task LoadFavicon()
        {
            try
            {
                using (var stream = await View.CoreWebView2.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png))
                {
                    if (stream == null || stream.Length == 0)
                    {
                        _icon.Source = null;
                    }
                    else
                    {
                        var copy = new MemoryStream();
                        await stream.CopyToAsync(copy);
                        copy.Position = 0;
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = copy;
                        image.EndInit();
                        image.Freeze();
                        _icon.Source = image;
                    }
                }
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                _icon.Source = null; // A broken site icon just isn't shown.
            }
            Refresh();
        }

        public async void Navigate(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            Url = url;
            IsLoading = true;
            Refresh();
            if (View?.CoreWebView2 != null)
            {
                View.CoreWebView2.Navigate(url);
                return;
            }
            _pending = url;
            await EnsureView();
        }

        /// <summary>Shows the page saved from last time (when the tab is first shown).</summary>
        public void LoadIfNeeded()
        {
            if (View == null && !IsBlank) Navigate(Url);
        }

        public void GoBack() => View?.CoreWebView2?.GoBack();
        public void GoForward() => View?.CoreWebView2?.GoForward();

        public void Reload()
        {
            if (View?.CoreWebView2 != null) View.CoreWebView2.Reload();
            else LoadIfNeeded();
        }

        public void Stop() => View?.CoreWebView2?.Stop();

        public double Zoom
        {
            get => View?.ZoomFactor ?? 1;
            set
            {
                if (View != null) View.ZoomFactor = Math.Max(0.25, Math.Min(5, value));
                Refresh();
            }
        }

        public void Close()
        {
            _closed = true;
            Blocker.Changed -= OnBlockerChanged;
            try { View?.Dispose(); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException) { }
            View = null;
        }

        private void Refresh()
        {
            _title.Text = Title;
            Header.ToolTip = (_roomName == null ? string.Empty : "[" + _roomName + "] ") + Title + (IsBlank ? string.Empty : "\n" + AddressBar.Display(Url)) + (IsSleeping ? "\n(sleeping: wakes up when clicked)" : string.Empty);
            _icon.Visibility = _icon.Source != null ? Visibility.Visible : Visibility.Collapsed;
            _blankIcon.Visibility = _icon.Source != null ? Visibility.Collapsed : Visibility.Visible;
            _blankIcon.Opacity = IsLoading ? 0.45 : 1;
            System.Windows.Automation.AutomationProperties.SetName(Header, "Tab: " + Title);
            Changed?.Invoke(this);
        }

        private void UpdateLook()
        {
            if (IsActive) Header.SetResourceReference(Border.BackgroundProperty, "SurfaceBg");
            else if (Header.IsMouseOver) Header.SetResourceReference(Border.BackgroundProperty, "HoverBg");
            else Header.Background = Brushes.Transparent;
            _title.SetResourceReference(TextBlock.ForegroundProperty, IsActive ? "Text" : "SubtleText");
        }
    }
}
