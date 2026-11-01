using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kudubrowse.Core
{
    /// <summary>How pages look: as the site shows them, dark when the site has a dark look, or always dark.</summary>
    public enum DarkMode
    {
        Off,
        WhenSiteHasIt,
        Always
    }

    /// <summary>
    /// Dark pages. Sites with a dark look of their own are asked for it (the engine's "prefers dark" setting). For "always
    /// dark", light pages are turned dark by inverting their colours (pictures and videos are inverted back, so they look
    /// as they should); a page that's already dark, or a site on the "don't darken" list, is left as it is.
    /// </summary>
    public static class DarkPages
    {
        /// <summary>Raised when the choice or the "don't darken" list changes, so open pages follow.</summary>
        public static event Action Changed;

        public static void Notify() => Changed?.Invoke();

        public static DarkMode Parse(string text) => Enum.TryParse(text, true, out DarkMode m) ? m : DarkMode.Off;

        /// <summary>A site on the list covers its own pages and those of its sub-sites ("example.com" covers "www.example.com").</summary>
        public static bool Covers(string site, string host)
        {
            if (string.IsNullOrEmpty(site) || string.IsNullOrEmpty(host)) return false;
            host = host.ToLowerInvariant();
            site = site.ToLowerInvariant();
            return host == site || host.EndsWith("." + site, StringComparison.Ordinal);
        }

        public static bool IsLightSite(IEnumerable<string> lightSites, string host) => lightSites.Any(s => Covers(s, host));

        /// <summary>Takes the darkening away from the page shown now.</summary>
        public const string UndoScript = "document.getElementById('kudu-dark')?.remove();";

        /// <summary>The script that darkens light pages (run as each page starts, and on the page shown now).</summary>
        public static string Script(IEnumerable<string> lightSites)
        {
            var list = new StringBuilder("[");
            foreach (var s in lightSites.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                if (list.Length > 1) list.Append(',');
                list.Append('"').Append(Escape(s.Trim().ToLowerInvariant())).Append('"');
            }
            list.Append(']');
            return @"(() => {
  if (!/^(https?|file):$/.test(location.protocol) || window.top !== window) return;
  const light = " + list + @";
  const h = location.hostname.toLowerCase();
  if (light.some(d => h === d || h.endsWith('.' + d))) return;
  const css = 'html { filter: invert(0.9) hue-rotate(180deg) !important; background: #fff !important; }' +
    'img, video, picture, canvas, iframe, embed, object, svg image, [style*=""background-image""] { filter: invert(1) hue-rotate(180deg) !important; }';
  // As a page starts there may be nothing to put the style in yet: then it goes in as soon as there is.
  const add = () => {
    if (document.getElementById('kudu-dark')) return true;
    const root = document.head || document.documentElement;
    if (!root) return false;
    const s = document.createElement('style');
    s.id = 'kudu-dark';
    s.textContent = css;
    root.appendChild(s);
    return true;
  };
  // A page that's dark already is left as it is (its own colours, read with the darkening switched off for a moment).
  const check = () => {
    const s = document.getElementById('kudu-dark');
    if (!s || !document.body) return;
    s.disabled = true;
    const shade = el => {
      const m = getComputedStyle(el).backgroundColor.match(/[\d.]+/g);
      if (!m || (m.length > 3 && +m[3] === 0)) return null;
      return (0.2126 * m[0] + 0.7152 * m[1] + 0.0722 * m[2]) / 255;
    };
    const l = shade(document.body) ?? shade(document.documentElement) ?? 1;
    s.disabled = false;
    if (l < 0.4) s.remove();
  };
  if (!add()) {
    const watch = new MutationObserver(() => { if (add()) watch.disconnect(); });
    watch.observe(document, { childList: true, subtree: true });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => { add(); check(); });
  else check();
  window.addEventListener('load', check);
})();";
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20 || c == '<' || c == '>' || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
