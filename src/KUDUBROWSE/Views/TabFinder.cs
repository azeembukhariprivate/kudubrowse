using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kudubrowse.Controls;
using Kudubrowse.Core;

namespace Kudubrowse.Views
{
    /// <summary>
    /// Find any tab (Ctrl+Shift+A): type a few words of a tab's title or address to find it among the tabs open in every
    /// window and the recently closed ones, then Enter goes to it (or opens a closed one again).
    /// </summary>
    public sealed class TabFinder : Window
    {
        /// <summary>One tab that can be gone to: an open one (with its window) or a recently closed one.</summary>
        public sealed class Hit
        {
            public BrowserTab Tab { get; set; }
            public MainWindow Window { get; set; }
            public SavedTab Closed { get; set; }
            public string Title { get; set; }
            public string Url { get; set; }
        }

        private readonly MainWindow _owner;
        private readonly TextBox _search = new TextBox { FontSize = 14 };
        private readonly ListBox _list = new ListBox { MaxHeight = 430, BorderThickness = new Thickness(0) };
        private readonly TextBlock _empty = Ui.Subtle("No tab has those words.", 12.5, true);
        private readonly List<Hit> _results = new List<Hit>();
        private bool _closing;

        /// <summary>For the self-test: stay open when another window becomes active.</summary>
        public bool StayOpen { get; set; }

        public TabFinder(MainWindow owner)
        {
            _owner = owner;
            Width = 580;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            ShowInTaskbar = false;
            Dialogs.Prepare(this, "Find a tab", owner);
            WindowStartupLocation = WindowStartupLocation.Manual;
            if (owner.WindowState == WindowState.Maximized)
            {
                var screen = SystemParameters.WorkArea;
                Left = screen.Left + (screen.Width - Width) / 2;
                Top = screen.Top + 90;
            }
            else
            {
                Left = owner.Left + Math.Max(0, (owner.ActualWidth - Width) / 2);
                Top = owner.Top + 90;
            }

            System.Windows.Automation.AutomationProperties.SetName(_search, "Find a tab");
            _search.TextChanged += (s, e) => Refresh();
            _search.PreviewKeyDown += OnKey;
            _list.PreviewKeyDown += OnKey;
            _list.Res(Control.BackgroundProperty, "SurfaceBg");
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            _list.MouseUp += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left && _list.SelectedItem is ListBoxItem) OpenSelected();
            };

            var hint = Ui.Subtle("Type a few words of a tab's title or address · ↑↓ to choose · Enter to go to it · Esc to close", 11.5, true);
            var root = Ui.Column(
                Ui.Row(Ui.Icon("IconSearch", 16).Margin(0, 0, 10, 0), Ui.Heading("Find a tab", 14)),
                _search.Margin(0, 10, 0, 6),
                hint.Margin(0, 0, 0, 8),
                _list,
                _empty.Margin(4, 6, 0, 4));
            Content = new Border { Child = root, Padding = new Thickness(16, 14, 16, 14), BorderThickness = new Thickness(1) }
                .Res(Border.BorderBrushProperty, "InputBorder").Res(Border.BackgroundProperty, "WindowBg");

            Loaded += (s, e) =>
            {
                _search.Focus();
                Keyboard.Focus(_search);
            };
            Deactivated += (s, e) =>
            {
                if (!StayOpen) SafeClose();
            };
            Closing += (s, e) => _closing = true;
            Refresh();
        }

        public static TabFinder Show(MainWindow owner)
        {
            var f = new TabFinder(owner);
            f.Show();
            f.Activate();
            return f;
        }

        // ================================================================ for the self-test

        public IReadOnlyList<Hit> Results => _results;

        public string Query
        {
            get => _search.Text;
            set => _search.Text = value;
        }

        public int Selected
        {
            get => _results.IndexOf((_list.SelectedItem as ListBoxItem)?.Tag as Hit);
            set
            {
                var item = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, value >= 0 && value < _results.Count ? _results[value] : null));
                if (item != null) _list.SelectedItem = item;
            }
        }

        // ================================================================ the list

        private List<Hit> Everything()
        {
            var hits = new List<Hit>();
            // Tabs in this window first, then the other windows' (private windows only see private windows, and the other way round).
            foreach (var w in new[] { _owner }.Concat(MainWindow.Open.Where(w => w != _owner && w.IsPrivate == _owner.IsPrivate)))
                foreach (var t in w.Tabs)
                    hits.Add(new Hit { Tab = t, Window = w, Title = t.IsBlank ? "New tab" : t.Title ?? t.Url, Url = t.Url ?? string.Empty });
            if (!_owner.IsPrivate)
                foreach (var c in MainWindow.RecentlyClosed.Items)
                    hits.Add(new Hit { Closed = c, Title = string.IsNullOrEmpty(c.Title) ? c.Url : c.Title, Url = c.Url ?? string.Empty });
            return hits;
        }

        private void Refresh()
        {
            _results.Clear();
            _results.AddRange(TabSearch.Find(Everything(), _search.Text, h => h.Title, h => h.Url));
            _list.Items.Clear();
            var heading = (string)null;
            foreach (var hit in _results)
            {
                var section = hit.Closed != null ? "Recently closed" : "Open tabs";
                if (section != heading)
                {
                    heading = section;
                    _list.Items.Add(new ListBoxItem
                    {
                        Content = Ui.Subtle(section, 11.5, false).Margin(2, heading == "Open tabs" ? 0 : 8, 0, 2),
                        Focusable = false,
                        IsHitTestVisible = false
                    });
                }
                _list.Items.Add(new ListBoxItem { Content = Row(hit), Tag = hit, Padding = new Thickness(6, 5, 6, 5) });
            }
            _list.Visibility = _results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _empty.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Selected = 0;
        }

        private UIElement Row(Hit hit)
        {
            var grid = Ui.Columns(Ui.Auto, Ui.Star());
            UIElement icon = hit.Tab?.Icon != null
                ? new Image { Source = hit.Tab.Icon, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center }
                : (UIElement)Ui.Icon(hit.Closed != null ? "IconHistory" : "IconFile", 16);
            grid.Children.Add(((FrameworkElement)icon).Margin(0, 0, 10, 0).At(0, 0));
            var notes = new List<string>();
            var host = Host(hit.Url);
            if (host.Length > 0) notes.Add(host);
            var room = hit.Tab?.Room ?? hit.Closed?.Room;
            if (!_owner.IsPrivate && room != null && room != Rooms.DefaultId) notes.Add("in " + BrowserRooms.All.Find(room).Name);
            if (hit.Tab != null && hit.Window != _owner) notes.Add("in another window");
            if (hit.Tab != null && hit.Tab == _owner.Current) notes.Add("this tab");
            if (hit.Tab?.Muted == true) notes.Add("muted");
            else if (hit.Tab?.PlayingSound == true) notes.Add("playing sound");
            var text = Ui.Column(Ui.Text(hit.Title, 13, bold: true), Ui.Subtle(string.Join(" · ", notes), 11.5, false));
            grid.Children.Add(text.At(0, 1));
            grid.ToolTip = hit.Url;
            return grid;
        }

        private static string Host(string url) =>
            Uri.TryCreate(url ?? string.Empty, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https") ? u.Host : string.Empty;

        // ================================================================ going to a tab

        private void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    SafeClose();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    OpenSelected();
                    e.Handled = true;
                    break;
                case Key.Down:
                case Key.Up:
                    if (_results.Count == 0) break;
                    var i = Selected < 0 ? 0 : Selected + (e.Key == Key.Down ? 1 : -1);
                    Selected = Math.Max(0, Math.Min(_results.Count - 1, i));
                    if (_list.SelectedItem != null) _list.ScrollIntoView(_list.SelectedItem);
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>Goes to the chosen tab (bringing its window to the front), or opens the chosen closed tab again.</summary>
        public void OpenSelected()
        {
            var hit = Selected >= 0 ? _results[Selected] : null;
            if (hit == null) return;
            SafeClose();
            if (hit.Tab != null && hit.Window != null && MainWindow.Open.Contains(hit.Window) && hit.Window.Tabs.Contains(hit.Tab))
            {
                if (hit.Window.WindowState == WindowState.Minimized) hit.Window.WindowState = WindowState.Normal;
                hit.Window.Activate();
                hit.Window.Select(hit.Tab);
            }
            else if (hit.Closed != null && MainWindow.RecentlyClosed.Take(hit.Closed))
            {
                _owner.NewTab(hit.Closed.Url, true, _owner.Current, hit.Closed.Room);
            }
        }

        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            Dispatcher.BeginInvoke(new Action(Close));
        }
    }
}
