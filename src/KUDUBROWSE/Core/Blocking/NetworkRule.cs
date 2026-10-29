using System;
using System.Collections.Generic;
using System.Linq;

namespace Kudubrowse.Core.Blocking
{
    /// <summary>
    /// One blocking (or allowing, "@@") rule for web addresses, such as "||ads.example.com^$third-party" or
    /// "/banner/*/img^". * matches anything, ^ matches a separator (anything but a letter, digit or _-.%, or the end),
    /// || means "at the start of the host name or one of its parent domains", | anchors the start or the end.
    /// </summary>
    public sealed class NetworkRule
    {
        public bool Exception { get; private set; }
        public bool Important { get; private set; }
        public string Token { get; private set; }
        public string Text { get; private set; }

        private string _pattern; // lower case, without anchors
        private bool _hostAnchor, _startAnchor, _endAnchor;
        private RequestKind _kinds = RequestKind.All;
        private bool? _thirdParty;
        private string[] _include, _exclude;

        internal readonly struct HidingSwitch
        {
            public HidingSwitch(bool generic, string[] domains)
            {
                Generic = generic;
                Domains = domains;
            }

            public bool Generic { get; }
            public string[] Domains { get; }
        }

        public static bool IsTokenChar(char c) => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '%';

        private static bool IsSeparator(char c) => !(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '%');

        /// <summary>Reads a rule; null when it's one this browser leaves out.</summary>
        internal static NetworkRule Parse(string line, out HidingSwitch? special)
        {
            special = null;
            var r = new NetworkRule { Text = line };
            var s = line;
            if (s.StartsWith("@@"))
            {
                r.Exception = true;
                s = s.Substring(2);
            }
            // Regular-expression rules are rare and slow; they're left out.
            if (s.StartsWith("/") && s.EndsWith("/") && s.Length > 2) return null;

            var dollar = s.LastIndexOf('$');
            if (dollar >= 0 && dollar < s.Length - 1 && !s.Substring(dollar + 1).Contains('/'))
            {
                var options = s.Substring(dollar + 1);
                s = s.Substring(0, dollar);
                var include = new List<string>();
                var exclude = new List<string>();
                var positive = RequestKind.None;
                var negative = RequestKind.None;
                bool generichide = false, elemhide = false;
                foreach (var raw in options.Split(','))
                {
                    var opt = raw.Trim().ToLowerInvariant();
                    var not = opt.StartsWith("~");
                    var name = not ? opt.Substring(1) : opt;
                    var kind = KindOf(name);
                    if (kind != RequestKind.None)
                    {
                        if (not) negative |= kind;
                        else positive |= kind;
                        continue;
                    }
                    switch (name)
                    {
                        case "third-party":
                        case "3p":
                            r._thirdParty = !not;
                            break;
                        case "first-party":
                        case "1p":
                            r._thirdParty = not;
                            break;
                        case "important":
                            r.Important = true;
                            break;
                        case "match-case":
                            return null; // rare; matching is done in lower case
                        case "generichide":
                        case "ghide":
                            generichide = true;
                            break;
                        case "elemhide":
                        case "ehide":
                            elemhide = true;
                            break;
                        default:
                            if (name.StartsWith("domain=") || name.StartsWith("from="))
                            {
                                foreach (var d in name.Substring(name.IndexOf('=') + 1).Split('|'))
                                {
                                    if (d.StartsWith("~")) exclude.Add(d.Substring(1));
                                    else if (d.Length > 0) include.Add(d);
                                }
                                break;
                            }
                            // Anything else (popup, document, csp, redirect, removeparam, ...) changes what the rule means
                            // in ways this browser doesn't do, so the rule is left out rather than half-followed.
                            return null;
                    }
                }
                if (generichide || elemhide)
                {
                    // "@@||site^$generichide": no general hiding on that site. Needs a plain site rule.
                    if (!r.Exception) return null;
                    var host = s.StartsWith("||") ? s.Substring(2).TrimEnd('^', '/') : null;
                    var domains = include.Count > 0 ? include.ToArray() : host != null && host.All(c => IsTokenChar(c) || c == '.' || c == '-') ? new[] { host } : null;
                    if (domains == null) return null;
                    special = new HidingSwitch(generichide && !elemhide, domains);
                    return null;
                }
                if (positive != RequestKind.None) r._kinds = positive;
                else if (negative != RequestKind.None) r._kinds = RequestKind.All & ~negative;
                if (include.Count > 0) r._include = include.ToArray();
                if (exclude.Count > 0) r._exclude = exclude.ToArray();
            }

            s = s.ToLowerInvariant();
            if (s.StartsWith("||"))
            {
                r._hostAnchor = true;
                s = s.Substring(2);
            }
            else if (s.StartsWith("|"))
            {
                r._startAnchor = true;
                s = s.Substring(1);
            }
            if (s.EndsWith("|"))
            {
                r._endAnchor = true;
                s = s.Substring(0, s.Length - 1);
            }
            // Leading and trailing * mean nothing.
            s = s.Trim('*');
            if (s.Length == 0)
            {
                // A rule matching every address is only allowed when it's limited to some sites.
                if (r._include == null) return null;
            }
            r._pattern = s;
            r.Token = PickToken(s, r._hostAnchor || r._startAnchor, r._endAnchor);
            return r;
        }

        private static RequestKind KindOf(string name)
        {
            switch (name)
            {
                case "script": return RequestKind.Script;
                case "image": return RequestKind.Image;
                case "stylesheet": case "css": return RequestKind.Stylesheet;
                case "xmlhttprequest": case "xhr": return RequestKind.Xhr;
                case "subdocument": case "frame": return RequestKind.Subdocument;
                case "media": return RequestKind.Media;
                case "font": return RequestKind.Font;
                case "websocket": return RequestKind.Websocket;
                case "ping": case "beacon": return RequestKind.Ping;
                case "other": case "object": case "object-subrequest": return RequestKind.Other;
                default: return RequestKind.None;
            }
        }

        /// <summary>
        /// The longest run of letters and digits that must appear whole in any address the rule matches, used to find
        /// the rule quickly. A run touching a * (or an unanchored end) might be part of a longer word, so it doesn't count.
        /// </summary>
        private static string PickToken(string p, bool startAnchored, bool endAnchored)
        {
            string best = null;
            var i = 0;
            while (i < p.Length)
            {
                if (!IsTokenChar(p[i]))
                {
                    i++;
                    continue;
                }
                var start = i;
                while (i < p.Length && IsTokenChar(p[i])) i++;
                var before = start == 0 ? (startAnchored ? ' ' : '*') : p[start - 1];
                var after = i == p.Length ? (endAnchored ? ' ' : '*') : p[i];
                if (before == '*' || after == '*') continue;
                var t = p.Substring(start, i - start);
                if (t.Length >= 2 && (best == null || t.Length > best.Length)) best = t;
            }
            return best;
        }

        internal bool Matches(Request req)
        {
            if ((req.Kind & _kinds) == 0) return false;
            if (_thirdParty.HasValue && _thirdParty.Value != req.ThirdParty) return false;
            if (_include != null && !_include.Any(d => OnDomain(req.PageHost, d))) return false;
            if (_exclude != null && _exclude.Any(d => OnDomain(req.PageHost, d))) return false;
            if (_pattern.Length == 0) return true;
            var url = req.Url;
            if (_hostAnchor)
            {
                if (req.HostStart < 0) return false;
                // At the start of the host, or just after any dot in it.
                var hostEnd = req.HostStart + req.Host.Length;
                for (var at = req.HostStart; at < hostEnd; at++)
                {
                    if ((at == req.HostStart || url[at - 1] == '.') && MatchAt(url, at)) return true;
                }
                return false;
            }
            if (_startAnchor) return MatchAt(url, 0);
            for (var at = 0; at <= url.Length; at++)
                if (MatchAt(url, at)) return true;
            return false;
        }

        private static bool OnDomain(string host, string domain) =>
            host == domain || (host.Length > domain.Length && host.EndsWith(domain, StringComparison.Ordinal) && host[host.Length - domain.Length - 1] == '.');

        /// <summary>Does the pattern match the address starting at <paramref name="start"/>? (* may stretch; ^ is a separator.)</summary>
        private bool MatchAt(string url, int start)
        {
            var p = _pattern;
            int ui = start, pi = 0, starP = -1, starU = -1;
            while (true)
            {
                if (pi == p.Length)
                {
                    if (!_endAnchor || ui == url.Length) return true;
                }
                else if (p[pi] == '*')
                {
                    starP = pi++;
                    starU = ui;
                    continue;
                }
                else if (p[pi] == '^')
                {
                    if (ui == url.Length)
                    {
                        // ^ at the end of the address matches nothing more.
                        pi++;
                        continue;
                    }
                    if (IsSeparator(url[ui]))
                    {
                        pi++;
                        ui++;
                        continue;
                    }
                }
                else if (ui < url.Length && p[pi] == url[ui])
                {
                    pi++;
                    ui++;
                    continue;
                }
                // Mismatch: let the last * take one more character, or fail.
                if (starP < 0 || starU >= url.Length) return false;
                pi = starP + 1;
                ui = ++starU;
            }
        }

        public override string ToString() => Text;
    }
}
