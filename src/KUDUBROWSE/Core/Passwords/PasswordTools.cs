using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Kudubrowse.Core.Passwords
{
    /// <summary>Making strong passwords, spotting weak or reused ones, and checking for leaked ones.</summary>
    public static class PasswordTools
    {
        private const string Lower = "abcdefghijkmnopqrstuvwxyz"; // no l (looks like 1)
        private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // no I or O
        private const string Digits = "23456789"; // no 0 or 1
        private const string Symbols = "-_.!?@#%+=";

        /// <summary>A random password with lower and upper case letters, digits and a symbol (from Windows' secure random numbers).</summary>
        public static string Generate(int length = 16)
        {
            if (length < 8) throw new ArgumentOutOfRangeException(nameof(length));
            var all = Lower + Upper + Digits + Symbols;
            using (var rng = RandomNumberGenerator.Create())
            {
                while (true)
                {
                    var chars = new char[length];
                    for (var i = 0; i < length; i++) chars[i] = all[Next(rng, all.Length)];
                    var s = new string(chars);
                    if (s.Any(Lower.Contains) && s.Any(Upper.Contains) && s.Any(Digits.Contains) && s.Any(Symbols.Contains)) return s;
                }
            }
        }

        /// <summary>An unbiased random number below <paramref name="max"/>.</summary>
        private static int Next(RandomNumberGenerator rng, int max)
        {
            var b = new byte[4];
            var limit = uint.MaxValue - uint.MaxValue % (uint)max;
            uint v;
            do
            {
                rng.GetBytes(b);
                v = BitConverter.ToUInt32(b, 0);
            } while (v >= limit);
            return (int)(v % (uint)max);
        }

        private static readonly HashSet<string> Common = new HashSet<string>(new[]
        {
            "password", "passw0rd", "123456", "12345678", "123456789", "1234567890", "qwerty", "qwertyuiop", "abc123", "111111",
            "letmein", "welcome", "iloveyou", "admin", "monkey", "dragon", "football", "sunshine", "princess", "000000"
        });

        /// <summary>Is this password easy to guess (short, all the same kind of character, or a very common one)?</summary>
        public static bool IsWeak(string p)
        {
            if (string.IsNullOrEmpty(p) || p.Length < 8) return true;
            if (Common.Contains(p.ToLowerInvariant())) return true;
            var kinds = (p.Any(char.IsLower) ? 1 : 0) + (p.Any(char.IsUpper) ? 1 : 0) + (p.Any(char.IsDigit) ? 1 : 0) + (p.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
            if (kinds <= 1 && p.Length < 14) return true;
            return p.Distinct().Count() <= 3;
        }

        // ================================================================ leaks

        /// <summary>The SHA-1 of the password in capitals: its first 5 characters are all that's sent to check for leaks.</summary>
        public static string Sha1Hex(string password)
        {
            using (var sha = SHA1.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(password)).Select(b => b.ToString("X2")));
        }

        /// <summary>The address asked for one password: only the first 5 characters of its SHA-1 (shared by hundreds of passwords).</summary>
        public static string LeakQueryUrl(string sha1Hex) => "https://api.pwnedpasswords.com/range/" + sha1Hex.Substring(0, 5);

        /// <summary>In the service's answer (one "rest-of-hash:count" per line), how often this password has leaked; 0 if not.</summary>
        public static int LeakCount(string sha1Hex, string response)
        {
            var rest = sha1Hex.Substring(5);
            foreach (var line in (response ?? string.Empty).Split('\n'))
            {
                var parts = line.Trim().Split(':');
                if (parts.Length == 2 && string.Equals(parts[0], rest, StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[1], out var n)) return n;
            }
            return 0;
        }

        // ================================================================ importing

        /// <summary>
        /// Reads a passwords file exported from Chrome (name,url,username,password,note), Edge or other browsers and
        /// password managers that use the same column names (url or login_uri, username or login_username, password or
        /// login_password).
        /// </summary>
        public static List<(string Url, string Username, string Password)> ReadCsv(string text)
        {
            var rows = ParseCsv(text);
            var result = new List<(string, string, string)>();
            if (rows.Count == 0) return result;
            var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
            int Col(params string[] names) => names.Select(n => header.IndexOf(n)).FirstOrDefault(i => i >= 0, -1);
            var url = Col("url", "login_uri", "website", "origin");
            var user = Col("username", "login_username", "user", "login");
            var pass = Col("password", "login_password");
            if (url < 0 || pass < 0) throw new InvalidDataException("This file doesn't have the columns of a passwords file (url, username, password).");
            foreach (var r in rows.Skip(1))
            {
                string Get(int i) => i >= 0 && i < r.Count ? r[i] : string.Empty;
                if (Get(url).Length == 0 || Get(pass).Length == 0) continue;
                result.Add((Get(url), Get(user), Get(pass)));
            }
            return result;
        }

        private static int FirstOrDefault(this IEnumerable<int> items, Func<int, bool> test, int fallback)
        {
            foreach (var i in items) if (test(i)) return i;
            return fallback;
        }

        /// <summary>Comma-separated values with "quoted, fields" and "" for a quote inside one; rows end at line breaks outside quotes.</summary>
        public static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            text = (text ?? string.Empty).TrimStart('﻿');
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else quoted = false;
                    }
                    else field.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(field.ToString());
                    field.Clear();
                    if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                    row = new List<string>();
                }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>The newest file in a folder that looks like a passwords export ("Chrome Passwords.csv", ...), or null.</summary>
        public static string FindExport(string folder)
        {
            try
            {
                return new DirectoryInfo(folder).GetFiles("*.csv")
                    .Where(f => f.Name.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(f => f.LastWriteTimeUtc).Select(f => f.FullName).FirstOrDefault();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return null;
            }
        }
    }
}
