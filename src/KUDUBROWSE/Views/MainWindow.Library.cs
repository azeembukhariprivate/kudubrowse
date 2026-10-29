using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Kudubrowse.Controls;
using Kudubrowse.Core;
using Kudubrowse.Core.Library;

namespace Kudubrowse.Views
{
    /// <summary>The window's bookmarks parts: the star in the address bar, the bookmarks bar, and suggestions while typing.</summary>
    public sealed partial class MainWindow
    {
        private Button _star;
        private Glyph _starIcon;
        private WrapPanel _barItems;
        private Border _bar;
        private Popup _suggestPopup;
        private ListBox _suggestList;
        private bool _settingAddress;

        private void InitLibrary(Grid addressGrid)
        {
            // The star: bookmark this page.
            _starIcon = Ui.Icon("IconStar", 16);
            _star = Ui.Button(null, "IconStar", "GhostButton");
            _star.Content = _starIcon;
            _star.Padding = new Thickness(0);
            _star.Width = 32;
            _star.MinWidth = 32;
            _star.Height = 30;
            _star.Margin = new Thickness(0, 0, 4, 0);
            _star.ToolTip = "Bookmark this page (Ctrl+D)";
            _star.Click += (s, e) => ShowBookmarkPanel();
            addressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addressGrid.Children.Add(_star.At(0, 2));

            // The bookmarks bar.
            _barItems = new WrapPanel { Height = 32, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
            _bar = new Border { Child = _barItems, Padding = new Thickness(8, 2, 8, 2), BorderThickness = new Thickness(0, 0, 0, 1) }
                .Res(Border.BackgroundProperty, "SurfaceBg").Res(Border.BorderBrushProperty, "BorderBrush");
            BrowserLibrary.Bookmarks.Changed += OnBookmarksChanged;
            Closed += (s, e) => BrowserLibrary.Bookmarks.Changed -= OnBookmarksChanged;
            FillBar();

            // Suggestions while typing in the address bar.
            _suggestList = new ListBox { MaxHeight = 380, BorderThickness = new Thickness(0), Focusable = false };
            _suggestList.SetResourceReference(BackgroundProperty, "SurfaceBg");
            _suggestList.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_suggestList.SelectedItem is ListBoxItem item && item.Tag is Suggestion sug) OpenSuggestion(sug);
            };
            _suggestPopup = new Popup
            {
                Child = new Border { Child = _suggestList, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4) }
                    .Res(Border.BackgroundProperty, "SurfaceBg").Res(Border.BorderBrushProperty, "BorderBrush"),
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true
            };
            _address.TextChanged += (s, e) =>
            {
                if (!_settingAddress && _address.IsKeyboardFocusWithin) UpdateSuggestions();
            };
            _address.LostKeyboardFocus += (s, e) => _suggestPopup.IsOpen = false;
        }

        private void OnBookmarksChanged()
        {
            FillBar();
            UpdateStar();
        }

        public bool BarVisible => _bar.Visibility == Visibility.Visible;

        private void FillBar()
        {
            _barItems.Children.Clear();
            var items = BrowserLibrary.Bookmarks.Bar.Children;
            _bar.Visibility = Settings.ShowBookmarksBar ? Visibility.Visible : Visibility.Collapsed;
            if (items.Count == 0)
            {
                _barItems.Children.Add(Ui.Subtle("Bookmarks you add with the star appear here.", 12, false).Margin(6, 7, 0, 0));
                return;
            }
            foreach (var item in items) _barItems.Children.Add(BarButton(item));
        }

        private Button BarButton(Bookmark item)
        {
            var text = item.Title.Length > 28 ? item.Title.Substring(0, 26) + "…" : item.Title;
            var b = Ui.Small(Ui.Button(text, item.IsFolder ? "IconFolder" : "IconGlobe", "GhostButton"));
            b.Margin = new Thickness(0, 0, 2, 0);
            b.MinHeight = 28;
            b.ToolTip = item.IsFolder ? item.Title : item.Title + "\n" + item.Url;
            b.Click += (s, e) =>
            {
                if (item.IsFolder) FolderMenu(item, b).IsOpen = true;
                else OpenAddress(item.Url, NewTabWanted());
            };
            b.PreviewMouseUp += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle && !item.IsFolder)
                {
                    OpenAddress(item.Url, true);
                    e.Handled = true;
                }
            };
            var menu = new ContextMenu();
            if (!item.IsFolder) menu.Items.Add(Item("Open in a new tab", () => OpenAddress(item.Url, true)));
            menu.Items.Add(Item("Rename...", () =>
            {
                var name = Dialogs.Ask(this, "Rename", "Name", item.Title);
                if (name != null) BrowserLibrary.Bookmarks.Rename(item, name);
            }));
            menu.Items.Add(Item("Delete", () => BrowserLibrary.Bookmarks.Remove(item)));
            b.ContextMenu = menu;
            return b;
        }

        private ContextMenu FolderMenu(Bookmark folder, UIElement anchor)
        {
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
            void Fill(ItemsControl into, Bookmark f)
            {
                if (f.Children.Count == 0) into.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
                foreach (var c in f.Children)
                {
                    if (c.IsFolder)
                    {
                        var sub = new MenuItem { Header = c.Title };
                        Fill(sub, c);
                        into.Items.Add(sub);
                    }
                    else into.Items.Add(Item(c.Title, () => OpenAddress(c.Url, NewTabWanted())));
                }
            }
            Fill(menu, folder);
            return menu;
        }

        private static bool NewTabWanted() => (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        /// <summary>Opens an address here, or in a new tab.</summary>
        public void OpenAddress(string url, bool newTab)
        {
            if (newTab || Current == null) NewTab(url, true, Current);
            else
            {
                _newTabPage.Visibility = Visibility.Collapsed;
                Current.Navigate(url);
                Current.View?.Focus();
            }
        }

        public void ToggleBookmarksBar()
        {
            Settings.ShowBookmarksBar = !Settings.ShowBookmarksBar;
            foreach (var w in Open) w.FillBar();
        }

        // ================================================================ the star

        private void UpdateStar()
        {
            if (_star == null) return;
            var url = Current?.Url;
            var marked = !string.IsNullOrEmpty(url) && BrowserLibrary.Bookmarks.Contains(url);
            _star.IsEnabled = Current != null && Session.IsSafeToReopen(url);
            _starIcon.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, marked ? "Accent" : "Text");
            _starIcon.Opacity = marked ? 1 : 0.7;
            _star.ToolTip = marked ? "Edit this bookmark (Ctrl+D)" : "Bookmark this page (Ctrl+D)";
        }

        /// <summary>Bookmarks the page (on the bar) and shows a small panel to rename it, choose where it goes, or remove it.</summary>
        public Popup ShowBookmarkPanel()
        {
            var t = Current;
            if (t == null || !Session.IsSafeToReopen(t.Url)) return null;
            var lib = BrowserLibrary.Bookmarks;
            var mark = lib.Find(t.Url) ?? lib.Add(t.IsBlank ? t.Url : t.Title, t.Url);
            var name = new TextBox { Text = mark.Title, MinHeight = 32, VerticalContentAlignment = VerticalAlignment.Center };
            var where = new ComboBox { MinHeight = 32 };
            var folders = new List<Bookmark>();
            void AddFolders(Bookmark f, string indent)
            {
                folders.Add(f);
                where.Items.Add(indent + f.Title);
                foreach (var c in f.Children.Where(c => c.IsFolder)) AddFolders(c, indent + "    ");
            }
            AddFolders(lib.Bar, string.Empty);
            AddFolders(lib.Other, string.Empty);
            where.SelectedIndex = Math.Max(0, folders.IndexOf(lib.ParentOf(mark)));
            var popup = new Popup { PlacementTarget = _star, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
            var remove = Ui.Button("Remove");
            var done = Ui.Button("Done", null, "PrimaryButton");
            remove.Click += (s, e) =>
            {
                lib.Remove(mark);
                popup.IsOpen = false;
            };
            done.Click += (s, e) => popup.IsOpen = false;
            popup.Closed += (s, e) =>
            {
                if (lib.ParentOf(mark) == null) return; // removed
                if (name.Text.Trim().Length > 0 && name.Text.Trim() != mark.Title) lib.Rename(mark, name.Text);
                if (where.SelectedIndex >= 0 && folders[where.SelectedIndex] != lib.ParentOf(mark)) lib.Move(mark, folders[where.SelectedIndex]);
            };
            name.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) popup.IsOpen = false;
            };
            var buttons = Ui.Row(remove, done.Margin(8, 0, 0, 0));
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            var panel = Ui.Column(
                Ui.Heading("Bookmark added", 15),
                Ui.Subtle("Name", 12).Margin(0, 10, 0, 4), name,
                Ui.Subtle("Folder", 12).Margin(0, 10, 0, 4), where,
                buttons.Margin(0, 14, 0, 0));
            panel.Width = 320;
            popup.Child = new Border
            {
                Child = panel,
                Padding = new Thickness(18, 16, 18, 16),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 4, 8, 8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.25 }
            }.Res(Border.BackgroundProperty, "SurfaceBg").Res(Border.BorderBrushProperty, "BorderBrush");
            popup.Opened += (s, e) =>
            {
                name.Focus();
                name.SelectAll();
            };
            popup.IsOpen = true;
            return popup;
        }

        // ================================================================ suggestions

        public IReadOnlyList<Suggestion> CurrentSuggestions { get; private set; } = new List<Suggestion>();

        private void UpdateSuggestions()
        {
            var list = Suggestions.For(_address.Text, BrowserLibrary.Bookmarks.AllLinks(), BrowserLibrary.History.All, SearchEngines.Find(Settings.SearchEngine));
            CurrentSuggestions = list;
            _suggestList.Items.Clear();
            foreach (var s in list)
            {
                var icon = s.Kind == SuggestionKind.Search ? "IconSearch" : s.Kind == SuggestionKind.Bookmark ? "IconStar" : s.Kind == SuggestionKind.History ? "IconHistory" : "IconGlobe";
                var text = s.Kind == SuggestionKind.Search ? s.Text + "  —  " + SearchEngines.Find(Settings.SearchEngine).Name + " search" : s.Text;
                var row = Ui.Columns(Ui.Px(26), Ui.Star(), Ui.Auto);
                row.Children.Add(Ui.Icon(icon, 15).At(0, 0));
                row.Children.Add(new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 }.Res(TextBlock.ForegroundProperty, "Text").At(0, 1));
                if (s.Kind == SuggestionKind.Bookmark || s.Kind == SuggestionKind.History)
                    row.Children.Add(Ui.Subtle("  " + AddressBar.Display(s.Url), 12, false).At(0, 2));
                ((FrameworkElement)row.Children[row.Children.Count - 1]).MaxWidth = 380;
                _suggestList.Items.Add(new ListBoxItem { Content = row, Tag = s, Padding = new Thickness(8, 6, 8, 6) });
            }
            _suggestPopup.PlacementTarget = _address;
            _suggestPopup.Width = Math.Max(300, _address.ActualWidth + 40);
            _suggestPopup.IsOpen = list.Count > 0;
        }

        /// <summary>Up and Down move through the suggestions; Enter opens the chosen one. Returns true when the key was used.</summary>
        private bool SuggestionKey(Key key)
        {
            if (!_suggestPopup.IsOpen || _suggestList.Items.Count == 0) return false;
            if (key == Key.Down || key == Key.Up)
            {
                var i = _suggestList.SelectedIndex + (key == Key.Down ? 1 : -1);
                _suggestList.SelectedIndex = Math.Max(-1, Math.Min(_suggestList.Items.Count - 1, i));
                if (_suggestList.SelectedItem is ListBoxItem item && item.Tag is Suggestion s)
                {
                    _settingAddress = true;
                    _address.Text = s.Kind == SuggestionKind.Search ? s.Text : AddressBar.Display(s.Url);
                    _address.CaretIndex = _address.Text.Length;
                    _settingAddress = false;
                }
                return true;
            }
            if (key == Key.Enter && _suggestList.SelectedItem is ListBoxItem chosen && chosen.Tag is Suggestion sug)
            {
                OpenSuggestion(sug);
                return true;
            }
            if (key == Key.Escape)
            {
                _suggestPopup.IsOpen = false;
                return true;
            }
            return false;
        }

        /// <summary>For the self-test: types into the address bar and returns what's suggested.</summary>
        public IReadOnlyList<Suggestion> TypeInAddress(string text)
        {
            _address.Text = text;
            UpdateSuggestions();
            return CurrentSuggestions;
        }

        public void CloseSuggestions() => _suggestPopup.IsOpen = false;

        private void OpenSuggestion(Suggestion s)
        {
            _suggestPopup.IsOpen = false;
            OpenAddress(s.Url, false);
        }

        private void SetAddressText(string text)
        {
            _settingAddress = true;
            _address.Text = text;
            _settingAddress = false;
        }
    }
}
