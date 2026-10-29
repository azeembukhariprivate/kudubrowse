using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kudubrowse.Controls;
using Kudubrowse.Core;
using Microsoft.Web.WebView2.Core;

namespace Kudubrowse.Views
{
    /// <summary>One file being downloaded (or done).</summary>
    public sealed class DownloadItem
    {
        public DownloadItem(CoreWebView2DownloadOperation op)
        {
            Operation = op;
            Path = op.ResultFilePath;
            Started = DateTime.Now;
        }

        public CoreWebView2DownloadOperation Operation { get; }
        public string Path { get; private set; }
        public string FileName => System.IO.Path.GetFileName(Path);
        public DateTime Started { get; }
        public long Received => Operation.BytesReceived;
        public long Total => (long)(Operation.TotalBytesToReceive ?? 0);
        public CoreWebView2DownloadState State => Operation.State;
        public bool Done => State == CoreWebView2DownloadState.Completed;
        public bool Failed => State == CoreWebView2DownloadState.Interrupted;

        internal void Refresh() => Path = Operation.ResultFilePath;
    }

    /// <summary>
    /// Downloads, shown in the browser's own list (Ctrl+J) instead of the engine's. Files go to the Downloads folder;
    /// Windows' virus scanner checks them as they arrive, and files that can run programs ask before opening.
    /// </summary>
    public static class Downloads
    {
        public static readonly List<DownloadItem> Items = new List<DownloadItem>();
        public static event Action Changed;

        /// <summary>For the self-test: where files go (otherwise the engine's usual place, the Downloads folder).</summary>
        public static string Folder { get; set; }

        public static void Start(CoreWebView2DownloadStartingEventArgs e)
        {
            e.Handled = true; // our list instead of the engine's
            if (Folder != null)
            {
                Directory.CreateDirectory(Folder);
                e.ResultFilePath = Path.Combine(Folder, Path.GetFileName(e.ResultFilePath));
            }
            var item = new DownloadItem(e.DownloadOperation);
            Items.Insert(0, item);
            var op = e.DownloadOperation;
            var lastTell = DateTime.MinValue;
            op.BytesReceivedChanged += (s, a) =>
            {
                if ((DateTime.UtcNow - lastTell).TotalMilliseconds < 250) return;
                lastTell = DateTime.UtcNow;
                Changed?.Invoke();
            };
            op.StateChanged += (s, a) =>
            {
                item.Refresh();
                Changed?.Invoke();
            };
            Changed?.Invoke();
        }

        /// <summary>The toolbar's short summary: a percentage while files arrive, or null when nothing is downloading.</summary>
        public static string Progress()
        {
            var active = Items.Where(i => i.State == CoreWebView2DownloadState.InProgress).ToList();
            if (active.Count == 0) return null;
            var total = active.Sum(i => i.Total);
            return total > 0 ? (active.Sum(i => i.Received) * 100 / total) + "%" : "…";
        }

        public static void Open(Window owner, DownloadItem item)
        {
            if (!item.Done || !File.Exists(item.Path)) return;
            if (DownloadRules.IsRisky(item.FileName) &&
                !Dialogs.Confirm(owner, $"\"{item.FileName}\" can run a program or change Windows.\n\nOnly open it if you trust the site it came from. Open it?"))
                return;
            AppInfo.OpenFile(item.Path);
        }
    }

    public sealed class DownloadsWindow : Window
    {
        private static DownloadsWindow _open;
        private readonly StackPanel _list = new StackPanel();

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
            Width = 620;
            Height = 520;
            Dialogs.Prepare(this, "Downloads · " + AppInfo.Name, owner);
            var clear = Ui.Small(Ui.Button("Clear the list"));
            clear.HorizontalAlignment = HorizontalAlignment.Right;
            clear.Click += (s, e) =>
            {
                Downloads.Items.RemoveAll(i => i.State != CoreWebView2DownloadState.InProgress);
                Fill();
            };
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
            root.Children.Add(clear.Margin(0, 12, 0, 0).At(1, 0));
            Content = root;
            Downloads.Changed += OnChanged;
            Closed += (s, e) =>
            {
                Downloads.Changed -= OnChanged;
                _open = null;
            };
            Fill();
        }

        public int ItemCount => _list.Children.OfType<Grid>().Count();

        private void OnChanged() => Dispatcher.BeginInvoke(new Action(Fill));

        private void Fill()
        {
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
                g.Children.Add(Ui.Icon(item.Done ? "IconFile" : item.Failed ? "IconWarn" : "IconImport", 16).At(0, 0));
                string status;
                if (item.Done) status = DownloadRules.Size(item.Received) + " · " + item.Started.ToString("t") + (DownloadRules.IsRisky(item.FileName) ? " · can run programs" : string.Empty);
                else if (item.Failed) status = "Didn't finish (" + item.Operation.InterruptReason + ")";
                else status = item.Total > 0 ? $"{DownloadRules.Size(item.Received)} of {DownloadRules.Size(item.Total)}" : DownloadRules.Size(item.Received);
                g.Children.Add(Ui.Column(Ui.Text(item.FileName, 13), Ui.Subtle(status, 11.5, false)).At(0, 1));
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                if (item.Done)
                {
                    var open = Ui.Small(Ui.Button("Open"));
                    open.Click += (s, e) => Downloads.Open(this, item);
                    var folder = Ui.Small(Ui.Button(null, "IconFolder"));
                    folder.ToolTip = "Show in folder";
                    folder.Click += (s, e) => AppInfo.OpenFolder(System.IO.Path.GetDirectoryName(item.Path), item.Path);
                    buttons.Children.Add(open);
                    buttons.Children.Add(folder.Margin(6, 0, 0, 0));
                }
                else if (!item.Failed)
                {
                    var cancel = Ui.Small(Ui.Button("Cancel"));
                    cancel.Click += (s, e) => item.Operation.Cancel();
                    buttons.Children.Add(cancel);
                }
                g.Children.Add(buttons.At(0, 2));
                _list.Children.Add(g);
            }
        }
    }
}
