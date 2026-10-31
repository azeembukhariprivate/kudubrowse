using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Kudubrowse.Controls;
using Kudubrowse.Core;

namespace Kudubrowse.Views
{
    /// <summary>
    /// About KUDUBROWSE: who made it, its licence and version, updates, and everything it can do (what makes it
    /// different and its best everyday features), each with a way to try it.
    /// </summary>
    public sealed class AboutWindow : Window
    {
        private static AboutWindow _open;
        private readonly MainWindow _main;

        /// <summary>One feature: what it's called, a line about it, its shortcut (or where to find it), and how to try it.</summary>
        public sealed class Feature
        {
            public Feature(string icon, string name, string about, string keys = null, Action<MainWindow> tryIt = null)
            {
                Icon = icon;
                Name = name;
                About = about;
                Keys = keys;
                TryIt = tryIt;
            }

            public string Icon { get; }
            public string Name { get; }
            public string About { get; }
            public string Keys { get; }
            public Action<MainWindow> TryIt { get; }
        }

        public static readonly IReadOnlyList<Feature> Different = new[]
        {
            new Feature("IconUser", "Rooms", "Keep separate logins apart, like your personal and work email, each in its own room with its own colour.",
                "Menu › New tab in a room", m => m.NewTab(select: true, room: BrowserRooms.All.All.Skip(1).FirstOrDefault()?.Id)),
            new Feature("IconEye", "No account, nothing collected", "Nothing about you is sent anywhere. Your tabs, history and passwords stay on this PC."),
            new Feature("IconShield", "Ads, trackers, cookie banners and pop-ups blocked", "Built in, nothing to add. The shield shows what was stopped, and lets a site off if it doesn't work right.",
                "The shield in the toolbar", m => m.ShowShield()),
            new Feature("IconLock", "Passwords filled only when you pick them", "Saved logins go into a page only after you choose one from the browser's own list, so a page can't take them. Seeing a password asks Windows first.",
                "Menu › Passwords", m => PasswordsWindow.Show(m)),
            new Feature("IconWarn", "Leaked password check", "Finds passwords that have turned up in known leaks. Only the first 5 characters of a scrambled copy are sent, never the password.",
                "Passwords › Check for leaks", m => PasswordsWindow.Show(m)),
            new Feature("IconImport", "Moving from Chrome", "Brings over your bookmarks and history, and your passwords from Chrome's saved passwords file.",
                "Menu › Bring bookmarks and history from Chrome", m => new ImportWindow(m).ShowDialog()),
            new Feature("IconPause", "Sleeping tabs", "Tabs you haven't looked at for 10 minutes rest and use less memory. They wake up when you click them.")
        };

        public static readonly IReadOnlyList<Feature> Everyday = new[]
        {
            new Feature("IconStar", "Bookmarks and the bookmarks bar", "Keep the pages you like one click away.", "Ctrl+D, Ctrl+Shift+O", m => LibraryWindow.Show(m, true)),
            new Feature("IconHistory", "History", "Find a page you saw before; it's kept for 90 days.", "Ctrl+H", m => LibraryWindow.Show(m, false)),
            new Feature("IconSearch", "Suggestions as you type", "The address bar suggests pages from your bookmarks and history.", "Ctrl+L", m => m.FocusAddress()),
            new Feature("IconList", "Tab groups", "Put tabs in a named, coloured group, and fold the group away when you don't need it.", "Right-click a tab › Add to group",
                m => { if (m.Current != null) m.AddToGroup(m.Current); }),
            new Feature("IconCompare", "Two pages side by side", "Show two tabs next to each other, and drag the line between them.", "Right-click a tab › Show side by side",
                m =>
                {
                    var other = m.Tabs.FirstOrDefault(t => t != m.Current) ?? m.NewTab(select: false);
                    m.ShowSideBySide(other);
                }),
            new Feature("IconNote", "Reading mode", "Just the article, as clean text, with the size and colours you like.", "F9", async m => await m.ToggleReadingMode()),
            new Feature("IconPicture", "Screenshot of the whole page", "Saves the whole page as a picture, not just the part on screen.", "Ctrl+Shift+S", async m => await m.SaveScreenshot()),
            new Feature("IconFile", "Save a page as PDF", "Keeps a page as a PDF file, with its links.", "Menu › Save the page as PDF", async m => await m.SavePdf()),
            new Feature("IconEye", "Private windows", "Nothing is kept: no history, and sites' cookies go when the window closes.", "Ctrl+Shift+N", m => m.NewPrivateWindow()),
            new Feature("IconDown", "Download manager", "Big files come faster in pieces at once; pause and carry on (even after closing the browser), try again, and sort files into folders by type.", "Ctrl+J", m => DownloadsWindow.Show(m)),
            new Feature("IconUndo", "Reopen closed tabs", "Bring back a tab you closed by mistake.", "Ctrl+Shift+T", m => m.ReopenClosedTab()),
            new Feature("IconRefresh", "Your tabs come back", "The tabs you had open are there next time, and load when you click them."),
            new Feature("IconMonitor", "Zoom, print and full screen", "Make pages bigger or smaller, print them, or fill the screen.", "Ctrl+plus, Ctrl+P, F11")
        };

        public static AboutWindow Show(MainWindow main, Window owner = null)
        {
            if (_open == null || !_open.IsLoaded)
            {
                _open = new AboutWindow(main, owner);
                _open.Show();
            }
            _open.Activate();
            return _open;
        }

        public AboutWindow(MainWindow main, Window owner = null)
        {
            _main = main;
            Width = 700;
            Height = Math.Min(800, SystemParameters.WorkArea.Height - 40);
            Dialogs.Prepare(this, "About " + AppInfo.Name, owner ?? main);
            var list = new StackPanel();

            // Who made it, its licence and where it comes from.
            var header = Ui.Row(LogoTile.Create(64), Ui.Column(Ui.Text(AppInfo.Name, 20, bold: true),
                Ui.Subtle("Version " + AppInfo.VersionText + " · A private everyday web browser", 12.5)).Margin(16, 0, 0, 0));
            ((FrameworkElement)header.Children[1]).VerticalAlignment = VerticalAlignment.Center;
            list.Children.Add(header);
            var rows = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            void Row(string label, UIElement value)
            {
                var g = Ui.Columns(Ui.Px(120), Ui.Star());
                g.Children.Add(Ui.Subtle(label, 12.5, false).At(0, 0));
                g.Children.Add(value.At(0, 1));
                rows.Children.Add(new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 7, 0, 7), Child = g }.Res(Border.BorderBrushProperty, "BorderBrush"));
            }
            var madeBy = Ui.Text("Azeem, with Claude and ", 12.5);
            var heart = new Run("♥");
            heart.SetResourceReference(TextElement.ForegroundProperty, "Accent");
            madeBy.Inlines.Add(heart);
            Row("Made by", madeBy);
            Row("Licence", Ui.Row(Ui.Text("MIT · free and open source  ", 12.5), Ui.Link("View", (s, e) => OpenLocal("LICENSE.txt"))));
            Row("Source code", Ui.Link("github.com/" + AppInfo.RepoOwner + "/" + AppInfo.RepoName, (s, e) => AppInfo.OpenUrl(AppInfo.RepoUrl)));
            var web = Web.RuntimeVersion;
            Row("Web engine", Ui.Text(web != null ? "Microsoft Edge WebView2 " + web + " (kept up to date by Windows)" : "Microsoft Edge WebView2 isn't installed", 12.5, wrap: true));
            Row("Third-party", Ui.Link("Third-party notices", (s, e) => OpenLocal("THIRD-PARTY-NOTICES.txt")));

            // Updates: what the browser knows, and a way to look now.
            var status = Ui.Text(string.Empty, 12.5, wrap: true);
            var checkNow = Ui.Small(Ui.Button("Check now", "IconRefresh"));
            var downloadPage = Ui.Link("Download page", (s, e) => AppInfo.OpenUrl(AppInfo.ReleasesUrl + "/latest"));
            void ShowStatus()
            {
                var text = Updater.Status;
                if (text.Length == 0)
                    text = Settings.CheckForUpdates
                        ? (Updater.Installed ? "Updates itself: new versions are installed when you close " + AppInfo.Name + "." : "This copy wasn't installed, so it isn't updated by itself.")
                        : "Updating by itself is switched off in Settings.";
                status.Text = text;
                downloadPage.Visibility = Updater.Installed ? Visibility.Collapsed : Visibility.Visible;
            }
            checkNow.Click += async (s, e) =>
            {
                checkNow.IsEnabled = false;
                try { await Updater.CheckNow(); }
                finally { checkNow.IsEnabled = true; }
            };
            Action changed = () => Dispatcher.BeginInvoke(new Action(ShowStatus));
            Updater.StatusChanged += changed;
            Closed += (s, e) => Updater.StatusChanged -= changed;
            ShowStatus();
            var updateRow = Ui.Columns(Ui.Star(), Ui.Auto);
            updateRow.Children.Add(Ui.Column(status, downloadPage).At(0, 0));
            ((FrameworkElement)updateRow.Children[0]).VerticalAlignment = VerticalAlignment.Center;
            updateRow.Children.Add(checkNow.Margin(12, 0, 0, 0).At(0, 1));
            Row("Updates", updateRow);
            rows.Children.Add(Ui.Divider());
            list.Children.Add(rows);
            list.Children.Add(Ui.Subtle(AppInfo.Name + " has no account and collects nothing about you. Your tabs, history and site data stay in your AppData folder.", 11.5)
                .Margin(0, 10, 0, 6));

            // What it can do, each with a way to try it.
            AddGroup(list, "What makes it different", Different);
            AddGroup(list, "Best everyday features", Everyday);

            var close = Ui.Button("Close", null, "PrimaryButton");
            close.IsCancel = true;
            close.Click += (s, e) => Close();
            close.HorizontalAlignment = HorizontalAlignment.Right;
            var root = new Grid { Margin = new Thickness(22, 18, 22, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) }.At(0, 0));
            root.Children.Add(close.Margin(0, 12, 0, 0).At(1, 0));
            Content = root;
            Closed += (s, e) => _open = null;
        }

        /// <summary>What the updates line says (for the self-test).</summary>
        public static string UpdateText => Updater.Status;

        private static void OpenLocal(string name)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AppInfo.ExePath) ?? string.Empty, name);
            if (System.IO.File.Exists(path)) AppInfo.OpenFile(path);
            else AppInfo.OpenUrl(AppInfo.RepoUrl);
        }

        /// <summary>How many "Try it" buttons there are (for the self-test).</summary>
        public int TryButtons { get; private set; }

        private void AddGroup(StackPanel list, string title, IEnumerable<Feature> features)
        {
            list.Children.Add(Ui.Heading(title, 15).Margin(0, 16, 0, 6));
            foreach (var f in features)
            {
                var g = Ui.Columns(Ui.Px(32), Ui.Star(), Ui.Auto);
                g.Children.Add(Ui.Icon(f.Icon, 18).At(0, 0));
                ((FrameworkElement)g.Children[0]).VerticalAlignment = VerticalAlignment.Top;
                ((FrameworkElement)g.Children[0]).Margin = new Thickness(0, 2, 0, 0);
                var text = Ui.Column(Ui.Text(f.Name, 13.5, bold: true), Ui.Subtle(f.About, 12.5));
                if (f.Keys != null) text.Children.Add(Ui.Subtle(f.Keys, 11.5, false).Margin(0, 3, 0, 0).Res(TextBlock.ForegroundProperty, "Accent"));
                g.Children.Add(text.At(0, 1));
                // A private window has no rooms, and nothing to bring from Chrome.
                if (f.TryIt != null && _main != null && !(_main.IsPrivate && (f.Name == "Rooms" || f.Name == "Moving from Chrome")))
                {
                    var feature = f;
                    var tryIt = Ui.Small(Ui.Button("Try it"));
                    tryIt.VerticalAlignment = VerticalAlignment.Center;
                    System.Windows.Automation.AutomationProperties.SetName(tryIt, "Try " + f.Name);
                    tryIt.Click += (s, e) =>
                    {
                        Close();
                        _main.Activate();
                        feature.TryIt(_main);
                    };
                    g.Children.Add(tryIt.Margin(12, 0, 0, 0).At(0, 2));
                    TryButtons++;
                }
                list.Children.Add(new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 9, 0, 9), Child = g }
                    .Res(Border.BorderBrushProperty, "BorderBrush"));
            }
        }
    }
}
