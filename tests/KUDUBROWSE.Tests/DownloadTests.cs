using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kudubrowse.Core;
using Kudubrowse.Views;

namespace Kudubrowse.Tests
{
    /// <summary>The download manager's own parts, against a small web server on this computer.</summary>
    public static class DownloadTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("Downloads: sorted into folders by type, safe names, never replacing a file", Rules);
            test("Downloads: big files are split into pieces; the site's answers are read", Pieces);
            test("Downloads: a big file arrives whole in pieces at the same time, with the site's cookies", InPieces);
            test("Downloads: a paused download carries on from where it stopped, also after a restart", PauseAndResume);
            test("Downloads: dropped connections are tried again; a changed file starts again", DroppedAndChanged);
            test("Downloads: sites that don't allow parts, redirects, and refusals in plain words", OtherSites);
            test("Downloads: the list is kept for next time, without private windows' downloads", ListKept);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected \"{expected}\", got \"{actual}\"");
        }

        private static string TempFolder()
        {
            var d = Path.Combine(Path.GetTempPath(), "kudubrowse-dl-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        private static void Rules()
        {
            Equal("Pictures", DownloadRules.Kind("Holiday.JPG"), "pictures");
            Equal("Programs", DownloadRules.Kind("setup.exe"), "programs");
            Equal("Documents", DownloadRules.Kind("report.pdf"), "documents");
            Equal("Archives", DownloadRules.Kind("photos.7z"), "archives");
            Equal("Videos", DownloadRules.Kind("film.mkv"), "videos");
            Equal("Music", DownloadRules.Kind("song.flac"), "music");
            Equal(null, DownloadRules.Kind("notes.xyz"), "other files");
            Equal(null, DownloadRules.Kind("README"), "no ending");
            var d = Path.Combine("D", "Downloads");
            Equal(Path.Combine(d, "Pictures"), DownloadRules.FolderFor(d, "a.png", true), "sorted");
            Equal(d, DownloadRules.FolderFor(d, "a.png", false), "not sorted");
            Equal(d, DownloadRules.FolderFor(d, "a.xyz", true), "other files stay in the main folder");

            Equal("evil.exe", DownloadRules.SafeName("..\\..\\Windows\\evil.exe"), "no folders (back slashes)");
            Equal("passwd", DownloadRules.SafeName("../../etc/passwd"), "no folders (slashes)");
            Equal("a_b_c.txt", DownloadRules.SafeName("a:b?c.txt"), "characters Windows refuses");
            Equal("download", DownloadRules.SafeName("..."), "never empty");
            var longName = DownloadRules.SafeName(new string('x', 300) + ".pdf");
            Equal(true, longName.Length == 180 && longName.EndsWith(".pdf"), "very long names are shortened, keeping the ending");

            var taken = new HashSet<string> { Path.Combine("f", "a.txt"), Path.Combine("f", "a (1).txt") };
            Equal(Path.Combine("f", "a (2).txt"), DownloadRules.Unique(Path.Combine("f", "a.txt"), taken.Contains), "a free name");
            Equal(Path.Combine("f", "b.txt"), DownloadRules.Unique(Path.Combine("f", "b.txt"), taken.Contains), "the name itself when free");
            var real = TempFolder();
            try
            {
                File.WriteAllText(Path.Combine(real, "c.zip" + DownloadRules.PartExtension), "arriving");
                Equal(Path.Combine(real, "c (1).zip"), DownloadRules.Unique(Path.Combine(real, "c.zip")), "a file still arriving counts as taken");
            }
            finally { Directory.Delete(real, true); }

            Equal("12 seconds left", DownloadRules.TimeLeft(12_000_000, 1_000_000), "seconds");
            Equal("3 minutes left", DownloadRules.TimeLeft(150_000_000, 1_000_000), "minutes");
            Equal("about 2 hours left", DownloadRules.TimeLeft(7_000_000_000, 1_000_000), "hours");
            Equal(string.Empty, DownloadRules.TimeLeft(100, 0), "no speed yet");
        }

        private static void Pieces()
        {
            var plan = DownloadRules.Plan(100L * 1024 * 1024 + 7, 6);
            Equal(6, plan.Count, "six pieces for a big file");
            Equal(0L, plan[0].Start, "starts at the beginning");
            Equal(100L * 1024 * 1024 + 6, plan.Last().End, "ends at the last byte");
            for (var i = 1; i < plan.Count; i++) Equal(plan[i - 1].End + 1, plan[i].Start, "no gaps or overlaps");
            Equal(2, DownloadRules.Plan(9L * 1024 * 1024, 6).Count, "pieces are at least 4 MB");
            Equal(1, DownloadRules.Plan(1000, 6).Count, "small files in one piece");
            Equal(0, DownloadRules.Plan(0, 6).Count, "nothing for an empty file");
            Equal(12345L, DownloadRules.TotalFromContentRange("bytes 0-0/12345"), "the size from a part answer");
            Equal(-1L, DownloadRules.TotalFromContentRange("bytes 0-0/*"), "unknown size");
        }

        private static (FastDownloader, DownloadRecord) Downloader(TestServer server, string path, string folder, string name)
        {
            var record = new DownloadRecord { Url = server.Url(path), File = Path.Combine(folder, name), Referrer = server.Url("page.html") };
            var d = new FastDownloader(record, FastDownloader.CreateClient())
            {
                Cookies = u => Task.FromResult(u.AbsolutePath.StartsWith("/other") ? "other=2" : "session=abc"),
                UserAgent = "KUDUBROWSE test",
                RetryDelay = TimeSpan.FromMilliseconds(20)
            };
            return (d, record);
        }

        private static void InPieces()
        {
            var folder = TempFolder();
            using (var server = new TestServer())
            {
                try
                {
                    var body = TestServer.Made(30 * 1024 * 1024 + 123);
                    server.Add("big.bin", new TestServer.Item { Body = body });
                    var (d, r) = Downloader(server, "big.bin", folder, "big.bin");
                    d.Run(CancellationToken.None).GetAwaiter().GetResult();
                    Equal(DownloadState.Done, r.State, "finished");
                    Equal(true, r.InPieces, "fetched in pieces");
                    Equal(FastDownloader.Connections, r.Pieces.Count, "six at once");
                    Equal(true, File.ReadAllBytes(r.File).SequenceEqual(body), "the file is exactly the site's");
                    Equal(false, File.Exists(r.PartFile), "no part file left");
                    Equal((long)body.Length, r.Received, "all of it counted");
                    Equal(true, server.CookiesSeen.All(c => c.EndsWith("session=abc")) && server.CookiesSeen.Count >= 7, "the site's cookies sent with each piece");
                }
                finally { Directory.Delete(folder, true); }
            }
        }

        private static void PauseAndResume()
        {
            var folder = TempFolder();
            using (var server = new TestServer())
            {
                try
                {
                    var body = TestServer.Made(24 * 1024 * 1024, 3);
                    var item = server.Add("slow.bin", new TestServer.Item { Body = body, SlowMs = 4 });
                    var (d, r) = Downloader(server, "slow.bin", folder, "slow.bin");
                    using (var pause = new CancellationTokenSource())
                    {
                        d.Progress += () => { if (r.Received > 3 * 1024 * 1024) pause.Cancel(); };
                        var paused = false;
                        try { d.Run(pause.Token).GetAwaiter().GetResult(); }
                        catch (OperationCanceledException) { paused = true; }
                        Equal(true, paused, "paused part way");
                    }
                    var before = r.Received;
                    Equal(true, before > 0 && before < body.Length, "part of it arrived: " + before);
                    Equal(true, File.Exists(r.PartFile), "the part is kept");

                    // As after a restart: the list is read back, then the download carries on.
                    var list = Path.Combine(folder, "downloads.json");
                    r.State = DownloadState.Running;
                    DownloadList.Save(list, new[] { r });
                    var again = DownloadList.Load(list).Single();
                    Equal(DownloadState.Paused, again.State, "comes back paused");
                    item.SlowMs = 0;
                    var asked = server.RangesAsked.Count;
                    var d2 = new FastDownloader(again, FastDownloader.CreateClient());
                    d2.Run(CancellationToken.None).GetAwaiter().GetResult();
                    Equal(true, File.ReadAllBytes(again.File).SequenceEqual(body), "the file is whole and right");
                    var resumed = server.RangesAsked.Skip(asked).Where(x => x != "bytes=0-0").ToList();
                    Equal(true, resumed.All(x => !x.StartsWith("bytes=0-") || x == "bytes=0-0"), "carried on, didn't start again: " + string.Join(", ", resumed));
                }
                finally { Directory.Delete(folder, true); }
            }
        }

        private static void DroppedAndChanged()
        {
            var folder = TempFolder();
            using (var server = new TestServer())
            {
                try
                {
                    var body = TestServer.Made(22 * 1024 * 1024, 5);
                    server.Add("flaky.bin", new TestServer.Item { Body = body, Drops = 3, DropAfter = 1024 * 1024 });
                    var (d, r) = Downloader(server, "flaky.bin", folder, "flaky.bin");
                    d.Run(CancellationToken.None).GetAwaiter().GetResult();
                    Equal(true, File.ReadAllBytes(r.File).SequenceEqual(body), "whole despite dropped connections");

                    // Paused, then the file changes on the site: it starts again instead of mixing old and new.
                    var item = server.Add("changing.bin", new TestServer.Item { Body = body, SlowMs = 4 });
                    var (d2, r2) = Downloader(server, "changing.bin", folder, "changing.bin");
                    using (var pause = new CancellationTokenSource())
                    {
                        d2.Progress += () => { if (r2.Received > 2 * 1024 * 1024) pause.Cancel(); };
                        try { d2.Run(pause.Token).GetAwaiter().GetResult(); }
                        catch (OperationCanceledException) { }
                    }
                    var newer = TestServer.Made(21 * 1024 * 1024, 9);
                    server.Add("changing.bin", new TestServer.Item { Body = newer, Tag = "\"v2\"" });
                    new FastDownloader(r2, FastDownloader.CreateClient()).Run(CancellationToken.None).GetAwaiter().GetResult();
                    Equal(true, File.ReadAllBytes(r2.File).SequenceEqual(newer), "the new file, whole");

                    // A site that keeps failing: a plain reason.
                    server.Add("broken.bin", new TestServer.Item { Body = body, Drops = 1000, DropAfter = 100 });
                    var (d3, r3) = Downloader(server, "broken.bin", folder, "broken.bin");
                    var reason = string.Empty;
                    try { d3.Run(CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (DownloadException ex) { reason = ex.Message; }
                    Equal("The connection kept dropping", reason, "gives up with a reason");
                }
                finally { Directory.Delete(folder, true); }
            }
        }

        private static void OtherSites()
        {
            var folder = TempFolder();
            using (var server = new TestServer())
            {
                try
                {
                    var body = TestServer.Made(3 * 1024 * 1024, 11);
                    server.Add("whole.bin", new TestServer.Item { Body = body, Parts = false, Drops = 2, DropAfter = 500000 });
                    var (d, r) = Downloader(server, "whole.bin", folder, "whole.bin");
                    d.Run(CancellationToken.None).GetAwaiter().GetResult();
                    Equal(false, r.InPieces, "in one go");
                    Equal(true, File.ReadAllBytes(r.File).SequenceEqual(body), "whole, after starting again");

                    server.Add("go", new TestServer.Item { RedirectTo = server.Url("other/file.bin") });
                    server.Add("other/file.bin", new TestServer.Item { Body = body });
                    var (d2, r2) = Downloader(server, "go", folder, "moved.bin");
                    d2.Run(CancellationToken.None).GetAwaiter().GetResult();
                    Equal(true, File.ReadAllBytes(r2.File).SequenceEqual(body), "followed the redirect");
                    Equal(true, server.CookiesSeen.Any(c => c.StartsWith("/other/file.bin other=2")), "each address gets its own cookies");

                    File.WriteAllText(Path.Combine(folder, "taken.bin"), "already here");
                    var (d3, r3) = Downloader(server, "whole.bin", folder, "taken.bin");
                    d3.Run(CancellationToken.None).GetAwaiter().GetResult();
                    Equal(Path.Combine(folder, "taken (1).bin"), r3.File, "an existing file is never replaced");
                    Equal("already here", File.ReadAllText(Path.Combine(folder, "taken.bin")), "it's untouched");

                    var (d4, _) = Downloader(server, "missing.bin", folder, "missing.bin");
                    var reason = string.Empty;
                    try { d4.Run(CancellationToken.None).GetAwaiter().GetResult(); }
                    catch (DownloadException ex) { reason = ex.Message; }
                    Equal("The file isn't on the site any more", reason, "a missing file in plain words");
                }
                finally { Directory.Delete(folder, true); }
            }
        }

        private static void ListKept()
        {
            var folder = TempFolder();
            try
            {
                var file = Path.Combine(folder, "downloads.json");
                var done = new DownloadRecord { Url = "https://a.example/x.zip", File = @"C:\Downloads\x.zip", Total = 10, Received = 10, State = DownloadState.Done };
                var engine = new DownloadRecord { Url = "https://a.example/y.zip", File = @"C:\Downloads\y.zip", State = DownloadState.Running };
                var secret = new DownloadRecord { Url = "https://b.example/z.zip", File = @"C:\Downloads\z.zip", State = DownloadState.Done, Private = true };
                DownloadList.Save(file, new[] { done, engine, secret });
                var back = DownloadList.Load(file);
                Equal(2, back.Count, "private windows' downloads aren't kept");
                Equal(DownloadState.Done, back[0].State, "finished");
                Equal(DownloadState.Failed, back[1].State, "the engine's unfinished download can be tried again");
                Equal("Stopped when the browser was closed", back[1].Error, "with a reason");
                Equal(0, DownloadList.Load(Path.Combine(folder, "none.json")).Count, "no file: empty");
                File.WriteAllText(file, "{ not json");
                Equal(0, DownloadList.Load(file).Count, "a damaged file: empty");
            }
            finally { Directory.Delete(folder, true); }
        }
    }
}
