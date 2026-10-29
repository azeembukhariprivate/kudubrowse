using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kudubrowse.Core
{
    public sealed class SavedTab
    {
        public string Url { get; set; }
        public string Title { get; set; }
        /// <summary>The room the tab is in (null or "Default" for the first room).</summary>
        public string Room { get; set; }
    }

    /// <summary>
    /// The open tabs, kept in %LocalAppData%\KUDUBROWSE\session.txt so they come back after closing or a crash.
    /// One line per tab: the address, a tab character, then the title. The first line says which tab was selected.
    /// </summary>
    public static class Session
    {
        public static string DataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name);

        /// <summary>The session file (the self-test uses its own).</summary>
        public static string DefaultFile { get; set; } = Path.Combine(DataFolder, "session.txt");

        public static void Save(IEnumerable<SavedTab> tabs, int selected, string file = null)
        {
            file = file ?? DefaultFile;
            var sb = new StringBuilder();
            sb.Append("selected\t").Append(selected).Append('\n');
            foreach (var t in tabs)
            {
                if (string.IsNullOrEmpty(t.Url) || t.Url == "about:blank") continue;
                sb.Append(Clean(t.Url)).Append('\t').Append(Clean(t.Title)).Append('\t').Append(t.Room ?? Rooms.DefaultId).Append('\n');
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                // Write a new copy, then swap it in, so a crash halfway never leaves a broken file.
                var temp = file + ".new";
                File.WriteAllText(temp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(file)) File.Replace(temp, file, null);
                else File.Move(temp, file);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Best-effort: the tabs just won't come back this time.
            }
        }

        public static (List<SavedTab> Tabs, int Selected) Load(string file = null)
        {
            file = file ?? DefaultFile;
            var tabs = new List<SavedTab>();
            var selected = 0;
            try
            {
                if (!File.Exists(file)) return (tabs, 0);
                foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    var parts = line.Split('\t');
                    if (parts[0] == "selected")
                    {
                        if (parts.Length == 2) int.TryParse(parts[1], out selected);
                        continue;
                    }
                    if (!IsSafeToReopen(parts[0])) continue;
                    tabs.Add(new SavedTab
                    {
                        Url = parts[0],
                        Title = parts.Length >= 2 ? parts[1] : string.Empty,
                        Room = parts.Length >= 3 && Rooms.ValidId(parts[2]) ? parts[2] : Rooms.DefaultId
                    });
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return (new List<SavedTab>(), 0);
            }
            return (tabs, tabs.Count == 0 ? 0 : Math.Max(0, Math.Min(selected, tabs.Count - 1)));
        }

        /// <summary>Only ordinary web pages and files come back; never script or data addresses.</summary>
        public static bool IsSafeToReopen(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https" || u.Scheme == "file");

        private static string Clean(string s) => (s ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    /// <summary>Recently closed tabs, newest first, for "Reopen closed tab" (Ctrl+Shift+T).</summary>
    public sealed class ClosedTabs
    {
        private readonly List<SavedTab> _list = new List<SavedTab>();
        public const int Max = 25;

        public int Count => _list.Count;
        public IReadOnlyList<SavedTab> Items => _list;

        public void Add(SavedTab tab)
        {
            if (tab == null || !Session.IsSafeToReopen(tab.Url)) return;
            _list.Insert(0, tab);
            if (_list.Count > Max) _list.RemoveAt(_list.Count - 1);
        }

        public SavedTab TakeLatest()
        {
            if (_list.Count == 0) return null;
            var t = _list[0];
            _list.RemoveAt(0);
            return t;
        }
    }
}
