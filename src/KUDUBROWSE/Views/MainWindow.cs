using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Kudubrowse.Controls;
using Kudubrowse.Core;
using Microsoft.Web.WebView2.Core;

namespace Kudubrowse.Views
{
    /// <summary>A browser window: the tab strip, the toolbar with the address bar, and the page of the selected tab.</summary>
    public sealed partial class MainWindow : Window
    {
        /// <summary>Every open browser window, so the tabs of all of them are saved together.</summary>
        public static readonly List<MainWindow> Open = new List<MainWindow>();
        public static readonly ClosedTabs RecentlyClosed = new ClosedTabs();

        private readonly List<BrowserTab> _tabs = new List<BrowserTab>();
        private readonly StackPanel _strip = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly Grid _pages = new Grid();
        private readonly Border _newTabPage;
        private readonly TextBox _address = new TextBox { MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13.5 };
        private readonly Glyph _addressIcon = Ui.Icon("IconGlobe", 15);
        private readonly TextBox _newTabSearch = new TextBox { MinHeight = 46, FontSize = 16, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(14, 0, 14, 0) };
        private readonly Button _back, _forward, _reload, _newTabButton, _shield;
        private readonly TextBlock _shieldCount = new TextBlock { FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly Glyph _shieldIcon = Ui.Icon("IconShield", 16);
        private Button _downloads, _menuButton;
        private DispatcherTimer _sleepTimer;
        private readonly Grid _chrome;
        private readonly DispatcherTimer _saveTimer;
        private bool _fullScreen;
        private WindowState _stateBeforeFullScreen;

        /// <summary>The window used last (links from other apps open there).</summary>
        public static MainWindow LastActive => Open.LastOrDefault(w => w._activatedAt == Open.Max(x => x._activatedAt)) ?? Open.FirstOrDefault();
        private DateTime _activatedAt = DateTime.UtcNow;

        /// <summary>A private window: its tabs keep no history and their site data goes when the window closes.</summary>
        public bool IsPrivate { get; }

        public MainWindow(bool isPrivate = false)
        {
            IsPrivate = isPrivate;
            Activated += (s, e) => _activatedAt = DateTime.UtcNow;
            Width = 1280;
            Height = 820;
            MinWidth = 480;
            MinHeight = 320;
            Dialogs.Prepare(this, AppInfo.Name);
            Open.Add(this);

            _back = ToolButton("IconArrowLeft", "Back (Alt+Left)", (s, e) => Current?.GoBack());
            _forward = ToolButton("IconArrowRight", "Forward (Alt+Right)", (s, e) => Current?.GoForward());
            _reload = ToolButton("IconRefresh", "Reload (F5)", (s, e) =>
            {
                if (Current?.IsLoading == true) Current.Stop();
                else Current?.Reload();
            });
            var menu = ToolButton("IconMore", "Menu", null);
            _menuButton = menu;
            _shield = ToolButton(null, "Ads and trackers blocked on this page", null);
            _shield.Content = Ui.Row(_shieldIcon, _shieldCount);
            _shield.Width = double.NaN;
            _shield.MinWidth = 40;
            _shield.Padding = new Thickness(8, 0, 8, 0);
            _shield.Click += (s, e) => ShowShield();
            System.Windows.Automation.AutomationProperties.SetName(_shield, "Blocking on this site");
            menu.Click += (s, e) => ShowMenu(menu);
            _newTabButton = ToolButton("IconPlus", "New tab (Ctrl+T)", (s, e) => NewTab(select: true));
            _newTabButton.Margin = new Thickness(4, 6, 0, 0);
            _newTabButton.VerticalAlignment = VerticalAlignment.Center;

            // Tab strip.
            var stripRow = Ui.Columns(Ui.Auto, Ui.Auto, Ui.Star());
            stripRow.Children.Add(_strip.At(0, 0));
            stripRow.Children.Add(_newTabButton.At(0, 1));
            if (isPrivate)
            {
                var badge = Ui.Chip("Private", Tone.Accent, "IconEye");
                badge.HorizontalAlignment = HorizontalAlignment.Right;
                badge.VerticalAlignment = VerticalAlignment.Center;
                badge.ToolTip = "Nothing from this window is kept: no history, and sites' cookies go when it closes.";
                stripRow.Children.Add(badge.Margin(0, 6, 4, 0).At(0, 2));
            }
            stripRow.SizeChanged += (s, e) => SizeTabs();
            var stripBar = new Border { Child = stripRow, Padding = new Thickness(8, 0, 8, 0) }.Res(Border.BackgroundProperty, "SurfaceAltBg");

            // Toolbar with the address bar.
            System.Windows.Automation.AutomationProperties.SetName(_address, "Address and search bar");
            _address.GotKeyboardFocus += (s, e) => Dispatcher.BeginInvoke(new Action(_address.SelectAll), DispatcherPriority.Input);
            _address.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (!_address.IsKeyboardFocusWithin)
                {
                    _address.Focus();
                    e.Handled = true;
                }
            };
            _address.PreviewKeyDown += (s, e) =>
            {
                if (SuggestionKey(e.Key)) e.Handled = true;
            };
            _address.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Go(_address.Text);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    UpdateToolbar();
                    _address.SelectAll();
                    e.Handled = true;
                }
            };
            var addressGrid = Ui.Columns(Ui.Auto, Ui.Star());
            addressGrid.Children.Add(_addressIcon.Margin(12, 0, 0, 0).At(0, 0));
            _address.BorderThickness = new Thickness(0);
            _address.Background = Brushes.Transparent;
            _address.Padding = new Thickness(6, 0, 10, 0);
            addressGrid.Children.Add(_address.At(0, 1));
            InitLibrary(addressGrid);
            InitExtras(addressGrid);
            var addressBox = new Border { Child = addressGrid, CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(1), Margin = new Thickness(6, 0, 6, 0) }
                .Res(Border.BackgroundProperty, "InputBg").Res(Border.BorderBrushProperty, "InputBorder");

            _downloads = ToolButton("IconImport", "Downloads (Ctrl+J)", (s, e) => DownloadsWindow.Show(this));
            _downloads.Width = double.NaN;
            _downloads.MinWidth = 36;
            _downloads.Padding = new Thickness(8, 0, 8, 0);
            _downloads.Visibility = Visibility.Collapsed;
            Downloads.Changed += OnDownloadsChanged;
            Closed += (s, e) => Downloads.Changed -= OnDownloadsChanged;
            OnDownloadsChanged(); // downloads paused last time show the button straight away
            Action<DownloadItem, MainWindow> started = (item, from) => { if (from == this || (from == null && this == LastActive)) ShowDownloadNote(item); };
            Downloads.Started += started;
            Closed += (s, e) => Downloads.Started -= started;
            WatchNotes();
            var toolbar = Ui.Columns(Ui.Auto, Ui.Auto, Ui.Auto, Ui.Star(), Ui.Auto, Ui.Auto, Ui.Auto);
            toolbar.Children.Add(_back.At(0, 0));
            toolbar.Children.Add(_forward.At(0, 1));
            toolbar.Children.Add(_reload.At(0, 2));
            toolbar.Children.Add(addressBox.At(0, 3));
            toolbar.Children.Add(_shield.At(0, 4));
            toolbar.Children.Add(_downloads.At(0, 5));
            toolbar.Children.Add(menu.At(0, 6));
            var toolbarBar = new Border { Child = toolbar, Padding = new Thickness(8, 6, 8, 6), BorderThickness = new Thickness(0, 0, 0, 1) }
                .Res(Border.BackgroundProperty, "SurfaceBg").Res(Border.BorderBrushProperty, "BorderBrush");

            // The page shown for a new tab: the logo and a search box.
            System.Windows.Automation.AutomationProperties.SetName(_newTabSearch, "Search or type a web address");
            _newTabSearch.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                Go(_newTabSearch.Text);
                e.Handled = true;
            };
            var hint = Ui.Subtle("Search with " + SearchEngines.Find(Settings.SearchEngine).Name + " or type a web address", 12.5, false);
            _newTabSearch.TextChanged += (s, e) => hint.Visibility = _newTabSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden;
            hint.IsHitTestVisible = false;
            hint.Margin = new Thickness(16, 0, 0, 0);
            hint.VerticalAlignment = VerticalAlignment.Center;
            var searchHost = new Grid();
            searchHost.Children.Add(_newTabSearch);
            searchHost.Children.Add(hint);
            var logo = LogoTile.Create(72);
            logo.HorizontalAlignment = HorizontalAlignment.Center;
            var newTabContent = Ui.Column(logo, Ui.Text(AppInfo.Name, 26, bold: true).Margin(0, 14, 0, 22), searchHost);
            ((FrameworkElement)newTabContent.Children[1]).HorizontalAlignment = HorizontalAlignment.Center;
            newTabContent.Width = 560;
            newTabContent.VerticalAlignment = VerticalAlignment.Center;
            newTabContent.Margin = new Thickness(0, 0, 0, 120);
            _newTabPage = new Border { Child = newTabContent }.Res(Border.BackgroundProperty, "WindowBg");
            _pages.Children.Add(_newTabPage);

            _chrome = new Grid();
            _chrome.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _chrome.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _chrome.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _chrome.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _chrome.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _chrome.Children.Add(stripBar.At(0, 0));
            _chrome.Children.Add(toolbarBar.At(1, 0));
            _chrome.Children.Add(_bar.At(2, 0));
            _chrome.Children.Add(InitPasswords().At(3, 0));
            _chrome.Children.Add(_pages.At(4, 0));
            Content = _chrome;

            PreviewKeyDown += OnKey;
            // Tabs not looked at for a while go to sleep.
            _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _sleepTimer.Tick += async (s, e) => await SleepIdleTabs();
            _sleepTimer.Start();
            Closed += (s, e) => _sleepTimer.Stop();
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _saveTimer.Tick += (s, e) =>
            {
                _saveTimer.Stop();
                SaveSession();
            };
            Closing += (s, e) =>
            {
                // The last window's tabs are kept for next time; tabs of an extra window that's closed are put in "closed tabs".
                if (Open.Count > 1 && !IsPrivate)
                    foreach (var t in _tabs) RecentlyClosed.Add(t.ToSaved());
                Open.Remove(this);
                if (Open.Count == 0)
                {
                    BrowserLibrary.SaveAll();
                    Downloads.Closing();
                }
                SaveSession(includeClosing: !IsPrivate && !Open.Any(w => !w.IsPrivate) ? this : null);
                foreach (var t in _tabs) t.Close();
            };
            Closed += (s, e) =>
            {
                if (Open.Count == 0 && !SelfTest.Running) Application.Current.Shutdown();
            };
        }

        public BrowserTab Current { get; private set; }
        public IReadOnlyList<BrowserTab> Tabs => _tabs;
        public string AddressText => _address.Text;

        private static Button ToolButton(string icon, string tip, RoutedEventHandler click)
        {
            var b = Ui.Button(null, icon ?? "IconShield", "GhostButton", click);
            b.ToolTip = tip;
            b.Width = 36;
            b.MinWidth = 36;
            b.Height = 34;
            b.Padding = new Thickness(0);
            return b;
        }

        /// <summary>Opens the window with the tabs from last time, and the address it was started with.</summary>
        public void Start(string address)
        {
            if (Settings.RestoreTabs)
            {
                var (saved, selected) = Session.Load();
                var groups = new Dictionary<string, TabGroup>();
                foreach (var t in saved)
                {
                    var tab = new BrowserTab(t.Url, t.Title, t.Room);
                    if (t.Group != null)
                    {
                        var key = t.Group + "|" + t.GroupColor;
                        if (!groups.TryGetValue(key, out var g)) groups[key] = g = new TabGroup { Name = t.Group, Color = t.GroupColor };
                        tab.Group = g;
                    }
                    AddTab(tab, _tabs.Count, false);
                }
                if (_tabs.Count > 0) Select(_tabs[selected]);
            }
            if (!string.IsNullOrWhiteSpace(address))
            {
                var url = AddressBar.Resolve(address, SearchEngines.Find(Settings.SearchEngine));
                if (url != null) NewTab(url, true);
            }
            if (_tabs.Count == 0) NewTab(select: true);
        }

        // ================================================================ tabs

        public BrowserTab NewTab(string url = null, bool select = true, BrowserTab after = null, string room = null)
        {
            var tab = new BrowserTab(url, null, room ?? (after ?? Current)?.Room, IsPrivate) { Group = after?.Group };
            var index = after != null ? _tabs.IndexOf(after) + 1 : _tabs.Count;
            AddTab(tab, index, select);
            if (!string.IsNullOrEmpty(url) && select) tab.Navigate(url);
            if (select && string.IsNullOrEmpty(url)) FocusNewTabSearch();
            return tab;
        }

        private void AddTab(BrowserTab tab, int index, bool select)
        {
            index = Math.Max(0, Math.Min(index, _tabs.Count));
            _tabs.Insert(index, tab);
            tab.Changed += t =>
            {
                if (t == Current) UpdateToolbar();
                ScheduleSave();
            };
            tab.Selected += Select;
            tab.CloseClicked += CloseTab;
            tab.ViewCreated += t =>
            {
                t.View.Visibility = t == Current ? Visibility.Visible : Visibility.Hidden;
                _pages.Children.Add(t.View);
                Layout();
            };
            WatchFocus(tab);
            tab.NewWindowRequested += OnNewWindow;
            WatchPasswords(tab);
            tab.PageClosed += CloseTab;
            tab.FullScreenChanged += (t, on) => SetFullScreen(on);
            tab.BlockedChanged += t =>
            {
                if (t == Current) UpdateShield();
            };
            tab.Header.ContextMenu = TabMenu(tab);
            RebuildStrip();
            if (select) Select(tab);
            ScheduleSave();
        }

        public void Select(BrowserTab tab)
        {
            if (tab == null) return;
            // Picking the other half of two pages side by side keeps both shown; picking another tab ends it.
            if (_splitPartner != null && tab != Current)
                _splitPartner = tab == _splitPartner ? Current : null;
            var previous = Current;
            Current = tab;
            foreach (var t in _tabs) t.SetActive(t == tab);
            Layout();
            if (tab.Group?.Collapsed == true || previous?.Group?.Collapsed == true) RebuildStrip();
            tab.LoadIfNeeded();
            UpdateSaveBar();
            _newTabPage.Visibility = tab.IsBlank && tab.View == null ? Visibility.Visible : Visibility.Collapsed;
            UpdateToolbar();
            if (tab.IsBlank && tab.View == null) FocusNewTabSearch();
            else tab.View?.Focus();
            ScheduleSave();
        }

        public void CloseTab(BrowserTab tab)
        {
            var index = _tabs.IndexOf(tab);
            if (index < 0) return;
            if (!IsPrivate) RecentlyClosed.Add(tab.ToSaved());
            _tabs.RemoveAt(index);
            if (tab == _splitPartner) _splitPartner = null;
            if (tab.View != null) _pages.Children.Remove(tab.View);
            tab.Close();
            if (_tabs.Count == 0)
            {
                // Closing the last tab closes the window, as in other browsers.
                Close();
                return;
            }
            if (tab == Current)
            {
                // The other half of two pages side by side takes over; otherwise the next tab.
                var next = _splitPartner ?? _tabs[Math.Min(index, _tabs.Count - 1)];
                _splitPartner = null;
                Current = null;
                Select(next);
            }
            else Layout();
            RebuildStrip();
            ScheduleSave();
        }

        public void ReopenClosedTab()
        {
            var saved = RecentlyClosed.TakeLatest();
            if (saved != null) NewTab(saved.Url, true, Current, saved.Room);
        }

        private void CloseOtherTabs(BrowserTab keep)
        {
            foreach (var t in _tabs.Where(t => t != keep).ToList()) CloseTab(t);
        }

        private async void OnNewWindow(BrowserTab opener, CoreWebView2NewWindowRequestedEventArgs e)
        {
            // Links that open a new window (and pop-ups like "Sign in with ...") open in a new tab next to this one,
            // still connected to the page that opened them.
            // Pages that open a window by themselves (not after a click) are usually pop-up ads.
            if (Settings.BlockPopups && !e.IsUserInitiated && !opener.SiteAllowed)
            {
                e.Handled = true;
                opener.CountBlockedPopup();
                return;
            }
            var deferral = e.GetDeferral();
            try
            {
                // Same room as the page that opened it (the engine needs that, and so do sign-in windows).
                var tab = new BrowserTab(e.Uri, null, opener.Room, opener.IsPrivate) { Group = opener.Group };
                AddTab(tab, _tabs.IndexOf(opener) + 1, true);
                await tab.EnsureView();
                e.NewWindow = tab.View.CoreWebView2;
                e.Handled = true;
            }
            finally
            {
                deferral.Complete();
            }
        }

        private ContextMenu TabMenu(BrowserTab tab)
        {
            var menu = new ContextMenu();
            menu.Opened += (s, e) => FillTabMenu(menu, tab);
            menu.Items.Add(new MenuItem());
            return menu;
        }

        private void FillTabMenu(ContextMenu menu, BrowserTab tab)
        {
            menu.Items.Clear();
            menu.Items.Add(Item("Reload", () => tab.Reload()));
            menu.Items.Add(Item("Duplicate", () => NewTab(tab.Url, true, tab)));
            if (!IsPrivate)
            {
                var move = new MenuItem { Header = "Move to room" };
                move.SubmenuOpened += (s, e) =>
                {
                    move.Items.Clear();
                    foreach (var r in BrowserRooms.All.All.Where(r => r.Id != tab.Room))
                    {
                        var room = r;
                        move.Items.Add(Item(r.Name, () =>
                        {
                            // A room has its own logins, so the page is opened afresh there.
                            var moved = NewTab(tab.IsBlank ? null : tab.Url, true, tab, room.Id);
                            CloseTab(tab);
                        }));
                    }
                };
                move.Items.Add(new MenuItem());
                menu.Items.Add(move);
            }
            menu.Items.Add(GroupMenu(tab));
            if (IsSplit && (tab == Current || tab == _splitPartner))
                menu.Items.Add(Item("Stop showing side by side", EndSideBySide));
            else if (tab != Current)
                menu.Items.Add(Item("Show side by side with this tab", () => ShowSideBySide(tab)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Close tab", () => CloseTab(tab), "Ctrl+W"));
            menu.Items.Add(Item("Close other tabs", () => CloseOtherTabs(tab)));
            menu.Items.Add(Item("Reopen closed tab", ReopenClosedTab, "Ctrl+Shift+T"));
        }

        private static MenuItem Item(string text, Action act, string keys = null)
        {
            var item = new MenuItem { Header = text, InputGestureText = keys ?? string.Empty };
            item.Click += (s, e) => act();
            return item;
        }

        /// <summary>Tabs share the strip's width, between 90 and 240 pixels each.</summary>
        private void SizeTabs()
        {
            var shown = _tabs.Count(t => t.Header.Visibility == Visibility.Visible);
            if (shown == 0) return;
            var chips = _strip.Children.OfType<FrameworkElement>().Where(c => !_tabs.Any(t => t.Header == c)).Sum(c => c.ActualWidth > 0 ? c.ActualWidth + 8 : 80);
            var available = Math.Max(0, ActualWidth - 80 - chips);
            var width = Math.Max(90, Math.Min(240, available / shown));
            foreach (var t in _tabs) t.Header.Width = width;
        }

        // ================================================================ address bar and toolbar

        public void Go(string typed)
        {
            var url = AddressBar.Resolve(typed, SearchEngines.Find(Settings.SearchEngine));
            if (url == null) return;
            if (Current == null) NewTab(url, true);
            else
            {
                _newTabPage.Visibility = Visibility.Collapsed;
                Current.Navigate(url);
                Current.View?.Focus();
            }
            _newTabSearch.Text = string.Empty;
        }

        private void UpdateToolbar()
        {
            var t = Current;
            if (t == null) return;
            if (!_address.IsKeyboardFocusWithin) SetAddressText(AddressBar.Display(t.Url));
            UpdateStar();
            _back.IsEnabled = t.CanGoBack;
            _forward.IsEnabled = t.CanGoForward;
            _reload.Content = Ui.Label(t.IsLoading ? "IconClose" : "IconRefresh", null);
            _reload.ToolTip = t.IsLoading ? "Stop (Esc)" : "Reload (F5)";
            _addressIcon.SetResourceReference(Glyph.DataProperty, AddressBar.IsSecure(t.Url) ? "IconLock" : "IconGlobe");
            _addressIcon.ToolTip = t.IsBlank ? null : AddressBar.IsSecure(t.Url) ? "The connection to this site is private (https)" : "The connection to this site isn't private";
            Title = (t.IsBlank ? "New tab" : t.Title) + " - " + AppInfo.Name + (IsPrivate ? " (private)" : string.Empty);
            if (t.View != null) _newTabPage.Visibility = Visibility.Collapsed;
            UpdateShield();
        }

        public async System.Threading.Tasks.Task<int> SleepIdleTabs()
        {
            if (!Settings.SleepTabs) return 0;
            var n = 0;
            foreach (var t in _tabs.ToList())
                if (await t.SleepIfIdle()) n++;
            return n;
        }

        private void OnDownloadsChanged() => Dispatcher.BeginInvoke(new Action(() =>
        {
            var progress = Downloads.Progress();
            _downloads.Visibility = Downloads.ShowButton ? Visibility.Visible : Visibility.Collapsed;
            _downloads.Content = progress == null ? (object)Ui.Icon("IconImport", 16) : Ui.Row(Ui.Icon("IconImport", 16), Ui.Text(progress, 11.5).Margin(4, 0, 0, 0));
        }));

        private void UpdateShield()
        {
            var t = Current;
            if (t == null) return;
            var on = Settings.BlockAds || Settings.HideCookieBanners;
            var count = t.Blocked + t.PopupsBlocked;
            _shieldCount.Text = on && !t.SiteAllowed && count > 0 ? (count > 99 ? "99+" : count.ToString()) : string.Empty;
            _shieldIcon.Opacity = on && !t.SiteAllowed ? 1 : 0.4;
            _shield.ToolTip = !on ? "Blocking is switched off" : t.SiteAllowed ? "Blocking is off for this site" : $"{count} blocked on this page";
        }

        /// <summary>The shield's panel: what was blocked, and a switch to allow the site.</summary>
        public System.Windows.Controls.Primitives.Popup ShowShield()
        {
            var t = Current;
            var site = t == null ? string.Empty : Core.Blocking.Domains.Registrable(t.PageHost);
            var panel = new StackPanel { Width = 300 };
            panel.Children.Add(Ui.Row(Ui.Icon("IconShield", 18), Ui.Heading("Ads and trackers", 15).Margin(8, 0, 0, 0)));
            var on = Settings.BlockAds || Settings.HideCookieBanners;
            if (!on)
                panel.Children.Add(Ui.Subtle("Blocking is switched off in Settings.", 12.5).Margin(0, 8, 0, 0));
            else if (string.IsNullOrEmpty(site))
                panel.Children.Add(Ui.Subtle("Nothing to block on this page.", 12.5).Margin(0, 8, 0, 0));
            else
            {
                panel.Children.Add(Ui.Text(t.SiteAllowed ? "Nothing is blocked on " + site : $"{t.Blocked} blocked on this page", 13.5, bold: true).Margin(0, 10, 0, 0));
                if (t.PopupsBlocked > 0 && !t.SiteAllowed)
                    panel.Children.Add(Ui.Subtle(t.PopupsBlocked == 1 ? "1 pop-up blocked" : $"{t.PopupsBlocked} pop-ups blocked", 12.5).Margin(0, 2, 0, 0));
                var toggle = new CheckBox { Content = "Block on " + site, IsChecked = !t.SiteAllowed }.Styled("Switch");
                toggle.Margin = new Thickness(0, 12, 0, 0);
                toggle.Click += async (s, e) =>
                {
                    Blocker.SetAllowed(t.PageHost, toggle.IsChecked != true);
                    await t.RefreshBlocking();
                    t.Reload();
                };
                panel.Children.Add(toggle);
                panel.Children.Add(Ui.Subtle("Switch it off if the site doesn't work properly, or to support it with its ads. The page reloads.", 11.5).Margin(0, 4, 0, 0));
                var clearSite = Ui.Small(Ui.Button("Clear this site's cookies and data", "IconTrash"));
                clearSite.HorizontalAlignment = HorizontalAlignment.Left;
                clearSite.Margin = new Thickness(0, 12, 0, 0);
                clearSite.ToolTip = "Signs you out of " + site + (t.Room == Rooms.DefaultId ? string.Empty : " in this room") + " and forgets what it stored on this PC.";
                clearSite.Click += async (s, e) =>
                {
                    if (!Dialogs.Confirm(this, "Clear the cookies and data of " + site + "? You'll be signed out of it.")) return;
                    await t.ClearSiteData();
                };
                panel.Children.Add(clearSite);
            }
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = _shield,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true
            };
            panel.Children.Add(Ui.Link("Blocking settings", (s, e) =>
            {
                popup.IsOpen = false;
                new SettingsWindow(this).ShowDialog();
            }).Margin(0, 12, 0, 0));
            ((FrameworkElement)panel.Children[panel.Children.Count - 1]).HorizontalAlignment = HorizontalAlignment.Left;
            popup.Child = new Border
            {
                Child = panel,
                Padding = new Thickness(18, 16, 18, 16),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 4, 8, 8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.25 }
            }.Res(Border.BackgroundProperty, "SurfaceBg").Res(Border.BorderBrushProperty, "BorderBrush");
            popup.IsOpen = true;
            return popup;
        }

        private void FocusNewTabSearch() =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _newTabSearch.Focus();
                Keyboard.Focus(_newTabSearch);
            }), DispatcherPriority.Input);

        public void FocusAddress()
        {
            _address.Focus();
            _address.SelectAll();
        }

        private void ShowMenu(Button anchor)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            menu.Items.Add(Item("New tab", () => NewTab(select: true), "Ctrl+T"));
            menu.Items.Add(Item("New window", NewWindow, "Ctrl+N"));
            menu.Items.Add(Item("New private window", () => NewPrivateWindow(), "Ctrl+Shift+N"));
            if (!IsPrivate)
            {
                var inRoom = new MenuItem { Header = "New tab in a room" };
                foreach (var r in BrowserRooms.All.All)
                {
                    var room = r;
                    inRoom.Items.Add(Item(r.Name, () => NewTab(select: true, room: room.Id)));
                }
                inRoom.Items.Add(new Separator());
                inRoom.Items.Add(Item("New room...", () =>
                {
                    var name = Dialogs.Ask(this, "New room", "Name of the room (for example Shopping, or a second email account)", string.Empty);
                    if (name != null) NewTab(select: true, room: BrowserRooms.All.Add(name).Id);
                }));
                menu.Items.Add(inRoom);
            }
            var reopen = Item("Reopen closed tab", ReopenClosedTab, "Ctrl+Shift+T");
            reopen.IsEnabled = RecentlyClosed.Count > 0;
            menu.Items.Add(reopen);
            menu.Items.Add(new Separator());
            var zoom = new MenuItem { Header = $"Zoom ({Math.Round((Current?.Zoom ?? 1) * 100)}%)" };
            zoom.Items.Add(Item("Bigger", () => ZoomBy(1.1), "Ctrl++"));
            zoom.Items.Add(Item("Smaller", () => ZoomBy(1 / 1.1), "Ctrl+-"));
            zoom.Items.Add(Item("Normal size", () => { if (Current != null) Current.Zoom = 1; }, "Ctrl+0"));
            menu.Items.Add(zoom);
            menu.Items.Add(Item("Print...", Print, "Ctrl+P"));
            menu.Items.Add(Item("Reading mode", async () => await ToggleReadingMode(), "F9"));
            menu.Items.Add(Item("Screenshot of the whole page...", async () => await SaveScreenshot(), "Ctrl+Shift+S"));
            menu.Items.Add(Item("Save the page as PDF...", async () => await SavePdf()));
            menu.Items.Add(Item("Full screen", () => SetFullScreen(!_fullScreen), "F11"));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("History", () => LibraryWindow.Show(this, false), "Ctrl+H"));
            menu.Items.Add(Item("Bookmarks", () => LibraryWindow.Show(this, true), "Ctrl+Shift+O"));
            menu.Items.Add(Item("Passwords", () => PasswordsWindow.Show(this)));
            menu.Items.Add(Item("Downloads", () => DownloadsWindow.Show(this), "Ctrl+J"));
            var barItem = Item(Settings.ShowBookmarksBar ? "Hide the bookmarks bar" : "Show the bookmarks bar", ToggleBookmarksBar, "Ctrl+Shift+B");
            menu.Items.Add(barItem);
            menu.Items.Add(Item("Bring bookmarks and history from Chrome...", () => new ImportWindow(this).ShowDialog()));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Settings", () => new SettingsWindow(this).ShowDialog()));
            menu.Items.Add(Item("About " + AppInfo.Name, () => About.Show(this)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Close window", Close));
            menu.IsOpen = true;
        }

        public void NewWindow()
        {
            var w = new MainWindow();
            w.Show();
            w.NewTab(select: true);
        }

        public MainWindow NewPrivateWindow()
        {
            var w = new MainWindow(true);
            w.Show();
            w.NewTab(select: true);
            return w;
        }

        private void ZoomBy(double factor)
        {
            if (Current?.View != null) Current.Zoom = Current.Zoom * factor;
        }

        private void Print()
        {
            try { Current?.View?.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is NotImplementedException) { }
        }

        private void SetFullScreen(bool on)
        {
            if (on == _fullScreen) return;
            _fullScreen = on;
            if (on)
            {
                _stateBeforeFullScreen = WindowState;
                WindowStyle = WindowStyle.None;
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; // so the task bar is covered too
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = _stateBeforeFullScreen;
            }
            _chrome.RowDefinitions[0].Height = on ? new GridLength(0) : GridLength.Auto;
            _chrome.RowDefinitions[1].Height = on ? new GridLength(0) : GridLength.Auto;
            _chrome.RowDefinitions[2].Height = on ? new GridLength(0) : GridLength.Auto;
            _chrome.RowDefinitions[3].Height = on ? new GridLength(0) : GridLength.Auto;
        }

        // ================================================================ keyboard

        private void OnKey(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            var handled = true;
            if (ctrl && shift && key == Key.T) ReopenClosedTab();
            else if (ctrl && key == Key.T) NewTab(select: true);
            else if (ctrl && shift && key == Key.N) NewPrivateWindow();
            else if (ctrl && key == Key.N) NewWindow();
            else if (ctrl && (key == Key.W || key == Key.F4)) { if (Current != null) CloseTab(Current); }
            else if (ctrl && key == Key.Tab) SelectBy(shift ? -1 : 1);
            else if (ctrl && (key == Key.PageDown || key == Key.PageUp)) SelectBy(key == Key.PageDown ? 1 : -1);
            else if (ctrl && key >= Key.D1 && key <= Key.D9) SelectNumber(key - Key.D1);
            else if ((ctrl && key == Key.L) || (alt && key == Key.D) || key == Key.F6) FocusAddress();
            else if (key == Key.F5 || (ctrl && key == Key.R)) Current?.Reload();
            else if (alt && key == Key.Left) Current?.GoBack();
            else if (alt && key == Key.Right) Current?.GoForward();
            else if (ctrl && (key == Key.OemPlus || key == Key.Add)) ZoomBy(1.1);
            else if (ctrl && (key == Key.OemMinus || key == Key.Subtract)) ZoomBy(1 / 1.1);
            else if (ctrl && (key == Key.D0 || key == Key.NumPad0)) { if (Current != null) Current.Zoom = 1; }
            else if (ctrl && key == Key.P) Print();
            else if (key == Key.F9) _ = ToggleReadingMode();
            else if (ctrl && shift && key == Key.S) _ = SaveScreenshot();
            else if (key == Key.F11) SetFullScreen(!_fullScreen);
            else if (ctrl && shift && key == Key.B) ToggleBookmarksBar();
            else if (ctrl && shift && key == Key.O) LibraryWindow.Show(this, true);
            else if (ctrl && key == Key.H) LibraryWindow.Show(this, false);
            else if (ctrl && key == Key.J) DownloadsWindow.Show(this);
            else if (ctrl && key == Key.D) ShowBookmarkPanel();
            else if (key == Key.Escape && _fullScreen) SetFullScreen(false);
            else if (key == Key.Escape && Current?.IsLoading == true && !_address.IsKeyboardFocusWithin) Current.Stop();
            else handled = false;
            if (handled) e.Handled = true;
        }

        private void SelectBy(int step)
        {
            if (_tabs.Count == 0) return;
            var i = (_tabs.IndexOf(Current) + step + _tabs.Count) % _tabs.Count;
            Select(_tabs[i]);
        }

        /// <summary>Ctrl+1 to Ctrl+8 pick that tab; Ctrl+9 picks the last one.</summary>
        private void SelectNumber(int n)
        {
            if (_tabs.Count == 0) return;
            Select(n == 8 ? _tabs[_tabs.Count - 1] : _tabs[Math.Min(n, _tabs.Count - 1)]);
        }

        // ================================================================ saving the tabs

        private void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>Saves the tabs of every window (the window being closed last included) for next time.</summary>
        private static void SaveSession(MainWindow includeClosing = null)
        {
            // Private windows are never kept.
            var windows = Open.Where(w => !w.IsPrivate).ToList();
            if (includeClosing != null) windows.Add(includeClosing);
            if (windows.Count == 0) return;
            var tabs = new List<SavedTab>();
            var selected = 0;
            foreach (var w in windows)
            {
                if (w == windows[0] && w.Current != null) selected = w._tabs.Where(t => !t.IsBlank).ToList().IndexOf(w.Current);
                tabs.AddRange(w._tabs.Select(t => t.ToSaved()));
            }
            Session.Save(tabs, Math.Max(0, selected));
        }

        private void SaveSession() => SaveSession(null);

        /// <summary>Saves the tabs of every window now.</summary>
        public static void SaveAll() => SaveSession(null);
    }
}
