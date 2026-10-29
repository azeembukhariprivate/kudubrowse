using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Kudubrowse.Core.Blocking;

namespace Kudubrowse.Tests
{
    public static class BlockingTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("Blocking: host rules block a site and its subdomains, not look-alikes", HostRules);
            test("Blocking: wildcards, separators and anchors", Patterns);
            test("Blocking: third-party, kinds of request and site options", Options);
            test("Blocking: allow rules and important rules", Exceptions);
            test("Blocking: rules it can't follow are left out", Unsupported);
            test("Blocking: a site's own domain (first or third party)", RegistrableDomains);
            test("Hiding: general, per-site and switched-off selectors", Hiding);
            test("Real lists: EasyList and EasyPrivacy load quickly and block well-known ads and trackers", RealLists);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!Equals(expected, actual)) throw new Exception($"{what}: expected \"{expected}\", got \"{actual}\"");
        }

        private static bool Block(FilterEngine e, string url, string page = "example.com", RequestKind kind = RequestKind.Script) => e.ShouldBlock(url, page, kind);

        private static void HostRules()
        {
            var e = FilterEngine.FromText("||ads.example.net^\n||tracker.io^");
            Equal(true, Block(e, "https://ads.example.net/x.js"), "the host");
            Equal(true, Block(e, "https://cdn.ads.example.net/x.js"), "a subdomain");
            Equal(true, Block(e, "https://tracker.io"), "no path");
            Equal(true, Block(e, "https://tracker.io:8443/p"), "with a port");
            Equal(false, Block(e, "https://badads.example.net/x.js"), "a look-alike host");
            Equal(false, Block(e, "https://tracker.io.example.com/"), "the name inside another host");
            Equal(false, Block(e, "https://example.com/?u=ads.example.net"), "in the query only");
            Equal(false, Block(e, "data:text/plain,ads.example.net"), "not a web address");
        }

        private static void Patterns()
        {
            var e = FilterEngine.FromText("/banner/*/img^\n|https://start.example/\n.swf|\n&ad_type=\n-ad-300x250.");
            Equal(true, Block(e, "https://x.com/banner/123/img?a"), "* and ^ before ?");
            Equal(true, Block(e, "https://x.com/banner/123/img"), "^ at the end");
            Equal(false, Block(e, "https://x.com/banner/123/imgs"), "^ is not a letter");
            Equal(true, Block(e, "https://start.example/a"), "start anchor");
            Equal(false, Block(e, "https://x.com/?r=https://start.example/"), "start anchor elsewhere");
            Equal(true, Block(e, "https://x.com/movie.swf"), "end anchor");
            Equal(false, Block(e, "https://x.com/movie.swf?x"), "end anchor not at the end");
            Equal(true, Block(e, "https://x.com/page?x=1&ad_type=video"), "plain text");
            Equal(true, Block(e, "https://x.com/img/BIG-AD-300x250.png"), "ignores letter case");
        }

        private static void Options()
        {
            var e = FilterEngine.FromText("||cdn.widgets.com^$third-party\n||pics.net/ads/$image\n||everywhere.org^$~script\n||only.org^$domain=news.com|~sport.news.com");
            Equal(true, Block(e, "https://cdn.widgets.com/w.js", "blog.org"), "third-party");
            Equal(false, Block(e, "https://cdn.widgets.com/w.js", "www.widgets.com"), "same site is first-party");
            Equal(true, Block(e, "https://pics.net/ads/1.png", "a.com", RequestKind.Image), "image");
            Equal(false, Block(e, "https://pics.net/ads/1.js", "a.com", RequestKind.Script), "not an image");
            Equal(false, Block(e, "https://everywhere.org/a.js", "a.com", RequestKind.Script), "~script");
            Equal(true, Block(e, "https://everywhere.org/a.png", "a.com", RequestKind.Image), "~script allows others");
            Equal(true, Block(e, "https://only.org/x", "www.news.com"), "on the named site");
            Equal(false, Block(e, "https://only.org/x", "sport.news.com"), "not on the excluded part");
            Equal(false, Block(e, "https://only.org/x", "other.com"), "not on other sites");
        }

        private static void Exceptions()
        {
            var e = FilterEngine.FromText("||ads.net^\n@@||ads.net/needed.js\n||must.net^$important\n@@||must.net^");
            Equal(true, Block(e, "https://ads.net/x.js"), "blocked");
            Equal(false, Block(e, "https://ads.net/needed.js"), "allowed by @@");
            Equal(true, Block(e, "https://must.net/x"), "important wins over @@");
        }

        private static void Unsupported()
        {
            var e = FilterEngine.FromText("||a.com^$popup\n||b.com^$csp=script-src 'none'\n||c.com^$redirect=noop.js\n/ads[0-9]+/\n||d.com^$document\n$domain=e.com\n*\n##+js(nobab)\nexample.com#?#div:-abp-has(.ad)\n");
            foreach (var host in new[] { "a.com", "b.com", "c.com", "d.com", "ads123.com" })
                Equal(false, Block(e, "https://" + host + "/ads123/x"), host);
            Equal(true, Block(e, "https://anything.com/x", "e.com"), "a rule naming only a site covers everything that site asks for");
            Equal(false, Block(e, "https://anything.com/x", "f.com"), "but nothing elsewhere ('*' alone is left out)");
            Equal(1, e.NetworkRules, "only that rule was taken");
            Equal(0, e.GenericSelectors.Count, "no hiding taken");
        }

        private static void RegistrableDomains()
        {
            Equal("example.co.uk", Domains.Registrable("news.example.co.uk"), "two-part ending");
            Equal("example.com", Domains.Registrable("a.b.example.com"), "subdomains");
            Equal("example.com", Domains.Registrable("example.com"), "itself");
            Equal("192.168.1.1", Domains.Registrable("192.168.1.1"), "an IP address");
            Equal("me.github.io", Domains.Registrable("me.github.io"), "a shared host");
        }

        private static void Hiding()
        {
            var e = FilterEngine.FromText("##.ad-box\n###sponsor\nnews.com##.promo\nnews.com#@#.ad-box\n~safe.org##.banner\n@@||quiet.net^$generichide\n@@||bare.net^$elemhide\nbare.net##.x");
            Equal(".ad-box|#sponsor|.banner", string.Join("|", e.GenericSelectors), "general selectors");
            var news = e.HidingFor("www.news.com");
            Equal(".promo", string.Join("|", news.Specific), "site selector");
            Equal(".ad-box", string.Join("|", news.Unhidden), "site switches one off");
            Equal(".banner", string.Join("|", e.HidingFor("safe.org").Unhidden), "~site means not there");
            Equal(true, e.HidingFor("www.quiet.net").NoGeneric, "generichide");
            Equal(true, e.HidingFor("bare.net").NoHiding, "elemhide");
            Equal(0, e.HidingFor("other.com").Specific.Count, "nothing special elsewhere");
            var css = FilterEngine.Css(Enumerable.Range(0, 85).Select(i => ".a" + i));
            Equal(3, css.Split('\n').Count(l => l.EndsWith("{display:none!important}")), "groups of 40");
        }

        /// <summary>Uses the real lists when they've been downloaded (the build does that) into src/KUDUBROWSE/Lists.</summary>
        private static void RealLists()
        {
            var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "KUDUBROWSE", "Lists"));
            var easylist = Path.Combine(folder, "easylist.txt");
            var easyprivacy = Path.Combine(folder, "easyprivacy.txt");
            if (!File.Exists(easylist) || !File.Exists(easyprivacy))
            {
                Console.WriteLine("      (the real lists aren't downloaded here; skipped)");
                return;
            }
            var watch = Stopwatch.StartNew();
            var e = FilterEngine.FromText(File.ReadAllText(easylist), File.ReadAllText(easyprivacy));
            var loadMs = watch.ElapsedMilliseconds;
            Console.WriteLine($"      {e.NetworkRules} blocking rules, {e.HidingRules} hiding rules, {e.Skipped} left out; read in {loadMs} ms");
            if (e.NetworkRules < 20000) throw new Exception("too few rules: " + e.NetworkRules);
            if (loadMs > 15000) throw new Exception("too slow to read: " + loadMs + " ms");

            foreach (var url in new[]
            {
                "https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js",
                "https://www.google-analytics.com/analytics.js",
                "https://securepubads.g.doubleclick.net/tag/js/gpt.js",
                "https://connect.facebook.net/en_US/fbevents.js",
                "https://static.hotjar.com/c/hotjar-123.js",
            })
                Equal(true, e.ShouldBlock(url, "news.example.com", RequestKind.Script), url);
            foreach (var url in new[]
            {
                "https://www.wikipedia.org/static/app.js",
                "https://ajax.googleapis.com/ajax/libs/jquery/3.7.1/jquery.min.js",
                "https://cdn.jsdelivr.net/npm/react@18.3.1/umd/react.production.min.js",
                "https://news.example.com/assets/main.css",
            })
                Equal(false, e.ShouldBlock(url, "news.example.com", url.EndsWith(".css") ? RequestKind.Stylesheet : RequestKind.Script), url);

            // Speed: checking an address has to be quick, as every picture and script on a page is checked.
            watch.Restart();
            var n = 0;
            for (var i = 0; i < 20000; i++)
                if (e.ShouldBlock("https://cdn.site" + (i % 50) + ".com/assets/img/photo-" + i + ".jpg?w=640&h=480", "site3.com", RequestKind.Image)) n++;
            var per = watch.Elapsed.TotalMilliseconds / 20000;
            Console.WriteLine($"      {per * 1000:0} microseconds per address");
            if (per > 0.5) throw new Exception($"too slow: {per:0.000} ms per address");
            if (e.GenericSelectors.Count < 1000) throw new Exception("too few general hiding rules");
        }
    }
}
