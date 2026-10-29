using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kudubrowse.Controls;
using Kudubrowse.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Kudubrowse.Views
{
    /// <summary>A named, coloured group of tabs; it can be folded away to just its name.</summary>
    public sealed class TabGroup
    {
        public string Name { get; set; }
        public string Color { get; set; }
        public bool Collapsed { get; set; }
    }

    /// <summary>Tab groups, two pages side by side, reading mode, full-page screenshots and saving pages as PDF.</summary>
    public sealed partial class MainWindow
    {
        private BrowserTab _splitPartner;
        private GridSplitter _splitter;

        /// <summary>The reading mode button, at the end of the address bar.</summary>
        private void InitExtras(Grid addressGrid)
        {
            var reading = Ui.Button(null, "IconNote", "GhostButton");
            reading.Padding = new Thickness(0);
            reading.Width = 32;
            reading.MinWidth = 32;
            reading.Height = 30;
            reading.Margin = new Thickness(0, 0, 4, 0);
            reading.ToolTip = "Reading mode: just the article, as clean text (F9)";
            System.Windows.Automation.AutomationProperties.SetName(reading, "Reading mode");
            reading.Click += async (s, e) => await ToggleReadingMode();
            addressGrid.ColumnDefinitions.Insert(2, new ColumnDefinition { Width = GridLength.Auto });
            foreach (UIElement c in addressGrid.Children)
                if (Grid.GetColumn(c) >= 2) Grid.SetColumn(c, Grid.GetColumn(c) + 1);
            addressGrid.Children.Add(reading.At(0, 2));
        }

        // ================================================================ tab groups

        public IEnumerable<TabGroup> Groups => _tabs.Select(t => t.Group).Where(g => g != null).Distinct();

        /// <summary>Puts a tab into a group (a new one when <paramref name="group"/> is null), next to the group's other tabs.</summary>
        public TabGroup AddToGroup(BrowserTab tab, TabGroup group = null, string name = null)
        {
            if (group == null)
            {
                var used = Groups.Select(g => g.Color).ToList();
                group = new TabGroup { Name = string.IsNullOrWhiteSpace(name) ? "Group " + (Groups.Count() + 1) : name.Trim(), Color = Rooms.Colors.Skip(1).FirstOrDefault(c => !used.Contains(c)) ?? Rooms.Colors[2] };
            }
            tab.Group = group;
            var last = _tabs.LastOrDefault(t => t.Group == group && t != tab);
            if (last != null)
            {
                _tabs.Remove(tab);
                _tabs.Insert(_tabs.IndexOf(last) + 1, tab);
            }
            RebuildStrip();
            ScheduleSave();
            return group;
        }

        public void RemoveFromGroup(BrowserTab tab)
        {
            tab.Group = null;
            RebuildStrip();
            ScheduleSave();
        }

        public void ToggleGroup(TabGroup group)
        {
            group.Collapsed = !group.Collapsed;
            // The tab being looked at never disappears: folding its group moves to another tab.
            if (group.Collapsed && Current?.Group == group)
            {
                var other = _tabs.FirstOrDefault(t => t.Group != group);
                if (other != null) Select(other);
                else group.Collapsed = false;
            }
            RebuildStrip();
        }

        /// <summary>Lays the tab headers out again, with a name label in front of each group.</summary>
        private void RebuildStrip()
        {
            _strip.Children.Clear();
            TabGroup previous = null;
            foreach (var t in _tabs)
            {
                if (t.Group != null && t.Group != previous) _strip.Children.Add(GroupChip(t.Group));
                previous = t.Group;
                t.Header.Visibility = t.Group != null && t.Group.Collapsed && t != Current ? Visibility.Collapsed : Visibility.Visible;
                t.ShowRoom();
                _strip.Children.Add(t.Header);
            }
            SizeTabs();
        }

        private Border GroupChip(TabGroup g)
        {
            var color = (Color)ColorConverter.ConvertFromString(g.Color);
            var count = _tabs.Count(t => t.Group == g);
            var text = new TextBlock { Text = g.Collapsed ? $"{g.Name} ({count})" : g.Name, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var chip = new Border
            {
                Child = text,
                Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 2, 10, 3),
                Margin = new Thickness(4, 10, 4, 4),
                Cursor = Cursors.Hand,
                ToolTip = g.Collapsed ? "Show the tabs in " + g.Name : "Fold " + g.Name + " away (click), or right-click for more"
            };
            System.Windows.Automation.AutomationProperties.SetName(chip, "Tab group " + g.Name);
            chip.MouseLeftButtonUp += (s, e) => ToggleGroup(g);
            var menu = new ContextMenu();
            menu.Items.Add(Item("Rename group...", () =>
            {
                var name = Dialogs.Ask(this, "Rename group", "Name", g.Name);
                if (name != null && name.Trim().Length > 0)
                {
                    g.Name = name.Trim();
                    RebuildStrip();
                    ScheduleSave();
                }
            }));
            menu.Items.Add(Item("Ungroup", () =>
            {
                foreach (var t in _tabs.Where(t => t.Group == g)) t.Group = null;
                RebuildStrip();
                ScheduleSave();
            }));
            menu.Items.Add(Item("Close the group's tabs", () =>
            {
                foreach (var t in _tabs.Where(t => t.Group == g).ToList()) CloseTab(t);
            }));
            chip.ContextMenu = menu;
            return chip;
        }

        private MenuItem GroupMenu(BrowserTab tab)
        {
            var add = new MenuItem { Header = "Add to group" };
            add.SubmenuOpened += (s, e) =>
            {
                add.Items.Clear();
                add.Items.Add(Item("New group...", () =>
                {
                    var name = Dialogs.Ask(this, "New group", "Name of the group", string.Empty);
                    if (name != null) AddToGroup(tab, null, name);
                }));
                var others = Groups.Where(g => g != tab.Group).ToList();
                if (others.Count > 0) add.Items.Add(new Separator());
                foreach (var g in others)
                {
                    var group = g;
                    add.Items.Add(Item(g.Name, () => AddToGroup(tab, group)));
                }
                if (tab.Group != null)
                {
                    add.Items.Add(new Separator());
                    add.Items.Add(Item("Remove from " + tab.Group.Name, () => RemoveFromGroup(tab)));
                }
            };
            add.Items.Add(new MenuItem());
            return add;
        }

        // ================================================================ side by side

        public bool IsSplit => _splitPartner != null && _tabs.Contains(_splitPartner) && Current != _splitPartner;
        public BrowserTab SplitPartner => IsSplit ? _splitPartner : null;

        /// <summary>Shows <paramref name="other"/> next to the current tab, each taking half the window (the line between them can be dragged).</summary>
        public void ShowSideBySide(BrowserTab other)
        {
            if (other == null || other == Current) return;
            _splitPartner = other;
            other.LoadIfNeeded();
            if (other.View == null) other.Navigate("about:blank");
            Layout();
        }

        public void EndSideBySide()
        {
            _splitPartner = null;
            Layout();
        }

        /// <summary>Shows the current tab's page (and its partner's, side by side), hiding the others.</summary>
        private void Layout()
        {
            if (_splitter == null)
            {
                _pages.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _pages.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _pages.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
                _splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Visibility = Visibility.Collapsed };
                _splitter.SetResourceReference(BackgroundProperty, "BorderBrush");
                Grid.SetColumn(_splitter, 1);
                _pages.Children.Add(_splitter);
                Grid.SetColumnSpan(_newTabPage, 3);
            }
            var split = IsSplit;
            _pages.ColumnDefinitions[2].Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            _pages.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            _splitter.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
            foreach (var t in _tabs)
            {
                if (t.View == null) continue;
                var shown = t == Current || (split && t == _splitPartner);
                t.View.Visibility = shown ? Visibility.Visible : Visibility.Hidden;
                // The current tab on the left (or alone); its partner on the right.
                var left = !split || t == Current;
                if (split && t == _splitPartner && _tabs.IndexOf(_splitPartner) < _tabs.IndexOf(Current)) left = true;
                if (split && t == Current && _tabs.IndexOf(_splitPartner) < _tabs.IndexOf(Current)) left = false;
                Grid.SetColumn(t.View, left ? 0 : 2);
                Grid.SetColumnSpan(t.View, split ? 1 : 3);
            }
        }

        /// <summary>Clicking into the other half makes it the current tab (the address bar follows), keeping both shown.</summary>
        private void WatchFocus(BrowserTab tab)
        {
            tab.ViewCreated += t => t.View.GotFocus += (s, e) =>
            {
                if (IsSplit && t == _splitPartner)
                {
                    var was = Current;
                    _splitPartner = was;
                    Current = t;
                    foreach (var x in _tabs) x.SetActive(x == t);
                    Layout();
                    UpdateSaveBar();
                    UpdateToolbar();
                }
            };
        }

        // ================================================================ reading mode

        private static string _readability;

        /// <summary>Shows the page's article as clean text (Mozilla's Readability finds it), or closes that view again.</summary>
        public async Task<string> ToggleReadingMode()
        {
            var core = Current?.View?.CoreWebView2;
            if (core == null) return "none";
            if (_readability == null)
                using (var s = typeof(MainWindow).Assembly.GetManifestResourceStream("Kudubrowse.Assets.Readability.js"))
                using (var r = new StreamReader(s))
                    _readability = r.ReadToEnd();
            string state;
            try { state = (await core.ExecuteScriptAsync(ReadingScript.Build(_readability, Settings.ReadingSize, Settings.ReadingTheme))).Trim('"'); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException) { state = "none"; }
            if (state == "none") Dialogs.Info(this, "This page doesn't have an article to show as clean text.");
            return state;
        }

        // ================================================================ screenshots and PDF

        /// <summary>The whole page as a picture (not just the part on screen). Returns the file saved, or null.</summary>
        public async Task<string> SaveScreenshot(string file = null)
        {
            var t = Current;
            var core = t?.View?.CoreWebView2;
            if (core == null) return null;
            file = file ?? AskFile("Save a screenshot of the whole page", Safe(t.Title) + ".png", "PNG picture (*.png)|*.png", Environment.SpecialFolder.MyPictures);
            if (file == null) return null;
            try
            {
                await Capture(core, file);
                return file;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException || ex is IOException ||
                                       ex is UnauthorizedAccessException || ex is FormatException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                Dialogs.Error(this, "Couldn't save the screenshot: " + ex.Message);
                return null;
            }
        }

        private static async Task Capture(CoreWebView2 core, string file)
        {
            var metrics = await core.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}");
            var size = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(metrics);
            var content = (Dictionary<string, object>)(size.ContainsKey("cssContentSize") ? size["cssContentSize"] : size["contentSize"]);
            var width = Math.Min(Convert.ToDouble(content["width"]), 8000);
            var height = Math.Min(Convert.ToDouble(content["height"]), 16000); // very long pages are cut at 16,000 pixels
            var shot = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot",
                "{\"format\":\"png\",\"captureBeyondViewport\":true,\"clip\":{\"x\":0,\"y\":0,\"width\":" + width.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"height\":" + height.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"scale\":1}}");
            var data = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(shot)["data"] as string;
            File.WriteAllBytes(file, Convert.FromBase64String(data));
        }

        /// <summary>Saves the page as a PDF file (as printed, with links kept). Returns the file saved, or null.</summary>
        public async Task<string> SavePdf(string file = null)
        {
            var t = Current;
            var core = t?.View?.CoreWebView2;
            if (core == null) return null;
            file = file ?? AskFile("Save the page as PDF", Safe(t.Title) + ".pdf", "PDF document (*.pdf)|*.pdf", Environment.SpecialFolder.MyDocuments);
            if (file == null) return null;
            try
            {
                var settings = (await Web.Environment).CreatePrintSettings();
                settings.ShouldPrintBackgrounds = true;
                if (await core.PrintToPdfAsync(file, settings)) return file;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException || ex is ArgumentException) { }
            Dialogs.Error(this, "Couldn't save the page as PDF. Check the folder can be written to, and try again.");
            return null;
        }

        private string AskFile(string title, string name, string filter, Environment.SpecialFolder folder)
        {
            var dlg = new SaveFileDialog { Title = title, FileName = name, Filter = filter, InitialDirectory = Environment.GetFolderPath(folder) };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        }

        private static string Safe(string title)
        {
            var s = new string((title ?? "page").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray()).Trim();
            return s.Length == 0 ? "page" : s.Length > 80 ? s.Substring(0, 80) : s;
        }
    }
}
