using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using Kudubrowse.Core;
using Kudubrowse.Core.Blocking;
using Microsoft.Web.WebView2.Core;

namespace Kudubrowse.Views
{
    /// <summary>
    /// Ad, tracker and cookie-banner blocking for every tab: requests are checked against the lists before they're
    /// sent, and the parts of pages the lists name are hidden. Sites can be allowed from the shield button.
    /// </summary>
    public static class Blocker
    {
        private static FilterEngine _ads, _cookies;
        // Settings are read once (not for every request a page makes).
        private static bool _adsOn = Settings.BlockAds, _cookiesOn = Settings.HideCookieBanners;
        private static HashSet<string> _allowed = new HashSet<string>(Settings.AllowedSites);
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        /// <summary>The lists were (re)loaded: tabs refresh their hiding script.</summary>
        public static event Action Changed;

        public static bool Ready => _ads != null || _cookies != null;
        public static int RuleCount => (_ads?.NetworkRules ?? 0) + (_cookies?.NetworkRules ?? 0);
        public static int HidingCount => (_ads?.HidingRules ?? 0) + (_cookies?.HidingRules ?? 0);

        /// <summary>Loads the lists in the background, then fetches newer ones if they're more than a few days old.</summary>
        public static async void Start()
        {
            await Task.Run(Load);
            Changed?.Invoke();
            if (BlockLists.NeedsRefresh) await UpdateNow();
        }

        public static void Load()
        {
            var ads = FilterEngine.FromText(BlockLists.Read(BlockLists.EasyList), BlockLists.Read(BlockLists.EasyPrivacy));
            var cookies = FilterEngine.FromText(BlockLists.Read(BlockLists.Cookies));
            _ads = ads;
            _cookies = cookies;
            _css.Clear();
        }

        /// <summary>Fetches the lists now. Returns how many were updated.</summary>
        public static async Task<int> UpdateNow()
        {
            int updated;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.Name + "/" + AppInfo.VersionText);
                    updated = await BlockLists.FetchAsync(http);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is InvalidOperationException)
            {
                return 0;
            }
            if (updated > 0)
            {
                await Task.Run(Load);
                Changed?.Invoke();
            }
            return updated;
        }

        /// <summary>For the self-test: use these rules instead of the lists.</summary>
        public static void UseRules(string ads, string cookies = "")
        {
            _ads = FilterEngine.FromText(ads);
            _cookies = FilterEngine.FromText(cookies);
            _css.Clear();
            Changed?.Invoke();
        }

        // ================================================================ allowed sites

        public static bool IsAllowed(string host) => !string.IsNullOrEmpty(host) && _allowed.Contains(Domains.Registrable(host));

        /// <summary>The blocking settings changed: read them again and refresh the tabs.</summary>
        public static void SettingsChanged()
        {
            _adsOn = Settings.BlockAds;
            _cookiesOn = Settings.HideCookieBanners;
            _allowed = new HashSet<string>(Settings.AllowedSites);
            _css.Clear();
            Changed?.Invoke();
        }

        public static void SetAllowed(string host, bool allowed)
        {
            if (string.IsNullOrEmpty(host)) return;
            var site = Domains.Registrable(host);
            var list = Settings.AllowedSites;
            list.Remove(site);
            if (allowed) list.Add(site);
            Settings.AllowedSites = list;
            SettingsChanged();
        }

        // ================================================================ requests

        public static bool ShouldBlock(string url, string pageHost, CoreWebView2WebResourceContext context)
        {
            var kind = KindOf(context);
            if (_adsOn && _ads != null && _ads.ShouldBlock(url, pageHost, kind)) return true;
            return _cookiesOn && _cookies != null && _cookies.ShouldBlock(url, pageHost, kind);
        }

        private static RequestKind KindOf(CoreWebView2WebResourceContext c)
        {
            switch (c)
            {
                case CoreWebView2WebResourceContext.Script: return RequestKind.Script;
                case CoreWebView2WebResourceContext.Image: return RequestKind.Image;
                case CoreWebView2WebResourceContext.Stylesheet: return RequestKind.Stylesheet;
                case CoreWebView2WebResourceContext.XmlHttpRequest:
                case CoreWebView2WebResourceContext.Fetch:
                case CoreWebView2WebResourceContext.EventSource: return RequestKind.Xhr;
                case CoreWebView2WebResourceContext.Document: return RequestKind.Subdocument;
                case CoreWebView2WebResourceContext.Media: return RequestKind.Media;
                case CoreWebView2WebResourceContext.Font: return RequestKind.Font;
                case CoreWebView2WebResourceContext.Websocket: return RequestKind.Websocket;
                case CoreWebView2WebResourceContext.Ping: return RequestKind.Ping;
                default: return RequestKind.Other;
            }
        }

        // ================================================================ hiding

        private static readonly Dictionary<string, string> _css = new Dictionary<string, string>();

        private static IEnumerable<FilterEngine> Engines()
        {
            if (_adsOn && _ads != null) yield return _ads;
            if (_cookiesOn && _cookies != null) yield return _cookies;
        }

        private static string GenericCss(ICollection<string> leaveOut = null)
        {
            var key = "generic|" + _adsOn + _cookiesOn;
            if (leaveOut == null && _css.TryGetValue(key, out var css)) return css;
            var selectors = Engines().SelectMany(e => e.GenericSelectors);
            if (leaveOut != null) selectors = selectors.Where(s => !leaveOut.Contains(s));
            css = FilterEngine.Css(selectors.Distinct());
            if (leaveOut == null) _css[key] = css;
            return css;
        }

        /// <summary>
        /// The script added to every page before it starts: puts the general hiding rules in place straight away
        /// (top-level pages only; skipped on allowed sites).
        /// </summary>
        public static string DocumentScript()
        {
            var css = GenericCss();
            if (css.Length == 0) return null;
            return "(() => {" +
                   "if (window.top !== window) return;" +
                   "const allowed = " + Json.Serialize(_allowed.ToList()) + ";" +
                   "const h = location.hostname;" +
                   "if (allowed.some(d => h === d || h.endsWith('.' + d))) return;" +
                   "const s = document.createElement('style'); s.id = 'kudubrowse-hide';" +
                   "s.textContent = " + Json.Serialize(css) + ";" +
                   "const add = () => { const r = document.documentElement; if (!r) return false; r.appendChild(s); return true; };" +
                   "if (!add()) { const mo = new MutationObserver(() => { if (add()) mo.disconnect(); }); mo.observe(document, { childList: true }); }" +
                   "})();";
        }

        /// <summary>Run once the page is read: adds the site's own hiding rules and drops general ones the site switches off.</summary>
        public static string SiteScript(string host)
        {
            if (string.IsNullOrEmpty(host) || IsAllowed(host)) return "(() => { const g = document.getElementById('kudubrowse-hide'); if (g) g.remove(); })();";
            var specific = new List<string>();
            var unhide = new HashSet<string>();
            bool noGeneric = false, noHiding = false;
            foreach (var e in Engines())
            {
                var h = e.HidingFor(host);
                specific.AddRange(h.Specific);
                unhide.UnionWith(h.Unhidden);
                noGeneric |= h.NoGeneric;
                noHiding |= h.NoHiding;
            }
            if (noHiding) return "(() => { const g = document.getElementById('kudubrowse-hide'); if (g) g.remove(); })();";
            var parts = new List<string> { "const g = document.getElementById('kudubrowse-hide');" };
            if (noGeneric) parts.Add("if (g) g.remove();");
            else if (unhide.Count > 0) parts.Add("if (g) g.textContent = " + Json.Serialize(GenericCss(unhide)) + ";");
            if (specific.Count > 0)
                parts.Add("const s = document.createElement('style'); s.id = 'kudubrowse-site'; s.textContent = " + Json.Serialize(FilterEngine.Css(specific.Distinct())) +
                          "; (document.head || document.documentElement).appendChild(s);");
            return parts.Count == 1 ? null : "(() => {" + string.Join(string.Empty, parts) + "})();";
        }
    }
}
