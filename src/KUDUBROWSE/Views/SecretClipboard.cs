using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Kudubrowse.Views
{
    /// <summary>
    /// Copies a password or a sign-in code so that it doesn't linger: Windows is told to keep it out of the clipboard
    /// history (Win+V) and out of the clipboard shared between devices, and it's taken off the clipboard again after
    /// half a minute unless something else has been copied meanwhile.
    /// </summary>
    public static class SecretClipboard
    {
        /// <summary>How long a copied secret stays on the clipboard.</summary>
        public static TimeSpan ClearAfter = TimeSpan.FromSeconds(30);

        // The names Windows and clipboard tools look for; see "Clipboard Formats" in Microsoft's documentation.
        public const string NotInHistory = "CanIncludeInClipboardHistory";
        public const string NotInCloud = "CanUploadToCloudClipboard";
        public const string NotForMonitors = "ExcludeClipboardContentFromMonitorProcessing";

        private static DispatcherTimer _timer;
        private static uint _ours;

        /// <summary>Puts the secret on the clipboard. False when the clipboard was busy.</summary>
        public static bool Copy(string secret)
        {
            if (string.IsNullOrEmpty(secret)) return false;
            try
            {
                var data = new DataObject();
                data.SetText(secret);
                data.SetData(NotInHistory, new MemoryStream(BitConverter.GetBytes(0)));
                data.SetData(NotInCloud, new MemoryStream(BitConverter.GetBytes(0)));
                data.SetData(NotForMonitors, new MemoryStream(BitConverter.GetBytes(1)));
                Clipboard.SetDataObject(data, true);
            }
            catch (ExternalException)
            {
                return false; // another program has the clipboard open
            }
            _ours = GetClipboardSequenceNumber();
            _timer?.Stop();
            _timer = new DispatcherTimer { Interval = ClearAfter };
            _timer.Tick += (s, e) => ClearIfStillOurs();
            _timer.Start();
            return true;
        }

        /// <summary>True while the clipboard still holds the secret copied last (nothing else was copied since).</summary>
        public static bool StillOurs => _ours != 0 && GetClipboardSequenceNumber() == _ours;

        /// <summary>Empties the clipboard if it still holds the secret. Also called when the browser closes.</summary>
        public static void ClearIfStillOurs()
        {
            _timer?.Stop();
            _timer = null;
            if (!StillOurs) return;
            _ours = 0;
            try { Clipboard.Clear(); }
            catch (ExternalException) { /* busy: the next copy replaces it anyway */ }
        }

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();
    }
}
