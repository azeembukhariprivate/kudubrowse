using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kudubrowse.Core
{
    public sealed class SearchEngine
    {
        public SearchEngine(string id, string name, string query)
        {
            Id = id;
            Name = name;
            Query = query;
        }

        public string Id { get; }
        public string Name { get; }
        /// <summary>The search address, with {0} where the words go.</summary>
        public string Query { get; }

        public string UrlFor(string words) => string.Format(Query, Uri.EscapeDataString(words.Trim()));
    }

    public static class SearchEngines
    {
        public static readonly IReadOnlyList<SearchEngine> All = new[]
        {
            new SearchEngine("google", "Google", "https://www.google.com/search?q={0}"),
            new SearchEngine("bing", "Bing", "https://www.bing.com/search?q={0}"),
            new SearchEngine("duckduckgo", "DuckDuckGo", "https://duckduckgo.com/?q={0}"),
            new SearchEngine("brave", "Brave Search", "https://search.brave.com/search?q={0}"),
            new SearchEngine("startpage", "Startpage", "https://www.startpage.com/do/search?q={0}")
        };

        public static SearchEngine Default => All[0];

        public static SearchEngine Find(string id) => All.FirstOrDefault(e => e.Id == id) ?? Default;
    }

    /// <summary>Works out what was typed in the address bar: a web address to open, or words to search for.</summary>
    public static class AddressBar
    {
        private static readonly string[] Schemes = { "http", "https", "file", "about", "data", "view-source", "edge", "mailto" };

        // A host name like example.com, a.b.co.uk or xn--fiqs8s.cn: letters, digits and hyphens between dots, ending in letters.
        private static readonly Regex Host = new Regex(@"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:[a-z]{2,63}|xn--[a-z0-9-]{1,59})$", RegexOptions.IgnoreCase);
        private static readonly Regex Ip = new Regex(@"^\d{1,3}(?:\.\d{1,3}){3}$");

        /// <summary>The address to open for what was typed, or null when there's nothing to open.</summary>
        public static string Resolve(string typed, SearchEngine engine)
        {
            var text = (typed ?? string.Empty).Trim();
            if (text.Length == 0) return null;

            // Starts with a scheme the browser knows: open it as it is.
            var colon = text.IndexOf(':');
            if (colon > 0 && Schemes.Contains(text.Substring(0, colon).ToLowerInvariant()) && !text.Contains(' '))
                return text;
            // A Windows path to a file.
            if (Regex.IsMatch(text, @"^[a-zA-Z]:\\") || text.StartsWith(@"\\"))
                return new Uri(text).AbsoluteUri;

            if (!text.Contains(' ') && LooksLikeAddress(text, out var local))
                return (local ? "http://" : "https://") + text;
            return engine.UrlFor(text);
        }

        private static bool LooksLikeAddress(string text, out bool local)
        {
            local = false;
            var end = text.IndexOfAny(new[] { '/', '?', '#' });
            var hostPort = end < 0 ? text : text.Substring(0, end);
            var host = hostPort;
            var colon = hostPort.LastIndexOf(':');
            if (colon > 0)
            {
                if (!int.TryParse(hostPort.Substring(colon + 1), out var port) || port <= 0 || port > 65535) return false;
                host = hostPort.Substring(0, colon);
            }
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || Ip.IsMatch(host))
            {
                local = true;
                return true;
            }
            return Host.IsMatch(host);
        }

        /// <summary>The address as shown in the bar: without "https://" and a lone trailing slash.</summary>
        public static string Display(string url)
        {
            if (string.IsNullOrEmpty(url) || url == "about:blank") return string.Empty;
            var s = url;
            if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) s = s.Substring(8);
            if (s.EndsWith("/") && s.IndexOf('/') == s.Length - 1) s = s.Substring(0, s.Length - 1);
            return s;
        }

        /// <summary>Is this a page whose connection is private (https, or a file on this PC)?</summary>
        public static bool IsSecure(string url) =>
            url != null && (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("file:", StringComparison.OrdinalIgnoreCase));
    }
}
