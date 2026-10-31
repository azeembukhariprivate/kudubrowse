using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Kudubrowse.Controls;
using Kudubrowse.Core;
using Kudubrowse.Core.Blocking;

namespace Kudubrowse.Views
{
    public sealed class SettingsWindow : Window
    {
        public SettingsWindow(Window owner)
        {
            Width = 600;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Dialogs.Prepare(this, "Settings · " + AppInfo.Name, owner);

            var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
            root.Children.Add(Ui.Heading("Settings", 20).Margin(0, 0, 0, 14));

            root.Children.Add(Ui.Heading("Appearance", 14).Margin(0, 0, 0, 8));
            var themes = new WrapPanel();
            foreach (var mode in new[] { ThemeMode.System, ThemeMode.Light, ThemeMode.Dark })
            {
                var chip = new RadioButton { Content = mode == ThemeMode.System ? "Same as Windows" : mode.ToString(), GroupName = "theme", IsChecked = Settings.Theme == mode }
                    .Styled("ChipRadio");
                chip.Checked += (s, e) =>
                {
                    Settings.Theme = mode;
                    ThemeManager.Apply(mode);
                };
                themes.Children.Add(chip);
            }
            root.Children.Add(themes);

            root.Children.Add(Ui.Heading("Search", 14).Margin(0, 16, 0, 4));
            root.Children.Add(Ui.Subtle("Words typed in the address bar are searched for with:", 12.5).Margin(0, 0, 0, 8));
            var engines = new WrapPanel();
            foreach (var engine in SearchEngines.All)
            {
                var chip = new RadioButton { Content = engine.Name, GroupName = "engine", IsChecked = SearchEngines.Find(Settings.SearchEngine) == engine }.Styled("ChipRadio");
                chip.Checked += (s, e) => Settings.SearchEngine = engine.Id;
                engines.Children.Add(chip);
            }
            root.Children.Add(engines);

            root.Children.Add(Ui.Heading("Ads and trackers", 14).Margin(0, 16, 0, 4));
            CheckBox Option(string text, bool value, Action<bool> save)
            {
                var box = new CheckBox { Content = text, IsChecked = value };
                box.Click += (s, e) =>
                {
                    save(box.IsChecked == true);
                    Blocker.SettingsChanged();
                };
                root.Children.Add(box);
                return box;
            }
            Option("Block ads and trackers", Settings.BlockAds, v => Settings.BlockAds = v);
            Option("Hide cookie banners", Settings.HideCookieBanners, v => Settings.HideCookieBanners = v).Margin = new Thickness(0, 6, 0, 0);
            Option("Block pop-ups that pages open by themselves", Settings.BlockPopups, v => Settings.BlockPopups = v).Margin = new Thickness(0, 6, 0, 0);
            var listInfo = Ui.Subtle(string.Empty, 12);
            void UpdateListInfo()
            {
                var fetched = BlockLists.LastFetched;
                listInfo.Text = $"Uses the public EasyList, EasyPrivacy and EasyList Cookie List ({Blocker.RuleCount:N0} rules), fetched again every few days. " +
                                (fetched == null ? "Using the copy that came with " + AppInfo.Name + "." : "Last fetched " + fetched.Value.ToLocalTime().ToString("d MMM yyyy") + ".");
            }
            UpdateListInfo();
            root.Children.Add(listInfo.Margin(0, 8, 0, 0));
            var update = Ui.Small(Ui.Button("Update the lists now", "IconRefresh", "SecondaryButton"));
            update.HorizontalAlignment = HorizontalAlignment.Left;
            update.Margin = new Thickness(0, 8, 0, 0);
            update.Click += async (s, e) =>
            {
                update.IsEnabled = false;
                var n = await Blocker.UpdateNow();
                update.IsEnabled = true;
                UpdateListInfo();
                if (n == 0) Dialogs.Info(this, "Couldn't fetch the lists right now. The copy already here is still used.");
            };
            root.Children.Add(update);

            var allowed = Settings.AllowedSites;
            if (allowed.Count > 0)
            {
                root.Children.Add(Ui.Text("Allowed sites (nothing blocked there):", 12.5).Margin(0, 12, 0, 4));
                var sites = new WrapPanel();
                foreach (var site in allowed)
                {
                    var chip = Ui.Small(Ui.Button(site, "IconClose", "SecondaryButton"));
                    chip.ToolTip = "Block on " + site + " again";
                    chip.Margin = new Thickness(0, 0, 6, 6);
                    chip.Click += (s, e) =>
                    {
                        Blocker.SetAllowed(site, false);
                        sites.Children.Remove(chip);
                    };
                    sites.Children.Add(chip);
                }
                root.Children.Add(sites);
            }

            root.Children.Add(Ui.Heading("Passwords", 14).Margin(0, 16, 0, 4));
            var offer = new CheckBox { Content = "Offer to save passwords when you sign in to sites", IsChecked = Settings.OfferToSavePasswords };
            offer.Click += (s, e) => Settings.OfferToSavePasswords = offer.IsChecked == true;
            root.Children.Add(offer);
            root.Children.Add(Ui.Subtle("Saved passwords are locked to your Windows account. Showing or copying one asks for Windows Hello or your Windows password.", 12).Margin(27, 0, 0, 0));
            var managePasswords = Ui.Small(Ui.Button("Manage passwords", "IconLock", "SecondaryButton"));
            managePasswords.HorizontalAlignment = HorizontalAlignment.Left;
            managePasswords.Margin = new Thickness(0, 8, 0, 0);
            managePasswords.Click += (s, e) => PasswordsWindow.Show(Owner);
            root.Children.Add(managePasswords);

            root.Children.Add(Ui.Heading("Downloads", 14).Margin(0, 16, 0, 4));
            var folderText = Ui.Text(Downloads.DefaultFolder, 12.5, wrap: true);
            var changeFolder = Ui.Small(Ui.Button("Change...", "IconFolder", "SecondaryButton"));
            changeFolder.Click += (s, e) =>
            {
                var picked = Dialogs.PickFolder(this, "Where should downloads go?", Downloads.DefaultFolder);
                if (picked == null) return;
                Settings.DownloadFolder = picked;
                folderText.Text = Downloads.DefaultFolder;
            };
            var folderRow = Ui.Columns(Ui.Auto, Ui.Star(), Ui.Auto);
            folderRow.Children.Add(Ui.Subtle("Save to", 12.5, false).Margin(0, 0, 10, 0).At(0, 0));
            folderRow.Children.Add(folderText.At(0, 1));
            folderRow.Children.Add(changeFolder.Margin(10, 0, 0, 0).At(0, 2));
            foreach (FrameworkElement c in folderRow.Children) c.VerticalAlignment = VerticalAlignment.Center;
            root.Children.Add(folderRow);
            var askWhere = new CheckBox { Content = "Ask where to save each file", IsChecked = Settings.AskWhereToSave, Margin = new Thickness(0, 8, 0, 0) };
            askWhere.Click += (s, e) => Settings.AskWhereToSave = askWhere.IsChecked == true;
            root.Children.Add(askWhere);
            var sortDownloads = new CheckBox { Content = "Put files in folders by type", IsChecked = Settings.SortDownloads, Margin = new Thickness(0, 8, 0, 0) };
            sortDownloads.Click += (s, e) => Settings.SortDownloads = sortDownloads.IsChecked == true;
            root.Children.Add(sortDownloads);
            root.Children.Add(Ui.Subtle("Pictures, Music, Videos, Documents, Archives and Programs each get a folder inside the downloads folder.", 12).Margin(27, 0, 0, 0));
            var fast = new CheckBox { Content = "Download big files faster", IsChecked = Settings.FastDownloads, Margin = new Thickness(0, 8, 0, 0) };
            fast.Click += (s, e) => Settings.FastDownloads = fast.IsChecked == true;
            root.Children.Add(fast);
            root.Children.Add(Ui.Subtle("Files over 20 MB are fetched in up to six pieces at the same time, when the site allows it. They can also be paused and carried on later, even after closing the browser.", 12).Margin(27, 0, 0, 0));

            root.Children.Add(Ui.Heading("Default browser", 14).Margin(0, 16, 0, 4));
            root.Children.Add(Ui.Subtle(ShellIntegration.IsDefault
                ? AppInfo.Name + " is your default browser: links you click in other apps open here."
                : "Make " + AppInfo.Name + " the browser that opens links from other apps. Windows asks you to choose it in its own settings.", 12.5));
            if (!ShellIntegration.IsDefault)
            {
                var makeDefault = Ui.Small(Ui.Button("Make it my default browser", "IconGlobe", "SecondaryButton"));
                makeDefault.HorizontalAlignment = HorizontalAlignment.Left;
                makeDefault.Margin = new Thickness(0, 8, 0, 0);
                makeDefault.Click += (s, e) => ShellIntegration.OpenDefaultAppsSettings();
                root.Children.Add(makeDefault);
            }

            root.Children.Add(Ui.Heading("Starting", 14).Margin(0, 16, 0, 4));
            var restore = new CheckBox { Content = "Open the tabs from last time", IsChecked = Settings.RestoreTabs };
            restore.Click += (s, e) => Settings.RestoreTabs = restore.IsChecked == true;
            root.Children.Add(restore);
            root.Children.Add(Ui.Subtle("They load when you first click them, so starting stays quick.", 12).Margin(27, 0, 0, 0));

            var sleep = new CheckBox { Content = "Put tabs to sleep when they haven't been looked at for 10 minutes", IsChecked = Settings.SleepTabs, Margin = new Thickness(0, 8, 0, 0) };
            sleep.Click += (s, e) => Settings.SleepTabs = sleep.IsChecked == true;
            root.Children.Add(sleep);
            root.Children.Add(Ui.Subtle("Sleeping tabs use almost no processor and less memory; they wake up as soon as you click them. Tabs playing sound stay awake.", 12).Margin(27, 0, 0, 0));

            root.Children.Add(Ui.Heading("Rooms", 14).Margin(0, 16, 0, 4));
            root.Children.Add(Ui.Subtle("Each room keeps its own logins and site data, so you can be signed in to two accounts of the same site, or keep work and home apart. A coloured line on a tab shows its room.", 12.5));
            var roomList = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            void FillRooms()
            {
                roomList.Children.Clear();
                foreach (var r in BrowserRooms.All.All.ToList())
                {
                    var room = r;
                    var g = Ui.Columns(Ui.Px(18), Ui.Star(), Ui.Auto, Ui.Auto);
                    g.Margin = new Thickness(0, 2, 0, 2);
                    g.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(r.Color)) }.At(0, 0));
                    g.Children.Add(Ui.Text(r.Name + (r.IsDefault ? "  (the usual room)" : string.Empty), 13).At(0, 1));
                    var rename = Ui.Small(Ui.Button("Rename"));
                    rename.Click += (s, e) =>
                    {
                        var name = Dialogs.Ask(this, "Rename room", "Name", room.Name);
                        if (name != null)
                        {
                            BrowserRooms.All.Rename(room, name);
                            FillRooms();
                        }
                    };
                    g.Children.Add(rename.At(0, 2));
                    if (!r.IsDefault)
                    {
                        var remove = Ui.Small(Ui.Button("Remove"));
                        remove.Margin = new Thickness(6, 0, 0, 0);
                        remove.Click += async (s, e) =>
                        {
                            if (MainWindow.Open.SelectMany(w => w.Tabs).Any(t => t.Room == room.Id))
                            {
                                Dialogs.Info(this, "Close the tabs in \"" + room.Name + "\" first.");
                                return;
                            }
                            if (!Dialogs.Confirm(this, "Remove the room \"" + room.Name + "\"? Its logins to sites and site data are deleted.")) return;
                            await BrowserRooms.ClearData(this, room.Id);
                            BrowserRooms.All.Remove(room);
                            FillRooms();
                        };
                        g.Children.Add(remove.At(0, 3));
                    }
                    roomList.Children.Add(g);
                }
            }
            FillRooms();
            root.Children.Add(roomList);
            var addRoom = Ui.Small(Ui.Button("Add a room", "IconPlus"));
            addRoom.HorizontalAlignment = HorizontalAlignment.Left;
            addRoom.Margin = new Thickness(0, 6, 0, 0);
            addRoom.Click += (s, e) =>
            {
                var name = Dialogs.Ask(this, "New room", "Name of the room", string.Empty);
                if (name == null) return;
                BrowserRooms.All.Add(name);
                FillRooms();
            };
            root.Children.Add(addRoom);

            root.Children.Add(Ui.Heading("Clear browsing data", 14).Margin(0, 16, 0, 4));
            root.Children.Add(Ui.Subtle("Deletes the history, and every room's cookies, logins to sites and saved site data. Bookmarks and saved passwords are kept.", 12.5));
            var clearAll = Ui.Small(Ui.Button("Clear browsing data", "IconTrash"));
            clearAll.HorizontalAlignment = HorizontalAlignment.Left;
            clearAll.Margin = new Thickness(0, 8, 0, 0);
            clearAll.Click += async (s, e) =>
            {
                if (!Dialogs.Confirm(this, "Clear the history and all site data? You'll be signed out of every site.")) return;
                clearAll.IsEnabled = false;
                BrowserLibrary.History.Clear();
                BrowserLibrary.History.Save();
                foreach (var r in BrowserRooms.All.All) await BrowserRooms.ClearData(this, r.Id);
                clearAll.IsEnabled = true;
                Dialogs.Info(this, "Browsing data cleared.");
            };
            root.Children.Add(clearAll);

            root.Children.Add(Ui.Heading("Updates", 14).Margin(0, 16, 0, 4));
            var updates = new CheckBox { Content = "Update " + AppInfo.Name + " by itself", IsChecked = Settings.CheckForUpdates };
            updates.Click += (s, e) => Settings.CheckForUpdates = updates.IsChecked == true;
            root.Children.Add(updates);
            root.Children.Add(Ui.Subtle("Once a day it asks GitHub for a new version. A new one is downloaded quietly, checked, and installed when you close " +
                AppInfo.Name + "; your tabs are there when you open it again. The engine that shows web pages is kept up to date by Windows.", 12)
                .Margin(27, 0, 0, 0));

            root.Children.Add(Ui.Divider().Margin(0, 18, 0, 0));
            var bottom = Ui.Columns(Ui.Star(), Ui.Auto);
            bottom.Children.Add(Ui.Link("About " + AppInfo.Name, (s, e) => About.Show(this)).At(0, 0));
            var closeButton = Ui.Button("Close", null, "PrimaryButton");
            closeButton.IsCancel = true;
            closeButton.Click += (s, e) => Close();
            bottom.Children.Add(closeButton.At(0, 1));
            root.Children.Add(bottom.Margin(0, 14, 0, 0));
            // A long list of settings: it scrolls when the screen isn't tall enough.
            MaxHeight = SystemParameters.WorkArea.Height - 40;
            Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
    }

    /// <summary>Opens "About KUDUBROWSE" (one window with everything about the browser).</summary>
    public static class About
    {
        public static Window Build(Window owner) => new AboutWindow(owner as MainWindow ?? MainWindow.LastActive, owner);

        public static void Show(Window owner) => AboutWindow.Show(owner as MainWindow ?? MainWindow.LastActive, owner);
    }

    /// <summary>Shown the first time: bring things over from Chrome, choose a search engine, make it the default browser.</summary>
    public sealed class WelcomeWindow : Window
    {
        public static void ShowOnce(Window owner)
        {
            Settings.WelcomeShown = true;
            new WelcomeWindow(owner).ShowDialog();
        }

        public WelcomeWindow(Window owner)
        {
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Dialogs.Prepare(this, "Welcome · " + AppInfo.Name, owner);
            var root = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
            var header = Ui.Row(LogoTile.Create(52), Ui.Column(Ui.Heading("Welcome to " + AppInfo.Name, 20), Ui.Subtle("Three quick things to get started. All of them can be done later from the menu.", 12.5)).Margin(14, 0, 0, 0));
            ((FrameworkElement)header.Children[1]).VerticalAlignment = VerticalAlignment.Center;
            root.Children.Add(header);

            Border Card(string title, string text, UIElement action) =>
                Ui.Card(Ui.Column(Ui.Heading(title, 14), Ui.Subtle(text, 12.5).Margin(0, 4, 0, 10), action), 16).Margin(0, 14, 0, 0) as Border;

            var bring = Ui.Small(Ui.Button("Bring bookmarks and history from Chrome", "IconImport", "PrimaryButton"));
            bring.HorizontalAlignment = HorizontalAlignment.Left;
            bring.Click += (s, e) => new ImportWindow(this).ShowDialog();
            root.Children.Add(Card("1. Moving from Chrome?", "Your bookmarks and the pages you visited come across. Chrome's files are only read.", bring));

            var engines = new WrapPanel();
            foreach (var engine in SearchEngines.All)
            {
                var chip = new RadioButton { Content = engine.Name, GroupName = "welcome-engine", IsChecked = SearchEngines.Find(Settings.SearchEngine) == engine }.Styled("ChipRadio");
                chip.Checked += (s, e) => Settings.SearchEngine = engine.Id;
                engines.Children.Add(chip);
            }
            root.Children.Add(Card("2. Search with", "Words you type in the address bar are searched for with:", engines));

            var makeDefault = Ui.Small(Ui.Button("Make it my default browser", "IconGlobe", "SecondaryButton"));
            makeDefault.HorizontalAlignment = HorizontalAlignment.Left;
            makeDefault.Click += (s, e) => ShellIntegration.OpenDefaultAppsSettings();
            root.Children.Add(Card("3. Open links here", "Windows asks you to pick " + AppInfo.Name + " in its Default apps settings; this opens them.", makeDefault));

            var done = Ui.Button("Start browsing", null, "PrimaryButton");
            done.IsDefault = true;
            done.IsCancel = true;
            done.Click += (s, e) => Close();
            done.HorizontalAlignment = HorizontalAlignment.Right;
            root.Children.Add(done.Margin(0, 18, 0, 0));
            Content = root;
        }
    }
}
