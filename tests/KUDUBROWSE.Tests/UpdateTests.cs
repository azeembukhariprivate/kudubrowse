using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Kudubrowse.Core;
using Kudubrowse.Views;

namespace Kudubrowse.Tests
{
    /// <summary>Updating by itself: only the app's own installer, only with GitHub's checksum, and checked after download.</summary>
    public static class UpdateTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("Updates: a newer release's installer is found, with GitHub's checksum", Newer);
            test("Updates: older or same versions, drafts, other sites and missing checksums are ignored", Ignored);
            test("Updates: the installer is kept only when its checksum matches", Downloaded);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected \"{expected}\", got \"{actual}\"");
        }

        private const string Prefix = "https://github.com/azeembukhariprivate/kudubrowse/releases/download/";
        private static readonly string Sha = new string('a', 64);

        private static string Json(string tag, string asset, string url = null, string digest = null, bool draft = false) =>
            "{\"tag_name\":\"" + tag + "\",\"draft\":" + (draft ? "true" : "false") + ",\"prerelease\":false,\"html_url\":\"x\",\"assets\":[" +
            "{\"name\":\"KUDUBROWSE-1.9.0-portable.zip\",\"browser_download_url\":\"" + Prefix + "v1.9.0/KUDUBROWSE-1.9.0-portable.zip\",\"digest\":\"sha256:" + Sha + "\",\"size\":10}," +
            "{\"name\":\"" + asset + "\",\"browser_download_url\":\"" + (url ?? Prefix + tag + "/" + asset) + "\"" +
            (digest == null ? string.Empty : ",\"digest\":\"" + digest + "\"") + ",\"size\":1234}]}";

        private static void Newer()
        {
            var r = AutoUpdate.Parse(Json("v1.9.0", "KUDUBROWSE-1.9.0-setup.exe", digest: "sha256:" + Sha.ToUpperInvariant()), "KUDUBROWSE", new Version(1, 2, 0), Prefix);
            Equal("1.9.0", r?.Version.ToString(3), "the new version");
            Equal(Prefix + "v1.9.0/KUDUBROWSE-1.9.0-setup.exe", r.InstallerUrl, "its installer");
            Equal(Sha, r.Sha256, "its checksum");
            Equal(1234L, r.Size, "its size");
            Equal("1.10.0", AutoUpdate.Parse(Json("v1.10.0", "KUDUBROWSE-1.10.0-setup.exe", digest: "sha256:" + Sha), "KUDUBROWSE", new Version(1, 9, 0), Prefix)?.Version.ToString(3), "1.10 is newer than 1.9");
        }

        private static void Ignored()
        {
            var now = new Version(1, 2, 0);
            Equal(null, AutoUpdate.Parse(Json("v1.2.0", "KUDUBROWSE-1.2.0-setup.exe", digest: "sha256:" + Sha), "KUDUBROWSE", now, Prefix), "the same version");
            Equal(null, AutoUpdate.Parse(Json("v1.1.9", "KUDUBROWSE-1.1.9-setup.exe", digest: "sha256:" + Sha), "KUDUBROWSE", now, Prefix), "an older one");
            Equal(null, AutoUpdate.Parse(Json("v1.9.0", "KUDUBROWSE-1.9.0-setup.exe", digest: "sha256:" + Sha, draft: true), "KUDUBROWSE", now, Prefix), "a draft");
            Equal(null, AutoUpdate.Parse(Json("v1.9.0", "KUDUBROWSE-1.9.0-setup.exe", url: "https://evil.example/KUDUBROWSE-1.9.0-setup.exe", digest: "sha256:" + Sha), "KUDUBROWSE", now, Prefix), "an installer from another site");
            Equal(null, AutoUpdate.Parse(Json("v1.9.0", "KUDUBROWSE-1.9.0-setup.exe"), "KUDUBROWSE", now, Prefix), "no checksum");
            Equal(null, AutoUpdate.Parse(Json("v1.9.0", "KUDUBROWSE-1.9.0-setup.exe", digest: "md5:abc"), "KUDUBROWSE", now, Prefix), "another kind of checksum");
            Equal(null, AutoUpdate.Parse(Json("v1.9.0", "OTHER-1.9.0-setup.exe", digest: "sha256:" + Sha), "KUDUBROWSE", now, Prefix), "another app's installer");
            Equal(null, AutoUpdate.Parse("{ not json", "KUDUBROWSE", now, Prefix), "a broken answer");
        }

        private static void Downloaded()
        {
            var folder = Path.Combine(Path.GetTempPath(), "kudubrowse-update-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            using (var server = new TestServer())
            using (var http = new HttpClient())
            {
                try
                {
                    var body = TestServer.Made(300000, 4);
                    server.Add("setup.exe", new TestServer.Item { Body = body });
                    var path = Path.Combine(Path.GetTempPath(), "kb-sha-" + Guid.NewGuid().ToString("N"));
                    File.WriteAllBytes(path, body);
                    var good = AutoUpdate.Sha256Of(path);
                    File.Delete(path);
                    var release = new NewRelease { Version = new Version(9, 0, 0), InstallerUrl = server.Url("setup.exe"), InstallerName = "KUDUBROWSE-9.0.0-setup.exe", Sha256 = good };
                    var file = AutoUpdate.Download(http, release, folder, CancellationToken.None).GetAwaiter().GetResult();
                    Equal(true, file != null && File.ReadAllBytes(file).SequenceEqual(body), "kept when the checksum matches");
                    Equal(true, AutoUpdate.Matches(file, good), "checked again before installing");
                    File.Delete(file);
                    release.Sha256 = new string('0', 64);
                    Equal(null, AutoUpdate.Download(http, release, folder, CancellationToken.None).GetAwaiter().GetResult(), "refused when it doesn't");
                    Equal(0, Directory.GetFiles(folder).Length, "and nothing is left behind");
                }
                finally
                {
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                }
            }
        }
    }
}
