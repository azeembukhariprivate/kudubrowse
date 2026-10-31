using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kudubrowse.Core;

namespace Kudubrowse.Tests
{
    public static class Program
    {
        private static int _passed, _failed;

        public static int Main()
        {
            Test("Typed web addresses open as they are, or with https:// added", AddressesOpen);
            Test("Words in the address bar are searched for with the chosen search engine", WordsAreSearched);
            Test("Addresses are shown without https:// and a lone trailing slash", AddressesShowShort);
            Test("Open tabs are saved and come back in order, with the selected one", TabsComeBack);
            Test("Only ordinary web pages and files are reopened", OnlySafeTabsReopen);
            Test("Closed tabs can be reopened newest first, up to a limit", ClosedTabsReopen);
            BlockingTests.Run(Test);
            LibraryTests.Run(Test);
            PasswordTests.Run(Test);
            RoomsTests.Run(Test);
            DownloadTests.Run(Test);
            UpdateTests.Run(Test);
            Console.WriteLine();
            Console.WriteLine($"{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }

        private static void Test(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("ok    " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("FAIL  " + name + ": " + ex.Message);
            }
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected \"{expected}\", got \"{actual}\"");
        }

        private static readonly SearchEngine Google = SearchEngines.Find("google");

        private static void AddressesOpen()
        {
            Equal("https://example.com", AddressBar.Resolve("example.com", Google), "plain host");
            Equal("https://news.bbc.co.uk/sport?x=1", AddressBar.Resolve("  news.bbc.co.uk/sport?x=1 ", Google), "path and query");
            Equal("https://example.com", AddressBar.Resolve("https://example.com", Google), "already has https");
            Equal("http://example.com/a", AddressBar.Resolve("http://example.com/a", Google), "keeps http when typed");
            Equal("http://localhost:8080/test", AddressBar.Resolve("localhost:8080/test", Google), "localhost uses http");
            Equal("http://192.168.1.1", AddressBar.Resolve("192.168.1.1", Google), "router address");
            Equal("about:blank", AddressBar.Resolve("about:blank", Google), "about page");
            Equal("file:///C:/Users/me/page.html", AddressBar.Resolve(@"C:\Users\me\page.html", Google), "Windows path");
            Equal(null, AddressBar.Resolve("   ", Google), "nothing typed");
        }

        private static void WordsAreSearched()
        {
            Equal("https://www.google.com/search?q=kerala%20backwaters", AddressBar.Resolve("kerala backwaters", Google), "two words");
            Equal("https://www.google.com/search?q=weather", AddressBar.Resolve("weather", Google), "one word without a dot");
            Equal("https://duckduckgo.com/?q=c%23%20%26%20.net", AddressBar.Resolve("c# & .net", SearchEngines.Find("duckduckgo")), "symbols are escaped");
            Equal("https://www.google.com/search?q=example.com%20login", AddressBar.Resolve("example.com login", Google), "an address with words is a search");
            Equal("https://www.google.com/search?q=file.txt2", AddressBar.Resolve("file.txt2", Google), "not a real ending");
            Equal("https://www.google.com/search?q=example.com%3A99999", AddressBar.Resolve("example.com:99999", Google), "impossible port");
            Equal("google", SearchEngines.Find("unknown").Id, "unknown engine falls back to the default");
        }

        private static void AddressesShowShort()
        {
            Equal("example.com", AddressBar.Display("https://example.com/"), "trailing slash");
            Equal("example.com/a/", AddressBar.Display("https://example.com/a/"), "keeps a slash after a path");
            Equal("http://example.com/", AddressBar.Display("http://example.com/"), "shows http so it's clear it's not private");
            Equal(string.Empty, AddressBar.Display("about:blank"), "blank page");
            Equal(true, AddressBar.IsSecure("https://x.org"), "https is private");
            Equal(false, AddressBar.IsSecure("http://x.org"), "http is not");
        }

        private static string TempFile() => Path.Combine(Path.GetTempPath(), "kudubrowse-test-" + Guid.NewGuid().ToString("N") + ".txt");

        private static void TabsComeBack()
        {
            var file = TempFile();
            try
            {
                Session.Save(new[]
                {
                    new SavedTab { Url = "https://example.com/", Title = "Example" },
                    new SavedTab { Url = "about:blank", Title = "New tab" },
                    new SavedTab { Url = "https://kerala.gov.in/a?b=c", Title = "Tabs\tand\nnew lines" }
                }, 2, file);
                var (tabs, selected) = Session.Load(file);
                Equal(2, tabs.Count, "blank tabs are left out");
                Equal("https://example.com/", tabs[0].Url, "first address");
                Equal("Tabs and new lines", tabs[1].Title, "title is kept on one line");
                Equal(1, selected, "selected index is kept within the tabs");
                // Saving again replaces the file.
                Session.Save(new[] { new SavedTab { Url = "https://a.org/", Title = "A" } }, 0, file);
                Equal(1, Session.Load(file).Tabs.Count, "saved again");
                Equal(0, Session.Load(file + ".missing").Tabs.Count, "no file: no tabs");
            }
            finally
            {
                File.Delete(file);
            }
        }

        private static void OnlySafeTabsReopen()
        {
            var file = TempFile();
            try
            {
                File.WriteAllText(file, "selected\t9\njavascript:alert(1)\tx\ndata:text/html,hi\ty\nhttps://ok.org/\tOK\nfile:///C:/a.html\tA\n");
                var (tabs, selected) = Session.Load(file);
                Equal("https://ok.org/|file:///C:/a.html", string.Join("|", tabs.Select(t => t.Url)), "only web pages and files");
                Equal(1, selected, "selected index past the end is the last tab");
            }
            finally
            {
                File.Delete(file);
            }
        }

        private static void ClosedTabsReopen()
        {
            var closed = new ClosedTabs();
            closed.Add(new SavedTab { Url = "https://one.org/" });
            closed.Add(new SavedTab { Url = "javascript:void(0)" });
            closed.Add(new SavedTab { Url = "https://two.org/" });
            Equal(2, closed.Count, "unsafe address not kept");
            Equal("https://two.org/", closed.TakeLatest().Url, "newest first");
            Equal("https://one.org/", closed.TakeLatest().Url, "then the one before");
            Equal(null, closed.TakeLatest(), "none left");
            for (var i = 0; i < 40; i++) closed.Add(new SavedTab { Url = "https://x.org/" + i });
            Equal(ClosedTabs.Max, closed.Count, "limited");
        }
    }
}
