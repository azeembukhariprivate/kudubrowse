// Saved tab sets ("Work", "Trip planning"): a window's tabs put away under a name, to open them all again later.
// lang.py: only Lang.T texts (the rest are the file's own words)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kudubrowse.Core
{
    /// <summary>A set of tabs kept under a name.</summary>
    public sealed class TabSet
    {
        /// <summary>Its name.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>When it was saved (UTC).</summary>
        public DateTime Saved { get; set; }
        /// <summary>Its tabs, in order (with their rooms, groups and muting).</summary>
        public List<SavedTab> Tabs { get; } = new List<SavedTab>();
    }

    /// <summary>
    /// The tab sets, kept in tabsets.txt next to the other files: a "set" line (the name and when it was saved, between
    /// tab characters) and then a line per tab, written as the session file writes them (Session.Line).
    /// </summary>
    public sealed class TabSets
    {
        /// <summary>The most tabs kept in one set.</summary>
        public const int MaxTabs = 500;
        /// <summary>The longest name.</summary>
        public const int MaxName = 60;

        /// <summary>Every set, newest first.</summary>
        private readonly List<TabSet> _sets = new List<TabSet>();

        /// <summary>Where they're kept.</summary>
        public string File { get; }

        /// <summary>Something was added, renamed or removed.</summary>
        public event Action Changed;

        /// <summary>Empty sets kept in this file.</summary>
        private TabSets(string file) => File = file;

        /// <summary>Every set, newest first.</summary>
        public IReadOnlyList<TabSet> All => _sets;

        /// <summary>Reads the sets (none when the file isn't there or can't be read).</summary>
        public static TabSets Load(string file)
        {
            var sets = new TabSets(file);
            try
            {
                if (!System.IO.File.Exists(file)) return sets;
                TabSet current = null;
                foreach (var line in System.IO.File.ReadAllLines(file, Encoding.UTF8))
                {
                    var parts = line.Split('\t');
                    if (parts[0] == "set" && parts.Length >= 2)
                    {
                        current = new TabSet { Name = CleanName(parts[1]) };
                        current.Saved = parts.Length >= 3 && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)
                                        && ticks > 0 && ticks <= DateTime.MaxValue.Ticks
                            ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.UtcNow;
                        if (current.Name.Length > 0) sets._sets.Add(current);
                        else current = null;
                        continue;
                    }
                    if (current == null || current.Tabs.Count >= MaxTabs) continue;
                    var tab = Session.Parse(parts);
                    if (tab != null) current.Tabs.Add(tab);
                }
                sets._sets.RemoveAll(s => s.Tabs.Count == 0);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Unreadable: none this time; the file is left as it is.
            }
            return sets;
        }

        /// <summary>Keeps the sets (to a new file first, then put in place).</summary>
        public void Save()
        {
            var sb = new StringBuilder();
            foreach (var set in _sets)
            {
                sb.Append("set\t").Append(CleanName(set.Name)).Append('\t').Append(set.Saved.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
                foreach (var t in set.Tabs)
                {
                    var line = Session.Line(t);
                    if (line != null) sb.Append(line).Append('\n');
                }
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File));
                var temp = File + ".new";
                System.IO.File.WriteAllText(temp, sb.ToString(), Encoding.UTF8);
                if (System.IO.File.Exists(File)) System.IO.File.Replace(temp, File, null);
                else System.IO.File.Move(temp, File);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Tried again at the next change.
            }
        }

        /// <summary>
        /// Keeps these tabs as a new set with this name (a number is added when the name is taken); null when none of the
        /// tabs has a page that can be opened again.
        /// </summary>
        public TabSet Add(string name, IEnumerable<SavedTab> tabs)
        {
            var set = new TabSet { Name = UniqueName(name), Saved = DateTime.UtcNow };
            set.Tabs.AddRange(tabs.Where(t => t != null && Session.IsSafeToReopen(t.Url)).Take(MaxTabs));
            if (set.Tabs.Count == 0) return null;
            _sets.Insert(0, set);
            Save();
            Changed?.Invoke();
            return set;
        }

        /// <summary>Renames a set (to a name not taken by another set).</summary>
        public void Rename(TabSet set, string name)
        {
            var clean = CleanName(name);
            if (clean.Length == 0 || !_sets.Contains(set) || clean == set.Name) return;
            set.Name = UniqueName(clean, set);
            Save();
            Changed?.Invoke();
        }

        /// <summary>Removes a set.</summary>
        public void Remove(TabSet set)
        {
            if (!_sets.Remove(set)) return;
            Save();
            Changed?.Invoke();
        }

        /// <summary>A name not used by another set yet: "Work", else "Work (2)", "Work (3)"...</summary>
        private string UniqueName(string name, TabSet except = null)
        {
            var clean = CleanName(name);
            if (clean.Length == 0) clean = "Tabs";
            bool Taken(string n) => _sets.Any(s => s != except && string.Equals(s.Name, n, StringComparison.CurrentCultureIgnoreCase));
            if (!Taken(clean)) return clean;
            for (var i = 2; ; i++)
            {
                var candidate = clean + " (" + i + ")";
                if (!Taken(candidate)) return candidate;
            }
        }

        /// <summary>A name without tabs or line breaks, trimmed, at most 60 characters.</summary>
        public static string CleanName(string name)
        {
            var clean = Session.Clean(name).Trim();
            return clean.Length > MaxName ? clean.Substring(0, MaxName).Trim() : clean;
        }
    }
}
