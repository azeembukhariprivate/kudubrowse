using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Kudubrowse.Core.Blocking
{
    /// <summary>A public block list and where to get it (the official address first, then a copy).</summary>
    public sealed class BlockList
    {
        public BlockList(string file, string name, params string[] urls)
        {
            File = file;
            Name = name;
            Urls = urls;
        }

        public string File { get; }
        public string Name { get; }
        public string[] Urls { get; }
    }

    /// <summary>
    /// The lists: a copy comes with the app (downloaded when it's built), and newer copies are fetched every few days
    /// into %LocalAppData%\KUDUBROWSE\Lists. Only the lists are downloaded; nothing is sent about you.
    /// </summary>
    public static class BlockLists
    {
        public static readonly BlockList EasyList = new BlockList("easylist.txt", "EasyList (ads)",
            "https://easylist.to/easylist/easylist.txt", "https://raw.githubusercontent.com/easylist/easylist/gh-pages/easylist.txt");
        public static readonly BlockList EasyPrivacy = new BlockList("easyprivacy.txt", "EasyPrivacy (trackers)",
            "https://easylist.to/easylist/easyprivacy.txt", "https://raw.githubusercontent.com/easylist/easylist/gh-pages/easyprivacy.txt");
        public static readonly BlockList Cookies = new BlockList("easylist-cookie.txt", "EasyList Cookie List (cookie banners)",
            "https://secure.fanboy.co.nz/fanboy-cookiemonster.txt", "https://easylist-downloads.adblockplus.org/easylist-cookie.txt");

        public static IReadOnlyList<BlockList> All => new[] { EasyList, EasyPrivacy, Cookies };

        /// <summary>Lists say "Expires: 4 days"; they're fetched again after that.</summary>
        public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(4);

        public static string DownloadFolder { get; set; } = Path.Combine(Session.DataFolder, "Lists");
        public static string BundledFolder { get; set; } = Path.Combine(AppContext.BaseDirectory, "Lists");

        /// <summary>The newest copy of a list on this PC (downloaded, or the one that came with the app), or null.</summary>
        public static string PathOf(BlockList list)
        {
            var downloaded = Path.Combine(DownloadFolder, list.File);
            var bundled = Path.Combine(BundledFolder, list.File);
            var candidates = new[] { downloaded, bundled }.Where(System.IO.File.Exists).ToList();
            return candidates.OrderByDescending(p => VersionOf(p)).FirstOrDefault();
        }

        public static string Read(BlockList list)
        {
            var path = PathOf(list);
            try { return path == null ? string.Empty : System.IO.File.ReadAllText(path, Encoding.UTF8); }
            catch (IOException) { return string.Empty; }
        }

        /// <summary>When the list was made: its "Last modified" line, else the file's date.</summary>
        public static DateTime VersionOf(string path)
        {
            try
            {
                using (var reader = new StreamReader(path))
                {
                    for (var i = 0; i < 20; i++)
                    {
                        var line = reader.ReadLine();
                        if (line == null) break;
                        const string tag = "! Last modified:";
                        if (line.StartsWith(tag) &&
                            DateTime.TryParse(line.Substring(tag.Length).Replace("UTC", string.Empty).Trim(), System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var when))
                            return when;
                    }
                }
                return System.IO.File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>When the lists were last fetched, or null if never.</summary>
        public static DateTime? LastFetched
        {
            get
            {
                var times = All.Select(l => Path.Combine(DownloadFolder, l.File)).Where(System.IO.File.Exists).Select(System.IO.File.GetLastWriteTimeUtc).ToList();
                return times.Count == 0 ? (DateTime?)null : times.Min();
            }
        }

        public static bool NeedsRefresh => LastFetched == null || DateTime.UtcNow - LastFetched.Value > RefreshAfter;

        /// <summary>Is this text really a block list (not an error page)?</summary>
        public static bool LooksValid(string text) =>
            text != null && text.TrimStart('﻿').StartsWith("[Adblock", StringComparison.OrdinalIgnoreCase) && text.Count(c => c == '\n') > 100;

        /// <summary>Fetches every list. Returns how many were updated. Each is checked before it replaces the old copy.</summary>
        public static async Task<int> FetchAsync(HttpClient http)
        {
            var updated = 0;
            Directory.CreateDirectory(DownloadFolder);
            foreach (var list in All)
            {
                foreach (var url in list.Urls)
                {
                    try
                    {
                        var text = await http.GetStringAsync(url).ConfigureAwait(false);
                        if (!LooksValid(text)) continue;
                        var path = Path.Combine(DownloadFolder, list.File);
                        var temp = path + ".new";
                        System.IO.File.WriteAllText(temp, text, Encoding.UTF8);
                        if (System.IO.File.Exists(path)) System.IO.File.Replace(temp, path, null);
                        else System.IO.File.Move(temp, path);
                        updated++;
                        break;
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException || ex is UnauthorizedAccessException)
                    {
                        // Try the next address; if none work, the copy already here is kept.
                    }
                }
            }
            return updated;
        }
    }
}
