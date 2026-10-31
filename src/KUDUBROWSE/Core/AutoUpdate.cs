using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kudubrowse.Core
{
    /// <summary>A newer version published on GitHub, with its installer and the installer's SHA-256 checksum.</summary>
    public sealed class NewRelease
    {
        public Version Version { get; set; }
        public string InstallerUrl { get; set; }
        public string InstallerName { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
    }

    /// <summary>
    /// Updating by itself: reads GitHub's answer about the latest release, picks its installer (only one published on
    /// the app's own Releases page, and only with a SHA-256 checksum from GitHub), downloads it and checks it.
    /// </summary>
    public static class AutoUpdate
    {
        [DataContract]
        private sealed class Release
        {
            [DataMember(Name = "tag_name")] public string Tag { get; set; }
            [DataMember(Name = "draft")] public bool Draft { get; set; }
            [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
            [DataMember(Name = "assets")] public List<Asset> Assets { get; set; }
        }

        [DataContract]
        private sealed class Asset
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "browser_download_url")] public string Url { get; set; }
            [DataMember(Name = "digest")] public string Digest { get; set; }
            [DataMember(Name = "size")] public long Size { get; set; }
        }

        public static string LatestUrl(string owner, string repo) => $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

        /// <summary>
        /// The release described by <paramref name="json"/> when it's newer than <paramref name="current"/> and has a
        /// usable installer ("{app}-{version}-setup.exe" under <paramref name="downloadPrefix"/>, with a sha256 digest);
        /// otherwise null.
        /// </summary>
        public static NewRelease Parse(string json, string app, Version current, string downloadPrefix)
        {
            Release r;
            try
            {
                using (var s = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty)))
                    r = (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(s);
            }
            catch (Exception ex) when (ex is SerializationException || ex is ArgumentException || ex is InvalidCastException)
            {
                return null;
            }
            if (r == null || r.Draft || r.Prerelease || string.IsNullOrEmpty(r.Tag)) return null;
            var tag = r.Tag.TrimStart('v', 'V');
            if (!Version.TryParse(tag.Contains(".") ? tag : tag + ".0", out var parsed)) return null;
            var version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
            var now = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
            if (version <= now) return null;
            var name = $"{app}-{version.ToString(3)}-setup.exe";
            var asset = (r.Assets ?? new List<Asset>()).FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (asset == null || string.IsNullOrEmpty(asset.Url) || !asset.Url.StartsWith(downloadPrefix, StringComparison.Ordinal)) return null;
            var digest = asset.Digest ?? string.Empty;
            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
            var sha = digest.Substring(7).ToLowerInvariant();
            if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) return null;
            return new NewRelease { Version = version, InstallerUrl = asset.Url, InstallerName = name, Sha256 = sha, Size = asset.Size };
        }

        public static string Sha256Of(string file)
        {
            using (var sha = SHA256.Create())
            using (var s = File.OpenRead(file))
                return string.Concat(sha.ComputeHash(s).Select(b => b.ToString("x2")));
        }

        /// <summary>The file is there and has exactly the checksum GitHub gave for it.</summary>
        public static bool Matches(string file, string sha256)
        {
            try { return File.Exists(file) && string.Equals(Sha256Of(file), sha256, StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
        }

        /// <summary>
        /// Downloads the installer into <paramref name="folder"/> and keeps it only if its checksum matches. Returns its
        /// path, or null (a file that doesn't match is deleted).
        /// </summary>
        public static async Task<string> Download(HttpClient http, NewRelease release, string folder, CancellationToken cancel)
        {
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, release.InstallerName);
            var part = file + ".part";
            using (var response = await http.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) return null;
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = File.Create(part))
                    await input.CopyToAsync(output, 81920, cancel).ConfigureAwait(false);
            }
            if (!Matches(part, release.Sha256))
            {
                File.Delete(part);
                return null;
            }
            if (File.Exists(file)) File.Delete(file);
            File.Move(part, file);
            return file;
        }
    }
}
