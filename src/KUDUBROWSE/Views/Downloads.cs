using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kudubrowse.Controls;
using Kudubrowse.Core;
using Microsoft.Web.WebView2.Core;

namespace Kudubrowse.Views
{
    /// <summary>
    /// One download: fetched by the engine (small files, and sites that don't allow parts), or by the browser itself
    /// (<see cref="FastDownloader"/>: big files in pieces, retries, carrying on after a restart).
    /// </summary>
    public sealed class DownloadItem
    {
        private CancellationTokenSource _cancel;
        private bool _cancelling;
        private DateTime _lastTell = DateTime.MinValue, _lastSave = DateTime.UtcNow;
        private DateTime _speedAt = DateTime.UtcNow;
        private long _speedFrom;

        public DownloadItem(DownloadRecord record, CoreWebView2DownloadOperation operation = null)
        {
            Record = record;
            Operation = operation;
            if (operation != null) Watch(operation);
        }

        public DownloadRecord Record { get; }
        /// <summary>The engine's download, while it's the engine fetching it (this session only).</summary>
        public CoreWebView2DownloadOperation Operation { get; private set; }
        /// <summary>Started or touched since the browser opened (shows the toolbar button).</summary>
        public bool ThisSession { get; set; } = true;

        public string Path => Record.File;
        public string FileName => Record.FileName;
        public long Received => Record.Received;
        public long Total => Record.Total;
        public DownloadState State => Record.State;
        public bool Done => State == DownloadState.Done;
        public bool Failed => State == DownloadState.Failed;
        public bool Running => State == DownloadState.Running;
        public bool Paused => State == DownloadState.Paused;
        /// <summary>Bytes a second, lately.</summary>
        public double Speed { get; private set; }

        public bool CanPause => Running;
        public bool CanCarryOn => Paused || Failed;

        // ---------------------------------------------------------------- the engine's downloads

        private void Watch(CoreWebView2DownloadOperation op)
        {
            op.BytesReceivedChanged += (s, a) =>
            {
                Record.Received = op.BytesReceived;
                if (op.TotalBytesToReceive.HasValue) Record.Total = (long)op.TotalBytesToReceive.Value;
                Tell();
            };
            op.StateChanged += (s, a) =>
            {
                Record.File = op.ResultFilePath;
                Record.Received = op.BytesReceived;
                switch (op.State)
                {
                    case CoreWebView2DownloadState.Completed:
                        Record.State = DownloadState.Done;
                        if (Record.Total < 0) Record.Total = Record.Received;
                        break;
                    case CoreWebView2DownloadState.InProgress:
                        Record.State = DownloadState.Running;
                        break;
                    default:
                        if (_cancelling || op.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled)
                            Record.State = DownloadState.Cancelled;
                        else if (op.InterruptReason == CoreWebView2DownloadInterruptReason.UserPaused) Record.State = DownloadState.Paused;
                        else
                        {
                            Record.State = DownloadState.Failed;
                            Record.Error = Reason(op.InterruptReason);
                        }
                        break;
                }
                Tell(true);
            };
        }

        private static string Reason(CoreWebView2DownloadInterruptReason r)
        {
            switch (r)
            {
                case CoreWebView2DownloadInterruptReason.NetworkFailed:
                case CoreWebView2DownloadInterruptReason.NetworkTimeout:
                case CoreWebView2DownloadInterruptReason.NetworkDisconnected:
                    return "The connection dropped";
                case CoreWebView2DownloadInterruptReason.FileNoSpace:
                    return "The disk is full";
                case CoreWebView2DownloadInterruptReason.FileAccessDenied:
                    return "The folder can't be written to";
                case CoreWebView2DownloadInterruptReason.FileBlockedByPolicy:
                case CoreWebView2DownloadInterruptReason.FileSecurityCheckFailed:
                case CoreWebView2DownloadInterruptReason.FileMalicious:
                    return "Windows' virus scanner blocked it";
                case CoreWebView2DownloadInterruptReason.ServerUnauthorized:
                case CoreWebView2DownloadInterruptReason.ServerForbidden:
                    return "The site didn't allow it (you may need to sign in again)";
                case CoreWebView2DownloadInterruptReason.ServerBadContent:
                    return "The file isn't on the site any more";
                case CoreWebView2DownloadInterruptReason.UserShutdown:
                    return "Stopped when the browser was closed";
                default:
                    return "It stopped part way";
            }
        }

        // ---------------------------------------------------------------- pause, carry on, cancel

        public void Pause()
        {
            if (!CanPause) return;
            if (Operation != null && !Record.Own)
            {
                Operation.Pause();
                Record.State = DownloadState.Paused;
                Tell(true);
            }
            else _cancel?.Cancel();
        }

        /// <summary>Carries on after a pause, or tries again after a failure (from where it stopped when possible).</summary>
        public void CarryOn()
        {
            if (!CanCarryOn) return;
            ThisSession = true;
            if (Operation != null && !Record.Own && Operation.CanResume)
            {
                Operation.Resume();
                Record.State = DownloadState.Running;
                Record.Error = null;
                Tell(true);
                return;
            }
            // The engine can't carry on: the browser fetches it itself (from the start, or where it stopped if it was its own).
            Operation = null;
            if (!Record.Own) Record.Pieces = null;
            RunOwn();
        }

        public void Cancel()
        {
            if (!(Running || Paused)) return;
            _cancelling = true;
            if (Operation != null && !Record.Own) Operation.Cancel();
            else if (Running) _cancel?.Cancel();
            else Cancelled();
        }

        private void Cancelled()
        {
            Record.State = DownloadState.Cancelled;
            Record.Pieces = null;
            try { File.Delete(Record.PartFile); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            Tell(true);
        }

        // ---------------------------------------------------------------- the browser's own downloader

        internal async void RunOwn()
        {
            _cancel = new CancellationTokenSource();
            _cancelling = false;
            Record.State = DownloadState.Running;
            Record.Error = null;
            Speed = 0;
            _speedFrom = Record.Received;
            _speedAt = DateTime.UtcNow;
            Tell(true);
            var d = Downloads.Downloader(Record);
            d.Progress += () => Tell();
            try
            {
                var token = _cancel.Token;
                await Task.Run(() => d.Run(token));
                // Like the engine's own downloads: the "came from the internet" mark and the virus scanner.
                if (!await AttachmentServices.Check(Record.File, Record.Url, Record.Referrer))
                {
                    Record.State = DownloadState.Failed;
                    Record.Error = "Windows' virus scanner removed it";
                }
            }
            catch (OperationCanceledException)
            {
                if (_cancelling) Cancelled();
                else Record.State = DownloadState.Paused;
            }
            catch (DownloadException ex)
            {
                Record.State = DownloadState.Failed;
                Record.Error = ex.Message;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Record.State = DownloadState.Failed;
                Record.Error = ex is UnauthorizedAccessException ? "The folder can't be written to" : "Couldn't write the file: " + ex.Message;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is InvalidOperationException || ex is UriFormatException)
            {
                Record.State = DownloadState.Failed;
                Record.Error = "Couldn't reach the site";
            }
            Speed = 0;
            Tell(true);
        }

        /// <summary>Lets the list know (at most 4 times a second, unless the state changed) and keeps the list file current.</summary>
        private void Tell(bool now = false)
        {
            var at = DateTime.UtcNow;
            if (!now && (at - _lastTell).TotalMilliseconds < 250) return;
            _lastTell = at;
            var seconds = (at - _speedAt).TotalSeconds;
            if (seconds >= 1)
            {
                var latest = Math.Max(0, Record.Received - _speedFrom) / seconds;
                Speed = Speed <= 0 ? latest : Speed * 0.6 + latest * 0.4;
                _speedFrom = Record.Received;
                _speedAt = at;
            }
            if (now || (at - _lastSave).TotalSeconds > 5)
            {
                _lastSave = at;
                Downloads.SaveSoon();
            }
            Downloads.Tell();
        }
    }

    /// <summary>
    /// Downloads, shown in the browser's own list (Ctrl+J) instead of the engine's. Files go to the downloads folder
    /// (sorted by type when that's switched on) or where the person chooses; big files are fetched in pieces at the same
    /// time when the site allows it. The list is kept in downloads.json for next time.
    /// </summary>
    public static class Downloads
    {
        private static List<DownloadItem> _items;
        private static readonly HttpClient Http = FastDownloader.CreateClient();
        private static DispatcherTimer _saveTimer;
        private static string _userAgent;

        public static List<DownloadItem> Items
        {
            get
            {
                if (_items == null)
                    _items = DownloadList.Load(ListFile).Select(r => new DownloadItem(r) { ThisSession = false }).ToList();
                return _items;
            }
        }

        public static event Action Changed;

        /// <summary>For the self-test: where files go, and its own list file.</summary>
        public static string Folder { get; set; }
        public static string ListFile { get; set; } = Path.Combine(Session.DataFolder, "downloads.json");

        public static void Reload() => _items = null;

        /// <summary>The folder chosen in Settings, or Windows' Downloads folder.</summary>
        public static string DefaultFolder
        {
            get
            {
                if (Folder != null) return Folder;
                var chosen = Settings.DownloadFolder;
                if (!string.IsNullOrEmpty(chosen) && Directory.Exists(chosen)) return chosen;
                try
                {
                    if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var path) == 0 && !string.IsNullOrEmpty(path))
                        return path;
                }
                catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException) { }
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }

        public static async void Start(BrowserTab tab, CoreWebView2 core, CoreWebView2DownloadStartingEventArgs e)
        {
            e.Handled = true; // our list instead of the engine's
            var deferral = e.GetDeferral();
            try
            {
                if (_userAgent == null) _userAgent = core.Settings.UserAgent;
                var op = e.DownloadOperation;
                var name = DownloadRules.SafeName(System.IO.Path.GetFileName(e.ResultFilePath));
                string path;
                if (Settings.AskWhereToSave && Folder == null)
                {
                    var owner = Window.GetWindow(tab.View) ?? MainWindow.LastActive;
                    var folder = DownloadRules.FolderFor(DefaultFolder, name, Settings.SortDownloads);
                    var ext = System.IO.Path.GetExtension(name);
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Save the download",
                        FileName = name,
                        InitialDirectory = Directory.Exists(folder) ? folder : DefaultFolder,
                        Filter = (ext.Length > 0 ? ext.TrimStart('.').ToUpperInvariant() + " file (*" + ext + ")|*" + ext + "|" : string.Empty) + "All files (*.*)|*.*"
                    };
                    if (dlg.ShowDialog(owner) != true)
                    {
                        e.Cancel = true;
                        return;
                    }
                    path = dlg.FileName;
                }
                else
                {
                    var folder = DownloadRules.FolderFor(DefaultFolder, name, Settings.SortDownloads);
                    Directory.CreateDirectory(folder);
                    path = DownloadRules.Unique(System.IO.Path.Combine(folder, name));
                }
                var record = new DownloadRecord
                {
                    Url = op.Uri,
                    File = path,
                    Referrer = tab.Url,
                    Room = tab.Room,
                    Private = tab.IsPrivate,
                    Total = op.TotalBytesToReceive.HasValue ? (long)op.TotalBytesToReceive.Value : -1
                };

                // Big files: fetched in pieces by the browser itself, when the site allows parts and it's the same file.
                if (Settings.FastDownloads && record.Total >= DownloadRules.FastFrom &&
                    Uri.TryCreate(op.Uri, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
                {
                    DownloadCheck check = null;
                    try
                    {
                        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                            check = await Downloader(record).Check(timeout.Token);
                    }
                    catch (Exception ex) when (ex is OperationCanceledException || ex is HttpRequestException || ex is DownloadException || ex is IOException ||
                                               ex is InvalidOperationException)
                    {
                    }
                    if (check != null && check.InParts && check.Total == record.Total)
                    {
                        e.Cancel = true;
                        var own = new DownloadItem(record);
                        Add(own);
                        own.RunOwn();
                        return;
                    }
                }
                e.ResultFilePath = path;
                Add(new DownloadItem(record, op));
            }
            finally
            {
                deferral.Complete();
            }
        }

        private static void Add(DownloadItem item)
        {
            Items.Insert(0, item);
            SaveSoon();
            Tell();
        }

        /// <summary>The browser's own downloader for a download, with the cookies of its room for each address it goes to.</summary>
        internal static FastDownloader Downloader(DownloadRecord record)
        {
            var dispatcher = Application.Current.Dispatcher;
            return new FastDownloader(record, Http)
            {
                UserAgent = _userAgent,
                Cookies = uri => dispatcher.InvokeAsync(() => CookiesFor(record, uri)).Task.Unwrap()
            };
        }

        /// <summary>The cookies a page in the download's room would send to that address (from an open tab in the room, or a hidden page).</summary>
        private static async Task<string> CookiesFor(DownloadRecord record, Uri uri)
        {
            try
            {
                var room = record.Room ?? Rooms.DefaultId;
                var core = MainWindow.Open.SelectMany(w => w.Tabs)
                    .FirstOrDefault(t => t.Room == room && t.IsPrivate == record.Private && t.View?.CoreWebView2 != null)?.View.CoreWebView2;
                if (core == null && !record.Private) core = await BrowserRooms.Hidden(room);
                if (core == null) return null;
                if (_userAgent == null) _userAgent = core.Settings.UserAgent;
                var cookies = await core.CookieManager.GetCookiesAsync(uri.AbsoluteUri);
                return string.Join("; ", cookies.Select(c => c.Name + "=" + c.Value));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is COMException || ex is ArgumentException)
            {
                return null;
            }
        }

        public static void Tell()
        {
            var d = Application.Current?.Dispatcher;
            if (d == null) return;
            if (d.CheckAccess()) Changed?.Invoke();
            else d.BeginInvoke(new Action(() => Changed?.Invoke()));
        }

        /// <summary>Writes the list within a second (several changes close together are written once).</summary>
        public static void SaveSoon()
        {
            var d = Application.Current?.Dispatcher;
            if (d == null) return;
            d.BeginInvoke(new Action(() =>
            {
                if (_saveTimer == null)
                {
                    _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _saveTimer.Tick += (s, e) =>
                    {
                        _saveTimer.Stop();
                        SaveNow();
                    };
                }
                _saveTimer.Stop();
                _saveTimer.Start();
            }));
        }

        public static void SaveNow()
        {
            if (_items != null) DownloadList.Save(ListFile, _items.Select(i => i.Record).ToList());
        }

        /// <summary>When the last window closes: downloads the browser fetches itself are kept, ready to carry on next time.</summary>
        public static void Closing()
        {
            _saveTimer?.Stop();
            SaveNow();
        }

        /// <summary>Removes finished, failed and cancelled downloads from the list (the files stay).</summary>
        public static void Clear()
        {
            Items.RemoveAll(i => !(i.Running || i.Paused));
            SaveNow();
            Tell();
        }

        /// <summary>The toolbar's short summary: a percentage while files arrive, or null when nothing is downloading.</summary>
        public static string Progress()
        {
            var active = Items.Where(i => i.Running).ToList();
            if (active.Count == 0) return null;
            var total = active.Sum(i => Math.Max(0, i.Total));
            return total > 0 && active.All(i => i.Total > 0) ? (active.Sum(i => i.Received) * 100 / total) + "%" : "…";
        }

        public static bool ShowButton => Items.Any(i => i.ThisSession || i.Running || i.Paused);

        public static void Open(Window owner, DownloadItem item)
        {
            if (!item.Done || !File.Exists(item.Path)) return;
            if (DownloadRules.IsRisky(item.FileName) &&
                !Dialogs.Confirm(owner, $"\"{item.FileName}\" can run a program or change Windows.\n\nOnly open it if you trust the site it came from. Open it?"))
                return;
            AppInfo.OpenFile(item.Path);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out string path);
    }

    public sealed class DownloadsWindow : Window
    {
        private static DownloadsWindow _open;
        private readonly StackPanel _list = new StackPanel();
        private readonly DispatcherTimer _refresh;
        private bool _pending;

        public static DownloadsWindow Show(Window owner)
        {
            if (_open == null || !_open.IsLoaded)
            {
                _open = new DownloadsWindow(owner);
                _open.Show();
            }
            _open.Activate();
            return _open;
        }

        public DownloadsWindow(Window owner)
        {
            Width = 680;
            Height = 540;
            Dialogs.Prepare(this, "Downloads · " + AppInfo.Name, owner);
            var clear = Ui.Small(Ui.Button("Clear the list"));
            clear.ToolTip = "Removes finished downloads from the list. The files stay where they are.";
            clear.Click += (s, e) => Downloads.Clear();
            var folder = Ui.Small(Ui.Button("Open the downloads folder", "IconFolder"));
            folder.Click += (s, e) =>
            {
                Directory.CreateDirectory(Downloads.DefaultFolder);
                AppInfo.OpenFolder(Downloads.DefaultFolder, null);
            };
            var settings = Ui.Link("Download settings", (s, e) => new SettingsWindow(this).ShowDialog());
            var bottom = Ui.Columns(Ui.Auto, Ui.Star(), Ui.Auto, Ui.Auto);
            bottom.Children.Add(settings.At(0, 0));
            settings.VerticalAlignment = VerticalAlignment.Center;
            bottom.Children.Add(folder.At(0, 2));
            bottom.Children.Add(clear.Margin(8, 0, 0, 0).At(0, 3));
            var root = new Grid { Margin = new Thickness(20, 16, 20, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new Border
            {
                Child = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6)
            }.Res(Border.BorderBrushProperty, "BorderBrush").Res(Border.BackgroundProperty, "SurfaceBg").At(0, 0));
            root.Children.Add(bottom.Margin(0, 12, 0, 0).At(1, 0));
            Content = root;
            // Redrawn at most twice a second while files arrive.
            _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _refresh.Tick += (s, e) =>
            {
                _refresh.Stop();
                if (_pending) Fill();
            };
            Downloads.Changed += OnChanged;
            Closed += (s, e) =>
            {
                Downloads.Changed -= OnChanged;
                _refresh.Stop();
                _open = null;
            };
            Fill();
        }

        public int ItemCount => _list.Children.OfType<Grid>().Count();

        private void OnChanged()
        {
            _pending = true;
            if (!_refresh.IsEnabled) _refresh.Start();
        }

        private void Fill()
        {
            _pending = false;
            _list.Children.Clear();
            if (Downloads.Items.Count == 0)
            {
                _list.Children.Add(Ui.Subtle("Files you download appear here.", 13).Margin(8, 8, 0, 0));
                return;
            }
            foreach (var item in Downloads.Items)
            {
                var g = Ui.Columns(Ui.Px(30), Ui.Star(), Ui.Auto);
                g.Margin = new Thickness(4, 6, 4, 6);
                var icon = Ui.Icon(item.Done ? "IconFile" : item.Failed ? "IconWarn" : item.Paused ? "IconPause" : item.State == DownloadState.Cancelled ? "IconClose" : "IconImport", 16);
                icon.VerticalAlignment = VerticalAlignment.Top;
                icon.Margin = new Thickness(0, 2, 0, 0);
                g.Children.Add(icon.At(0, 0));
                var text = Ui.Column(Ui.Text(item.FileName, 13), Ui.Subtle(Status(item), 11.5, false));
                if (item.Running || item.Paused)
                {
                    var bar = new ProgressBar { Height = 4, Maximum = 1000, Margin = new Thickness(0, 5, 0, 1), IsIndeterminate = item.Total <= 0 && item.Running };
                    if (item.Total > 0) bar.Value = item.Received * 1000.0 / item.Total;
                    text.Children.Add(bar);
                }
                g.Children.Add(text.At(0, 1));
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                void Add(Button b, string tip, Action act)
                {
                    b.ToolTip = tip;
                    b.Click += (s, e) => act();
                    if (buttons.Children.Count > 0) b.Margin = new Thickness(6, 0, 0, 0);
                    buttons.Children.Add(b);
                }
                if (item.Done && File.Exists(item.Path))
                {
                    Add(Ui.Small(Ui.Button("Open")), "Open the file", () => Downloads.Open(this, item));
                    Add(Ui.Small(Ui.Button(null, "IconFolder")), "Show in folder", () => AppInfo.OpenFolder(System.IO.Path.GetDirectoryName(item.Path), item.Path));
                }
                if (item.CanPause) Add(Ui.Small(Ui.Button("Pause", "IconPause")), "Pause; carry on later, even after closing the browser", item.Pause);
                if (item.Paused) Add(Ui.Small(Ui.Button("Carry on", "IconPlay")), "Carry on from where it stopped", item.CarryOn);
                if (item.Failed) Add(Ui.Small(Ui.Button("Try again", "IconRefresh")), "Try again, from where it stopped when the site allows it", item.CarryOn);
                if (item.Running || item.Paused) Add(Ui.Small(Ui.Button(null, "IconClose")), "Cancel", item.Cancel);
                g.Children.Add(buttons.At(0, 2));
                _list.Children.Add(g);
            }
        }

        private static string Status(DownloadItem item)
        {
            var size = item.Total > 0 ? $"{DownloadRules.Size(item.Received)} of {DownloadRules.Size(item.Total)}" : DownloadRules.Size(item.Received);
            switch (item.State)
            {
                case DownloadState.Running:
                    var parts = new List<string> { size };
                    if (item.Speed > 0) parts.Add(DownloadRules.Size((long)item.Speed) + "/s");
                    var left = item.Total > 0 ? DownloadRules.TimeLeft(item.Total - item.Received, item.Speed) : string.Empty;
                    if (left.Length > 0) parts.Add(left);
                    if (item.Record.InPieces) parts.Add(item.Record.Pieces.Count + " pieces at once");
                    return string.Join(" · ", parts);
                case DownloadState.Paused:
                    return "Paused · " + size;
                case DownloadState.Done:
                    var when = item.Record.Started.ToLocalTime();
                    return DownloadRules.Size(item.Total > 0 ? item.Total : item.Received) + " · " + (when.Date == DateTime.Today ? when.ToString("t") : when.ToString("d MMM, t")) +
                           (File.Exists(item.Path) ? string.Empty : " · moved or deleted") +
                           (DownloadRules.IsRisky(item.FileName) ? " · can run programs" : string.Empty);
                case DownloadState.Cancelled:
                    return "Cancelled";
                default:
                    return "Didn't finish: " + (item.Record.Error ?? "it stopped part way");
            }
        }
    }
}
