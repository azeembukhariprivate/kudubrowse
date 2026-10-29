using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace Kudubrowse.Core.Library
{
    public sealed class ChromeProfile
    {
        public string Name { get; set; }
        public string Folder { get; set; }
        public bool HasBookmarks => File.Exists(Path.Combine(Folder, "Bookmarks"));
        public bool HasHistory => File.Exists(Path.Combine(Folder, "History"));
    }

    /// <summary>Reads Chrome's bookmarks (a JSON file) and history (an SQLite file) from its profile folders. Only reads.</summary>
    public static class ChromeImport
    {
        public static string UserDataFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data");

        /// <summary>Chrome's profiles on this PC ("Default", "Profile 1", ...), with the names shown in Chrome.</summary>
        public static List<ChromeProfile> Profiles()
        {
            var list = new List<ChromeProfile>();
            if (!Directory.Exists(UserDataFolder)) return list;
            string localState = null;
            try { localState = File.ReadAllText(Path.Combine(UserDataFolder, "Local State")); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            foreach (var dir in Directory.GetDirectories(UserDataFolder))
            {
                var name = Path.GetFileName(dir);
                if (name != "Default" && !name.StartsWith("Profile ")) continue;
                var p = new ChromeProfile { Folder = dir, Name = ProfileName(localState, name) ?? name };
                if (p.HasBookmarks || p.HasHistory) list.Add(p);
            }
            return list.OrderBy(p => p.Folder.EndsWith("Default") ? 0 : 1).ThenBy(p => p.Name).ToList();
        }

        private static string ProfileName(string localState, string dir)
        {
            if (localState == null) return null;
            var at = localState.IndexOf("\"" + dir + "\"", localState.IndexOf("\"info_cache\"", StringComparison.Ordinal) + 1, StringComparison.Ordinal);
            if (at < 0) return null;
            var m = new Regex("\"name\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"").Match(localState, at);
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }

        // ================================================================ bookmarks

        [DataContract]
        private sealed class ChromeNode
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "type")] public string Type { get; set; }
            [DataMember(Name = "url")] public string Url { get; set; }
            [DataMember(Name = "date_added")] public string DateAdded { get; set; }
            [DataMember(Name = "children")] public List<ChromeNode> Children { get; set; }
        }

        [DataContract]
        private sealed class ChromeRoots
        {
            [DataMember(Name = "bookmark_bar")] public ChromeNode Bar { get; set; }
            [DataMember(Name = "other")] public ChromeNode Other { get; set; }
            [DataMember(Name = "synced")] public ChromeNode Synced { get; set; }
        }

        [DataContract]
        private sealed class ChromeBookmarksFile
        {
            [DataMember(Name = "roots")] public ChromeRoots Roots { get; set; }
        }

        public sealed class ImportedBookmarks
        {
            public List<Bookmark> Bar { get; } = new List<Bookmark>();
            public List<Bookmark> Other { get; } = new List<Bookmark>();
            public int Count => Bar.Concat(Other).Sum(b => b.IsFolder ? b.AllLinks().Count() : 1);
        }

        public static ImportedBookmarks ReadBookmarks(string file)
        {
            ChromeBookmarksFile data;
            using (var s = OpenShared(file))
                data = (ChromeBookmarksFile)new DataContractJsonSerializer(typeof(ChromeBookmarksFile)).ReadObject(s);
            var result = new ImportedBookmarks();
            if (data?.Roots == null) return result;
            result.Bar.AddRange(Convert(data.Roots.Bar?.Children));
            result.Other.AddRange(Convert(data.Roots.Other?.Children));
            var synced = Convert(data.Roots.Synced?.Children);
            if (synced.Count > 0)
            {
                var folder = Bookmark.Folder("Mobile bookmarks");
                folder.Children.AddRange(synced);
                result.Other.Add(folder);
            }
            return result;
        }

        private static List<Bookmark> Convert(List<ChromeNode> nodes)
        {
            var list = new List<Bookmark>();
            if (nodes == null) return list;
            foreach (var n in nodes)
            {
                if (n.Type == "folder")
                {
                    var f = Bookmark.Folder(n.Name ?? "Folder");
                    f.Children.AddRange(Convert(n.Children));
                    list.Add(f);
                }
                else if (n.Type == "url" && Session.IsSafeToReopen(n.Url))
                {
                    list.Add(new Bookmark { Title = string.IsNullOrWhiteSpace(n.Name) ? n.Url : n.Name, Url = n.Url, Added = ChromeTime(n.DateAdded) ?? DateTime.UtcNow });
                }
            }
            return list;
        }

        // ================================================================ history

        /// <summary>Chrome's history (the "urls" table). Chrome keeps the file open while it runs, so it may need closing first.</summary>
        public static List<Visit> ReadHistory(string file)
        {
            byte[] data;
            using (var s = OpenShared(file))
            using (var m = new MemoryStream())
            {
                s.CopyTo(m);
                data = m.ToArray();
            }
            var list = new List<Visit>();
            foreach (var row in new SqliteReader(data).Rows("urls"))
            {
                var url = row.TryGetValue("url", out var u) ? u as string : null;
                if (url == null || !(url.StartsWith("http://") || url.StartsWith("https://"))) continue;
                if (row.TryGetValue("hidden", out var hidden) && hidden is long h && h != 0) continue;
                var visits = row.TryGetValue("visit_count", out var vc) && vc is long c ? (int)Math.Min(int.MaxValue, c) : 1;
                var when = row.TryGetValue("last_visit_time", out var lv) && lv is long t ? ChromeTime(t) : null;
                if (when == null) continue;
                list.Add(new Visit { Url = url, Title = row.TryGetValue("title", out var title) ? title as string ?? string.Empty : string.Empty, Visits = Math.Max(1, visits), LastVisit = when.Value });
            }
            return list;
        }

        /// <summary>Chrome's times are microseconds since 1 January 1601 (UTC).</summary>
        public static DateTime? ChromeTime(long micro)
        {
            if (micro <= 0) return null;
            try { return new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(micro * 10); }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        private static DateTime? ChromeTime(string micro) => long.TryParse(micro, out var v) ? ChromeTime(v) : null;

        /// <summary>Opens a file Chrome may have open, without getting in its way.</summary>
        private static Stream OpenShared(string file) => new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }
}
