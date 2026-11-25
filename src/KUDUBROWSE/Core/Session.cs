// The tabs kept for next time (address, title, room, group, muted), and the recently closed tabs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kudubrowse.Core
{
    public sealed class SavedTab
    {
        /// <summary>The tab's address.</summary>
        public string Url { get; set; }
        /// <summary>Its title.</summary>
        public string Title { get; set; }
        /// <summary>The room the tab is in (null or "Default" for the first room).</summary>
        public string Room { get; set; }
        /// <summary>The tab group it's in (null for none), and the group's colour.</summary>
        public string Group { get; set; }
        /// <summary>Its group's colour.</summary>
        public string GroupColor { get; set; }
        /// <summary>The tab was muted.</summary>
        public bool Muted { get; set; }
    }

    /// <summary>
    /// The open tabs, kept in %LocalAppData%\KUDUBROWSE\session.txt so they come back after closing or a crash.
    /// One line per tab: the address, the title, the room, the tab group ("name|colour") and "muted", between tab characters.
    /// The first line says which tab was selected.
    /// </summary>
    public static class Session
    {
        /// <summary>The browser's data folder.</summary>
        public static string DataFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name);

        /// <summary>The session file (the self-test uses its own).</summary>
        public static string DefaultFile { get; set; } = Path.Combine(DataFolder, "session.txt");

        /// <summary>Keeps the tabs and which one was chosen.</summary>
        public static void Save(IEnumerable<SavedTab> tabs, int selected, string file = null)
        {
            file = file ?? DefaultFile;
            var sb = new StringBuilder();
            sb.Append("selected\t").Append(selected).Append('\n');
            foreach (var t in tabs)
            {
                var line = Line(t);
                if (line != null) sb.Append(line).Append('\n');
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

        /// <summary>Reads the tabs kept last time.</summary>
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
                    var tab = Parse(parts);
                    if (tab != null) tabs.Add(tab);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return (new List<SavedTab>(), 0);
            }
            return (tabs, tabs.Count == 0 ? 0 : Math.Max(0, Math.Min(selected, tabs.Count - 1)));
        }

        /// <summary>
        /// A tab as one line of the file: the address, the title, the room, the group ("name|colour") and "muted", between
        /// tab characters. Null for a tab with no page. Tab sets (TabSets.cs) keep their tabs the same way.
        /// </summary>
        public static string Line(SavedTab t)
        {
            if (t == null || string.IsNullOrEmpty(t.Url) || t.Url == "about:blank") return null;
            return Clean(t.Url) + "\t" + Clean(t.Title) + "\t" + (t.Room ?? Rooms.DefaultId)
                   + "\t" + (string.IsNullOrEmpty(t.Group) ? string.Empty : Clean(t.Group).Replace('|', ' ') + "|" + Clean(t.GroupColor))
                   + "\t" + (t.Muted ? "muted" : string.Empty);
        }

        /// <summary>A tab from a line's parts, or null when its address isn't one that may be opened again.</summary>
        public static SavedTab Parse(string[] parts)
        {
            if (parts.Length == 0 || !IsSafeToReopen(parts[0])) return null;
            var tab = new SavedTab
            {
                Url = parts[0],
                Title = parts.Length >= 2 ? parts[1] : string.Empty,
                Room = parts.Length >= 3 && Rooms.ValidId(parts[2]) ? parts[2] : Rooms.DefaultId
            };
            var group = parts.Length >= 4 ? parts[3].Split('|') : new string[0];
            if (group.Length == 2 && group[0].Trim().Length > 0)
            {
                tab.Group = group[0].Trim();
                tab.GroupColor = IsColor(group[1]) ? group[1] : Rooms.Colors[1];
            }
            tab.Muted = parts.Length >= 5 && parts[4] == "muted";
            return tab;
        }

        /// <summary>Only ordinary web pages and files come back; never script or data addresses.</summary>
        public static bool IsSafeToReopen(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https" || u.Scheme == "file");

        /// <summary>True for a colour like "#0E8C7E".</summary>
        private static bool IsColor(string s) => s.Length == 7 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);

        /// <summary>A text without tabs and line breaks.</summary>
        internal static string Clean(string s) => (s ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    /// <summary>Recently closed tabs, newest first, for "Reopen closed tab" (Ctrl+Shift+T).</summary>
    public sealed class ClosedTabs
    {
        /// <summary>The closed tabs, newest first.</summary>
        private readonly List<SavedTab> _list = new List<SavedTab>();
        /// <summary>How many are kept.</summary>
        public const int Max = 25;

        /// <summary>How many there are.</summary>
        public int Count => _list.Count;
        public IReadOnlyList<SavedTab> Items => _list;

        /// <summary>Adds a closed tab (the oldest go after 25).</summary>
        public void Add(SavedTab tab)
        {
            if (tab == null || !Session.IsSafeToReopen(tab.Url)) return;
            _list.Insert(0, tab);
            if (_list.Count > Max) _list.RemoveAt(_list.Count - 1);
        }

        /// <summary>Takes one closed tab off the list (it's being opened again).</summary>
        public bool Take(SavedTab tab) => _list.Remove(tab);

        /// <summary>Takes the last closed tab (null when none).</summary>
        public SavedTab TakeLatest()
        {
            if (_list.Count == 0) return null;
            var t = _list[0];
            _list.RemoveAt(0);
            return t;
        }
    }
}
