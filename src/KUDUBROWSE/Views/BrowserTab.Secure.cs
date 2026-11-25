// Secure connections in a tab: an http:// page is first opened as https://; when the site has no secure connection
// (the https try fails, takes too long, or the site sends back to http), the tab shows a warning instead of the page,
// with "Go back" and "Open it anyway". Pages on this PC and the local network, and sites allowed, are left alone.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kudubrowse.Controls;
using Kudubrowse.Core;
using Microsoft.Web.WebView2.Core;

namespace Kudubrowse.Views
{
    public sealed partial class BrowserTab
    {
        /// <summary>How long the https try may take before the site counts as having no secure connection.</summary>
        public static TimeSpan SecureTryTime { get; set; } = TimeSpan.FromSeconds(6);

        /// <summary>Sites opened anyway without a secure connection, until the browser closes.</summary>
        private static readonly HashSet<string> AllowedThisTime = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The http:// address being tried as https:// (null when none is).</summary>
        private string _upgradeFrom;
        /// <summary>The https:// address tried.</summary>
        private string _upgradeTo;
        /// <summary>The page shown when the https try began (it's still there if the try never loaded anything).</summary>
        private string _beforeTry;
        /// <summary>The engine's id of the https try (0 until it starts).</summary>
        private ulong _upgradeNavigation;
        /// <summary>Gives up on the https try when the site doesn't answer in time.</summary>
        private DispatcherTimer _upgradeTimer;
        /// <summary>The warning shown instead of the page, once made.</summary>
        private FrameworkElement _warning;
        /// <summary>"Don't warn again for this site" on the warning.</summary>
        private CheckBox _dontWarn;
        /// <summary>The warning's text.</summary>
        private TextBlock _warningText;

        /// <summary>The http:// address waiting for the person's choice while the warning is shown, else null.</summary>
        public string InsecureUrl { get; private set; }

        /// <summary>The warning was shown or taken away.</summary>
        public event Action<BrowserTab> InsecureChanged;

        /// <summary>True once the warning has been made (it's in the window then).</summary>
        public bool HasWarning => _warning != null;

        /// <summary>The warning shown instead of the page (made the first time it's needed).</summary>
        public FrameworkElement Warning => _warning ?? (_warning = BuildWarning());

        /// <summary>The sites that may be opened without a secure connection: the ones kept in settings, and this time's.</summary>
        private static IEnumerable<string> AllowedSites() => Settings.InsecureSites.Concat(AllowedThisTime);

        /// <summary>
        /// Called as a page starts to open: an http:// page is opened as https:// instead (a page the site sends back
        /// to http:// during the try gets the warning). True when this opening was stopped.
        /// </summary>
        private bool CheckSecure(CoreWebView2 core, CoreWebView2NavigationStartingEventArgs e)
        {
            if (_upgradeTo != null && _upgradeNavigation == 0 && e.Uri == _upgradeTo)
            {
                _upgradeNavigation = e.NavigationId;
                return false;
            }
            if (!Settings.SecureConnections) return false;
            var secure = SecureConnections.SecureAddress(e.Uri, AllowedSites());
            if (secure == null) return false;
            e.Cancel = true;
            if (_upgradeFrom != null && e.NavigationId == _upgradeNavigation)
            {
                // The https page sent us back to http: the site has no secure connection after all.
                ShowWarning(e.Uri);
                return true;
            }
            TrySecure(core, e.Uri, secure);
            return true;
        }

        /// <summary>Opens the https:// address instead of the http:// one, and starts the clock on it.</summary>
        private void TrySecure(CoreWebView2 core, string from, string to)
        {
            _upgradeFrom = from;
            _upgradeTo = to;
            try { _beforeTry = core.Source; }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException) { _beforeTry = null; }
            _upgradeNavigation = 0;
            _upgradeTimer?.Stop();
            _upgradeTimer = new DispatcherTimer { Interval = SecureTryTime };
            _upgradeTimer.Tick += (s, e) =>
            {
                _upgradeTimer?.Stop();
                if (_upgradeFrom == null || _closed) return;
                var url = _upgradeFrom;
                ShowWarning(url);
                try { core.Stop(); }
                catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException) { }
            };
            _upgradeTimer.Start();
            // Opening another page from inside the engine's "starting" event isn't allowed: a moment later.
            View?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed || _upgradeTo != to) return;
                try { core.Navigate(to); }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is System.Runtime.InteropServices.COMException) { }
            }));
        }

        /// <summary>The https try's page started to arrive: the site has a secure connection, so the clock stops.</summary>
        private void SecureAnswered(CoreWebView2ContentLoadingEventArgs e)
        {
            if (_upgradeFrom != null && e.NavigationId == _upgradeNavigation && !e.IsErrorPage) _upgradeTimer?.Stop();
        }

        /// <summary>A page finished opening: a failed https try (no secure connection) gets the warning.</summary>
        private void SecureFinished(CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_upgradeFrom == null || e.NavigationId != _upgradeNavigation) return;
            var from = _upgradeFrom;
            if (e.IsSuccess || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
            {
                // It worked (the page is shown over https), or the person stopped it or went elsewhere.
                EndTry();
                return;
            }
            ShowWarning(from);
        }

        /// <summary>Forgets the https try.</summary>
        private void EndTry()
        {
            _upgradeTimer?.Stop();
            _upgradeFrom = _upgradeTo = null;
            _upgradeNavigation = 0;
        }

        /// <summary>Shows the warning instead of the page, for this http:// address.</summary>
        private void ShowWarning(string url)
        {
            EndTry();
            InsecureUrl = url;
            Url = url;
            IsLoading = false;
            var site = SecureConnections.SiteOf(url) ?? AddressBar.Display(url);
            Title = site;
            if (_warning == null) _warning = BuildWarning(); // made now, so its text can be set
            _warningText.Text = Lang.T("{0} can only be opened without a secure connection (https). What you see and send there, such as passwords, could be read or changed by others on the network.", site);
            _dontWarn.Content = Lang.T("Don't warn me again for {0}", site);
            _dontWarn.IsChecked = false;
            Refresh();
            InsecureChanged?.Invoke(this);
        }

        /// <summary>"Go back": the warning goes, and the page before it (if any) comes back.</summary>
        public void LeaveInsecure()
        {
            if (InsecureUrl == null) return;
            InsecureUrl = null;
            var core = View?.CoreWebView2;
            try
            {
                if (core != null)
                {
                    // The page from before is still there when the https try never loaded; else the try's error page is
                    // left by going back (or to an empty page when there's nothing to go back to).
                    if (core.Source != _beforeTry)
                    {
                        if (core.CanGoBack) core.GoBack();
                        else core.Navigate("about:blank");
                    }
                    Url = core.Source;
                    Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? AddressBar.Display(core.Source) : core.DocumentTitle;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException) { }
            Refresh();
            InsecureChanged?.Invoke(this);
        }

        /// <summary>"Open it anyway": the site is allowed without a secure connection (for good when asked) and opened.</summary>
        public void OpenInsecureAnyway(bool always)
        {
            var url = InsecureUrl;
            if (url == null) return;
            var site = SecureConnections.SiteOf(url);
            if (site != null)
            {
                AllowedThisTime.Add(site);
                if (always)
                {
                    var list = Settings.InsecureSites;
                    list.Add(site);
                    Settings.InsecureSites = list;
                }
            }
            InsecureUrl = null;
            InsecureChanged?.Invoke(this);
            Navigate(url);
        }

        /// <summary>Takes a site off the "allowed without a secure connection" lists (Settings).</summary>
        public static void WarnAgainFor(string site)
        {
            AllowedThisTime.Remove(site);
            var list = Settings.InsecureSites;
            list.Remove(site);
            Settings.InsecureSites = list;
        }

        /// <summary>The warning: what's wrong, in plain words, and the two ways on.</summary>
        private FrameworkElement BuildWarning()
        {
            _warningText = Ui.Text(string.Empty, 13.5, wrap: true);
            _dontWarn = new CheckBox { Margin = new Thickness(0, 14, 0, 0) };
            var back = Ui.Button("Go back", "IconArrowLeft", "PrimaryButton");
            back.Click += (s, e) => LeaveInsecure();
            var anyway = Ui.Button("Open it anyway", null, "SecondaryButton");
            anyway.Click += (s, e) => OpenInsecureAnyway(_dontWarn.IsChecked == true);
            var buttons = Ui.Row(back, anyway.Margin(10, 0, 0, 0));
            buttons.Margin = new Thickness(0, 20, 0, 0);
            var icon = Ui.Icon("IconWarn", 40);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "WarnText");
            var content = Ui.Column(icon,
                Ui.Heading("This site doesn't offer a secure connection", 20).Margin(0, 14, 0, 10),
                _warningText,
                Ui.Subtle("Most sites have one; this one doesn't, or it isn't working right now. Sites on this PC and your own network are opened without this warning.", 12.5).Margin(0, 10, 0, 0),
                _dontWarn,
                buttons);
            content.MaxWidth = 560;
            content.Margin = new Thickness(40, 0, 40, 80);
            content.VerticalAlignment = VerticalAlignment.Center;
            content.HorizontalAlignment = HorizontalAlignment.Center;
            var page = new Border { Child = content }.Res(Border.BackgroundProperty, "WindowBg");
            System.Windows.Automation.AutomationProperties.SetName(page, "Not a secure connection");
            return page;
        }
    }
}
