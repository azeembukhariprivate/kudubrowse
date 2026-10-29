using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Kudubrowse.Core
{
    /// <summary>
    /// Rules for downloads: files that can run programs or change Windows (the browser asks before opening them), the folder
    /// a file goes in when downloads are sorted by type, names that don't replace an existing file, and splitting a big file
    /// into pieces fetched at the same time.
    /// </summary>
    public static class DownloadRules
    {
        private static readonly string[] Risky =
        {
            ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".bat", ".cmd", ".com", ".scr", ".pif", ".cpl",
            ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".jar", ".lnk", ".reg", ".dll", ".sys",
            ".iso", ".img", ".vhd", ".vhdx", ".application", ".appref-ms", ".docm", ".xlsm", ".pptm", ".chm", ".url",
            ".one", ".onepkg", ".xll", ".xlam", ".ppam", ".dotm", ".xltm", ".potm", ".wsc", ".sct", ".msc", ".msp", ".mst", ".inf",
            ".scf", ".gadget", ".vb", ".msh", ".psc1", ".ps1xml", ".appinstaller", ".diagcab", ".settingcontent-ms", ".library-ms", ".search-ms"
        };

        public static bool IsRisky(string fileName)
        {
            var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            return Risky.Contains(ext);
        }

        private static readonly Dictionary<string, string[]> Kinds = new Dictionary<string, string[]>
        {
            ["Pictures"] = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".heic", ".heif", ".avif", ".tif", ".tiff", ".ico", ".raw", ".psd" },
            ["Music"] = new[] { ".mp3", ".m4a", ".aac", ".wav", ".flac", ".ogg", ".opus", ".wma", ".mid", ".midi" },
            ["Videos"] = new[] { ".mp4", ".mkv", ".webm", ".mov", ".avi", ".wmv", ".m4v", ".mpg", ".mpeg", ".3gp", ".flv" },
            ["Documents"] = new[] { ".pdf", ".doc", ".docx", ".docm", ".xls", ".xlsx", ".xlsm", ".csv", ".ppt", ".pptx", ".pptm", ".odt", ".ods", ".odp",
                                    ".rtf", ".txt", ".md", ".epub", ".pst", ".ost", ".msg", ".eml", ".vcf", ".ics", ".json", ".xml" },
            ["Archives"] = new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".cab", ".zst" },
            ["Programs"] = new[] { ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".iso", ".img", ".vhd", ".vhdx", ".apk", ".bat", ".cmd", ".ps1" }
        };

        /// <summary>The folder a file goes in when downloads are sorted by type ("Pictures", "Programs"...), or null for other files.</summary>
        public static string Kind(string fileName)
        {
            var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            return ext.Length == 0 ? null : Kinds.FirstOrDefault(k => k.Value.Contains(ext)).Key;
        }

        /// <summary>Where a download goes: the downloads folder, or the folder for its type in there when sorting is on.</summary>
        public static string FolderFor(string downloads, string fileName, bool sortByType)
        {
            var kind = sortByType ? Kind(fileName) : null;
            return kind == null ? downloads : Path.Combine(downloads, kind);
        }

        /// <summary>A safe file name from what a site suggests: no folders, no characters Windows refuses, never empty.</summary>
        public static string SafeName(string suggested)
        {
            var name = suggested ?? string.Empty;
            name = name.Substring(name.LastIndexOfAny(new[] { '/', '\\' }) + 1);
            name = new string(name.Select(c => "<>:\"/\\|?*".IndexOf(c) >= 0 || c < 32 ? '_' : c).ToArray()).Trim().TrimEnd('.');
            return name.Length == 0 ? "download" : name.Length > 180 ? name.Substring(0, 180 - Path.GetExtension(name).Length) + Path.GetExtension(name) : name;
        }

        /// <summary>The path itself when nothing is there yet, otherwise "name (1).ext", "name (2).ext"... that is free.</summary>
        public static string Unique(string path, Func<string, bool> taken = null)
        {
            taken = taken ?? (p => File.Exists(p) || Directory.Exists(p) || File.Exists(p + PartExtension));
            if (!taken(path)) return path;
            var folder = Path.GetDirectoryName(path) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (var n = 1; ; n++)
            {
                var candidate = Path.Combine(folder, $"{name} ({n}){ext}");
                if (!taken(candidate)) return candidate;
            }
        }

        /// <summary>A file still arriving is kept as "name.ext.kudupart" until it's whole.</summary>
        public const string PartExtension = ".kudupart";

        /// <summary>Files at least this big (20 MB) are fetched in pieces at the same time, when the site allows it.</summary>
        public const long FastFrom = 20L * 1024 * 1024;

        /// <summary>Splits <paramref name="total"/> bytes into up to <paramref name="connections"/> pieces of at least 4 MB.</summary>
        public static List<DownloadPiece> Plan(long total, int connections)
        {
            var pieces = new List<DownloadPiece>();
            if (total <= 0) return pieces;
            var count = (int)Math.Max(1, Math.Min(connections, total / (4L * 1024 * 1024)));
            var size = total / count;
            for (var i = 0; i < count; i++)
            {
                var start = i * size;
                var end = i == count - 1 ? total - 1 : start + size - 1;
                pieces.Add(new DownloadPiece { Start = start, End = end });
            }
            return pieces;
        }

        /// <summary>Reads a "bytes 0-0/12345" answer: the file's whole size, or -1.</summary>
        public static long TotalFromContentRange(string contentRange)
        {
            if (string.IsNullOrEmpty(contentRange)) return -1;
            var slash = contentRange.LastIndexOf('/');
            return slash > 0 && long.TryParse(contentRange.Substring(slash + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var total) ? total : -1;
        }

        /// <summary>"3 minutes left", "20 seconds left", or empty when it can't be told.</summary>
        public static string TimeLeft(long remaining, double bytesPerSecond)
        {
            if (bytesPerSecond < 1 || remaining <= 0) return string.Empty;
            var seconds = remaining / bytesPerSecond;
            if (seconds < 60) return Math.Max(1, (int)Math.Ceiling(seconds)) + " seconds left";
            if (seconds < 3600) { var m = (int)Math.Ceiling(seconds / 60); return m == 1 ? "1 minute left" : m + " minutes left"; }
            var h = seconds / 3600;
            return h < 1.5 ? "about 1 hour left" : "about " + Math.Round(h) + " hours left";
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
