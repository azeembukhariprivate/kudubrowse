// Clear when closing: when the browser's last window closes, the cookies and site data and/or the history of the sites
// on the list (or of every site) are forgotten, in every room. If the browser is stopped before that's finished (Windows
// shutting down), it's finished at the next start, before any page opens.
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;
using Kudubrowse.Core;
using Microsoft.Web.WebView2.Core;

namespace Kudubrowse.Views
{
    public static class Forgetting
    {
        /// <summary>How long closing waits for the forgetting; what's left is finished at the next start.</summary>
        public static TimeSpan TimeLimit { get; set; } = TimeSpan.FromSeconds(20);

        /// <summary>True when something is to be forgotten when the browser closes.</summary>
        public static bool IsOn => ForgetOnClose.Active(Settings.ForgetWhenClosing, Settings.ForgetCookies, Settings.ForgetHistory, Settings.ForgetSites);

        /// <summary>
        /// Forgets what the settings say, now (waiting at most <see cref="TimeLimit"/>). The open tabs' addresses help
        /// find the sites' stored data. True when it all finished.
        /// </summary>
        public static async Task<bool> Run(params string[] openAddresses)
        {
            if (!IsOn)
            {
                Settings.ForgetPending = false;
                return true;
            }
            Settings.ForgetPending = true;
            var work = Forget(openAddresses);
            var done = await Task.WhenAny(work, Task.Delay(TimeLimit));
            if (done != work) return false;
            try
            {
                await work;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException || ex is ArgumentException)
            {
                return false; // tried again at the next start
            }
            Settings.ForgetPending = false;
            return true;
        }

        /// <summary>Forgets the history and then each room's cookies and site data, as the settings say.</summary>
        private static async Task Forget(string[] openAddresses)
        {
            var mode = Settings.ForgetWhenClosing;
            var sites = Settings.ForgetSites;
            // The places whose stored data goes, found before the history that lists them is forgotten.
            var origins = ForgetOnClose.Origins(sites, BrowserLibrary.History.All.Select(v => v.Url).Concat(openAddresses ?? new string[0]));
            if (Settings.ForgetHistory)
            {
                ForgetOnClose.ForgetHistory(BrowserLibrary.History, mode, sites);
                BrowserLibrary.History.Save();
            }
            if (!Settings.ForgetCookies) return;
            var env = await Web.Environment;
            // The engine's pages need a window to belong to; this one is never shown.
            var helper = new Window { Width = 0, Height = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false };
            var handle = new WindowInteropHelper(helper).EnsureHandle();
            try
            {
                foreach (var room in BrowserRooms.All.All)
                {
                    var options = env.CreateCoreWebView2ControllerOptions();
                    options.ProfileName = room.Id;
                    var controller = await env.CreateCoreWebView2ControllerAsync(handle, options);
                    try
                    {
                        controller.IsVisible = false;
                        var core = controller.CoreWebView2;
                        if (mode == ForgetMode.Everything)
                        {
                            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite | CoreWebView2BrowsingDataKinds.DiskCache);
                            continue;
                        }
                        foreach (var c in await core.CookieManager.GetCookiesAsync(null))
                            if (ForgetOnClose.CookieIsOnList(c.Domain, sites)) core.CookieManager.DeleteCookie(c);
                        var json = new JavaScriptSerializer();
                        foreach (var origin in origins)
                            await core.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin",
                                json.Serialize(new { origin, storageTypes = "local_storage,indexeddb,websql,service_workers,cache_storage,file_systems,shader_cache" }));
                    }
                    finally
                    {
                        controller.Close();
                    }
                }
            }
            finally
            {
                helper.Close();
            }
        }

        /// <summary>Puts a site on the list (turning "forget the sites in my list" on when nothing was forgotten), or takes it off.</summary>
        public static void SetSite(string site, bool forget)
        {
            if (string.IsNullOrEmpty(site)) return;
            var list = Settings.ForgetSites;
            if (forget)
            {
                list.Add(site);
                if (Settings.ForgetWhenClosing == ForgetMode.Off) Settings.ForgetWhenClosing = ForgetMode.Sites;
                if (!Settings.ForgetCookies && !Settings.ForgetHistory) Settings.ForgetCookies = true;
            }
            else list.Remove(site);
            Settings.ForgetSites = list;
        }
    }
}
