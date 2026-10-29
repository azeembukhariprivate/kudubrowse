using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Kudubrowse.Controls;
using Kudubrowse.Core;
using Kudubrowse.Core.Library;

namespace Kudubrowse.Views
{
    /// <summary>History and bookmarks in one window, with a search box. Double-click (or Enter) opens a page in a new tab.</summary>
    public sealed class LibraryWindow : Window
    {
        private static LibraryWindow _open;
        private readonly MainWindow _browser;
        private readonly TextBox _search = new TextBox { MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly ListBox _list = new ListBox { BorderThickness = new Thickness(0) };
        private readonly RadioButton _historyTab, _bookmarksTab;
        private readonly Button _clear;
        private readonly TextBlock _count = Ui.Subtle(string.Empty, 12, false);
        private readonly DispatcherTimer _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };

        public static LibraryWindow Show(MainWindow browser, bool bookmarks)
        {
            if (_open == null || !_open.IsLoaded)
            {
                _open = new LibraryWindow(browser);
                _open.Show();
            }
            _open.Activate();
            (bookmarks ? _open._bookmarksTab : _open._historyTab).IsChecked = true;
            return _open;
        }

        public LibraryWindow(MainWindow browser)
        {
            _browser = browser;
            Width = 820;
            Height = 640;
            Dialogs.Prepare(this, "History and bookmarks · " + AppInfo.Name, browser);
            _historyTab = new RadioButton { Content = Ui.Label("IconHistory", "History"), GroupName = "lib" }.Styled("TabRadio");
            _bookmarksTab = new RadioButton { Content = Ui.Label("IconStar", "Bookmarks"), GroupName = "lib" }.Styled("TabRadio");
            _historyTab.Checked += (s, e) => Fill();
            _bookmarksTab.Checked += (s, e) => Fill();
            System.Windows.Automation.AutomationProperties.SetName(_search, "Search history and bookmarks");
            _search.TextChanged += (s, e) =>
            {
                _searchDelay.Stop();
                _searchDelay.Start();
            };
            _searchDelay.Tick += (s, e) =>
            {
                _searchDelay.Stop();
                Fill();
            };
            _clear = Ui.Small(Ui.Button("Clear all history", "IconTrash"));
            _clear.Click += (s, e) =>
            {
                if (!Dialogs.Confirm(this, "Delete every page from the history?")) return;
                BrowserLibrary.History.Clear();
                BrowserLibrary.History.Save();
                Fill();
            };
            _list.MouseDoubleClick += (s, e) => OpenSelected();
            _list.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) OpenSelected();
                else if (e.Key == Key.Delete) DeleteSelected();
            };

            var top = Ui.Columns(Ui.Auto, Ui.Auto, Ui.Star(), Ui.Px(280));
            top.Children.Add(_historyTab.At(0, 0));
            top.Children.Add(_bookmarksTab.Margin(8, 0, 0, 0).At(0, 1));
            top.Children.Add(_search.At(0, 3));
            var bottom = Ui.Columns(Ui.Star(), Ui.Auto);
            bottom.Children.Add(_count.At(0, 0));
            bottom.Children.Add(_clear.At(0, 1));
            var root = new Grid { Margin = new Thickness(20, 16, 20, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(top.At(0, 0));
            root.Children.Add(new Border { Child = _list, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 12, 0, 12), Padding = new Thickness(4) }
                .Res(Border.BorderBrushProperty, "BorderBrush").Res(Border.BackgroundProperty, "SurfaceBg").At(1, 0));
            root.Children.Add(bottom.At(2, 0));
            Content = root;
            Loaded += (s, e) => _search.Focus();
            Closed += (s, e) => _open = null;
        }

        public bool ShowingBookmarks => _bookmarksTab.IsChecked == true;
        public int ItemCount => _list.Items.Count;

        public void Search(string text)
        {
            _search.Text = text;
            Fill();
        }

        private void Fill()
        {
            _list.Items.Clear();
            var words = _search.Text.Trim();
            _clear.Visibility = ShowingBookmarks ? Visibility.Collapsed : Visibility.Visible;
            if (ShowingBookmarks)
            {
                var lib = BrowserLibrary.Bookmarks;
                var terms = words.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                void Add(Bookmark folder, string path)
                {
                    foreach (var b in folder.Children)
                    {
                        if (b.IsFolder) Add(b, path + " › " + b.Title);
                        else if (terms.All(t => b.Title.ToLowerInvariant().Contains(t) || b.Url.ToLowerInvariant().Contains(t)))
                            _list.Items.Add(Row("IconStar", b.Title, AddressBar.Display(b.Url), path, b));
                    }
                }
                Add(lib.Bar, lib.Bar.Title);
                Add(lib.Other, lib.Other.Title);
                _count.Text = _list.Items.Count == 0 ? (words.Length > 0 ? "No bookmarks match." : "No bookmarks yet: use the star in the address bar.") : $"{_list.Items.Count} bookmarks";
            }
            else
            {
                var visits = BrowserLibrary.History.Search(words);
                DateTime? day = null;
                foreach (var v in visits)
                {
                    var local = v.LastVisit.ToLocalTime();
                    if (day != local.Date)
                    {
                        day = local.Date;
                        _list.Items.Add(new ListBoxItem
                        {
                            Content = Ui.Heading(DayName(local.Date), 13).Margin(4, 10, 0, 4),
                            IsEnabled = false
                        });
                    }
                    _list.Items.Add(Row("IconHistory", string.IsNullOrWhiteSpace(v.Title) ? AddressBar.Display(v.Url) : v.Title, AddressBar.Display(v.Url), local.ToString("t"), v));
                }
                _count.Text = visits.Count == 0 ? (words.Length > 0 ? "No pages match." : "No history yet.") : $"{visits.Count} pages" + (visits.Count == 500 ? " (the newest 500; search to find others)" : string.Empty);
            }
        }

        private static string DayName(DateTime d) =>
            d == DateTime.Today ? "Today" : d == DateTime.Today.AddDays(-1) ? "Yesterday" : d.ToString("dddd d MMMM yyyy");

        private ListBoxItem Row(string icon, string title, string url, string extra, object tag)
        {
            var g = Ui.Columns(Ui.Px(28), Ui.Star(), Ui.Auto, Ui.Auto);
            g.Children.Add(Ui.Icon(icon, 15).At(0, 0));
            var texts = Ui.Column(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 }.Res(TextBlock.ForegroundProperty, "Text"),
                new TextBlock { Text = url, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11.5 }.Res(TextBlock.ForegroundProperty, "SubtleText"));
            g.Children.Add(texts.At(0, 1));
            g.Children.Add(Ui.Subtle(extra, 11.5, false).Margin(12, 0, 8, 0).At(0, 2));
            var delete = Ui.Button(null, "IconTrash", "GhostButton");
            delete.ToolTip = "Delete";
            delete.Width = delete.MinWidth = 30;
            delete.Padding = new Thickness(0);
            g.Children.Add(delete.At(0, 3));
            var item = new ListBoxItem { Content = g, Tag = tag, Padding = new Thickness(6, 5, 6, 5) };
            delete.Click += (s, e) =>
            {
                _list.SelectedItem = item;
                DeleteSelected();
            };
            var menu = new ContextMenu();
            var open = new MenuItem { Header = "Open in a new tab" };
            open.Click += (s, e) => OpenSelected();
            var copy = new MenuItem { Header = "Copy the address" };
            copy.Click += (s, e) => Ui.Copy(tag is Bookmark b ? b.Url : ((Visit)tag).Url);
            menu.Items.Add(open);
            menu.Items.Add(copy);
            item.ContextMenu = menu;
            return item;
        }

        private void OpenSelected()
        {
            if (!(_list.SelectedItem is ListBoxItem item)) return;
            var url = item.Tag is Bookmark b ? b.Url : item.Tag is Visit v ? v.Url : null;
            if (url == null) return;
            var browser = MainWindow.Open.Contains(_browser) ? _browser : MainWindow.Open.FirstOrDefault();
            browser?.OpenAddress(url, true);
            browser?.Activate();
        }

        private void DeleteSelected()
        {
            if (!(_list.SelectedItem is ListBoxItem item)) return;
            if (item.Tag is Bookmark b) BrowserLibrary.Bookmarks.Remove(b);
            else if (item.Tag is Visit v)
            {
                BrowserLibrary.History.Remove(v.Url);
                BrowserLibrary.History.Save();
            }
            _list.Items.Remove(item);
        }
    }

    /// <summary>Brings bookmarks and history over from Chrome (it only reads Chrome's files).</summary>
    public sealed class ImportWindow : Window
    {
        private readonly ComboBox _profiles = new ComboBox { MinHeight = 32 };
        private readonly CheckBox _bookmarks = new CheckBox { Content = "Bookmarks", IsChecked = true };
        private readonly CheckBox _history = new CheckBox { Content = "History (the last 90 days)", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
        private readonly TextBlock _result = Ui.Subtle(string.Empty, 12.5);
        private readonly Button _go;
        private readonly List<ChromeProfile> _found;

        /// <summary>For the self-test: what the last import brought in.</summary>
        public static string LastResult { get; private set; }

        public ImportWindow(Window owner)
        {
            Width = 520;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Dialogs.Prepare(this, "Bring from Chrome · " + AppInfo.Name, owner);
            _found = ChromeImport.Profiles();
            foreach (var p in _found) _profiles.Items.Add(p.Name);
            _profiles.SelectedIndex = _found.Count > 0 ? 0 : -1;

            var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
            root.Children.Add(Ui.Heading("Bring bookmarks and history from Chrome", 18));
            if (_found.Count == 0)
            {
                root.Children.Add(Ui.Subtle("Chrome isn't installed for your Windows account, or it has no bookmarks or history yet.", 12.5).Margin(0, 10, 0, 0));
                var close = Ui.Button("Close", null, "PrimaryButton");
                close.IsCancel = true;
                close.Click += (s, e) => Close();
                close.HorizontalAlignment = HorizontalAlignment.Right;
                root.Children.Add(close.Margin(0, 18, 0, 0));
                Content = root;
                return;
            }
            root.Children.Add(Ui.Subtle("Chrome's own files are only read, never changed. Things you already have aren't added twice.", 12.5).Margin(0, 6, 0, 14));
            if (_found.Count > 1)
            {
                root.Children.Add(Ui.Text("Chrome profile", 12.5).Margin(0, 0, 0, 4));
                root.Children.Add(_profiles.Margin(0, 0, 0, 12));
            }
            root.Children.Add(_bookmarks);
            root.Children.Add(_history);
            root.Children.Add(Ui.Subtle("Passwords come later, with the password manager.", 12).Margin(0, 10, 0, 0));
            root.Children.Add(_result.Margin(0, 12, 0, 0));
            var cancel = Ui.Button("Close");
            cancel.IsCancel = true;
            cancel.Click += (s, e) => Close();
            _go = Ui.Button("Bring them over", "IconImport", "PrimaryButton");
            _go.IsDefault = true;
            _go.Click += async (s, e) => await Run();
            var buttons = Ui.Row(cancel, _go.Margin(8, 0, 0, 0));
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            root.Children.Add(buttons.Margin(0, 18, 0, 0));
            Content = root;
        }

        public async Task Run()
        {
            if (_profiles.SelectedIndex < 0) return;
            var profile = _found[_profiles.SelectedIndex];
            _go.IsEnabled = false;
            var notes = new List<string>();
            if (_bookmarks.IsChecked == true && profile.HasBookmarks)
            {
                try
                {
                    var data = await Task.Run(() => ChromeImport.ReadBookmarks(Path.Combine(profile.Folder, "Bookmarks")));
                    var lib = BrowserLibrary.Bookmarks;
                    var n = lib.Merge(data.Bar, lib.Bar);
                    var other = lib.Other.Children.FirstOrDefault(c => c.IsFolder && c.Title == "From Chrome") ?? lib.AddFolder("From Chrome");
                    n += lib.Merge(data.Other, other);
                    if (other.Children.Count == 0) lib.Remove(other);
                    notes.Add(n == 1 ? "1 bookmark added" : $"{n} bookmarks added");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Runtime.Serialization.SerializationException)
                {
                    notes.Add("Couldn't read Chrome's bookmarks (" + ex.Message + ")");
                }
            }
            if (_history.IsChecked == true && profile.HasHistory)
            {
                try
                {
                    var visits = await Task.Run(() => ChromeImport.ReadHistory(Path.Combine(profile.Folder, "History")));
                    var n = BrowserLibrary.History.Merge(visits);
                    BrowserLibrary.History.Save();
                    notes.Add(n == 1 ? "1 page added to the history" : $"{n} pages added to the history");
                }
                catch (IOException)
                {
                    notes.Add("Chrome's history is in use: close Chrome, then try again");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is InvalidDataException)
                {
                    notes.Add("Couldn't read Chrome's history (" + ex.Message + ")");
                }
            }
            LastResult = string.Join(". ", notes) + ".";
            _result.Text = LastResult;
            _go.IsEnabled = true;
        }
    }
}
