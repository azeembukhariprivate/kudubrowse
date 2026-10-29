using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kudubrowse.Core.Library
{
    public sealed class Visit
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public DateTime LastVisit { get; set; } // UTC
        public int Visits { get; set; }
    }

    /// <summary>
    /// The pages visited, one entry per address, kept in %LocalAppData%\KUDUBROWSE\history.txt (a line each: address,
    /// title, last visit, number of visits). Kept for 90 days, at most 50,000 addresses.
    /// </summary>
    public sealed class History
    {
        /// <summary>How long pages are kept (the self-test keeps its old made-up ones longer).</summary>
        public static TimeSpan KeepFor { get; set; } = TimeSpan.FromDays(90);
        public const int MaxEntries = 50000;

        private readonly Dictionary<string, Visit> _byUrl = new Dictionary<string, Visit>(StringComparer.Ordinal);
        public string File { get; }
        public bool Dirty { get; private set; }
        public event Action Changed;

        public History(string file)
        {
            File = file;
        }

        public int Count => _byUrl.Count;
        public IEnumerable<Visit> All => _byUrl.Values;

        public static History Load(string file)
        {
            var h = new History(file);
            try
            {
                if (!System.IO.File.Exists(file)) return h;
                var cutoff = DateTime.UtcNow - KeepFor;
                foreach (var line in System.IO.File.ReadLines(file, Encoding.UTF8))
                {
                    var p = line.Split('\t');
                    if (p.Length < 4 || !Session.IsSafeToReopen(p[0])) continue;
                    if (!long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks)) continue;
                    var when = new DateTime(ticks, DateTimeKind.Utc);
                    if (when < cutoff) continue;
                    int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var visits);
                    h._byUrl[p[0]] = new Visit { Url = p[0], Title = p[1], LastVisit = when, Visits = Math.Max(1, visits) };
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Unreadable: start with an empty history.
            }
            return h;
        }

        public void Save()
        {
            if (!Dirty) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File));
                var keep = _byUrl.Values.OrderByDescending(v => v.LastVisit).Take(MaxEntries).ToList();
                var sb = new StringBuilder();
                foreach (var v in keep)
                    sb.Append(Clean(v.Url)).Append('\t').Append(Clean(v.Title)).Append('\t')
                      .Append(v.LastVisit.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(v.Visits.ToString(CultureInfo.InvariantCulture)).Append('\n');
                var temp = File + ".new";
                System.IO.File.WriteAllText(temp, sb.ToString(), Encoding.UTF8);
                if (System.IO.File.Exists(File)) System.IO.File.Replace(temp, File, null);
                else System.IO.File.Move(temp, File);
                Dirty = false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Tried again at the next save.
            }
        }

        /// <summary>Records a visit to a page (only ordinary web pages are kept).</summary>
        public void Add(string url, string title, DateTime? when = null)
        {
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            if (_byUrl.TryGetValue(url, out var v))
            {
                v.Visits++;
                v.LastVisit = when ?? DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(title)) v.Title = title;
            }
            else _byUrl[url] = new Visit { Url = url, Title = title ?? string.Empty, LastVisit = when ?? DateTime.UtcNow, Visits = 1 };
            Dirty = true;
            Changed?.Invoke();
        }

        /// <summary>A page's title arrived after its address.</summary>
        public void SetTitle(string url, string title)
        {
            if (url == null || string.IsNullOrWhiteSpace(title) || !_byUrl.TryGetValue(url, out var v) || v.Title == title) return;
            v.Title = title;
            Dirty = true;
        }

        /// <summary>Adds visits brought from another browser; existing entries keep the larger count and the later date.</summary>
        public int Merge(IEnumerable<Visit> visits)
        {
            var added = 0;
            var cutoff = DateTime.UtcNow - KeepFor;
            foreach (var v in visits)
            {
                if (v.LastVisit < cutoff || !Session.IsSafeToReopen(v.Url) || v.Url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) continue;
                if (_byUrl.TryGetValue(v.Url, out var mine))
                {
                    mine.Visits = Math.Max(mine.Visits, v.Visits);
                    if (v.LastVisit > mine.LastVisit) mine.LastVisit = v.LastVisit;
                    if (string.IsNullOrWhiteSpace(mine.Title)) mine.Title = v.Title;
                }
                else
                {
                    _byUrl[v.Url] = new Visit { Url = v.Url, Title = v.Title ?? string.Empty, LastVisit = v.LastVisit, Visits = Math.Max(1, v.Visits) };
                    added++;
                }
            }
            Dirty = true;
            Changed?.Invoke();
            return added;
        }

        public void Remove(string url)
        {
            if (_byUrl.Remove(url))
            {
                Dirty = true;
                Changed?.Invoke();
            }
        }

        public void Clear()
        {
            _byUrl.Clear();
            Dirty = true;
            Changed?.Invoke();
        }

        /// <summary>Newest first, matching every word typed (in the address or the title).</summary>
        public List<Visit> Search(string words, int max = 500)
        {
            var terms = (words ?? string.Empty).ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return _byUrl.Values.Where(v => terms.All(t => v.Url.ToLowerInvariant().Contains(t) || (v.Title ?? string.Empty).ToLowerInvariant().Contains(t)))
                .OrderByDescending(v => v.LastVisit).Take(max).ToList();
        }

        private static string Clean(string s) => (s ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
