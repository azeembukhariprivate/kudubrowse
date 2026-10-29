using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Kudubrowse.Core.Passwords;

namespace Kudubrowse.Tests
{
    public static class PasswordTests
    {
        /// <summary>Stands in for Windows' protection in the tests: AES with a key, so a different key can't read the file.</summary>
        private sealed class TestProtector : IProtector
        {
            private readonly byte[] _key;
            public TestProtector(byte key) { _key = Enumerable.Repeat(key, 32).ToArray(); }

            public byte[] Protect(byte[] data)
            {
                using (var aes = Aes.Create())
                {
                    aes.Key = _key;
                    aes.GenerateIV();
                    var c = aes.CreateEncryptor().TransformFinalBlock(data, 0, data.Length);
                    return aes.IV.Concat(c).ToArray();
                }
            }

            public byte[] Unprotect(byte[] data)
            {
                using (var aes = Aes.Create())
                {
                    aes.Key = _key;
                    aes.IV = data.Take(16).ToArray();
                    return aes.CreateDecryptor().TransformFinalBlock(data, 16, data.Length - 16);
                }
            }
        }

        public static void Run(Action<string, Action> test)
        {
            test("Passwords: saved encrypted, read back, updated, removed", VaultWorks);
            test("Passwords: only offered on the same site (and its other parts), never elsewhere", MatchingIsStrict);
            test("Passwords: \"never for this site\", and nothing to save when unchanged", SaveDecisions);
            test("Passwords: a file that can't be read is kept aside, not overwritten", Unreadable);
            test("Passwords: strong ones are random and mixed; weak ones are spotted", Strength);
            test("Passwords: the leak check sends only 5 characters of the scrambled password", LeakCheck);
            test("Passwords: Chrome's export file and others like it are read", Csv);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected \"{expected}\", got \"{actual}\"");
        }

        private static string Temp() => Path.Combine(Path.GetTempPath(), "kudubrowse-test-" + Guid.NewGuid().ToString("N") + ".dat");

        private static void VaultWorks()
        {
            var file = Temp();
            try
            {
                var v = Vault.Load(file, new TestProtector(1));
                v.Save("https://accounts.example.com/login?next=/", "azeem", "Secret-123!");
                v.Save("https://accounts.example.com/other", "azeem", "Changed-456!");
                Equal(1, v.Logins.Count, "same site and name is one login");
                var raw = File.ReadAllBytes(file);
                Equal(false, System.Text.Encoding.UTF8.GetString(raw).Contains("Changed-456"), "the file isn't readable as it is");
                var again = Vault.Load(file, new TestProtector(1));
                Equal("Changed-456!", again.Logins.Single().Password, "read back with the new password");
                Equal("https://accounts.example.com", again.Logins.Single().Origin, "kept by site");
                again.Remove(again.Logins.Single());
                Equal(0, Vault.Load(file, new TestProtector(1)).Logins.Count, "removed");
            }
            finally
            {
                File.Delete(file);
            }
        }

        private static void MatchingIsStrict()
        {
            var file = Temp();
            try
            {
                var v = Vault.Load(file, new TestProtector(2));
                v.Save("https://login.bank.co.uk/", "me", "a");
                v.Save("https://www.bank.co.uk/", "me2", "b");
                v.Save("http://www.bank.co.uk/", "plain", "c");
                v.Save("https://evilbank.co.uk/", "x", "d");
                v.Save("https://bank.co.uk.evil.com/", "y", "e");
                var found = v.For("https://www.bank.co.uk/signin");
                Equal("me2|me", string.Join("|", found.Select(l => l.Username)), "exact site first, then its other parts");
                Equal("plain", string.Join("|", v.For("http://www.bank.co.uk/").Select(l => l.Username)), "an insecure page only gets insecure logins");
                Equal(0, v.For("https://bank.co.uk.example.org/").Count, "not on a look-alike");
                Equal(0, v.For("file:///C:/x.html").Count, "not on files");
                Equal("https://example.com:8443", Vault.OriginOf("https://EXAMPLE.com:8443/a"), "port kept");
                Equal(null, Vault.OriginOf("javascript:alert(1)"), "not a site");
            }
            finally
            {
                File.Delete(file);
            }
        }

        private static void SaveDecisions()
        {
            var file = Temp();
            try
            {
                var v = Vault.Load(file, new TestProtector(3));
                Equal(Vault.SaveKind.New, v.Check("https://shop.example/", "me", "pw1"), "new");
                v.Save("https://shop.example/", "me", "pw1");
                Equal(Vault.SaveKind.Nothing, v.Check("https://shop.example/cart", "me", "pw1"), "unchanged");
                Equal(Vault.SaveKind.Update, v.Check("https://shop.example/", "me", "pw2"), "changed");
                Equal(Vault.SaveKind.Nothing, v.Check("https://shop.example/", "me", ""), "empty password");
                v.NeverFor("https://forum.example/login");
                Equal(Vault.SaveKind.Nothing, v.Check("https://forum.example/", "x", "y"), "never for this site");
                Equal(1, Vault.Load(file, new TestProtector(3)).NeverSaveOrigins.Count, "remembered");
            }
            finally
            {
                File.Delete(file);
            }
        }

        private static void Unreadable()
        {
            var file = Temp();
            try
            {
                Vault.Load(file, new TestProtector(4)).Save("https://a.example/", "u", "p");
                var other = Vault.Load(file, new TestProtector(5));
                Equal(0, other.Logins.Count, "can't be read with another key");
                Equal(false, File.Exists(file), "moved aside");
                Equal(1, Directory.GetFiles(Path.GetDirectoryName(file), Path.GetFileName(file) + ".unreadable-*").Length, "kept");
            }
            finally
            {
                foreach (var f in Directory.GetFiles(Path.GetDirectoryName(file), Path.GetFileName(file) + "*")) File.Delete(f);
            }
        }

        private static void Strength()
        {
            var seen = new HashSet<string>();
            for (var i = 0; i < 200; i++)
            {
                var p = PasswordTools.Generate();
                Equal(16, p.Length, "length");
                Equal(false, PasswordTools.IsWeak(p), "strong: " + p);
                Equal(true, seen.Add(p), "never the same");
                Equal(false, p.Any(c => "lIO01".Contains(c)), "no look-alike characters");
            }
            foreach (var weak in new[] { "", "abc", "password", "Password", "12345678", "aaaaaaaaaaaa", "abcdefghij" })
                Equal(true, PasswordTools.IsWeak(weak), "weak: " + weak);
            Equal(false, PasswordTools.IsWeak("correct horse battery staple"), "a long passphrase is fine");
            Equal(false, PasswordTools.IsWeak("Kochi-2025!"), "mixed is fine");
        }

        private static void LeakCheck()
        {
            var hash = PasswordTools.Sha1Hex("password");
            Equal("5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8", hash, "SHA-1");
            Equal("https://api.pwnedpasswords.com/range/5BAA6", PasswordTools.LeakQueryUrl(hash), "only the first 5 characters are sent");
            var answer = "003D68EB55068C33ACE09247EE4C639306B:3\r\n1E4C9B93F3F0682250B6CF8331B7EE68FD8:9545824\r\n";
            Equal(9545824, PasswordTools.LeakCount(hash, answer), "found in the answer");
            Equal(0, PasswordTools.LeakCount(PasswordTools.Sha1Hex("Kochi-2025!"), answer), "not found");
        }

        private static void Csv()
        {
            var chrome = "name,url,username,password,note\r\nexample.com,https://example.com/login,azeem,\"pa,ss\"\"word\",\r\n" +
                         "no-password,https://x.com/,u,,\r\nandroid,android://abc@com.app/,u,p,\r\nbank,https://bank.example/,,\"multi\nline\",note\n";
            var rows = PasswordTools.ReadCsv(chrome);
            Equal(3, rows.Count, "rows with a password");
            Equal("pa,ss\"word", rows[0].Password, "commas and quotes inside a field");
            Equal("multi\nline", rows[2].Password, "a line break inside a field");
            var bitwarden = "folder,favorite,type,name,notes,fields,reprompt,login_uri,login_username,login_password,login_totp\n,,login,Site,,,0,https://site.example/,me,pw,\n";
            Equal("https://site.example/|me|pw", string.Join("|", PasswordTools.ReadCsv(bitwarden).Select(r => r.Url + "|" + r.Username + "|" + r.Password)), "other column names");
            var bad = false;
            try { PasswordTools.ReadCsv("a,b\n1,2\n"); }
            catch (InvalidDataException) { bad = true; }
            Equal(true, bad, "a file without the columns is refused");

            var file = Temp();
            try
            {
                var v = Vault.Load(file, new TestProtector(6));
                Equal(2, v.Merge(rows), "android addresses and the like left out");
                Equal(0, v.Merge(rows), "nothing twice");
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}
