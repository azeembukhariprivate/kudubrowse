using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Kudubrowse.Core.Library
{
    /// <summary>A bookmark (with a web address) or a folder (with children).</summary>
    [DataContract]
    public sealed class Bookmark
    {
        [DataMember(Name = "title", Order = 1)] public string Title { get; set; }
        [DataMember(Name = "url", Order = 2, EmitDefaultValue = false)] public string Url { get; set; }
        [DataMember(Name = "added", Order = 3, EmitDefaultValue = false)] public DateTime Added { get; set; }
        [DataMember(Name = "children", Order = 4, EmitDefaultValue = false)] public List<Bookmark> Children { get; set; }

        public bool IsFolder => Url == null;

        public static Bookmark Folder(string title) => new Bookmark { Title = title, Children = new List<Bookmark>() };
        public static Bookmark Link(string title, string url) => new Bookmark { Title = string.IsNullOrWhiteSpace(title) ? url : title.Trim(), Url = url, Added = DateTime.UtcNow };

        /// <summary>Every bookmark in this folder and the folders inside it.</summary>
        public IEnumerable<Bookmark> AllLinks() =>
            IsFolder ? (Children ?? new List<Bookmark>()).SelectMany(c => c.IsFolder ? c.AllLinks() : new[] { c }) : new[] { this };
    }

    /// <summary>The bookmarks: the bookmarks bar and "Other bookmarks", kept in %LocalAppData%\KUDUBROWSE\bookmarks.json.</summary>
    public sealed class Bookmarks
    {
        [DataContract]
        private sealed class Stored
        {
            [DataMember(Name = "bar", Order = 1)] public Bookmark Bar { get; set; }
            [DataMember(Name = "other", Order = 2)] public Bookmark Other { get; set; }
        }

        public Bookmark Bar { get; private set; } = Bookmark.Folder("Bookmarks bar");
        public Bookmark Other { get; private set; } = Bookmark.Folder("Other bookmarks");
        public string File { get; }

        /// <summary>Something was added, removed or renamed.</summary>
        public event Action Changed;

        public Bookmarks(string file)
        {
            File = file;
        }

        public static Bookmarks Load(string file)
        {
            var b = new Bookmarks(file);
            try
            {
                if (System.IO.File.Exists(file))
                {
                    using (var stream = System.IO.File.OpenRead(file))
                    {
                        var stored = (Stored)new DataContractJsonSerializer(typeof(Stored)).ReadObject(stream);
                        if (stored?.Bar?.Children != null) b.Bar = stored.Bar;
                        if (stored?.Other?.Children != null) b.Other = stored.Other;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SerializationException || ex is UnauthorizedAccessException || ex is InvalidCastException)
            {
                // A damaged file: keep a copy for safety and start empty.
                try { System.IO.File.Copy(file, file + ".damaged", true); }
                catch (Exception copy) when (copy is IOException || copy is UnauthorizedAccessException) { }
            }
            return b;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File));
                var temp = File + ".new";
                using (var stream = System.IO.File.Create(temp))
                    new DataContractJsonSerializer(typeof(Stored)).WriteObject(stream, new Stored { Bar = Bar, Other = Other });
                if (System.IO.File.Exists(File)) System.IO.File.Replace(temp, File, null);
                else System.IO.File.Move(temp, File);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Best-effort; tried again at the next change.
            }
        }

        private void Touch()
        {
            Save();
            Changed?.Invoke();
        }

        public IEnumerable<Bookmark> AllLinks() => Bar.AllLinks().Concat(Other.AllLinks());

        public Bookmark Find(string url) => string.IsNullOrEmpty(url) ? null : AllLinks().FirstOrDefault(b => SameUrl(b.Url, url));

        public bool Contains(string url) => Find(url) != null;

        public Bookmark Add(string title, string url, Bookmark folder = null)
        {
            var existing = Find(url);
            if (existing != null) return existing;
            var b = Bookmark.Link(title, url);
            (folder ?? Bar).Children.Add(b);
            Touch();
            return b;
        }

        public Bookmark AddFolder(string title, Bookmark parent = null)
        {
            var f = Bookmark.Folder(title);
            (parent ?? Other).Children.Add(f);
            Touch();
            return f;
        }

        public bool Remove(Bookmark item)
        {
            var parent = ParentOf(item);
            if (parent == null) return false;
            parent.Children.Remove(item);
            Touch();
            return true;
        }

        public void Rename(Bookmark item, string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return;
            item.Title = title.Trim();
            Touch();
        }

        public void Move(Bookmark item, Bookmark folder)
        {
            var parent = ParentOf(item);
            if (parent == null || parent == folder || item == folder) return;
            parent.Children.Remove(item);
            folder.Children.Add(item);
            Touch();
        }

        public Bookmark ParentOf(Bookmark item) => ParentOf(item, Bar) ?? ParentOf(item, Other);

        private static Bookmark ParentOf(Bookmark item, Bookmark folder)
        {
            if (folder.Children == null) return null;
            if (folder.Children.Contains(item)) return folder;
            foreach (var f in folder.Children.Where(c => c.IsFolder))
            {
                var p = ParentOf(item, f);
                if (p != null) return p;
            }
            return null;
        }

        /// <summary>Adds imported bookmarks (from Chrome), leaving out addresses already bookmarked. Returns how many were added.</summary>
        public int Merge(IEnumerable<Bookmark> items, Bookmark into)
        {
            var known = new HashSet<string>(AllLinks().Select(b => Normal(b.Url)));
            var added = 0;
            void Copy(IEnumerable<Bookmark> from, Bookmark to)
            {
                foreach (var item in from)
                {
                    if (item.IsFolder)
                    {
                        var f = to.Children.FirstOrDefault(c => c.IsFolder && c.Title == item.Title) ?? Bookmark.Folder(item.Title);
                        if (!to.Children.Contains(f)) to.Children.Add(f);
                        Copy(item.Children ?? new List<Bookmark>(), f);
                        if (f.Children.Count == 0) to.Children.Remove(f);
                    }
                    else if (Session.IsSafeToReopen(item.Url) && known.Add(Normal(item.Url)))
                    {
                        to.Children.Add(new Bookmark { Title = item.Title, Url = item.Url, Added = item.Added == default ? DateTime.UtcNow : item.Added });
                        added++;
                    }
                }
            }
            Copy(items, into);
            if (added > 0) Touch();
            return added;
        }

        public static bool SameUrl(string a, string b) => Normal(a) == Normal(b);

        private static string Normal(string url) => (url ?? string.Empty).TrimEnd('/').ToLowerInvariant();
    }
}
