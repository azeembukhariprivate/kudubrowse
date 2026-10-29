using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Kudubrowse.Controls;
using Kudubrowse.Core;

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

            root.Children.Add(Ui.Heading("Starting", 14).Margin(0, 16, 0, 4));
            var restore = new CheckBox { Content = "Open the tabs from last time", IsChecked = Settings.RestoreTabs };
            restore.Click += (s, e) => Settings.RestoreTabs = restore.IsChecked == true;
            root.Children.Add(restore);
            root.Children.Add(Ui.Subtle("They load when you first click them, so starting stays quick.", 12).Margin(27, 0, 0, 0));

            root.Children.Add(Ui.Heading("Updates", 14).Margin(0, 16, 0, 4));
            var updates = new CheckBox { Content = "Check for a new " + AppInfo.Name + " version when it starts", IsChecked = Settings.CheckForUpdates };
            updates.Click += (s, e) => Settings.CheckForUpdates = updates.IsChecked == true;
            root.Children.Add(updates);
            root.Children.Add(Ui.Subtle("Only asks GitHub for the latest version number. The engine that shows web pages is kept up to date by Windows.", 12)
                .Margin(27, 0, 0, 0));

            root.Children.Add(Ui.Divider().Margin(0, 18, 0, 0));
            var bottom = Ui.Columns(Ui.Star(), Ui.Auto);
            bottom.Children.Add(Ui.Link("About " + AppInfo.Name, (s, e) => About.Show(this)).At(0, 0));
            var closeButton = Ui.Button("Close", null, "PrimaryButton");
            closeButton.IsCancel = true;
            closeButton.Click += (s, e) => Close();
            bottom.Children.Add(closeButton.At(0, 1));
            root.Children.Add(bottom.Margin(0, 14, 0, 0));
            Content = root;
        }
    }

    public static class About
    {
        public static Window Build(Window owner)
        {
            var header = Ui.Row(LogoTile.Create(64), Ui.Column(Ui.Text(AppInfo.Name, 20, bold: true), Ui.Subtle("Version " + AppInfo.VersionText + " · A private everyday web browser", 12.5))
                .Margin(16, 0, 0, 0));
            ((FrameworkElement)header.Children[1]).VerticalAlignment = VerticalAlignment.Center;

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
            rows.Children.Add(Ui.Divider());

            var content = Ui.Column(header, rows,
                Ui.Subtle(AppInfo.Name + " has no account and collects nothing about you. Your tabs, history and site data stay in your AppData folder.", 11.5)
                    .Margin(0, 14, 0, 0));

            Window window = null;
            var updates = Ui.Button("Check for updates");
            var close = Ui.Button("Close", null, "PrimaryButton");
            close.IsCancel = true;
            close.IsDefault = true;
            window = Dialogs.Dialog(owner, "About " + AppInfo.Name, content, 580, updates, close);
            close.Click += (s, e) => window.Close();
            updates.Click += async (s, e) =>
            {
                updates.IsEnabled = false;
                try
                {
                    var info = await UpdateChecker.CheckAsync();
                    if (info == null) Dialogs.Info(window, "Couldn't find any published version yet.");
                    else if (info.IsNewer)
                    {
                        if (Dialogs.Confirm(window, $"{AppInfo.Name} {info.Latest.ToString(3)} is available. Open the download page?")) AppInfo.OpenUrl(info.Url);
                    }
                    else Dialogs.Info(window, "You have the latest version.");
                }
                catch
                {
                    Dialogs.Error(window, "Couldn't check for updates. Check your internet connection and try again.");
                }
                finally
                {
                    updates.IsEnabled = true;
                }
            };
            return window;
        }

        public static void Show(Window owner) => Build(owner).ShowDialog();

        private static void OpenLocal(string name)
        {
            var path = Path.Combine(Path.GetDirectoryName(AppInfo.ExePath) ?? string.Empty, name);
            if (File.Exists(path)) AppInfo.OpenFile(path);
            else AppInfo.OpenUrl(AppInfo.RepoUrl);
        }
    }
}
