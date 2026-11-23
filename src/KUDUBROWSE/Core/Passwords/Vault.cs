// The passwords (passwords.dat): one file encrypted for the Windows user, logins matched by exact site, and sites never
// to save for.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Kudubrowse.Core.Blocking;

namespace Kudubrowse.Core.Passwords
{
    /// <summary>Encrypts and decrypts the passwords file. The app uses Windows' own protection for the user (DPAPI).</summary>
    public interface IProtector
    {
        byte[] Protect(byte[] data);
        byte[] Unprotect(byte[] data);
    }

    [DataContract]
    public sealed class Login
    {
        /// <summary>Where it's used: scheme, host and port, such as https://accounts.example.com.</summary>
        [DataMember(Name = "origin", Order = 1)] public string Origin { get; set; }
        [DataMember(Name = "username", Order = 2)] public string Username { get; set; }
        [DataMember(Name = "password", Order = 3)] public string Password { get; set; }
        [DataMember(Name = "created", Order = 4)] public DateTime Created { get; set; }
        [DataMember(Name = "used", Order = 5)] public DateTime LastUsed { get; set; }
        [DataMember(Name = "changed", Order = 6)] public DateTime Changed { get; set; }
        /// <summary>The two-step sign-in key, as the site gave it (setup key or otpauth:// link); null when there's none.</summary>
        [DataMember(Name = "code", Order = 7, EmitDefaultValue = false)] public string CodeKey { get; set; }

        /// <summary>The sign-in code now, or null when there's no key.</summary>
        public string CodeNow(DateTimeOffset? at = null)
        {
            var key = SignInCodes.Parse(CodeKey);
            return key == null ? null : SignInCodes.Code(key, at ?? DateTimeOffset.UtcNow);
        }

        /// <summary>The login's site.</summary>
        public string Site => Vault.HostOf(Origin);
    }

    /// <summary>
    /// The saved passwords, kept in one encrypted file (%LocalAppData%\KUDUBROWSE\passwords.dat). The file is only ever
    /// decrypted in memory; it's written again, whole, after each change.
    /// </summary>
    public sealed class Vault
    {
        [DataContract]
        private sealed class Stored
        {
            [DataMember(Name = "logins", Order = 1)] public List<Login> Logins { get; set; }
            [DataMember(Name = "never", Order = 2)] public List<string> Never { get; set; }
        }

        /// <summary>Encrypts and decrypts the file (Windows' DPAPI in the app).</summary>
        private readonly IProtector _protector;
        /// <summary>Every login.</summary>
        private List<Login> _logins = new List<Login>();
        /// <summary>Sites never to offer saving for.</summary>
        private List<string> _never = new List<string>();
        /// <summary>Where it's kept.</summary>
        public string File { get; }

        /// <summary>A login was added, changed or removed.</summary>
        public event Action Changed;

        /// <summary>A vault in this file.</summary>
        private Vault(string file, IProtector protector)
        {
            File = file;
            _protector = protector;
        }

        /// <summary>Every login.</summary>
        public IReadOnlyList<Login> Logins => _logins;
        /// <summary>Sites never to offer saving for.</summary>
        public IReadOnlyList<string> NeverSaveOrigins => _never;

        /// <summary>Reads the file. A file that can't be decrypted (another Windows account's, or damaged) is kept aside, not overwritten.</summary>
        public static Vault Load(string file, IProtector protector)
        {
            var v = new Vault(file, protector);
            if (!System.IO.File.Exists(file)) return v;
            try
            {
                var plain = protector.Unprotect(System.IO.File.ReadAllBytes(file));
                using (var m = new MemoryStream(plain))
                {
                    var stored = (Stored)new DataContractJsonSerializer(typeof(Stored)).ReadObject(m);
                    v._logins = stored?.Logins ?? new List<Login>();
                    v._never = stored?.Never ?? new List<string>();
                }
                Array.Clear(plain, 0, plain.Length);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                var aside = file + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { System.IO.File.Move(file, aside); }
                catch (Exception move) when (move is IOException || move is UnauthorizedAccessException) { }
            }
            return v;
        }

        /// <summary>Keeps the vault, encrypted (to a new file first, then put in place).</summary>
        public void Save()
        {
            byte[] plain;
            using (var m = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(Stored)).WriteObject(m, new Stored { Logins = _logins, Never = _never });
                plain = m.ToArray();
            }
            var data = _protector.Protect(plain);
            Array.Clear(plain, 0, plain.Length);
            Directory.CreateDirectory(Path.GetDirectoryName(File));
            var temp = File + ".new";
            System.IO.File.WriteAllBytes(temp, data);
            if (System.IO.File.Exists(File)) System.IO.File.Replace(temp, File, null);
            else System.IO.File.Move(temp, File);
            Changed?.Invoke();
        }

        // ================================================================ finding logins

        /// <summary>"https://www.example.com:443/login?x" → "https://www.example.com". Null for addresses that can't hold a login.</summary>
        public static string OriginOf(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http")) return null;
            return u.Scheme + "://" + u.Host.ToLowerInvariant() + (u.IsDefaultPort ? string.Empty : ":" + u.Port);
        }

        /// <summary>An address's site.</summary>
        public static string HostOf(string origin) => Uri.TryCreate(origin, UriKind.Absolute, out var u) ? u.Host : origin ?? string.Empty;

        /// <summary>
        /// The logins for a page: first those saved for exactly this site, then those for the same site's other parts
        /// (login.example.com and www.example.com), never for other sites. A secure (https) page never gets logins saved
        /// on an insecure one's address and the other way round.
        /// </summary>
        public List<Login> For(string url)
        {
            var origin = OriginOf(url);
            if (origin == null) return new List<Login>();
            var u = new Uri(origin);
            var site = Domains.Registrable(u.Host);
            var exact = _logins.Where(l => l.Origin == origin);
            var related = _logins.Where(l => l.Origin != origin && Uri.TryCreate(l.Origin, UriKind.Absolute, out var lu) && lu.Scheme == u.Scheme &&
                                             Domains.Registrable(lu.Host) == site);
            return exact.OrderByDescending(l => l.LastUsed).Concat(related.OrderByDescending(l => l.LastUsed)).ToList();
        }

        /// <summary>The login for this site and user name, or null.</summary>
        public Login Find(string origin, string username) =>
            _logins.FirstOrDefault(l => l.Origin == origin && string.Equals(l.Username, username, StringComparison.Ordinal));

        /// <summary>What saving a login would do: nothing, add one, or change one.</summary>
        public enum SaveKind { Nothing, New, Update }

        /// <summary>What sending this login on this page would mean: nothing to do, a new login, or a changed password.</summary>
        public SaveKind Check(string url, string username, string password)
        {
            var origin = OriginOf(url);
            if (origin == null || string.IsNullOrEmpty(password) || _never.Contains(origin)) return SaveKind.Nothing;
            var existing = Find(origin, username ?? string.Empty);
            if (existing == null) return SaveKind.New;
            return existing.Password == password ? SaveKind.Nothing : SaveKind.Update;
        }

        /// <summary>Saves a login (a new one, or the password of one there).</summary>
        public Login Save(string url, string username, string password)
        {
            var origin = OriginOf(url) ?? throw new ArgumentException("not a web address", nameof(url));
            var now = DateTime.UtcNow;
            var l = Find(origin, username ?? string.Empty);
            if (l == null)
            {
                l = new Login { Origin = origin, Username = username ?? string.Empty, Password = password, Created = now, LastUsed = now, Changed = now };
                _logins.Add(l);
            }
            else if (l.Password != password)
            {
                l.Password = password;
                l.Changed = now;
            }
            Save();
            return l;
        }

        /// <summary>Keeps a two-step sign-in key with a login (null takes it away). False when the text isn't a key.</summary>
        public bool SetCodeKey(Login l, string key)
        {
            if (!string.IsNullOrWhiteSpace(key) && SignInCodes.Parse(key) == null) return false;
            l.CodeKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
            l.Changed = DateTime.UtcNow;
            Save();
            return true;
        }

        /// <summary>Notes a login was used.</summary>
        public void MarkUsed(Login l)
        {
            l.LastUsed = DateTime.UtcNow;
            Save();
        }

        /// <summary>Removes a login.</summary>
        public void Remove(Login l)
        {
            if (_logins.Remove(l)) Save();
        }

        /// <summary>Never offers saving for this site.</summary>
        public void NeverFor(string url)
        {
            var origin = OriginOf(url);
            if (origin == null || _never.Contains(origin)) return;
            _never.Add(origin);
            Save();
        }

        /// <summary>Offers saving for this site again.</summary>
        public void AllowSavingFor(string origin)
        {
            if (_never.Remove(origin)) Save();
        }

        /// <summary>Adds logins brought from elsewhere (a Chrome export). Same site and name: the password is updated. Returns how many were new or changed.</summary>
        public int Merge(IEnumerable<(string Url, string Username, string Password)> items)
        {
            var count = 0;
            var now = DateTime.UtcNow;
            foreach (var (url, username, password) in items)
            {
                var origin = OriginOf(url);
                if (origin == null || string.IsNullOrEmpty(password)) continue;
                var l = Find(origin, username ?? string.Empty);
                if (l == null)
                {
                    _logins.Add(new Login { Origin = origin, Username = username ?? string.Empty, Password = password, Created = now, LastUsed = DateTime.MinValue.ToUniversalTime(), Changed = now });
                    count++;
                }
                else if (l.Password != password)
                {
                    l.Password = password;
                    l.Changed = now;
                    count++;
                }
            }
            if (count > 0) Save();
            return count;
        }
    }
}
