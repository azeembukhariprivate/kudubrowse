using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Kudubrowse.Views
{
    /// <summary>
    /// Files the browser downloads itself get what the engine gives its own downloads: Windows' "came from the internet"
    /// mark (so Windows warns before running them) and a check by the virus scanner, which removes a harmful file.
    /// Uses Windows' Attachment Services; if that isn't available, the mark is written directly.
    /// </summary>
    public static class AttachmentServices
    {
        private static readonly Guid Client = new Guid("6C5A6E0F-2E64-4B7B-9C4E-4B5542524F57");

        /// <summary>Marks and checks the file. Returns false when the virus scanner removed it.</summary>
        public static Task<bool> Check(string path, string url, string referrer)
        {
            var done = new TaskCompletionSource<bool>();
            var thread = new Thread(() =>
            {
                try
                {
                    var saved = false;
                    try
                    {
                        var ae = (IAttachmentExecute)new AttachmentServicesClass();
                        try
                        {
                            var guid = Client;
                            ae.SetClientGuid(ref guid);
                            ae.SetClientTitle(Kudubrowse.Core.AppInfo.Name);
                            ae.SetLocalPath(path);
                            ae.SetSource(url);
                            if (!string.IsNullOrEmpty(referrer) && referrer.StartsWith("http")) ae.SetReferrer(referrer);
                            saved = ae.Save() >= 0;
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(ae);
                        }
                    }
                    catch (Exception ex) when (ex is COMException || ex is InvalidCastException) { }
                    if (!File.Exists(path))
                    {
                        done.SetResult(false);
                        return;
                    }
                    if (!saved || !HasMark(path)) Mark(path, url, referrer);
                    done.SetResult(true);
                }
                catch (Exception ex)
                {
                    done.TrySetException(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return done.Task;
        }

        /// <summary>Writes the "came from the internet" mark (the file's Zone.Identifier part).</summary>
        public static void Mark(string path, string url, string referrer)
        {
            var text = "[ZoneTransfer]\r\nZoneId=3\r\n" +
                       (string.IsNullOrEmpty(referrer) || !referrer.StartsWith("http") ? string.Empty : "ReferrerUrl=" + referrer + "\r\n") +
                       (string.IsNullOrEmpty(url) ? string.Empty : "HostUrl=" + url + "\r\n");
            using (var h = CreateFile(path + ":Zone.Identifier", GenericWrite, FileShare.Read, IntPtr.Zero, FileMode.Create, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return; // not NTFS (a USB stick, for example): nothing to mark
                using (var s = new FileStream(h, FileAccess.Write))
                {
                    var b = Encoding.ASCII.GetBytes(text);
                    s.Write(b, 0, b.Length);
                }
            }
        }

        /// <summary>The file has the "came from the internet" mark (used by the self-test).</summary>
        public static bool HasMark(string path) => (ReadMark(path) ?? string.Empty).Contains("ZoneId=3");

        public static string ReadMark(string path)
        {
            using (var h = CreateFile(path + ":Zone.Identifier", GenericRead, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                using (var s = new FileStream(h, FileAccess.Read))
                using (var r = new StreamReader(s))
                    return r.ReadToEnd();
            }
        }

        private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

        [ComImport, Guid("4125dd96-e03a-4103-8f70-e0597d803b9c")]
        private class AttachmentServicesClass
        {
        }

        [ComImport, Guid("73db1241-1e85-4581-8e4f-a81e1d0f8c57"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAttachmentExecute
        {
            void SetClientTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetClientGuid(ref Guid guid);
            void SetLocalPath([MarshalAs(UnmanagedType.LPWStr)] string path);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void SetSource([MarshalAs(UnmanagedType.LPWStr)] string url);
            void SetReferrer([MarshalAs(UnmanagedType.LPWStr)] string referrer);
            [PreserveSig] int CheckPolicy();
            [PreserveSig] int Prompt(IntPtr hwnd, int prompt, out int action);
            [PreserveSig] int Save();
            [PreserveSig] int Execute(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string verb, out IntPtr process);
            [PreserveSig] int SaveWithUI(IntPtr hwnd);
            void ClearClientState();
        }
    }
}
