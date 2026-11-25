// Read later: pages kept on this PC, so they open without the internet. Each page is saved whole (an MHTML file, the
// page with its pictures and styles in one file) in the ReadLater folder; list.txt lists them.
// lang.py: only Lang.T texts (the rest are the list file's own words)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kudubrowse.Core
{
    /// <summary>A page kept to read later.</summary>
    public sealed class LaterPage
    {
        /// <summary>Its id (also its file's name).</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>The page's own address.</summary>
        public string Url { get; set; } = string.Empty;
        /// <summary>Its title.</summary>
        public string Title { get; set; } = string.Empty;
        /// <summary>When it was saved (UTC).</summary>
        public DateTime Saved { get; set; }
        /// <summary>True once it has been opened.</summary>
        public bool Read { get; set; }
        /// <summary>How big the saved copy is, in bytes.</summary>
        public long Size { get; set; }
    }

    /// <summary>The pages kept to read later.</summary>
    public sealed class ReadLater
    {
        /// <summary>The biggest page kept (bigger ones are refused).</summary>
        public const long MaxSize = 100L << 20;

        /// <summary>Every page, newest first.</summary>
        private readonly List<LaterPage> _pages = new List<LaterPage>();

        /// <summary>The folder the pages are kept in.</summary>
        public string Folder { get; }
        /// <summary>The list of pages.</summary>
        public string ListFile => Path.Combine(Folder, "list.txt");

        /// <summary>A page was added, read or removed.</summary>
        public event Action Changed;

        /// <summary>An empty list kept in this folder.</summary>
        private ReadLater(string folder) => Folder = folder;

        /// <summary>Every page, newest first.</summary>
        public IReadOnlyList<LaterPage> All => _pages;
        /// <summary>How many haven't been opened yet.</summary>
        public int Unread => _pages.Count(p => !p.Read);

        /// <summary>Reads the list (pages whose saved copy is missing are left out).</summary>
        public static ReadLater Load(string folder)
        {
            var list = new ReadLater(folder);
            try
            {
                if (!File.Exists(list.ListFile)) return list;
                foreach (var line in File.ReadAllLines(list.ListFile, Encoding.UTF8))
                {
                    var p = line.Split('\t');
                    if (p.Length < 6 || !IsId(p[0]) || !IsWebAddress(p[1])) continue;
                    var page = new LaterPage
                    {
                        Id = p[0],
                        Url = p[1],
                        Title = p[2],
                        Saved = long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) && t > 0 && t <= DateTime.MaxValue.Ticks
                            ? new DateTime(t, DateTimeKind.Utc) : DateTime.UtcNow,
                        Read = p[4] == "read",
                        Size = long.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0
                    };
                    if (File.Exists(list.FileOf(page)) && list._pages.All(x => x.Id != page.Id)) list._pages.Add(page);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Unreadable: none listed this time.
            }
            return list;
        }

        /// <summary>Keeps the list (to a new file first, then put in place).</summary>
        public void Save()
        {
            var sb = new StringBuilder();
            foreach (var p in _pages)
                sb.Append(p.Id).Append('\t').Append(Session.Clean(p.Url)).Append('\t').Append(Session.Clean(p.Title)).Append('\t')
                  .Append(p.Saved.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(p.Read ? "read" : "new").Append('\t')
                  .Append(p.Size.ToString(CultureInfo.InvariantCulture)).Append('\n');
            try
            {
                Directory.CreateDirectory(Folder);
                var temp = ListFile + ".new";
                File.WriteAllText(temp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(ListFile)) File.Replace(temp, ListFile, null);
                else File.Move(temp, ListFile);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Tried again at the next change.
            }
        }

        /// <summary>
        /// Keeps a page (its saved copy, as MHTML text). Saving a page already in the list puts the new copy in its
        /// place, at the top, as not read. Throws InvalidDataException when the copy isn't a whole page or is too big.
        /// </summary>
        public LaterPage Add(string url, string title, string mhtml)
        {
            if (!IsWebAddress(url)) throw new InvalidDataException(Shown("Only web pages can be kept to read later."));
            if (!IsMhtml(mhtml)) throw new InvalidDataException(Shown("The page couldn't be saved."));
            var bytes = Encoding.UTF8.GetBytes(mhtml);
            if (bytes.LongLength > MaxSize) throw new InvalidDataException(Shown("The page is too big to keep (over 100 MB)."));
            var page = _pages.FirstOrDefault(p => p.Url == url) ?? new LaterPage { Id = Guid.NewGuid().ToString("N") };
            page.Url = url;
            page.Title = string.IsNullOrWhiteSpace(title) ? AddressBar.Display(url) : title.Trim();
            page.Saved = DateTime.UtcNow;
            page.Read = false;
            page.Size = bytes.LongLength;
            Directory.CreateDirectory(Folder);
            var file = FileOf(page);
            var temp = file + ".new";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(file)) File.Delete(file);
            File.Move(temp, file);
            _pages.Remove(page);
            _pages.Insert(0, page);
            Save();
            Changed?.Invoke();
            return page;
        }

        /// <summary>The page kept from this address, or null.</summary>
        public LaterPage Find(string url) => string.IsNullOrEmpty(url) ? null : _pages.FirstOrDefault(p => p.Url == url);

        /// <summary>The kept page whose saved copy this address opens (a file:// address), or null.</summary>
        public LaterPage FindByCopy(string address)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var u) || !u.IsFile) return null;
            var path = u.LocalPath;
            return _pages.FirstOrDefault(p => string.Equals(Path.GetFullPath(FileOf(p)), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Marks a page as read (or not).</summary>
        public void MarkRead(LaterPage page, bool read = true)
        {
            if (page == null || page.Read == read || !_pages.Contains(page)) return;
            page.Read = read;
            Save();
            Changed?.Invoke();
        }

        /// <summary>Removes a page and its saved copy.</summary>
        public void Remove(LaterPage page)
        {
            if (page == null || !_pages.Remove(page)) return;
            try { File.Delete(FileOf(page)); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            Save();
            Changed?.Invoke();
        }

        /// <summary>The saved copy's file.</summary>
        public string FileOf(LaterPage page) => Path.Combine(Folder, page.Id + ".mhtml");

        /// <summary>The address that opens the saved copy in a tab.</summary>
        public string AddressOf(LaterPage page) => new Uri(FileOf(page)).AbsoluteUri;

        /// <summary>The saved copies' size together, in bytes.</summary>
        public long TotalSize => _pages.Sum(p => p.Size);

        /// <summary>Marks a text as one the person reads (it's translated where it's shown, with Lang.T).</summary>
        private static string Shown(string text) => text;

        /// <summary>True for an id made by Add (32 hex digits), so a damaged list can't point at another file.</summary>
        private static bool IsId(string id) => id.Length == 32 && id.All(Uri.IsHexDigit);

        /// <summary>True for an http or https address.</summary>
        public static bool IsWebAddress(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");

        /// <summary>True when the text looks like a whole saved page (MHTML: a MIME message of several parts).</summary>
        public static bool IsMhtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var head = text.Length > 4000 ? text.Substring(0, 4000) : text;
            return head.IndexOf("MIME-Version:", StringComparison.OrdinalIgnoreCase) >= 0
                   && head.IndexOf("multipart/related", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
