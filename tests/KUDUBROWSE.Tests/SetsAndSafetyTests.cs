// The tests for saved tab sets, read later, secure connections and forgetting when the browser closes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Kudubrowse.Core;
using Kudubrowse.Core.Library;

namespace Kudubrowse.Tests
{
    public static class SetsAndSafetyTests
    {
        /// <summary>Runs these tests.</summary>
        public static void Run(Action<string, Action> test)
        {
            test("Tab sets: kept with their rooms, groups and muting; names made unique; renamed and removed", TabSetsWork);
            test("Tab sets: unsafe addresses, empty sets and damaged lines are left out", TabSetsAreSafe);
            test("Read later: a page is kept whole, saved again in place, marked read and removed with its copy", ReadLaterWorks);
            test("Read later: only web pages and whole saved copies are kept; a damaged list can't point elsewhere", ReadLaterIsSafe);
            test("Secure connections: http becomes https, except on this PC, the local network and allowed sites", SecureConnectionsWork);
            test("Clear when closing: which sites, which cookies, which places and which history are forgotten", ForgettingWorks);
        }

        /// <summary>Checks two values are equal.</summary>
        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected \"{expected}\", got \"{actual}\"");
        }

        /// <summary>Fails when the condition isn't true.</summary>
        private static void Check(bool condition, string what)
        {
            if (!condition) throw new Exception(what);
        }

        /// <summary>A new test folder.</summary>
        private static string TempFolder()
        {
            var dir = Path.Combine(Path.GetTempPath(), "kudubrowse-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void TabSetsWork()
        {
            var dir = TempFolder();
            try
            {
                var file = Path.Combine(dir, "tabsets.txt");
                var sets = TabSets.Load(file);
                Equal(0, sets.All.Count, "no sets to start with");
                var work = sets.Add("Work", new[]
                {
                    new SavedTab { Url = "https://mail.example.com/", Title = "Mail", Room = "room-2", Group = "Team", GroupColor = "#2563EB" },
                    new SavedTab { Url = "https://news.example.org/", Title = "News\twith a tab", Muted = true },
                    new SavedTab { Url = "about:blank", Title = "New tab" }
                });
                Equal(2, work.Tabs.Count, "the blank tab isn't kept");
                var again = sets.Add("work", new[] { new SavedTab { Url = "https://example.com/" } });
                Equal("work (2)", again.Name, "a taken name gets a number");
                Equal("work (2)", sets.All[0].Name, "the newest set first");

                var read = TabSets.Load(file);
                Equal(2, read.All.Count, "both sets come back");
                var w = read.All.Single(s => s.Name == "Work");
                Equal("https://mail.example.com/", w.Tabs[0].Url, "first tab");
                Equal("room-2", w.Tabs[0].Room, "its room");
                Equal("Team", w.Tabs[0].Group, "its group");
                Equal("#2563EB", w.Tabs[0].GroupColor, "its group's colour");
                Equal("News with a tab", w.Tabs[1].Title, "a title with a tab in it");
                Check(w.Tabs[1].Muted, "muting is kept");
                Check(Math.Abs((w.Saved - DateTime.UtcNow).TotalMinutes) < 5, "when it was saved");

                read.Rename(w, "Trip planning\nsoon");
                Equal("Trip planning soon", w.Name, "renamed without the line break");
                read.Rename(w, "work (2)");
                Equal("work (2) (2)", w.Name, "renaming to a taken name gets a number");
                read.Remove(read.All.Single(s => s.Name == "work (2)"));
                var after = TabSets.Load(file);
                Equal(1, after.All.Count, "one set left");
                Equal("work (2) (2)", after.All[0].Name, "the renamed one");
                Equal(new string('a', TabSets.MaxName), TabSets.CleanName(new string('a', 80)), "long names are cut");
            }
            finally { Directory.Delete(dir, true); }
        }

        private static void TabSetsAreSafe()
        {
            var dir = TempFolder();
            try
            {
                var file = Path.Combine(dir, "tabsets.txt");
                var sets = TabSets.Load(file);
                Check(sets.Add("Nothing", new[] { new SavedTab { Url = "javascript:alert(1)" }, new SavedTab { Url = "about:blank" } }) == null, "a set with no real page isn't kept");
                File.WriteAllText(file, "set\tGood\t637000000000000000\n" +
                                        "https://example.com/\tExample\tDefault\t\t\n" +
                                        "javascript:alert(1)\tBad\n" +
                                        "data:text/html,hi\tBad too\n" +
                                        "set\t\t1\n" +
                                        "https://lost.example.com/\tA tab of a set with no name\n" +
                                        "set\tEmpty\tnot a time\n" +
                                        "file:///C:/Users/me/page.html\tA file\tNoSuchRoom\n" +
                                        "set\tOnly bad\t1\n" +
                                        "vbscript:x\tBad\n", Encoding.UTF8);
                var read = TabSets.Load(file);
                Equal("Good,Empty", string.Join(",", read.All.Select(s => s.Name)), "sets with tabs left, in order");
                Equal(1, read.All[0].Tabs.Count, "only the safe tab");
                Equal(new DateTime(637000000000000000, DateTimeKind.Utc), read.All[0].Saved, "its time");
                Equal(Rooms.DefaultId, read.All[1].Tabs[0].Room, "an unknown room becomes the usual one");
                Check(TabSets.Load(Path.Combine(dir, "none.txt")).All.Count == 0, "no file, no sets");
            }
            finally { Directory.Delete(dir, true); }
        }

        /// <summary>A small saved page as the engine writes it.</summary>
        private static string Mhtml(string text) =>
            "From: <Saved by Blink>\r\nSnapshot-Content-Location: https://example.com/\r\nSubject: Page\r\nMIME-Version: 1.0\r\n" +
            "Content-Type: multipart/related;\r\n\ttype=\"text/html\";\r\n\tboundary=\"----MultipartBoundary--x\"\r\n\r\n" +
            "------MultipartBoundary--x\r\nContent-Type: text/html\r\n\r\n<html><body>" + text + "</body></html>\r\n------MultipartBoundary--x--\r\n";

        private static void ReadLaterWorks()
        {
            var dir = TempFolder();
            try
            {
                var later = ReadLater.Load(dir);
                var changed = 0;
                later.Changed += () => changed++;
                var a = later.Add("https://example.com/story", "A long story", Mhtml("first copy"));
                var b = later.Add("https://example.org/", "  ", Mhtml("other"));
                Equal("example.org", b.Title, "no title: the address is shown");
                Equal(2, later.All.Count, "two pages");
                Equal(2, later.Unread, "both unread");
                Check(File.ReadAllText(later.FileOf(a)).Contains("first copy"), "the copy is kept");
                Check(later.AddressOf(a).StartsWith("file:///"), "opened from the PC: " + later.AddressOf(a));
                Equal(a, later.FindByCopy(later.AddressOf(a)), "the page is found from its copy's address");

                later.MarkRead(a);
                Equal(1, later.Unread, "one read");
                var again = later.Add("https://example.com/story", "A long story, updated", Mhtml("second copy"));
                Equal(a.Id, again.Id, "saving the same page again keeps its place");
                Equal(2, later.All.Count, "still two pages");
                Equal(a, later.All[0], "the page saved again is at the top");
                Check(!a.Read, "saved again: not read");
                Check(File.ReadAllText(later.FileOf(a)).Contains("second copy") && !File.ReadAllText(later.FileOf(a)).Contains("first copy"), "the new copy replaces the old");

                var read = ReadLater.Load(dir);
                Equal(2, read.All.Count, "both come back");
                Equal("A long story, updated", read.All[0].Title, "the title");
                Equal("https://example.com/story", read.All[0].Url, "the address");
                Check(read.All[0].Size > 0, "its size");
                var copy = read.FileOf(read.All[1]);
                read.Remove(read.All[1]);
                Check(!File.Exists(copy), "removing takes the copy away too");
                Equal(1, ReadLater.Load(dir).All.Count, "one left");
                Check(changed >= 4, "the windows are told about changes");
            }
            finally { Directory.Delete(dir, true); }
        }

        private static void ReadLaterIsSafe()
        {
            var dir = TempFolder();
            try
            {
                var later = ReadLater.Load(dir);
                foreach (var url in new[] { "file:///C:/secret.txt", "javascript:alert(1)", "about:blank", "not an address" })
                {
                    try { later.Add(url, "x", Mhtml("x")); throw new Exception(url + " was kept"); }
                    catch (InvalidDataException) { }
                }
                try { later.Add("https://example.com/", "x", "<html>not a saved page</html>"); throw new Exception("a page that isn't a whole copy was kept"); }
                catch (InvalidDataException) { }
                Equal(0, later.All.Count, "nothing kept");
                later.Add("https://example.com/", "Good", Mhtml("good"));
                // A damaged list: a made-up id pointing outside the folder, a bad address, a missing copy.
                File.AppendAllText(later.ListFile,
                    "..\\..\\Windows\\win\thttps://example.com/\tBad id\t1\tnew\t5\n" +
                    "0123456789abcdef0123456789abcdef\tjavascript:x\tBad address\t1\tnew\t5\n" +
                    "fedcba9876543210fedcba9876543210\thttps://example.net/\tNo copy\t1\tnew\t5\n" +
                    "short line\n", Encoding.UTF8);
                var read = ReadLater.Load(dir);
                Equal("Good", string.Join(",", read.All.Select(p => p.Title)), "only the good page");
                Check(ReadLater.IsMhtml(Mhtml("x")) && !ReadLater.IsMhtml("") && !ReadLater.IsMhtml("MIME-Version: 1.0\r\nContent-Type: text/plain\r\n"), "what counts as a saved page");
                Check(ReadLater.Load(Path.Combine(dir, "none")).All.Count == 0, "no folder, no pages");
            }
            finally { Directory.Delete(dir, true); }
        }

        private static void SecureConnectionsWork()
        {
            var none = new List<string>();
            Equal("https://example.com/news?id=1#top", SecureConnections.SecureAddress("http://example.com/news?id=1#top", none), "upgraded, keeping the rest");
            Equal("https://example.com:8080/", SecureConnections.SecureAddress("http://example.com:8080/", none), "another port is kept");
            Equal("https://www.example.co.uk/", SecureConnections.SecureAddress("http://www.example.co.uk", none), "a www site");
            foreach (var url in new[] { "https://example.com/", "file:///C:/page.html", "ftp://example.com/", "about:blank", "nonsense" })
                Equal(null, SecureConnections.SecureAddress(url, none), "not http: " + url);
            foreach (var url in new[] { "http://localhost:8080/", "http://127.0.0.1/", "http://192.168.1.1/", "http://10.0.0.5/", "http://172.20.1.1/",
                                        "http://169.254.0.1/", "http://router/", "http://printer.local/", "http://nas.lan/", "http://box.home.arpa/",
                                        "http://[::1]/", "http://[fe80::1]/", "http://[fd00::1]/", "http://app.localhost/", "http://100.64.0.1/" })
                Equal(null, SecureConnections.SecureAddress(url, none), "on this PC or the local network: " + url);
            foreach (var url in new[] { "http://8.8.8.8/", "http://172.32.0.1/", "http://192.169.0.1/", "http://[2001:db8::1]/" })
                Check(SecureConnections.SecureAddress(url, none) != null, "a public address is upgraded: " + url);
            var allowed = new List<string> { "example.org" };
            Equal(null, SecureConnections.SecureAddress("http://shop.example.org/", allowed), "a site allowed without a secure connection (a part of it too)");
            Check(SecureConnections.SecureAddress("http://example.net/", allowed) != null, "another site isn't");
            Equal("example.org", SecureConnections.SiteOf("http://shop.example.org/cart"), "the site of an address");
            Check(SecureConnections.IsAllowed("WWW.EXAMPLE.ORG", allowed), "upper case doesn't matter");
        }

        private static void ForgettingWorks()
        {
            var sites = new List<string> { "example.com", "bank.co.uk" };
            Check(!ForgetOnClose.Active(ForgetMode.Off, true, true, sites), "off: nothing");
            Check(!ForgetOnClose.Active(ForgetMode.Sites, true, true, new List<string>()), "the list's sites, but none listed: nothing");
            Check(!ForgetOnClose.Active(ForgetMode.Everything, false, false, sites), "nothing ticked: nothing");
            Check(ForgetOnClose.Active(ForgetMode.Sites, false, true, sites) && ForgetOnClose.Active(ForgetMode.Everything, true, false, new List<string>()), "on");

            Check(ForgetOnClose.IsOnList("mail.example.com", sites) && ForgetOnClose.IsOnList("www.bank.co.uk", sites), "parts of a site are on the list");
            Check(!ForgetOnClose.IsOnList("example.com.evil.net", sites) && !ForgetOnClose.IsOnList("notexample.com", sites), "look-alike names aren't");
            Check(ForgetOnClose.CookieIsOnList(".example.com", sites) && ForgetOnClose.CookieIsOnList("login.bank.co.uk", sites), "cookies of the sites");
            Check(!ForgetOnClose.CookieIsOnList(".other.com", sites), "other cookies are kept");
            Check(ForgetOnClose.IsOnList("localhost", new[] { "localhost" }) && !ForgetOnClose.IsOnList("127.0.0.1", new[] { "localhost" }), "a name on this PC works as a site");
            Equal("localhost", ForgetOnClose.SiteOf("LOCALHOST"), "its site");

            var origins = ForgetOnClose.Origins(new[] { "example.com" }, new[] { "https://mail.example.com/inbox", "http://shop.example.com:8080/", "https://other.com/", "file:///x" });
            foreach (var o in new[] { "https://example.com", "http://example.com", "https://www.example.com", "https://mail.example.com", "http://shop.example.com:8080" })
                Check(origins.Contains(o), "place to clear: " + o + " (got " + string.Join(" ", origins) + ")");
            Check(!origins.Any(o => o.Contains("other.com")), "other sites' places are left");

            var dir = TempFolder();
            try
            {
                var history = History.Load(Path.Combine(dir, "history.txt"));
                history.Add("https://example.com/a", "A");
                history.Add("https://mail.example.com/b", "B");
                history.Add("https://other.com/c", "C");
                Equal(0, ForgetOnClose.ForgetHistory(history, ForgetMode.Off, sites), "off: nothing forgotten");
                Equal(2, ForgetOnClose.ForgetHistory(history, ForgetMode.Sites, sites), "the site's pages are forgotten");
                Equal("https://other.com/c", history.All.Single().Url, "other pages stay");
                history.Add("https://example.com/a", "A");
                Equal(2, ForgetOnClose.ForgetHistory(history, ForgetMode.Everything, sites), "everything");
                Equal(0, history.Count, "no history left");
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
