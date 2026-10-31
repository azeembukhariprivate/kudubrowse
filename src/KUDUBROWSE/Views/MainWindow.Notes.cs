using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Kudubrowse.Controls;
using Kudubrowse.Core;

namespace Kudubrowse.Views
{
    /// <summary>Short notes under a toolbar button: a download has started, the browser was updated.</summary>
    public sealed partial class MainWindow
    {
        private Popup _note;
        private DispatcherTimer _noteTimer;

        /// <summary>A note is showing, and what it says (for the self-test).</summary>
        public bool NoteShown => _note?.IsOpen == true;
        public string NoteText { get; private set; }

        /// <summary>"Downloading report.pdf": under the downloads button, which flashes; clicking it opens the downloads list.</summary>
        public void ShowDownloadNote(DownloadItem item)
        {
            _downloads.Visibility = Visibility.Visible;
            ShowNote(_downloads, "IconImport", item.FileName, "Downloading… Click to see your downloads.", () => DownloadsWindow.Show(this));
            var flash = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(220)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) };
            _downloads.BeginAnimation(OpacityProperty, flash);
        }

        /// <summary>"Updated to 1.2.0", once, the first time the new version starts; clicking it opens About.</summary>
        public void ShowUpdatedNote(string version) =>
            ShowNote(_menuButton, "IconCheck", AppInfo.Name + " was updated to " + version, "Click to see what it can do.", () => About.Show(this), 8);

        private void ShowNote(FrameworkElement anchor, string icon, string title, string text, Action click, double seconds = 4)
        {
            CloseNote();
            NoteText = title + " · " + text;
            var body = Ui.Row(Ui.Icon(icon, 18), Ui.Column(Ui.Text(title, 13, bold: true), Ui.Subtle(text, 12, false)).Margin(10, 0, 0, 0));
            ((FrameworkElement)body.Children[0]).VerticalAlignment = VerticalAlignment.Top;
            var border = new Border
            {
                Child = body,
                MaxWidth = 380,
                Padding = new Thickness(14, 10, 16, 10),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 4, 8, 8),
                Cursor = Cursors.Hand,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.25 }
            }.Res(Border.BackgroundProperty, "SurfaceBg").Res(Border.BorderBrushProperty, "BorderBrush");
            System.Windows.Automation.AutomationProperties.SetName(border, NoteText);
            border.MouseLeftButtonUp += (s, e) =>
            {
                CloseNote();
                click();
            };
            _note = new Popup
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
                StaysOpen = true,
                AllowsTransparency = true,
                Focusable = false,
                Child = border
            };
            _note.IsOpen = true;
            // It goes by itself after a few seconds (longer while the mouse is on it), or when the window moves or isn't in front.
            _noteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            _noteTimer.Tick += (s, e) =>
            {
                if (border.IsMouseOver) return;
                CloseNote();
            };
            _noteTimer.Start();
        }

        private void CloseNote()
        {
            _noteTimer?.Stop();
            if (_note != null) _note.IsOpen = false;
            _note = null;
        }

        private void WatchNotes()
        {
            LocationChanged += (s, e) => CloseNote();
            Deactivated += (s, e) => CloseNote();
            StateChanged += (s, e) => CloseNote();
            Closed += (s, e) => CloseNote();
        }
    }
}
