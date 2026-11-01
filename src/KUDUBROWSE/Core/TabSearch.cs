using System;
using System.Collections.Generic;
using System.Linq;

namespace Kudubrowse.Core
{
    /// <summary>
    /// Finding a tab by a few words: every word must be in the tab's title or address. Tabs whose title starts with the
    /// words come first, then those with a word in the title starting with them, then the rest (as they were).
    /// </summary>
    public static class TabSearch
    {
        /// <summary>How well a tab fits the words (higher is better), or -1 when it doesn't.</summary>
        public static int Score(string query, string title, string url)
        {
            var words = Words(query);
            if (words.Length == 0) return 0;
            title = title ?? string.Empty;
            url = url ?? string.Empty;
            var score = 0;
            foreach (var w in words)
            {
                var inTitle = title.IndexOf(w, StringComparison.OrdinalIgnoreCase);
                var inUrl = url.IndexOf(w, StringComparison.OrdinalIgnoreCase);
                if (inTitle < 0 && inUrl < 0) return -1;
                if (inTitle == 0) score += 4;
                else if (inTitle > 0 && !char.IsLetterOrDigit(title[inTitle - 1])) score += 3;
                else if (inTitle > 0) score += 2;
                else score += 1;
            }
            return score;
        }

        /// <summary>The items that fit the words, best first (keeping their order among equals).</summary>
        public static List<T> Find<T>(IEnumerable<T> items, string query, Func<T, string> title, Func<T, string> url) =>
            items.Select((item, i) => (item, i, score: Score(query, title(item), url(item))))
                .Where(x => x.score >= 0)
                .OrderByDescending(x => x.score).ThenBy(x => x.i)
                .Select(x => x.item).ToList();

        private static string[] Words(string query) =>
            (query ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
