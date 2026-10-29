using System;
using System.IO;
using System.Threading.Tasks;
using Kudubrowse.Core;
using Microsoft.Web.WebView2.Core;

namespace Kudubrowse.Views
{
    /// <summary>The Edge engine (WebView2) that shows the pages. All tabs share one engine and one profile folder.</summary>
    public static class Web
    {
        private static Task<CoreWebView2Environment> _environment;

        /// <summary>Where the profile is kept (cookies, cache, site data): %LocalAppData%\KUDUBROWSE\Profiles\Default.</summary>
        public static string ProfileFolder { get; set; } = Path.Combine(Session.DataFolder, "Profiles", "Default");

        public static Task<CoreWebView2Environment> Environment =>
            _environment ?? (_environment = CoreWebView2Environment.CreateAsync(null, ProfileFolder));

        /// <summary>The installed engine's version, or null when it isn't installed.</summary>
        public static string RuntimeVersion
        {
            get
            {
                try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
                catch (Exception) { return null; }
            }
        }
    }
}
