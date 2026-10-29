using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kudubrowse.Core;
using Kudubrowse.Core.Library;

namespace Kudubrowse.Tests
{
    public static class LibraryTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("Bookmarks: add, find, folders, rename, move, remove, saved and read back", BookmarksWork);
            test("Bookmarks: a damaged file is kept aside and starts empty", DamagedBookmarks);
            test("History: visits are counted, kept 90 days, searched and saved", HistoryWorks);
            test("Address bar suggestions: bookmarks and frequent pages first, and a search", SuggestionsWork);
            test("Chrome: profiles and their names are found", ChromeProfiles);
            test("Chrome: bookmarks are read, unsafe addresses left out, mobile ones in a folder", ChromeBookmarks);
            test("Chrome: history is read from its SQLite file (big tables, long rows, hidden pages)", ChromeHistory);
            test("Chrome: importing twice doesn't double anything", ImportTwice);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected \"{expected}\", got \"{actual}\"");
        }

        private static string Temp(string ext = ".json") => Path.Combine(Path.GetTempPath(), "kudubrowse-test-" + Guid.NewGuid().ToString("N") + ext);
        private static string Fixture => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "ChromeUserData"));

        private static void BookmarksWork()
        {
            var file = Temp();
            try
            {
                var b = Bookmarks.Load(file);
                var changes = 0;
                b.Changed += () => changes++;
                b.Add("Example", "https://example.com/");
                b.Add("Again", "https://EXAMPLE.com"); // the same address
                Equal(1, b.Bar.Children.Count, "no duplicate");
                var folder = b.AddFolder("Recipes");
                var curry = b.Add("Fish curry", "https://food.example/curry", folder);
                Equal(true, b.Contains("https://food.example/curry/"), "found inside a folder");
                b.Rename(curry, "  Meen curry ");
                b.Move(curry, b.Bar);
                Equal(folder.Children.Count, 0, "moved out");
                Equal(b.Bar, b.ParentOf(curry), "now on the bar");
                var again = Bookmarks.Load(file);
                Equal("Meen curry", again.Find("https://food.example/curry").Title, "saved and read back");
                Equal("Recipes", again.Other.Children[0].Title, "folder kept");
                b.Remove(b.Find("https://example.com/"));
                Equal(false, Bookmarks.Load(file).Contains("https://example.com/"), "removed");
                Equal(true, changes >= 5, "told about changes");
            }
            finally
            {
                File.Delete(file);
            }
        }

        private static void DamagedBookmarks()
        {
            var file = Temp();
            try
            {
                File.WriteAllText(file, "{ not json");
                var b = Bookmarks.Load(file);
                Equal(0, b.AllLinks().Count(), "empty");
                Equal(true, File.Exists(file + ".damaged"), "copy kept");
            }
            finally
            {
                File.Delete(file);
                File.Delete(file + ".damaged");
            }
        }

        private static void HistoryWorks()
        {
            var file = Temp(".txt");
            try
            {
                var h = History.Load(file);
                h.Add("https://news.example/a", "First title");
                h.Add("https://news.example/a", null);
                h.SetTitle("https://news.example/a", "Story\twith\ttabs");
                h.Add("file:///C:/x.html", "Local"); // not kept
                h.Add("https://shop.example/", "Shop");
                h.Merge(new[]
                {
                    new Visit { Url = "https://old.example/", Title = "Old", Visits = 3, LastVisit = DateTime.UtcNow.AddDays(-200) },
                    new Visit { Url = "https://shop.example/", Title = "Shop", Visits = 9, LastVisit = DateTime.UtcNow.AddDays(-1) }
                });
                h.Save();
                var again = History.Load(file);
                Equal(2, again.Count, "two pages (local file and old one left out)");
                var a = again.Search("news").Single();
                Equal(2, a.Visits, "visits counted");
                Equal("Story with tabs", a.Title, "title kept on one line");
                Equal(9, again.Search("shop").Single().Visits, "merged count");
                Equal(1, again.Search("story news").Count, "every word must match");
                again.Remove("https://shop.example/");
                again.Clear();
                Equal(0, again.Count, "cleared");
            }
            finally
            {
                File.Delete(file);
            }
        }

        private static void SuggestionsWork()
        {
            var engine = SearchEngines.Find("google");
            var marks = new[] { Bookmark.Link("GitHub", "https://github.com/") };
            var now = DateTime.UtcNow;
            var visits = new[]
            {
                new Visit { Url = "https://www.gitlab.com/", Title = "GitLab", Visits = 1, LastVisit = now.AddDays(-30) },
                new Visit { Url = "https://news.example/git-tips", Title = "Ten git tips", Visits = 50, LastVisit = now },
                new Visit { Url = "https://weather.example/", Title = "Weather in Kochi", Visits = 5, LastVisit = now }
            };
            var s = Suggestions.For("git", marks, visits, engine);
            Equal("https://github.com/", s[0].Url, "the bookmark whose address starts with it comes first");
            Equal(SuggestionKind.Search, s[1].Kind, "then the search");
            Equal(true, s.Any(x => x.Url == "https://news.example/git-tips"), "pages that mention it");
            Equal(false, s.Any(x => x.Url.Contains("weather")), "not unrelated pages");
            var w = Suggestions.For("kochi weather", marks, visits, engine);
            Equal(SuggestionKind.Search, w[0].Kind, "words: the search first");
            Equal("https://weather.example/", w[1].Url, "then the matching page");
            var addr = Suggestions.For("example.org", marks, visits, engine);
            Equal(SuggestionKind.Address, addr[0].Kind, "an address is offered to open");
            Equal("https://example.org", addr[0].Url, "its address");
            Equal(0, Suggestions.For("  ", marks, visits, engine).Count, "nothing typed");
        }

        private static void ChromeProfiles()
        {
            ChromeImport.UserDataFolder = Fixture;
            var p = ChromeImport.Profiles();
            Equal("Person 1|Work \"desk\"", string.Join("|", p.Select(x => x.Name)), "names from Local State");
            Equal(true, p[0].HasHistory && p[0].HasBookmarks, "Default has both");
            ChromeImport.UserDataFolder = Path.Combine(Fixture, "missing");
            Equal(0, ChromeImport.Profiles().Count, "no Chrome");
        }

        private static void ChromeBookmarks()
        {
            var b = ChromeImport.ReadBookmarks(Path.Combine(Fixture, "Default", "Bookmarks"));
            Equal("Kerala Tourism|News", string.Join("|", b.Bar.Select(x => x.Title)), "bar");
            Equal(1, b.Bar[1].Children.Count, "javascript: left out");
            Equal("Wikipedia|Mobile bookmarks", string.Join("|", b.Other.Select(x => x.Title)), "other and mobile");
            Equal(4, b.Count, "four bookmarks");
            Equal(2025, b.Bar[0].Added.Year, "date added from Chrome's time");
        }

        private static void ChromeHistory()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var v = ChromeImport.ReadHistory(Path.Combine(Fixture, "Default", "History"));
            Console.WriteLine($"      {v.Count} pages read in {watch.ElapsedMilliseconds} ms");
            Equal(3001, v.Count, "3000 pages and the long one (hidden and chrome:// left out)");
            var p = v.Single(x => x.Url == "https://site7.example.com/page/1207");
            Equal("Page number 1207 – café ✓", p.Title, "title with accents and symbols");
            Equal(1207 % 7 + 1, p.Visits, "visit count");
            Equal(9005, v.Single(x => x.Url == "https://long.example.com/").Title.Length, "a title spread over extra pages");
            Equal(new DateTime(2025, 8, 18), v.Single(x => x.Url == "https://site0.example.com/page/0").LastVisit.Date, "Chrome's date, read correctly");
        }

        private static void ImportTwice()
        {
            var file = Temp();
            try
            {
                var b = Bookmarks.Load(file);
                var data = ChromeImport.ReadBookmarks(Path.Combine(Fixture, "Default", "Bookmarks"));
                Equal(2, b.Merge(data.Bar, b.Bar), "bar added");
                Equal(2, b.Merge(data.Other, b.Other), "others added");
                Equal(0, b.Merge(data.Bar, b.Bar) + b.Merge(data.Other, b.Other), "nothing the second time");
                Equal(1, b.Bar.Children.Count(c => c.IsFolder && c.Title == "News"), "folders not doubled");
                var h = History.Load(Temp(".txt"));
                var visits = ChromeImport.ReadHistory(Path.Combine(Fixture, "Default", "History"));
                foreach (var x in visits) x.LastVisit = DateTime.UtcNow.AddDays(-1);
                Equal(3001, h.Merge(visits), "history added");
                Equal(0, h.Merge(visits), "not again");
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}
