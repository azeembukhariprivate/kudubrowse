using System;
using System.IO;
using System.Linq;

namespace Kudubrowse.Core
{
    /// <summary>Downloaded files that can run programs or change Windows: the browser asks before opening them.</summary>
    public static class DownloadRules
    {
        private static readonly string[] Risky =
        {
            ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".bat", ".cmd", ".com", ".scr", ".pif", ".cpl",
            ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".jar", ".lnk", ".reg", ".dll", ".sys",
            ".iso", ".img", ".vhd", ".vhdx", ".application", ".appref-ms", ".docm", ".xlsm", ".pptm", ".chm", ".url"
        };

        public static bool IsRisky(string fileName)
        {
            var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            return Risky.Contains(ext);
        }

        /// <summary>"12.4 MB", "830 KB", "512 bytes".</summary>
        public static string Size(long bytes)
        {
            if (bytes < 1024) return bytes + " bytes";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1048576.0).ToString(bytes < 10 * 1048576 ? "0.0" : "0") + " MB";
            return (bytes / 1073741824.0).ToString("0.00") + " GB";
        }
    }
}
