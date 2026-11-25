// The browser window's "Save for reading later": the page kept whole on this PC (an MHTML file made by the engine), so
// it opens from Menu › Read later even without the internet.
using System;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Kudubrowse.Core;

namespace Kudubrowse.Views
{
    public sealed partial class MainWindow
    {
        /// <summary>
        /// Keeps a tab's page (the one shown when none is given) to read later. Returns the page kept, or null when it
        /// couldn't be (the person is told why).
        /// </summary>
        public async Task<LaterPage> SaveForLater(BrowserTab tab = null)
        {
            tab = tab ?? Current;
            var core = tab?.View?.CoreWebView2;
            if (tab == null || IsPrivate) return null;
            if (BrowserLibrary.ReadLater.FindByCopy(tab.Url) != null)
            {
                Dialogs.Info(this, "This page is already kept to read later.");
                return null;
            }
            if (core == null || !ReadLater.IsWebAddress(tab.Url))
            {
                Dialogs.Info(this, "Only a web page that's open can be kept to read later.");
                return null;
            }
            try
            {
                tab.WakeUp();
                // The engine saves the page as it is now, with its pictures and styles, in one file.
                var json = await core.CallDevToolsProtocolMethodAsync("Page.captureSnapshot", "{\"format\":\"mhtml\"}");
                var data = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<SnapshotResult>(json)?.data;
                var page = BrowserLibrary.ReadLater.Add(tab.Url, tab.Title, data);
                ShowNote(_menuButton, "IconNote", "Kept to read later", page.Title, () => LibraryWindow.ShowReadLater(this));
                return page;
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException
                                       || ex is InvalidOperationException || ex is ArgumentException || ex is System.Runtime.InteropServices.COMException)
            {
                Dialogs.Error(this, Lang.T("The page couldn't be kept: {0}", Lang.T(ex.Message)));
                return null;
            }
        }

        /// <summary>The engine's answer to Page.captureSnapshot.</summary>
        private sealed class SnapshotResult
        {
            /// <summary>The page as MHTML text.</summary>
            public string data { get; set; }
        }
    }
}
