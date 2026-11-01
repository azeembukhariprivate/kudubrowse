using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Kudubrowse.Core.Passwords
{
    /// <summary>A site's two-step sign-in key: what the 6-digit codes are made from.</summary>
    public sealed class SignInKey
    {
        public byte[] Secret { get; set; }
        public int Digits { get; set; } = 6;
        public int Period { get; set; } = 30;
        /// <summary>"SHA1" (almost every site), "SHA256" or "SHA512".</summary>
        public string Algorithm { get; set; } = "SHA1";
    }

    /// <summary>
    /// Two-step sign-in codes: the codes that change every 30 seconds, made from a key the site gives when two-step
    /// sign-in is turned on (the standard way, as authenticator apps do: RFC 6238). Everything happens on this PC.
    /// </summary>
    public static class SignInCodes
    {
        /// <summary>
        /// Reads a key as a site gives it: the setup key (letters and digits, often in groups, shown under "Can't scan the
        /// code?"), or the otpauth:// link a QR code holds. Null when it isn't one.
        /// </summary>
        public static SignInKey Parse(string text)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0) return null;
            if (text.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !string.Equals(uri.Host, "totp", StringComparison.OrdinalIgnoreCase)) return null;
                var key = new SignInKey();
                foreach (var part in uri.Query.TrimStart('?').Split('&'))
                {
                    var eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    var name = Uri.UnescapeDataString(part.Substring(0, eq)).ToLowerInvariant();
                    var value = Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' '));
                    switch (name)
                    {
                        case "secret": key.Secret = Base32(value); break;
                        case "digits": if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var d)) key.Digits = d; break;
                        case "period": if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var p)) key.Period = p; break;
                        case "algorithm": key.Algorithm = value.ToUpperInvariant(); break;
                    }
                }
                return Valid(key) ? key : null;
            }
            var plain = new SignInKey { Secret = Base32(text) };
            return Valid(plain) ? plain : null;
        }

        private static bool Valid(SignInKey k) =>
            k.Secret != null && k.Secret.Length >= 5 && (k.Digits == 6 || k.Digits == 7 || k.Digits == 8) && k.Period >= 10 && k.Period <= 300 &&
            (k.Algorithm == "SHA1" || k.Algorithm == "SHA256" || k.Algorithm == "SHA512");

        /// <summary>Letters A-Z and digits 2-7 (spaces, dashes and "=" at the end don't count); null when it isn't that.</summary>
        public static byte[] Base32(string text)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var clean = new string((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).TrimEnd('=').ToUpperInvariant();
            if (clean.Length == 0) return null;
            var bytes = new byte[clean.Length * 5 / 8];
            int buffer = 0, bits = 0, i = 0;
            foreach (var c in clean)
            {
                var v = alphabet.IndexOf(c);
                if (v < 0) return null;
                buffer = (buffer << 5) | v;
                bits += 5;
                if (bits >= 8)
                {
                    bits -= 8;
                    if (i < bytes.Length) bytes[i++] = (byte)(buffer >> bits);
                    buffer &= (1 << bits) - 1;
                }
            }
            return bytes;
        }

        /// <summary>The code at a moment in time.</summary>
        public static string Code(SignInKey key, DateTimeOffset at)
        {
            var counter = at.ToUnixTimeSeconds() / key.Period;
            var message = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian) Array.Reverse(message);
            byte[] hash;
            using (HMAC mac = key.Algorithm == "SHA256" ? new HMACSHA256(key.Secret) : key.Algorithm == "SHA512" ? (HMAC)new HMACSHA512(key.Secret) : new HMACSHA1(key.Secret))
                hash = mac.ComputeHash(message);
            var offset = hash[hash.Length - 1] & 0x0F;
            var number = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
            var mod = 1;
            for (var d = 0; d < key.Digits; d++) mod *= 10;
            return (number % mod).ToString(new string('0', key.Digits), CultureInfo.InvariantCulture);
        }

        /// <summary>How many seconds the code at this moment still lasts.</summary>
        public static int SecondsLeft(SignInKey key, DateTimeOffset at) => key.Period - (int)(at.ToUnixTimeSeconds() % key.Period);

        /// <summary>A code shown in two halves, easier to read: "123 456".</summary>
        public static string Show(string code) => code.Length == 6 ? code.Substring(0, 3) + " " + code.Substring(3) : code;
    }
}
