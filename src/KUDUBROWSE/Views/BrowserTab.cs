using System;
using System.IO;
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

        public BrowserTab(string url = null, string title = null)
        {
            Url = url ?? string.Empty;
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
            Header.MouseEnter += (s, e) => UpdateLook();
            Header.MouseLeave += (s, e) => UpdateLook();
            Refresh();
        }

        public Border Header { get; }
        /// <summary>The page, once the tab has been shown.</summary>
        public WebView2 View { get; private set; }

        public string Url { get; private set; }
        public string Title { get; private set; }
        public bool IsLoading { get; private set; }
        public bool IsActive { get; private set; }
        public bool IsBlank => string.IsNullOrEmpty(Url) || Url == "about:blank";
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

        public SavedTab ToSaved() => new SavedTab { Url = Url, Title = Title };

        public void SetActive(bool active)
        {
            IsActive = active;
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
            var env = await Web.Environment;
            await View.EnsureCoreWebView2Async(env);
            var core = View.CoreWebView2;
            core.Settings.IsStatusBarEnabled = true;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.DocumentTitleChanged += (s, e) =>
            {
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
                Refresh();
            };
            core.NavigationCompleted += (s, e) =>
            {
                IsLoading = false;
                Refresh();
            };
            core.FaviconChanged += async (s, e) => await LoadFavicon();
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
            try { View?.Dispose(); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException) { }
            View = null;
        }

        private void Refresh()
        {
            _title.Text = Title;
            Header.ToolTip = Title + (IsBlank ? string.Empty : "\n" + AddressBar.Display(Url));
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
