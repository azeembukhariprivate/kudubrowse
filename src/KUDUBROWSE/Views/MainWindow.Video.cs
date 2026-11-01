using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kudubrowse.Controls;
using Kudubrowse.Core;

namespace Kudubrowse.Views
{
    /// <summary>
    /// A video in a small floating window that stays on top of other windows: the tab's page moves into the small window
    /// and only its video is shown there; closing the small window puts the page back in its tab.
    /// </summary>
    public sealed partial class MainWindow
    {
        private readonly Dictionary<BrowserTab, FloatingVideo> _floating = new Dictionary<BrowserTab, FloatingVideo>();
        private Border _floatingNote;

        // Marks the biggest video on the page (a playing one first) and shows only it, filling the page. Answers "none"
        // when there's no video.
        private const string FloatScript = @"(() => {
  const vids = [...document.querySelectorAll('video')].filter(v => v.offsetWidth * v.offsetHeight > 0 || v.readyState > 0 || v.srcObject);
  if (!vids.length) return 'none';
  vids.sort((a, b) => ((b.paused ? 0 : 1e9) + b.offsetWidth * b.offsetHeight) - ((a.paused ? 0 : 1e9) + a.offsetWidth * a.offsetHeight));
  const v = vids[0];
  v.setAttribute('data-kudu-float', '');
  if (!document.getElementById('kudu-float-style')) {
    const s = document.createElement('style');
    s.id = 'kudu-float-style';
    s.textContent = 'html, body { overflow: hidden !important; background: #000 !important; }' +
      'body * { visibility: hidden !important; }' +
      'video[data-kudu-float] { visibility: visible !important; position: fixed !important; inset: 0 !important; left: 0 !important; top: 0 !important;' +
      ' width: 100vw !important; height: 100vh !important; max-width: none !important; max-height: none !important; margin: 0 !important;' +
      ' transform: none !important; object-fit: contain !important; background: #000 !important; z-index: 2147483647 !important; }';
    (document.head || document.documentElement).appendChild(s);
  }
  return v.paused ? 'paused' : 'playing';
})()";

        private const string UnfloatScript = @"(() => {
  document.getElementById('kudu-float-style')?.remove();
  document.querySelectorAll('[data-kudu-float]').forEach(v => v.removeAttribute('data-kudu-float'));
  return 'ok';
})()";

        /// <summary>The page area shows the note about a floating video (for the self-test).</summary>
        public bool FloatingNoteShown => _floatingNote?.Visibility == Visibility.Visible;

        /// <summary>The tab's video is in a floating window.</summary>
        public bool IsFloating(BrowserTab tab) => tab != null && _floating.ContainsKey(tab);

        public FloatingVideo FloatingWindowOf(BrowserTab tab) => tab != null && _floating.TryGetValue(tab, out var f) ? f : null;

        /// <summary>
        /// Puts the tab's video in a floating window. Returns false (and shows a note) when the page has no video.
        /// </summary>
        public async Task<bool> FloatVideo(BrowserTab tab)
        {
            if (tab == null || IsFloating(tab)) return false;
            if (tab.View?.CoreWebView2 == null)
            {
                Dialogs.Info(this, "Open a page with a video first.");
                return false;
            }
            string answer;
            try { answer = await tab.View.CoreWebView2.ExecuteScriptAsync(FloatScript); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException) { answer = "\"none\""; }
            if (answer == "\"none\"" || answer == "null")
            {
                if (!SelfTest.Running) Dialogs.Info(this, "There's no video on this page.");
                return false;
            }

            _pages.Children.Remove(tab.View);
            var window = new FloatingVideo(tab);
            _floating[tab] = window;
            window.Closed += (s, e) => Unfloat(tab);
            window.Show();
            tab.View.Visibility = Visibility.Visible;
            Layout();
            UpdateFloatingNote();
            return true;
        }

        /// <summary>Puts the page back in its tab (when the floating window is closed).</summary>
        private void Unfloat(BrowserTab tab)
        {
            if (!_floating.TryGetValue(tab, out var window)) return;
            _floating.Remove(tab);
            window.Content = null;
            if (tab.View != null && _tabs.Contains(tab))
            {
                _pages.Children.Add(tab.View);
                _ = UndoFloatScript(tab);
            }
            Layout();
            UpdateFloatingNote();
        }

        private static async Task UndoFloatScript(BrowserTab tab)
        {
            try { if (tab.View?.CoreWebView2 != null) await tab.View.CoreWebView2.ExecuteScriptAsync(UnfloatScript); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException) { }
        }

        /// <summary>Closes the tab's floating window, if it has one (putting the page back first).</summary>
        public void BringBack(BrowserTab tab) => FloatingWindowOf(tab)?.Close();

        /// <summary>A note in the page area while the current tab's video is floating.</summary>
        private void UpdateFloatingNote()
        {
            if (_floatingNote == null)
            {
                var back = Ui.Button("Bring it back", "IconArrowLeft", "PrimaryButton", (s, e) => BringBack(Current));
                back.HorizontalAlignment = HorizontalAlignment.Center;
                var content = Ui.Column(
                    Ui.Text("The video is playing in a floating window", 18, bold: true),
                    Ui.Subtle("It stays on top of your other windows. Close it, or bring it back, to see this page here again.", 13, true).Margin(0, 6, 0, 16),
                    back);
                foreach (FrameworkElement c in content.Children) c.HorizontalAlignment = HorizontalAlignment.Center;
                content.VerticalAlignment = VerticalAlignment.Center;
                content.MaxWidth = 460;
                _floatingNote = new Border { Child = content }.Res(Border.BackgroundProperty, "WindowBg");
                Grid.SetColumnSpan(_floatingNote, 3);
                _pages.Children.Add(_floatingNote);
            }
            _floatingNote.Visibility = IsFloating(Current) ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Floating windows close with their tab or their browser window.</summary>
        private void CloseFloating(BrowserTab tab = null)
        {
            foreach (var t in _floating.Keys.Where(t => tab == null || t == tab).ToList()) _floating[t].Close();
        }
    }

    /// <summary>The small floating window that shows one tab's video and stays on top.</summary>
    public sealed class FloatingVideo : Window
    {
        public FloatingVideo(BrowserTab tab)
        {
            Tab = tab;
            Dialogs.Prepare(this, "Video · " + (string.IsNullOrEmpty(tab.Title) ? AppInfo.Name : tab.Title));
            WindowStyle = WindowStyle.ToolWindow;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Topmost = true;
            ShowInTaskbar = false;
            Width = 520;
            Height = 330;
            MinWidth = 240;
            MinHeight = 160;
            // Bottom right of the screen, out of the way.
            var screen = SystemParameters.WorkArea;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = screen.Right - Width - 24;
            Top = screen.Bottom - Height - 24;
            Background = System.Windows.Media.Brushes.Black;
            Content = tab.View;
            tab.Changed += OnTabChanged;
            Closed += (s, e) => tab.Changed -= OnTabChanged;
        }

        public BrowserTab Tab { get; }

        private void OnTabChanged(BrowserTab t)
        {
            if (!string.IsNullOrEmpty(t.Title)) Title = "Video · " + t.Title;
        }
    }
}
