// Secure connections: an http:// page is first tried as https://, and when the site has no secure connection the
// browser warns before opening it. Addresses on this PC and the local network are left alone.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Kudubrowse.Core.Blocking;

namespace Kudubrowse.Core
{
    public static class SecureConnections
    {
        /// <summary>
        /// The https:// address to try instead of an http:// one, or null when the address isn't http, is on this PC or
        /// the local network, or its site may be opened without a secure connection.
        /// </summary>
        public static string SecureAddress(string url, IEnumerable<string> allowedSites)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "http") return null;
            if (IsLocal(u.Host) || IsAllowed(u.Host, allowedSites)) return null;
            var b = new UriBuilder(u) { Scheme = "https" };
            if (u.IsDefaultPort) b.Port = -1; // http's port 80 becomes https' own
            return b.Uri.AbsoluteUri;
        }

        /// <summary>The site (registrable domain) the person allowed without a secure connection, for a host.</summary>
        public static bool IsAllowed(string host, IEnumerable<string> allowedSites)
        {
            if (string.IsNullOrEmpty(host) || allowedSites == null) return false;
            var site = Domains.Registrable(host.ToLowerInvariant());
            return allowedSites.Any(s => string.Equals(s, site, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// True for a host on this PC or the local network: localhost, names without a dot (a PC on the network), the
        /// local endings (.local, .lan, .home.arpa, .internal...), and private, loopback and link-local addresses.
        /// </summary>
        public static bool IsLocal(string host)
        {
            if (string.IsNullOrEmpty(host)) return true;
            var h = host.Trim('[', ']').TrimEnd('.').ToLowerInvariant();
            if (h == "localhost" || h.EndsWith(".localhost")) return true;
            if (IPAddress.TryParse(h, out var ip))
            {
                if (IPAddress.IsLoopback(ip)) return true;
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    var b = ip.GetAddressBytes();
                    return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                           || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] == 0;
                }
                if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                    return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;
                return false;
            }
            if (h.IndexOf('.') < 0) return true;
            return new[] { ".local", ".lan", ".home", ".home.arpa", ".internal", ".intranet", ".corp" }.Any(e => h.EndsWith(e));
        }

        /// <summary>A site (registrable domain) for the "allowed without a secure connection" list.</summary>
        public static string SiteOf(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.Length > 0 ? Domains.Registrable(u.Host.ToLowerInvariant()) : null;
    }
}
