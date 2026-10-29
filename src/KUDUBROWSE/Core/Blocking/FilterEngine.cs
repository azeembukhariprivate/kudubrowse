using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kudubrowse.Core.Blocking
{
    /// <summary>What kind of thing a page is asking for. A rule can be limited to some kinds.</summary>
    [Flags]
    public enum RequestKind
    {
        None = 0,
        Script = 1,
        Image = 2,
        Stylesheet = 4,
        Xhr = 8,
        Subdocument = 16,
        Media = 32,
        Font = 64,
        Websocket = 128,
        Ping = 256,
        Other = 512,
        All = 1023
    }

    /// <summary>
    /// Reads block lists written in the common ad-blocker format (EasyList, EasyPrivacy, the cookie-banner list) and
    /// answers two questions: should this request be blocked, and which parts of this page should be hidden.
    /// Rules it doesn't understand are left out, so it never blocks something a list didn't mean it to.
    /// </summary>
    public sealed class FilterEngine
    {
        private readonly Dictionary<string, List<NetworkRule>> _blockByToken = new Dictionary<string, List<NetworkRule>>();
        private readonly Dictionary<string, List<NetworkRule>> _allowByToken = new Dictionary<string, List<NetworkRule>>();
        private readonly List<NetworkRule> _blockNoToken = new List<NetworkRule>();
        private readonly List<NetworkRule> _allowNoToken = new List<NetworkRule>();

        private readonly List<string> _genericHide = new List<string>();
        private readonly Dictionary<string, List<string>> _hideByDomain = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, HashSet<string>> _unhideByDomain = new Dictionary<string, HashSet<string>>();
        private readonly HashSet<string> _noGenericHide = new HashSet<string>(); // $generichide sites
        private readonly HashSet<string> _noHide = new HashSet<string>(); // $elemhide sites

        public int NetworkRules { get; private set; }
        public int HidingRules { get; private set; }
        public int Skipped { get; private set; }

        // ================================================================ reading lists

        public static FilterEngine FromText(params string[] lists)
        {
            var e = new FilterEngine();
            foreach (var list in lists) e.AddList(list);
            return e;
        }

        public void AddList(string text)
        {
            using (var reader = new StringReader(text ?? string.Empty))
            {
                string line;
                while ((line = reader.ReadLine()) != null) AddRule(line);
            }
        }

        public void AddRule(string line)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] == '!' || line[0] == '[') return;
            var hash = line.IndexOf('#');
            if (hash >= 0 && TryAddHiding(line, hash)) return;
            if (line.Contains("##") || line.Contains("#@#") || line.Contains("#?#") || line.Contains("#$#") || line.Contains("#%#"))
            {
                Skipped++;
                return;
            }
            var rule = NetworkRule.Parse(line, out var special);
            if (special != null)
            {
                // Exceptions that switch off hiding on a site rather than allowing a request.
                foreach (var d in special.Value.Domains)
                    (special.Value.Generic ? _noGenericHide : _noHide).Add(d);
                return;
            }
            if (rule == null)
            {
                Skipped++;
                return;
            }
            NetworkRules++;
            var token = rule.Token;
            if (token == null) (rule.Exception ? _allowNoToken : _blockNoToken).Add(rule);
            else
            {
                var map = rule.Exception ? _allowByToken : _blockByToken;
                if (!map.TryGetValue(token, out var list)) map[token] = list = new List<NetworkRule>();
                list.Add(rule);
            }
        }

        private bool TryAddHiding(string line, int hash)
        {
            string separator;
            if (string.CompareOrdinal(line, hash, "##", 0, 2) == 0) separator = "##";
            else if (string.CompareOrdinal(line, hash, "#@#", 0, 3) == 0) separator = "#@#";
            else return false;
            var domains = line.Substring(0, hash);
            var selector = line.Substring(hash + separator.Length).Trim();
            // Extended selectors and scripts need an ad blocker's own code; they're left out.
            if (selector.Length == 0 || selector.StartsWith("+js(") || selector.Contains(":-abp-") || selector.Contains(":has-text(") ||
                selector.Contains(":xpath(") || selector.Contains(":style(") || selector.Contains(":remove(") || selector.Contains(":matches-") ||
                selector.Contains(":upward(") || selector.Contains(":min-text-length(") || selector.Contains(":watch-attr(") || selector.StartsWith("^"))
            {
                Skipped++;
                return true;
            }
            var include = new List<string>();
            var exclude = new List<string>();
            foreach (var d in domains.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = d.Trim().ToLowerInvariant();
                if (name.StartsWith("~")) exclude.Add(name.Substring(1));
                else include.Add(name);
            }
            HidingRules++;
            if (separator == "#@#")
            {
                foreach (var d in include.DefaultIfEmpty("*"))
                {
                    if (!_unhideByDomain.TryGetValue(d, out var set)) _unhideByDomain[d] = set = new HashSet<string>();
                    set.Add(selector);
                }
                return true;
            }
            if (include.Count == 0)
            {
                _genericHide.Add(selector);
                // "~site##.ad" means "everywhere but there": treated as an exception on those sites.
                foreach (var d in exclude)
                {
                    if (!_unhideByDomain.TryGetValue(d, out var set)) _unhideByDomain[d] = set = new HashSet<string>();
                    set.Add(selector);
                }
                return true;
            }
            foreach (var d in include)
            {
                if (!_hideByDomain.TryGetValue(d, out var list)) _hideByDomain[d] = list = new List<string>();
                list.Add(selector);
            }
            return true;
        }

        // ================================================================ requests

        /// <summary>Should a request for <paramref name="url"/> made by a page on <paramref name="pageHost"/> be blocked?</summary>
        public bool ShouldBlock(string url, string pageHost, RequestKind kind)
        {
            if (string.IsNullOrEmpty(url)) return false;
            var req = Request.Make(url, pageHost, kind);
            if (req == null) return false;
            var blocked = Find(req, _blockByToken, _blockNoToken, out var block);
            if (!blocked) return false;
            if (block.Important) return true;
            return !Find(req, _allowByToken, _allowNoToken, out _);
        }

        private static bool Find(Request req, Dictionary<string, List<NetworkRule>> byToken, List<NetworkRule> noToken, out NetworkRule found)
        {
            foreach (var token in req.Tokens)
            {
                if (!byToken.TryGetValue(token, out var list)) continue;
                foreach (var r in list)
                {
                    if (r.Matches(req))
                    {
                        found = r;
                        return true;
                    }
                }
            }
            foreach (var r in noToken)
            {
                if (r.Matches(req))
                {
                    found = r;
                    return true;
                }
            }
            found = null;
            return false;
        }

        // ================================================================ hiding parts of pages

        /// <summary>The selectors to hide on every page (before exceptions for particular sites).</summary>
        public IReadOnlyList<string> GenericSelectors => _genericHide;

        /// <summary>Hiding for one site: the selectors just for it, and the general ones it switches off.</summary>
        public SiteHiding HidingFor(string host)
        {
            var result = new SiteHiding();
            host = (host ?? string.Empty).ToLowerInvariant();
            foreach (var d in Suffixes(host))
            {
                if (_noHide.Contains(d)) result.NoHiding = true;
                if (_noGenericHide.Contains(d)) result.NoGeneric = true;
            }
            if (result.NoHiding) return result;
            var unhide = new HashSet<string>();
            foreach (var d in Suffixes(host).Concat(new[] { "*" }))
                if (_unhideByDomain.TryGetValue(d, out var set)) unhide.UnionWith(set);
            foreach (var d in Suffixes(host))
                if (_hideByDomain.TryGetValue(d, out var list))
                    result.Specific.AddRange(list.Where(s => !unhide.Contains(s)));
            result.Unhidden.AddRange(unhide.Where(s => _genericSet.Value.Contains(s)));
            return result;
        }

        private Lazy<HashSet<string>> _genericSet;

        public FilterEngine()
        {
            _genericSet = new Lazy<HashSet<string>>(() => new HashSet<string>(_genericHide));
        }

        /// <summary>The host and every parent domain: a.b.example.com, b.example.com, example.com, com.</summary>
        public static IEnumerable<string> Suffixes(string host)
        {
            var h = host ?? string.Empty;
            while (h.Length > 0)
            {
                yield return h;
                var dot = h.IndexOf('.');
                if (dot < 0) break;
                h = h.Substring(dot + 1);
            }
        }

        /// <summary>Builds CSS that hides these selectors, in groups (one bad selector only spoils its own group).</summary>
        public static string Css(IEnumerable<string> selectors)
        {
            var sb = new StringBuilder();
            foreach (var group in selectors.Select((s, i) => (s, i)).GroupBy(x => x.i / 40))
            {
                sb.Append(string.Join(",", group.Select(x => x.s)));
                sb.Append("{display:none!important}\n");
            }
            return sb.ToString();
        }
    }

    public sealed class SiteHiding
    {
        public List<string> Specific { get; } = new List<string>();
        /// <summary>General selectors that this site switches off.</summary>
        public List<string> Unhidden { get; } = new List<string>();
        public bool NoGeneric { get; set; }
        public bool NoHiding { get; set; }
    }

    internal sealed class Request
    {
        public string Url; // lower case
        public string RawUrl;
        public string Host;
        public int HostStart;
        public string PageHost;
        public bool ThirdParty;
        public RequestKind Kind;
        public List<string> Tokens;

        public static Request Make(string url, string pageHost, RequestKind kind)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "ws" && uri.Scheme != "wss") return null;
            var lower = url.ToLowerInvariant();
            var host = uri.Host.ToLowerInvariant();
            var r = new Request
            {
                Url = lower,
                RawUrl = url,
                Host = host,
                HostStart = lower.IndexOf(host, StringComparison.Ordinal),
                PageHost = (pageHost ?? string.Empty).ToLowerInvariant(),
                Kind = kind,
                Tokens = new List<string>()
            };
            r.ThirdParty = string.IsNullOrEmpty(r.PageHost) || Domains.Registrable(host) != Domains.Registrable(r.PageHost);
            var seen = new HashSet<string>();
            var start = -1;
            for (var i = 0; i <= lower.Length; i++)
            {
                var token = i < lower.Length && NetworkRule.IsTokenChar(lower[i]);
                if (token && start < 0) start = i;
                else if (!token && start >= 0)
                {
                    if (i - start >= 2)
                    {
                        var t = lower.Substring(start, i - start);
                        if (seen.Add(t)) r.Tokens.Add(t);
                    }
                    start = -1;
                }
            }
            return r;
        }
    }

    /// <summary>Works out a site's own domain (example.co.uk for news.example.co.uk), to tell first and third parties apart.</summary>
    public static class Domains
    {
        // Common two-part endings where the site's name is one level further in.
        private static readonly HashSet<string> TwoPart = new HashSet<string>(new[]
        {
            "co.uk", "org.uk", "ac.uk", "gov.uk", "me.uk", "net.uk", "ltd.uk", "plc.uk", "co.in", "net.in", "org.in", "gov.in", "ac.in", "edu.in", "res.in", "firm.in", "gen.in", "ind.in",
            "com.au", "net.au", "org.au", "edu.au", "gov.au", "co.nz", "org.nz", "govt.nz", "co.za", "org.za", "gov.za", "com.br", "net.br", "org.br", "gov.br",
            "co.jp", "ne.jp", "or.jp", "ac.jp", "go.jp", "co.kr", "or.kr", "com.cn", "net.cn", "org.cn", "gov.cn", "com.hk", "com.tw", "com.sg", "com.my", "com.pk",
            "com.mx", "com.ar", "com.tr", "com.sa", "com.eg", "com.ng", "co.ke", "co.il", "co.id", "or.id", "com.ph", "com.vn", "co.th", "in.th", "com.ua", "com.pl",
            "github.io", "blogspot.com", "herokuapp.com", "azurewebsites.net", "cloudfront.net", "appspot.com", "netlify.app", "vercel.app", "pages.dev", "web.app",
            "firebaseapp.com", "s3.amazonaws.com", "wordpress.com", "tumblr.com"
        });

        public static string Registrable(string host)
        {
            if (string.IsNullOrEmpty(host)) return string.Empty;
            host = host.ToLowerInvariant().TrimEnd('.');
            if (host.Length > 0 && char.IsDigit(host[host.Length - 1]) && host.Count(c => c == '.') == 3) return host; // an IP address
            var parts = host.Split('.');
            if (parts.Length <= 2) return host;
            var lastTwo = parts[parts.Length - 2] + "." + parts[parts.Length - 1];
            var keep = TwoPart.Contains(lastTwo) ? 3 : 2;
            if (parts.Length <= keep) return host;
            return string.Join(".", parts.Skip(parts.Length - keep));
        }
    }
}
