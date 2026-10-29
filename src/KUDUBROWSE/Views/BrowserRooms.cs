using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Kudubrowse.Core;
using Microsoft.Web.WebView2.Core;

namespace Kudubrowse.Views
{
    /// <summary>The rooms, shared by every window, and clearing a room's site data.</summary>
    public static class BrowserRooms
    {
        private static Rooms _rooms;
        public static Rooms All => _rooms ?? (_rooms = Rooms.Load(Path.Combine(BrowserLibrary.Folder, "rooms.txt")));

        public static void Reload() => _rooms = null;

        /// <summary>
        /// Deletes a room's cookies, logins to sites, cache and other site data (not bookmarks, history or saved passwords).
        /// Uses a hidden page of the engine in that room, as the data belongs to it.
        /// </summary>
        public static async Task ClearData(Window owner, string roomId)
        {
            var env = await Web.Environment;
            var options = env.CreateCoreWebView2ControllerOptions();
            options.ProfileName = roomId;
            var controller = await env.CreateCoreWebView2ControllerAsync(new WindowInteropHelper(owner).EnsureHandle(), options);
            try
            {
                controller.IsVisible = false;
                await controller.CoreWebView2.Profile.ClearBrowsingDataAsync();
            }
            finally
            {
                controller.Close();
            }
        }
    }
}
