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
            test("Sign-in codes: made as authenticator apps make them (the standard's own examples)", CodesMatchTheStandard);
            test("Sign-in codes: setup keys and otpauth links are read; other text isn't", KeysAreRead);
            test("Sign-in codes: kept with the login, encrypted, and taken away again", CodesAreKept);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected \"{expected}\", got \"{actual}\"");
        }

        private static void CodesMatchTheStandard()
        {
            // RFC 6238, appendix B: 8-digit codes for these times, with each kind of scrambling.
            var sha1 = new SignInKey { Secret = System.Text.Encoding.ASCII.GetBytes("12345678901234567890"), Digits = 8 };
            var sha256 = new SignInKey { Secret = System.Text.Encoding.ASCII.GetBytes("12345678901234567890123456789012"), Digits = 8, Algorithm = "SHA256" };
            var sha512 = new SignInKey { Secret = System.Text.Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234"), Digits = 8, Algorithm = "SHA512" };
            var cases = new (long Time, string A, string B, string C)[]
            {
                (59, "94287082", "46119246", "90693936"),
                (1111111109, "07081804", "68084774", "25091201"),
                (1111111111, "14050471", "67062674", "99943326"),
                (1234567890, "89005924", "91819424", "93441116"),
                (2000000000, "69279037", "90698825", "38618901"),
                (20000000000, "65353130", "77737706", "47863826")
            };
            foreach (var c in cases)
            {
                var at = DateTimeOffset.FromUnixTimeSeconds(c.Time);
                Equal(c.A, SignInCodes.Code(sha1, at), "SHA1 at " + c.Time);
                Equal(c.B, SignInCodes.Code(sha256, at), "SHA256 at " + c.Time);
                Equal(c.C, SignInCodes.Code(sha512, at), "SHA512 at " + c.Time);
            }
            var six = SignInCodes.Parse("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");
            Equal("287082", SignInCodes.Code(six, DateTimeOffset.FromUnixTimeSeconds(59)), "6 digits (the last six)");
            Equal(1, SignInCodes.SecondsLeft(six, DateTimeOffset.FromUnixTimeSeconds(59)), "seconds left");
            Equal(30, SignInCodes.SecondsLeft(six, DateTimeOffset.FromUnixTimeSeconds(60)), "a new code");
            Equal("287 082", SignInCodes.Show("287082"), "shown in halves");
        }

        private static void KeysAreRead()
        {
            var plain = SignInCodes.Parse(" gezd gnbv gy3t qojq-gezd gnbv gy3t qojq ");
            Equal("12345678901234567890", System.Text.Encoding.ASCII.GetString(plain.Secret), "spaces, dashes and small letters");
            var link = SignInCodes.Parse("otpauth://totp/Example:azeem%40example.com?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&issuer=Example&digits=8&period=60&algorithm=sha256");
            Equal(8, link.Digits, "digits from the link");
            Equal(60, link.Period, "period from the link");
            Equal("SHA256", link.Algorithm, "algorithm from the link");
            Equal(20, link.Secret.Length, "the secret");
            Equal(null, SignInCodes.Parse("not a key!"), "other text");
            Equal(null, SignInCodes.Parse("ABC"), "too short");
            Equal(null, SignInCodes.Parse(""), "nothing");
            Equal(null, SignInCodes.Parse("otpauth://hotp/x?secret=GEZDGNBVGY3TQOJQ"), "counter keys aren't time codes");
            Equal(null, SignInCodes.Parse("otpauth://totp/x?secret=GEZDGNBVGY3TQOJQ&digits=12"), "odd number of digits");
            Equal(null, SignInCodes.Parse("otpauth://totp/x?issuer=nothing"), "no secret");
        }

        private static void CodesAreKept()
        {
            var file = Temp();
            try
            {
                var v = Vault.Load(file, new TestProtector(3));
                var l = v.Save("https://bank.example/login", "azeem", "pw");
                Equal(null, l.CodeNow(), "no code before a key is added");
                Equal(false, v.SetCodeKey(l, "not a key"), "a wrong key isn't kept");
                Equal(true, v.SetCodeKey(l, "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"), "kept");
                Equal("287082", l.CodeNow(DateTimeOffset.FromUnixTimeSeconds(59)), "the code");
                Equal(false, File.ReadAllText(file).Contains("GEZDGNBV"), "the key isn't readable in the file");
                var again = Vault.Load(file, new TestProtector(3));
                Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", again.Logins.Single().CodeKey, "read back");
                Equal(true, again.SetCodeKey(again.Logins.Single(), null), "taken away");
                Equal(null, Vault.Load(file, new TestProtector(3)).Logins.Single().CodeKey, "gone");
            }
            finally
            {
                File.Delete(file);
            }
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
