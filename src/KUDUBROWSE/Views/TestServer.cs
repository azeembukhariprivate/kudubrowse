using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kudubrowse.Views
{
    /// <summary>
    /// A small web server on this PC for the self-test and the tests (never used otherwise): serves made-up files, in parts
    /// or not, slowly if asked, dropping the connection part way if asked, and redirects. Remembers the cookies it was sent.
    /// </summary>
    public sealed class TestServer : IDisposable
    {
        public sealed class Item
        {
            public byte[] Body;
            public string Type = "application/octet-stream";
            public bool Parts = true;
            public string Tag = "\"v1\"";
            /// <summary>The first this-many answers stop after <see cref="DropAfter"/> bytes.</summary>
            public int Drops;
            public long DropAfter;
            /// <summary>Pause after each 64 KB sent, to make a download take a while.</summary>
            public int SlowMs;
            public string RedirectTo;
            public string Disposition;
        }

        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly ConcurrentDictionary<string, Item> _items = new ConcurrentDictionary<string, Item>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        public TestServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Task.Run(Accept);
        }

        public int Port { get; }
        public string Url(string path) => "http://127.0.0.1:" + Port + "/" + path.TrimStart('/');
        public ConcurrentQueue<string> CookiesSeen { get; } = new ConcurrentQueue<string>();
        public ConcurrentQueue<string> RangesAsked { get; } = new ConcurrentQueue<string>();
        public int Requests;

        public Item Add(string path, Item item) => _items["/" + path.TrimStart('/')] = item;
        public Item Add(string path, string html) => Add(path, new Item { Body = Encoding.UTF8.GetBytes(html), Type = "text/html; charset=utf-8", Parts = false });

        /// <summary>Bytes that are the same every time for a given size, so a download can be checked.</summary>
        public static byte[] Made(int size, int seed = 7)
        {
            var b = new byte[size];
            uint x = (uint)seed * 2654435761u + 1;
            for (var i = 0; i < size; i++)
            {
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                b[i] = (byte)x;
            }
            return b;
        }

        private async Task Accept()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (Exception) { return; }
                _ = Task.Run(() => Serve(client));
            }
        }

        private async Task Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var head = await ReadHead(stream).ConfigureAwait(false);
                    if (head == null) return;
                    Interlocked.Increment(ref Requests);
                    var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var path = lines[0].Split(' ')[1];
                    var q = path.IndexOf('?');
                    if (q >= 0) path = path.Substring(0, q);
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 1; i < lines.Length; i++)
                    {
                        var colon = lines[i].IndexOf(':');
                        if (colon > 0) headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
                    }
                    if (headers.TryGetValue("Cookie", out var cookie)) CookiesSeen.Enqueue(path + " " + cookie);
                    if (!_items.TryGetValue(path, out var item))
                    {
                        await Write(stream, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n").ConfigureAwait(false);
                        return;
                    }
                    if (item.RedirectTo != null)
                    {
                        await Write(stream, "HTTP/1.1 302 Found\r\nLocation: " + item.RedirectTo + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n").ConfigureAwait(false);
                        return;
                    }
                    long from = 0, to = item.Body.Length - 1;
                    var partial = false;
                    if (item.Parts && headers.TryGetValue("Range", out var range) && range.StartsWith("bytes="))
                    {
                        RangesAsked.Enqueue(range);
                        var sameVersion = !headers.TryGetValue("If-Range", out var ifRange) || ifRange == item.Tag;
                        var ab = range.Substring(6).Split('-');
                        if (sameVersion && long.TryParse(ab[0], out from))
                        {
                            if (ab.Length > 1 && ab[1].Length > 0) to = Math.Min(long.Parse(ab[1], CultureInfo.InvariantCulture), item.Body.Length - 1);
                            partial = true;
                        }
                        else from = 0;
                    }
                    var sb = new StringBuilder();
                    sb.Append(partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n");
                    sb.Append("Content-Type: ").Append(item.Type).Append("\r\n");
                    sb.Append("Content-Length: ").Append(to - from + 1).Append("\r\n");
                    if (partial) sb.Append("Content-Range: bytes ").Append(from).Append('-').Append(to).Append('/').Append(item.Body.Length).Append("\r\n");
                    if (item.Parts) sb.Append("Accept-Ranges: bytes\r\nETag: ").Append(item.Tag).Append("\r\n");
                    if (item.Disposition != null) sb.Append("Content-Disposition: ").Append(item.Disposition).Append("\r\n");
                    sb.Append("Connection: close\r\n\r\n");
                    await Write(stream, sb.ToString()).ConfigureAwait(false);
                    var drop = item.Drops > 0 && Interlocked.Decrement(ref item.Drops) >= 0;
                    long sent = 0;
                    for (var at = from; at <= to; at += 65536)
                    {
                        var n = (int)Math.Min(65536, to - at + 1);
                        if (drop && sent + n > item.DropAfter) return;
                        await stream.WriteAsync(item.Body, (int)at, n).ConfigureAwait(false);
                        sent += n;
                        if (item.SlowMs > 0) await Task.Delay(item.SlowMs).ConfigureAwait(false);
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }
        }

        private static async Task<string> ReadHead(NetworkStream stream)
        {
            var bytes = new List<byte>();
            var one = new byte[1];
            while (bytes.Count < 16384)
            {
                if (await stream.ReadAsync(one, 0, 1).ConfigureAwait(false) == 0) return null;
                bytes.Add(one[0]);
                var c = bytes.Count;
                if (c >= 4 && bytes[c - 4] == '\r' && bytes[c - 3] == '\n' && bytes[c - 2] == '\r' && bytes[c - 1] == '\n')
                    return Encoding.ASCII.GetString(bytes.ToArray(), 0, c - 4);
            }
            return null;
        }

        private static Task Write(Stream s, string text)
        {
            var b = Encoding.ASCII.GetBytes(text);
            return s.WriteAsync(b, 0, b.Length);
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }
}
