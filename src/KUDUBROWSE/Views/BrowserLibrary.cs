using System;
using System.IO;
using System.Windows.Threading;
using Kudubrowse.Core;
using Kudubrowse.Core.Library;

namespace Kudubrowse.Views
{
    /// <summary>The bookmarks and history, shared by every window, kept in %LocalAppData%\KUDUBROWSE.</summary>
    public static class BrowserLibrary
    {
        private static Bookmarks _bookmarks;
        private static History _history;
        private static DispatcherTimer _saveTimer;

        /// <summary>Where they're kept (the self-test uses its own folder).</summary>
        public static string Folder { get; set; } = Session.DataFolder;

        public static Bookmarks Bookmarks => _bookmarks ?? (_bookmarks = Bookmarks.Load(Path.Combine(Folder, "bookmarks.json")));

        public static History History
        {
            get
            {
                if (_history != null) return _history;
                _history = History.Load(Path.Combine(Folder, "history.txt"));
                // History is saved every half minute when something changed, and when the browser closes.
                _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                _saveTimer.Tick += (s, e) => _history.Save();
                _saveTimer.Start();
                return _history;
            }
        }

        public static void SaveAll() => _history?.Save();

        /// <summary>For the self-test: start again from the files in <see cref="Folder"/>.</summary>
        public static void Reload()
        {
            _saveTimer?.Stop();
            _bookmarks = null;
            _history = null;
        }
    }
}
