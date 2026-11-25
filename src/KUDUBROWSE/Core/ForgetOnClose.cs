// Clear when closing: what the browser forgets when its last window closes (cookies and site data, history), for the
// sites on a list or for every site. The forgetting itself is done by Views/Forgetting.cs; this is what to forget.
// lang.py: only Lang.T texts (the rest are the file's own words)
using System;
using System.Collections.Generic;
using System.Linq;
using Kudubrowse.Core.Blocking;
using Kudubrowse.Core.Library;

namespace Kudubrowse.Core
{
    /// <summary>Which sites are forgotten when the browser closes: none, the ones on the list, or every one.</summary>
    public enum ForgetMode { Off, Sites, Everything }

    public static class ForgetOnClose
    {
        /// <summary>True when something is to be forgotten at all.</summary>
        public static bool Active(ForgetMode mode, bool cookies, bool history, IReadOnlyCollection<string> sites) =>
            (cookies || history) && (mode == ForgetMode.Everything || (mode == ForgetMode.Sites && sites != null && sites.Count > 0));

        /// <summary>True when a host belongs to one of the sites (by registrable domain: mail.example.com is example.com).</summary>
        public static bool IsOnList(string host, IEnumerable<string> sites)
        {
            if (string.IsNullOrEmpty(host) || sites == null) return false;
            var site = Domains.Registrable(host.ToLowerInvariant());
            return sites.Any(s => string.Equals(s, site, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>True when a cookie's domain (".example.com", "www.example.com") belongs to one of the sites.</summary>
        public static bool CookieIsOnList(string cookieDomain, IEnumerable<string> sites) =>
            IsOnList((cookieDomain ?? string.Empty).TrimStart('.'), sites);

        /// <summary>
        /// The places ("https://www.example.com") whose stored data is cleared for the sites: each site with and without
        /// "www.", and every host of theirs seen in these addresses (from the history and the open tabs), on http and https.
        /// </summary>
        public static List<string> Origins(IEnumerable<string> sites, IEnumerable<string> addresses)
        {
            var list = sites?.ToList() ?? new List<string>();
            var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in list)
            {
                hosts.Add(s);
                hosts.Add("www." + s);
            }
            foreach (var a in addresses ?? Enumerable.Empty<string>())
                if (Uri.TryCreate(a, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https") && IsOnList(u.Host, list))
                    hosts.Add(u.IsDefaultPort ? u.Host : u.Host + ":" + u.Port);
            return hosts.OrderBy(h => h).SelectMany(h => new[] { "https://" + h, "http://" + h }).ToList();
        }

        /// <summary>Forgets history: every page for "Everything", else the pages of the sites. Returns how many went.</summary>
        public static int ForgetHistory(History history, ForgetMode mode, IReadOnlyCollection<string> sites)
        {
            if (history == null || mode == ForgetMode.Off) return 0;
            if (mode == ForgetMode.Everything)
            {
                var all = history.Count;
                history.Clear();
                return all;
            }
            var gone = history.All.Where(v => Uri.TryCreate(v.Url, UriKind.Absolute, out var u) && IsOnList(u.Host, sites)).Select(v => v.Url).ToList();
            foreach (var url in gone) history.Remove(url);
            return gone.Count;
        }

        /// <summary>A site for the list, from a page's host (its registrable domain), or null.</summary>
        public static string SiteOf(string host) => string.IsNullOrEmpty(host) ? null : Domains.Registrable(host.ToLowerInvariant());
    }
}
