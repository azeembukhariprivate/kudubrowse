// The browser window's saved tab sets: putting a window's tabs away under a name, and opening them all again later.
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
    public sealed partial class MainWindow
    {
        /// <summary>True while tabs are being put away in a set (they don't go into "closed tabs").</summary>
        private bool _puttingAway;

        /// <summary>
        /// Adds saved tabs after the others, as they were: their rooms, groups (put together again) and muting. They load
        /// when first shown. Returns the tabs added.
        /// </summary>
        public List<BrowserTab> AddSavedTabs(IEnumerable<SavedTab> saved)
        {
            var added = new List<BrowserTab>();
            var groups = new Dictionary<string, TabGroup>();
            foreach (var t in saved)
            {
                var tab = new BrowserTab(t.Url, t.Title, IsPrivate ? null : t.Room, IsPrivate);
                if (t.Muted) tab.SetMuted(true);
                if (t.Group != null)
                {
                    var key = t.Group + "|" + t.GroupColor;
                    if (!groups.TryGetValue(key, out var g))
                        groups[key] = g = _tabs.Select(x => x.Group).FirstOrDefault(x => x != null && x.Name == t.Group && x.Color == t.GroupColor)
                                          ?? new TabGroup { Name = t.Group, Color = t.GroupColor };
                    tab.Group = g;
                }
                AddTab(tab, _tabs.Count, false);
                added.Add(tab);
            }
            return added;
        }

        /// <summary>The tabs of this window that a set can keep (not new, empty tabs).</summary>
        public List<SavedTab> TabsForSet() => _tabs.Where(t => !t.IsBlank && Session.IsSafeToReopen(t.Url)).Select(t => t.ToSaved()).ToList();

        /// <summary>
        /// Keeps this window's tabs as a set with this name; with <paramref name="putAway"/> they're closed too (a new tab
        /// is left). Returns the set, or null when there were no pages to keep.
        /// </summary>
        public TabSet SaveTabSet(string name, bool putAway)
        {
            var set = BrowserLibrary.TabSets.Add(name, TabsForSet());
            if (set == null || !putAway) return set;
            var old = _tabs.ToList();
            NewTab(select: true);
            _puttingAway = true;
            try
            {
                foreach (var t in old) CloseTab(t);
            }
            finally { _puttingAway = false; }
            return set;
        }

        /// <summary>Asks for a name and saves this window's tabs as a set ("Put these tabs away" ticked to start with).</summary>
        public void AskToSaveTabSet()
        {
            var tabs = TabsForSet();
            if (tabs.Count == 0)
            {
                Dialogs.Info(this, "There are no pages in this window to keep yet.");
                return;
            }
            var box = new TextBox { MinHeight = 36, Margin = new Thickness(0, 8, 0, 0), MaxLength = TabSets.MaxName };
            System.Windows.Automation.AutomationProperties.SetName(box, "Name of the set");
            var putAway = new CheckBox { Content = "Put these tabs away (close them now)", IsChecked = true, Margin = new Thickness(0, 12, 0, 0) };
            var content = Ui.Column(
                Ui.Heading("Save tabs as a set", 17),
                Ui.Subtle(Lang.T(tabs.Count == 1 ? "1 tab from this window is kept under a name, to open again later." : "{0} tabs from this window are kept under a name, to open them all again later.", tabs.Count), 12.5).Margin(0, 6, 0, 0),
                Ui.Text("Name (for example Work, or Trip planning)", 12.5).Margin(0, 12, 0, 0),
                box,
                putAway);
            var save = Ui.Button("Save", null, "PrimaryButton");
            var cancel = Ui.Button("Cancel");
            var window = Dialogs.Dialog(this, "Save tabs as a set", content, 440, cancel, save);
            save.IsDefault = true;
            cancel.IsCancel = true;
            save.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(box.Text)) return;
                window.DialogResult = true;
            };
            window.Loaded += (s, e) => box.Focus();
            if (window.ShowDialog() != true) return;
            var set = SaveTabSet(box.Text, putAway.IsChecked == true);
            if (set != null)
                ShowNote(_menuButton, "IconList", Lang.T("Saved the set \"{0}\"", set.Name), "Open it again from the menu › Tab sets.", () => TabSetsWindow.Show(this));
        }

        /// <summary>Opens a set's tabs: here (an empty window's new tab makes way for them), or in a new window.</summary>
        public MainWindow OpenTabSet(TabSet set, bool newWindow = false)
        {
            if (set == null || set.Tabs.Count == 0) return this;
            var target = this;
            if (newWindow)
            {
                target = new MainWindow();
                target.Show();
            }
            var empty = target._tabs.Where(t => t.IsBlank && t.View == null).ToList();
            var added = target.AddSavedTabs(set.Tabs);
            if (added.Count > 0) target.Select(added[0]);
            // A new, empty tab left over (an empty window, or after putting tabs away) goes.
            if (target._tabs.Count > empty.Count)
                foreach (var t in empty) target.CloseTab(t);
            target.Activate();
            return target;
        }

        /// <summary>The menu's "Tab sets": save these tabs, each set to open, and the list.</summary>
        private MenuItem TabSetsMenu()
        {
            var menu = new MenuItem { Header = "Tab sets" };
            menu.Items.Add(Item("Save these tabs as a set...", AskToSaveTabSet));
            var sets = BrowserLibrary.TabSets.All;
            if (sets.Count > 0) menu.Items.Add(new Separator());
            foreach (var s in sets.Take(12))
            {
                var set = s;
                menu.Items.Add(Item(Lang.T(set.Tabs.Count == 1 ? "{0} (1 tab)" : "{0} ({1} tabs)", set.Name, set.Tabs.Count), () => OpenTabSet(set)));
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("All tab sets...", () => TabSetsWindow.Show(this)));
            return menu;
        }
    }

    /// <summary>The saved tab sets: open one here or in a new window, rename or delete it.</summary>
    public sealed class TabSetsWindow : Window
    {
        /// <summary>The window when it's open (only one at a time).</summary>
        private static TabSetsWindow _open;
        /// <summary>The browser window it was opened from.</summary>
        private readonly MainWindow _browser;
        /// <summary>The list of sets.</summary>
        private readonly StackPanel _list = new StackPanel();
        /// <summary>How many sets there are.</summary>
        private readonly TextBlock _count = Ui.Subtle(string.Empty, 12, false);

        /// <summary>Shows the window (bringing it forward when it's already open).</summary>
        public static TabSetsWindow Show(MainWindow browser)
        {
            if (_open == null || !_open.IsLoaded)
            {
                _open = new TabSetsWindow(browser);
                _open.Show();
            }
            _open.Activate();
            return _open;
        }

        /// <summary>Makes the window.</summary>
        public TabSetsWindow(MainWindow browser)
        {
            _browser = browser;
            Width = 620;
            Height = 520;
            Dialogs.Prepare(this, "Tab sets · " + AppInfo.Name, browser);
            var save = Ui.Small(Ui.Button("Save this window's tabs as a set...", "IconPlus"));
            save.Click += (s, e) => (MainWindow.Open.Contains(_browser) ? _browser : MainWindow.LastActive)?.AskToSaveTabSet();
            save.IsEnabled = !browser.IsPrivate;
            var top = Ui.Columns(Ui.Star(), Ui.Auto);
            top.Children.Add(Ui.Column(Ui.Heading("Tab sets", 18),
                Ui.Subtle("Put a window's tabs away under a name, and open them all again when you need them.", 12.5).Margin(0, 4, 0, 0)).At(0, 0));
            top.Children.Add(save.At(0, 1));
            save.VerticalAlignment = VerticalAlignment.Top;
            var root = new Grid { Margin = new Thickness(20, 16, 20, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(top.At(0, 0));
            root.Children.Add(new Border
            {
                Child = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 12, 0, 12),
                Padding = new Thickness(6)
            }.Res(Border.BorderBrushProperty, "BorderBrush").Res(Border.BackgroundProperty, "SurfaceBg").At(1, 0));
            root.Children.Add(_count.At(2, 0));
            Content = root;
            Action changed = () => Dispatcher.BeginInvoke(new Action(Fill));
            BrowserLibrary.TabSets.Changed += changed;
            Closed += (s, e) =>
            {
                BrowserLibrary.TabSets.Changed -= changed;
                _open = null;
            };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Close();
            };
            Fill();
        }

        /// <summary>How many sets are listed (for the self-test).</summary>
        public int SetCount => _list.Children.OfType<Grid>().Count();

        /// <summary>Fills the list: each set with its tabs' names and its buttons.</summary>
        private void Fill()
        {
            _list.Children.Clear();
            var sets = BrowserLibrary.TabSets.All;
            foreach (var s in sets)
            {
                var set = s;
                var g = Ui.Columns(Ui.Px(28), Ui.Star(), Ui.Auto);
                g.Margin = new Thickness(4, 6, 4, 6);
                g.Children.Add(Ui.Icon("IconList", 16).At(0, 0));
                var names = string.Join(" · ", set.Tabs.Take(4).Select(t => string.IsNullOrWhiteSpace(t.Title) ? AddressBar.Display(t.Url) : t.Title));
                if (set.Tabs.Count > 4) names += " …";
                var texts = Ui.Column(
                    Ui.Text(Lang.T(set.Tabs.Count == 1 ? "{0} (1 tab)" : "{0} ({1} tabs)", set.Name, set.Tabs.Count), 13.5, bold: true),
                    new TextBlock { Text = names, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11.5 }.Res(TextBlock.ForegroundProperty, "SubtleText"),
                    Ui.Subtle(Lang.T("Saved {0}", set.Saved.ToLocalTime().ToString("d MMM yyyy, HH:mm")), 11, false));
                g.Children.Add(texts.At(0, 1));
                var buttons = Ui.Row();
                var open = Ui.Small(Ui.Button("Open", "IconExternal", "PrimaryButton"));
                open.ToolTip = "Open its tabs in this window";
                open.Click += (x, e) => Browser()?.OpenTabSet(set);
                var newWindow = Ui.Small(Ui.Button("New window"));
                newWindow.ToolTip = "Open its tabs in a new window";
                newWindow.Click += (x, e) => Browser()?.OpenTabSet(set, true);
                var more = Ui.Small(Ui.Button(null, "IconMore", "GhostButton"));
                more.ToolTip = "Rename or delete";
                more.Click += (x, e) =>
                {
                    var menu = new ContextMenu { PlacementTarget = more, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
                    var rename = new MenuItem { Header = "Rename..." };
                    rename.Click += (y, z) =>
                    {
                        var name = Dialogs.Ask(this, "Rename the set", "Name", set.Name);
                        if (name != null) BrowserLibrary.TabSets.Rename(set, name);
                    };
                    var delete = new MenuItem { Header = "Delete" };
                    delete.Click += (y, z) =>
                    {
                        if (Dialogs.Confirm(this, Lang.T("Delete the set \"{0}\"? Its tabs aren't open anywhere else.", set.Name))) BrowserLibrary.TabSets.Remove(set);
                    };
                    menu.Items.Add(rename);
                    menu.Items.Add(delete);
                    menu.IsOpen = true;
                };
                buttons.Children.Add(open);
                buttons.Children.Add(newWindow.Margin(6, 0, 0, 0));
                buttons.Children.Add(more.Margin(4, 0, 0, 0));
                buttons.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(buttons.At(0, 2));
                _list.Children.Add(g);
            }
            if (sets.Count == 0)
                _list.Children.Add(Ui.Subtle("No tab sets yet. Use \"Save this window's tabs as a set\", or the menu › Tab sets.", 12.5).Margin(10, 12, 10, 12));
            _count.Text = sets.Count == 0 ? string.Empty : Lang.T(sets.Count == 1 ? "1 set" : "{0} sets", sets.Count);
        }

        /// <summary>The browser window to open sets in: the one it was opened from, else the last one used.</summary>
        private MainWindow Browser() => MainWindow.Open.Contains(_browser) && !_browser.IsPrivate ? _browser : MainWindow.Open.LastOrDefault(w => !w.IsPrivate) ?? MainWindow.LastActive;
    }
}
