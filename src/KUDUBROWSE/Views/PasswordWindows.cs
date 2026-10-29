using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kudubrowse.Controls;
using Kudubrowse.Core;
using Kudubrowse.Core.Passwords;
using Microsoft.Win32;

namespace Kudubrowse.Views
{
    /// <summary>The saved passwords: search, show or copy (after Windows Hello), delete, check for weak, reused and leaked ones, bring them from Chrome.</summary>
    public sealed class PasswordsWindow : Window
    {
        private static PasswordsWindow _open;
        private readonly TextBox _search = new TextBox { MinHeight = 34, VerticalContentAlignment = VerticalAlignment.Center };
        private readonly StackPanel _rows = new StackPanel();
        private readonly TextBlock _summary = Ui.Subtle(string.Empty, 12.5);
        private readonly Dictionary<Login, int> _leaks = new Dictionary<Login, int>();
        private readonly Button _check;

        public static PasswordsWindow Show(Window owner)
        {
            if (_open == null || !_open.IsLoaded)
            {
                _open = new PasswordsWindow(owner);
                _open.Show();
            }
            _open.Activate();
            return _open;
        }

        public PasswordsWindow(Window owner)
        {
            Width = 780;
            Height = 620;
            Dialogs.Prepare(this, "Passwords · " + AppInfo.Name, owner);
            System.Windows.Automation.AutomationProperties.SetName(_search, "Search passwords");
            _search.TextChanged += (s, e) => Fill();
            _check = Ui.Small(Ui.Button("Check for leaks", "IconShield"));
            _check.ToolTip = "Checks each password against a public list of passwords from known data leaks.\nOnly the first 5 characters of a scrambled code of each password are sent.";
            _check.Click += async (s, e) => await CheckLeaks();
            var import = Ui.Small(Ui.Button("Bring passwords from Chrome...", "IconImport"));
            import.Click += (s, e) =>
            {
                new ImportPasswordsWindow(this).ShowDialog();
                Fill();
            };
            var top = Ui.Columns(Ui.Star(), Ui.Auto, Ui.Auto);
            top.Children.Add(_search.At(0, 0));
            top.Children.Add(_check.Margin(8, 0, 0, 0).At(0, 1));
            top.Children.Add(import.Margin(8, 0, 0, 0).At(0, 2));
            var root = new Grid { Margin = new Thickness(20, 16, 20, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(top.At(0, 0));
            root.Children.Add(_summary.Margin(2, 10, 0, 0).At(1, 0));
            root.Children.Add(new Border
            {
                Child = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(4)
            }.Res(Border.BorderBrushProperty, "BorderBrush").Res(Border.BackgroundProperty, "SurfaceBg").At(2, 0));
            Content = root;
            PasswordStore.Vault.Changed += Fill;
            Closed += (s, e) =>
            {
                PasswordStore.Vault.Changed -= Fill;
                _open = null;
            };
            Fill();
        }

        public int RowCount => _rows.Children.OfType<Grid>().Count();

        private void Fill()
        {
            _rows.Children.Clear();
            var vault = PasswordStore.Vault;
            var terms = _search.Text.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var reused = vault.Logins.GroupBy(l => l.Password).Where(g => g.Count() > 1).SelectMany(g => g).ToHashSet();
            var shown = vault.Logins.Where(l => terms.All(t => l.Site.ToLowerInvariant().Contains(t) || l.Username.ToLowerInvariant().Contains(t)))
                .OrderBy(l => l.Site).ThenBy(l => l.Username).ToList();
            foreach (var l in shown) _rows.Children.Add(Row(l, reused.Contains(l)));
            var weak = vault.Logins.Count(l => PasswordTools.IsWeak(l.Password));
            var leaked = _leaks.Count(x => x.Value > 0);
            var parts = new List<string> { vault.Logins.Count == 1 ? "1 saved password" : $"{vault.Logins.Count} saved passwords" };
            if (weak > 0) parts.Add($"{weak} easy to guess");
            if (reused.Count > 0) parts.Add($"{reused.Count} used on more than one site");
            if (_leaks.Count > 0) parts.Add(leaked == 0 ? "none found in known leaks" : $"{leaked} found in known leaks: change them");
            _summary.Text = vault.Logins.Count == 0 ? "No saved passwords yet. When you sign in to a site, " + AppInfo.Name + " offers to save the password." : string.Join(" · ", parts) + ".";
            if (vault.NeverSaveOrigins.Count > 0)
            {
                _rows.Children.Add(Ui.Heading("Never saved for", 13).Margin(8, 16, 0, 6));
                foreach (var origin in vault.NeverSaveOrigins.ToList())
                {
                    var g = Ui.Columns(Ui.Star(), Ui.Auto);
                    g.Children.Add(Ui.Text(Vault.HostOf(origin), 13).Margin(8, 0, 0, 0).At(0, 0));
                    var allow = Ui.Small(Ui.Button("Offer to save again"));
                    allow.Click += (s, e) => vault.AllowSavingFor(origin);
                    g.Children.Add(allow.At(0, 1));
                    _rows.Children.Add(new Border { Child = g, Padding = new Thickness(4) });
                }
            }
        }

        private Grid Row(Login l, bool reused)
        {
            var g = Ui.Columns(Ui.Px(28), Ui.Star(), Ui.Star(), Ui.Px(150), Ui.Auto);
            g.Margin = new Thickness(6, 4, 6, 4);
            g.Children.Add(Ui.Icon("IconLock", 15).At(0, 0));
            g.Children.Add(Ui.Column(Ui.Text(l.Site, 13), Ui.Subtle(Notes(l, reused), 11.5, false)).At(0, 1));
            g.Children.Add(Ui.Text(string.IsNullOrEmpty(l.Username) ? "(no name)" : l.Username, 13).At(0, 2));
            var pw = Ui.Mono("••••••••••", 13);
            g.Children.Add(pw.At(0, 3));
            var more = Ui.Button(null, "IconMore", "GhostButton");
            more.Width = more.MinWidth = 32;
            more.Padding = new Thickness(0);
            more.ToolTip = "Show, copy or delete";
            more.Click += (s, e) =>
            {
                var menu = new ContextMenu { PlacementTarget = more };
                void Add(string text, Action act)
                {
                    var i = new MenuItem { Header = text };
                    i.Click += (a, b) => act();
                    menu.Items.Add(i);
                }
                Add(pw.Text.StartsWith("•") ? "Show the password" : "Hide the password", async () =>
                {
                    if (!pw.Text.StartsWith("•")) pw.Text = "••••••••••";
                    else if (await Verify.Ask(this, "Show the password for " + l.Site)) pw.Text = l.Password;
                });
                Add("Copy the password", async () =>
                {
                    if (await Verify.Ask(this, "Copy the password for " + l.Site)) Ui.Copy(l.Password);
                });
                Add("Copy the name", () => Ui.Copy(l.Username));
                Add("Open the site", () => (Owner as MainWindow ?? MainWindow.Open.FirstOrDefault())?.OpenAddress(l.Origin, true));
                menu.Items.Add(new Separator());
                Add("Delete", () =>
                {
                    if (Dialogs.Confirm(this, $"Delete the saved password for {l.Username} on {l.Site}?")) PasswordStore.Vault.Remove(l);
                });
                menu.IsOpen = true;
            };
            g.Children.Add(more.At(0, 4));
            return g;
        }

        private string Notes(Login l, bool reused)
        {
            var notes = new List<string>();
            if (_leaks.TryGetValue(l, out var n) && n > 0) notes.Add($"found in known leaks ({n:N0} times)");
            if (PasswordTools.IsWeak(l.Password)) notes.Add("easy to guess");
            if (reused) notes.Add("also used on another site");
            return notes.Count == 0 ? "Saved " + l.Created.ToLocalTime().ToString("d MMM yyyy") : string.Join(" · ", notes);
        }

        /// <summary>
        /// For each password, asks the public Pwned Passwords service for every leaked password whose scrambled code
        /// (SHA-1) starts with the same 5 characters, and looks for it in the answer on this PC. The password itself, and
        /// the rest of its code, never leave the PC.
        /// </summary>
        private async Task CheckLeaks()
        {
            var logins = PasswordStore.Vault.Logins.ToList();
            if (logins.Count == 0) return;
            _check.IsEnabled = false;
            _summary.Text = "Checking...";
            var answers = new Dictionary<string, string>();
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.Name + "/" + AppInfo.VersionText);
                    // Padding makes every answer about the same size, so even its length says nothing.
                    http.DefaultRequestHeaders.Add("Add-Padding", "true");
                    _leaks.Clear();
                    foreach (var l in logins)
                    {
                        var hash = PasswordTools.Sha1Hex(l.Password);
                        var url = PasswordTools.LeakQueryUrl(hash);
                        if (!answers.TryGetValue(url, out var answer)) answers[url] = answer = await http.GetStringAsync(url);
                        _leaks[l] = PasswordTools.LeakCount(hash, answer);
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                _leaks.Clear();
                Dialogs.Error(this, "Couldn't check right now. Check your internet connection and try again.");
            }
            _check.IsEnabled = true;
            Fill();
        }
    }

    /// <summary>Brings passwords over from the file Chrome exports, then deletes that file (it holds every password as plain text).</summary>
    public sealed class ImportPasswordsWindow : Window
    {
        private readonly TextBlock _result = Ui.Subtle(string.Empty, 12.5);

        public ImportPasswordsWindow(Window owner)
        {
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Dialogs.Prepare(this, "Bring passwords from Chrome · " + AppInfo.Name, owner);
            var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
            root.Children.Add(Ui.Heading("Bring passwords from Chrome", 18));
            root.Children.Add(Ui.Subtle("Chrome keeps its passwords locked to itself, so they come across in a file Chrome makes for you:", 12.5).Margin(0, 8, 0, 10));
            var steps = new[]
            {
                "1. In Chrome, open the menu ⋮ › Passwords and autofill › Google Password Manager › Settings.",
                "2. Click \"Download file\" next to \"Export passwords\", and save the file.",
                "3. Come back here and click \"Choose the file\"."
            };
            foreach (var s in steps) root.Children.Add(Ui.Text(s, 13, wrap: true).Margin(0, 0, 0, 6));
            var chrome = ChromeExe();
            if (chrome != null)
            {
                var openChrome = Ui.Small(Ui.Button("Open Chrome's password settings", "IconExternal"));
                openChrome.HorizontalAlignment = HorizontalAlignment.Left;
                openChrome.Click += (s, e) =>
                {
                    try { System.Diagnostics.Process.Start(chrome, "chrome://password-manager/settings"); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) { }
                };
                root.Children.Add(openChrome.Margin(0, 4, 0, 0));
            }
            root.Children.Add(Ui.Subtle("Edge, Firefox, Bitwarden and other password managers make similar files; they can be brought in the same way.", 12).Margin(0, 12, 0, 0));
            root.Children.Add(_result.Margin(0, 12, 0, 0));
            var close = Ui.Button("Close");
            close.IsCancel = true;
            close.Click += (s, e) => Close();
            var choose = Ui.Button("Choose the file", "IconFolder", "PrimaryButton");
            choose.IsDefault = true;
            choose.Click += (s, e) =>
            {
                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                var dlg = new OpenFileDialog { Title = "The passwords file", Filter = "Passwords file (*.csv)|*.csv", InitialDirectory = Directory.Exists(downloads) ? downloads : null };
                var guess = PasswordTools.FindExport(downloads);
                if (guess != null) dlg.FileName = Path.GetFileName(guess);
                if (dlg.ShowDialog(this) != true) return;
                Import(dlg.FileName, Dialogs.Confirm(this, "Delete the file once its passwords are in " + AppInfo.Name + "?\n\nIt holds every password as plain text, so it shouldn't stay on the PC."));
            };
            var buttons = Ui.Row(close, choose.Margin(8, 0, 0, 0));
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            root.Children.Add(buttons.Margin(0, 16, 0, 0));
            Content = root;
        }

        /// <summary>Reads the file, adds its passwords, and deletes it if asked. Returns the message shown.</summary>
        public string Import(string file, bool deleteAfter)
        {
            try
            {
                var rows = PasswordTools.ReadCsv(File.ReadAllText(file));
                var n = PasswordStore.Vault.Merge(rows);
                var msg = n == 1 ? "1 password brought over." : $"{n} passwords brought over.";
                if (rows.Count > n) msg += $" {rows.Count - n} were already saved, or aren't for websites.";
                if (deleteAfter)
                {
                    try
                    {
                        File.Delete(file);
                        msg += " The file was deleted.";
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        msg += " The file couldn't be deleted: please delete it yourself.";
                    }
                }
                _result.Text = msg;
                return msg;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                _result.Text = "Couldn't read that file: " + ex.Message;
                return _result.Text;
            }
        }

        private static string ChromeExe()
        {
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using (var k = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe"))
                        if (k?.GetValue(string.Empty) is string p && File.Exists(p)) return p;
                }
                catch (System.Security.SecurityException) { }
            }
            return null;
        }
    }
}
