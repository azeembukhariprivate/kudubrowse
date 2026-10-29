using System;
using System.Collections.Generic;
using System.Linq;

namespace Kudubrowse.Core.Library
{
    public enum SuggestionKind { Search, Address, Bookmark, History }

    public sealed class Suggestion
    {
        public SuggestionKind Kind { get; set; }
        public string Text { get; set; } // what's shown big (the title, or the words searched for)
        public string Url { get; set; } // where it goes
        public double Score { get; set; }
    }

    /// <summary>What the address bar suggests while typing: bookmarks and pages visited before that match, and a search.</summary>
    public static class Suggestions
    {
        public static List<Suggestion> For(string typed, IEnumerable<Bookmark> bookmarks, IEnumerable<Visit> history, SearchEngine engine, int max = 7)
        {
            var text = (typed ?? string.Empty).Trim();
            var list = new List<Suggestion>();
            if (text.Length == 0) return list;
            var terms = text.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var now = DateTime.UtcNow;
            var byUrl = new Dictionary<string, Suggestion>();

            void Consider(string url, string title, double boost, SuggestionKind kind)
            {
                var score = Score(url, title, terms) * boost;
                if (score <= 0) return;
                var key = url.TrimEnd('/').ToLowerInvariant();
                if (byUrl.TryGetValue(key, out var s))
                {
                    s.Score += score;
                    if (kind == SuggestionKind.Bookmark) s.Kind = kind;
                    return;
                }
                byUrl[key] = new Suggestion { Kind = kind, Text = string.IsNullOrWhiteSpace(title) ? AddressBar.Display(url) : title, Url = url, Score = score };
            }

            foreach (var b in bookmarks) Consider(b.Url, b.Title, 2.0, SuggestionKind.Bookmark);
            foreach (var v in history)
            {
                // Pages visited often and lately come first.
                var days = Math.Max(0, (now - v.LastVisit).TotalDays);
                var boost = (1 + Math.Log(1 + v.Visits)) / (1 + days / 14);
                Consider(v.Url, v.Title, boost, SuggestionKind.History);
            }

            var resolved = AddressBar.Resolve(text, engine);
            var isAddress = resolved != null && resolved != engine.UrlFor(text);
            var top = byUrl.Values.OrderByDescending(s => s.Score).ThenBy(s => s.Url.Length).Take(max - 1).ToList();
            var typedEntry = isAddress
                ? new Suggestion { Kind = SuggestionKind.Address, Text = text, Url = resolved }
                : new Suggestion { Kind = SuggestionKind.Search, Text = text, Url = engine.UrlFor(text) };
            // A page whose address starts with what was typed goes first (typing "git" picks github.com); otherwise
            // what was typed (open it, or search for it) goes first.
            if (top.Count > 0 && StartsHost(top[0].Url, terms[0]) && terms.Length == 1)
            {
                list.Add(top[0]);
                list.Add(typedEntry);
                list.AddRange(top.Skip(1));
            }
            else
            {
                list.Add(typedEntry);
                list.AddRange(top);
            }
            return list.Where((s, i) => i == list.FindIndex(o => o.Url == s.Url)).Take(max).ToList();
        }

        private static double Score(string url, string title, string[] terms)
        {
            if (string.IsNullOrEmpty(url)) return 0;
            var u = url.ToLowerInvariant();
            var t = (title ?? string.Empty).ToLowerInvariant();
            var host = Host(u);
            double score = 0;
            foreach (var term in terms)
            {
                if (StartsHost(u, term)) score += 10;
                else if (host.Contains("." + term)) score += 6;
                else if (t.StartsWith(term) || t.Contains(" " + term)) score += 4;
                else if (u.Contains(term) || t.Contains(term)) score += 1;
                else return 0; // every word must match somewhere
            }
            return score;
        }

        private static string Host(string url)
        {
            var i = url.IndexOf("://", StringComparison.Ordinal);
            var rest = i < 0 ? url : url.Substring(i + 3);
            var end = rest.IndexOfAny(new[] { '/', '?', '#' });
            return end < 0 ? rest : rest.Substring(0, end);
        }

        /// <summary>Does the host start with what was typed (ignoring "www.")?</summary>
        public static bool StartsHost(string url, string term)
        {
            var host = Host(url.ToLowerInvariant());
            if (host.StartsWith("www.")) host = host.Substring(4);
            return host.StartsWith(term.ToLowerInvariant());
        }
    }
}
