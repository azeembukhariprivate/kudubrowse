// Where the passwords are kept: the vault encrypted with Windows' DPAPI for this user, and Verify, which asks Windows
// Hello or the Windows password before showing passwords.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Kudubrowse.Core;
using Kudubrowse.Core.Passwords;
using Windows.Foundation;
using Windows.Security.Credentials.UI;

namespace Kudubrowse.Views
{
    /// <summary>Encrypts with Windows' own protection for the signed-in user (DPAPI): another account or PC can't read the file.</summary>
    public sealed class WindowsProtector : IProtector
    {
        /// <summary>Extra bytes mixed into the encryption, so other programs can't read the file as theirs.</summary>
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KUDUBROWSE saved passwords v1");
        /// <summary>Encrypts for this Windows user.</summary>
        public byte[] Protect(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        /// <summary>Decrypts.</summary>
        public byte[] Unprotect(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
    }

    /// <summary>The saved passwords, shared by every window.</summary>
    public static class PasswordStore
    {
        /// <summary>The vault, once read.</summary>
        private static Vault _vault;
        /// <summary>The vault.</summary>
        public static Vault Vault => _vault ?? (_vault = Vault.Load(Path.Combine(BrowserLibrary.Folder, "passwords.dat"), new WindowsProtector()));

        /// <summary>For the self-test: read the file again from <see cref="BrowserLibrary.Folder"/>.</summary>
        public static void Reload() => _vault = null;
    }

    /// <summary>
    /// Makes sure it's really the person at the PC before a saved password is shown or copied: Windows Hello (PIN,
    /// fingerprint or face) when it's set up, otherwise the Windows account password. Not asked again for a minute.
    /// </summary>
    public static class Verify
    {
        /// <summary>Until when the person doesn't need to be asked again (a minute).</summary>
        private static DateTime _okUntil = DateTime.MinValue;

        /// <summary>For the self-test: the answer to give instead of asking.</summary>
        public static bool? AutoAnswer { get; set; }

        /// <summary>
        /// Asks the person to show it's them (Windows Hello, else the Windows password); true when they did.
        /// </summary>
        public static async Task<bool> Ask(Window owner, string reason)
        {
            if (AutoAnswer.HasValue) return AutoAnswer.Value;
            if (DateTime.UtcNow < _okUntil) return true;
            bool? ok = await TryHello(owner, reason);
            if (ok == null) ok = AskPassword(owner, reason);
            if (ok == true) _okUntil = DateTime.UtcNow.AddMinutes(1);
            return ok == true;
        }

        /// <summary>Asks again next time.</summary>
        public static void Forget() => _okUntil = DateTime.MinValue;

        // ================================================================ Windows Hello

        [ComImport, Guid("39E050C3-4E74-441A-8DC0-B81104DF949C"), InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        private interface IUserConsentVerifierInterop
        {
            IAsyncOperation<UserConsentVerificationResult> RequestVerificationForWindowAsync(IntPtr appWindow, [MarshalAs(UnmanagedType.HString)] string message, [In] ref Guid riid);
        }

        /// <summary>True or false from Windows Hello; null when it isn't set up (or can't be asked), so the password is asked instead.</summary>
        private static async Task<bool?> TryHello(Window owner, string reason)
        {
            try
            {
                var available = await UserConsentVerifier.CheckAvailabilityAsync();
                if (available != UserConsentVerifierAvailability.Available) return null;
                var hwnd = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
                var interop = (IUserConsentVerifierInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(UserConsentVerifier));
                var iid = typeof(IAsyncOperation<UserConsentVerificationResult>).GUID;
                var result = await interop.RequestVerificationForWindowAsync(hwnd, reason, ref iid);
                if (result == UserConsentVerificationResult.Verified) return true;
                if (result == UserConsentVerificationResult.Canceled || result == UserConsentVerificationResult.RetriesExhausted) return false;
                return null;
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return null;
            }
        }

        // ================================================================ the Windows password

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDUI_INFO
        {
            /// <summary>The details of Windows' password question, as Windows wants them.</summary>
            public int cbSize;
            public IntPtr hwndParent;
            public string pszMessageText;
            public string pszCaptionText;
            public IntPtr hbmBanner;
        }

        /// <summary>Asks for the Windows password.</summary>
        [DllImport("credui.dll", CharSet = CharSet.Unicode)]
        private static extern int CredUIPromptForWindowsCredentials(ref CREDUI_INFO info, int authError, ref uint authPackage, IntPtr inBuffer, uint inSize,
            out IntPtr outBuffer, out uint outSize, ref bool save, int flags);

        /// <summary>Reads the answer.</summary>
        [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredUnPackAuthenticationBuffer(int flags, IntPtr buffer, uint size, StringBuilder user, ref int userLen,
            StringBuilder domain, ref int domainLen, StringBuilder password, ref int passwordLen);

        /// <summary>Checks a Windows password.</summary>
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LogonUser(string user, string domain, string password, int logonType, int provider, out IntPtr token);

        /// <summary>Frees a handle.</summary>
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);

        /// <summary>Frees memory Windows gave.</summary>
        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr p);

        /// <summary>Ask for this user's own password.</summary>
        private const int CREDUIWIN_ENUMERATE_CURRENT_USER = 0x200;
        /// <summary>Windows' error for a wrong password.</summary>
        private const int ERROR_LOGON_FAILURE = 1326;

        /// <summary>Windows' own sign-in box for this account; the password typed is checked by Windows, then forgotten.</summary>
        private static bool AskPassword(Window owner, string reason)
        {
            var error = 0;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var info = new CREDUI_INFO
                {
                    cbSize = Marshal.SizeOf(typeof(CREDUI_INFO)),
                    hwndParent = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle,
                    pszCaptionText = AppInfo.Name,
                    pszMessageText = reason + "\n\nType your Windows password."
                };
                uint package = 0;
                var save = false;
                var r = CredUIPromptForWindowsCredentials(ref info, error, ref package, IntPtr.Zero, 0, out var buffer, out var size, ref save, CREDUIWIN_ENUMERATE_CURRENT_USER);
                if (r != 0) return false; // cancelled
                try
                {
                    var user = new StringBuilder(256);
                    var domain = new StringBuilder(256);
                    var password = new StringBuilder(256);
                    int ul = 256, dl = 256, pl = 256;
                    if (!CredUnPackAuthenticationBuffer(0, buffer, size, user, ref ul, domain, ref dl, password, ref pl)) return false;
                    var name = user.ToString();
                    var dom = domain.Length > 0 ? domain.ToString() : null;
                    var slash = name.IndexOf('\\');
                    if (dom == null && slash > 0)
                    {
                        dom = name.Substring(0, slash);
                        name = name.Substring(slash + 1);
                    }
                    var ok = LogonUser(name, dom, password.ToString(), 2 /* interactive */, 0, out var token);
                    password.Clear();
                    if (ok)
                    {
                        CloseHandle(token);
                        return true;
                    }
                    error = ERROR_LOGON_FAILURE;
                }
                finally
                {
                    // The sign-in box's buffer holds the password: wipe it before giving it back.
                    var zero = new byte[size];
                    Marshal.Copy(zero, 0, buffer, (int)size);
                    CoTaskMemFree(buffer);
                }
            }
            return false;
        }
    }
}
