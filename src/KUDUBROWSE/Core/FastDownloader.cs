using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Kudubrowse.Core
{
    /// <summary>What a site says about a file before it's fetched.</summary>
    public sealed class DownloadCheck
    {
        /// <summary>The site lets the file be fetched in parts (so in pieces at once, and carried on after a pause).</summary>
        public bool InParts { get; set; }
        public long Total { get; set; } = -1;
        public string Version { get; set; }
        /// <summary>The address after any redirects.</summary>
        public string Url { get; set; }
    }

    /// <summary>
    /// The browser's own downloader: fetches a file in up to six pieces at the same time when the site allows it, carries on
    /// after a pause, a dropped connection or a restart from where it stopped, and starts again if the file changed on the
    /// site. The file is written as "name.kudupart" and renamed when it's whole. Each address it's sent to (redirects
    /// included) gets only that site's cookies, asked for through <see cref="Cookies"/>.
    /// </summary>
    public sealed class FastDownloader
    {
        public const int Connections = 6;
        private const int Attempts = 4;
        private readonly HttpClient _http;
        private readonly object _lock = new object();

        public FastDownloader(DownloadRecord record, HttpClient http)
        {
            Record = record;
            _http = http;
        }

        public DownloadRecord Record { get; }

        /// <summary>The cookies to send to an address ("a=1; b=2"), or null.</summary>
        public Func<Uri, Task<string>> Cookies { get; set; }
        public string UserAgent { get; set; }

        /// <summary>Waits between tries after a dropped connection (the tests make it short).</summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>More bytes arrived (called from the download's own threads).</summary>
        public event Action Progress;

        /// <summary>An HttpClient that leaves redirects and cookies to the downloader.</summary>
        public static HttpClient CreateClient() =>
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None })
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

        /// <summary>Asks the site for the first byte of the file, to learn its size and whether it can be fetched in parts.</summary>
        public async Task<DownloadCheck> Check(CancellationToken cancel)
        {
            using (var response = await Send(Record.Url, 0, 0, null, cancel).ConfigureAwait(false))
            {
                var check = new DownloadCheck { Url = response.RequestMessage.RequestUri.AbsoluteUri, Version = VersionOf(response) };
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    check.Total = DownloadRules.TotalFromContentRange(response.Content.Headers.ContentRange?.ToString());
                    check.InParts = check.Total > 0;
                }
                else if (response.IsSuccessStatusCode) check.Total = response.Content.Headers.ContentLength ?? -1;
                else throw new DownloadException(Plain(response.StatusCode));
                return check;
            }
        }

        /// <summary>
        /// Fetches the file (or the rest of it). Returns when it's whole and renamed; throws OperationCanceledException when
        /// paused or cancelled (what arrived is kept), or DownloadException when the site keeps failing.
        /// </summary>
        public async Task Run(CancellationToken cancel)
        {
            Record.Own = true;
            Record.Error = null;
            Record.State = DownloadState.Running;
            for (var restart = 0; ; restart++)
            {
                var check = await Check(cancel).ConfigureAwait(false);
                var same = Record.Pieces != null && check.InParts && check.Total == Record.Total &&
                           (Record.Version == null || check.Version == null || Record.Version == check.Version) && File.Exists(Record.PartFile);
                if (!same)
                {
                    // A new start: in pieces when the site allows it (several for big files), otherwise in one go.
                    Record.Total = check.Total;
                    Record.Version = check.Version;
                    Record.Pieces = check.InParts ? DownloadRules.Plan(check.Total, check.Total >= DownloadRules.FastFrom ? Connections : 1) : null;
                    Directory.CreateDirectory(Path.GetDirectoryName(Record.PartFile));
                    using (var f = new FileStream(Record.PartFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                        if (check.InParts) f.SetLength(check.Total);
                }
                Received();
                try
                {
                    if (Record.Pieces != null) await Pieces(check.Url, cancel).ConfigureAwait(false);
                    else await Whole(check.Url, cancel).ConfigureAwait(false);
                    break;
                }
                catch (FileChangedException) when (restart == 0)
                {
                    Record.Pieces = null; // the site has a different file now: start again once
                }
            }
            Finish();
        }

        /// <summary>Fetches the unfinished pieces at the same time; when one fails for good, the others stop too.</summary>
        private async Task Pieces(string url, CancellationToken cancel)
        {
            using (var all = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                var tasks = Record.Pieces.Where(p => !p.Finished).Select(async p =>
                {
                    try { await Piece(url, p, all.Token).ConfigureAwait(false); }
                    catch
                    {
                        all.Cancel();
                        throw;
                    }
                }).ToList();
                try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch (Exception)
                {
                    cancel.ThrowIfCancellationRequested();
                    // The reason is the first piece that failed by itself, not the ones stopped because of it.
                    var reason = tasks.Where(t => t.IsFaulted).SelectMany(t => t.Exception.InnerExceptions)
                        .FirstOrDefault(e => !(e is OperationCanceledException));
                    if (reason != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(reason).Throw();
                    throw;
                }
            }
            Received();
        }

        private async Task Piece(string url, DownloadPiece piece, CancellationToken cancel)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using (var response = await Send(url, piece.Start + piece.Done, piece.End, Record.Version, cancel).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.OK) throw new FileChangedException();
                        if (response.StatusCode != HttpStatusCode.PartialContent) throw new DownloadException(Plain(response.StatusCode));
                        var from = response.Content.Headers.ContentRange?.From;
                        if (from != piece.Start + piece.Done) throw new FileChangedException();
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new FileStream(Record.PartFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 4096, true))
                        {
                            output.Position = piece.Start + piece.Done;
                            var buffer = new byte[81920];
                            while (!piece.Finished)
                            {
                                var want = (int)Math.Min(buffer.Length, piece.Length - piece.Done);
                                var n = await input.ReadAsync(buffer, 0, want, cancel).ConfigureAwait(false);
                                if (n == 0) throw new IOException("The connection closed early");
                                await output.WriteAsync(buffer, 0, n, cancel).ConfigureAwait(false);
                                lock (_lock) piece.Done += n;
                                Received();
                            }
                        }
                    }
                    return;
                }
                catch (Exception ex) when ((ex is IOException || ex is HttpRequestException || ex is DownloadException) && attempt < Attempts && !cancel.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromTicks(RetryDelay.Ticks * attempt), cancel).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException || ex is HttpRequestException)
                {
                    if (cancel.IsCancellationRequested) throw new OperationCanceledException(cancel);
                    throw new DownloadException("The connection kept dropping");
                }
            }
        }

        /// <summary>Sites that don't allow parts: the whole file in one go, from the start each time.</summary>
        private async Task Whole(string url, CancellationToken cancel)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    Record.Received = 0;
                    using (var response = await Send(url, -1, -1, null, cancel).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) throw new DownloadException(Plain(response.StatusCode));
                        if (Record.Total < 0) Record.Total = response.Content.Headers.ContentLength ?? -1;
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new FileStream(Record.PartFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 4096, true))
                        {
                            var buffer = new byte[81920];
                            int n;
                            while ((n = await input.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) > 0)
                            {
                                await output.WriteAsync(buffer, 0, n, cancel).ConfigureAwait(false);
                                Record.Received += n;
                                Progress?.Invoke();
                            }
                        }
                    }
                    if (Record.Total > 0 && Record.Received != Record.Total) throw new IOException("The connection closed early");
                    return;
                }
                catch (Exception ex) when ((ex is IOException || ex is HttpRequestException || ex is DownloadException) && attempt < Attempts && !cancel.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromTicks(RetryDelay.Ticks * attempt), cancel).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException || ex is HttpRequestException)
                {
                    if (cancel.IsCancellationRequested) throw new OperationCanceledException(cancel);
                    throw new DownloadException("The connection kept dropping");
                }
            }
        }

        private void Received()
        {
            if (Record.Pieces != null)
                lock (_lock) Record.Received = Record.Pieces.Sum(p => p.Done);
            Progress?.Invoke();
        }

        /// <summary>The whole file is there: it gets its real name (a free one, if something took the name meanwhile).</summary>
        private void Finish()
        {
            var part = Record.PartFile;
            if (Record.Total > 0 && new FileInfo(part).Length != Record.Total) throw new DownloadException("The file didn't arrive whole");
            var final = DownloadRules.Unique(Record.File, p => File.Exists(p) || Directory.Exists(p));
            File.Move(part, final);
            Record.File = final;
            Record.Received = new FileInfo(final).Length;
            if (Record.Total < 0) Record.Total = Record.Received;
            Record.State = DownloadState.Done;
            Progress?.Invoke();
        }

        /// <summary>Sends a GET for bytes <paramref name="from"/>-<paramref name="to"/> (or all of it), following up to 10 redirects.</summary>
        private async Task<HttpResponseMessage> Send(string url, long from, long to, string ifRange, CancellationToken cancel)
        {
            var uri = new Uri(url);
            for (var hops = 0; ; hops++)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                if (from >= 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);
                if (ifRange != null)
                {
                    if (ifRange.StartsWith("\"")) request.Headers.IfRange = new System.Net.Http.Headers.RangeConditionHeaderValue(new System.Net.Http.Headers.EntityTagHeaderValue(ifRange));
                    else if (DateTimeOffset.TryParse(ifRange, out var date)) request.Headers.IfRange = new System.Net.Http.Headers.RangeConditionHeaderValue(date);
                }
                request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
                request.Headers.TryAddWithoutValidation("Accept", "*/*");
                if (!string.IsNullOrEmpty(UserAgent)) request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                if (!string.IsNullOrEmpty(Record.Referrer) && Record.Referrer.StartsWith("http")) request.Headers.TryAddWithoutValidation("Referer", Record.Referrer);
                var cookies = Cookies == null ? null : await Cookies(uri).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(cookies)) request.Headers.TryAddWithoutValidation("Cookie", cookies);
                var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
                var code = (int)response.StatusCode;
                if (code < 300 || code > 399 || code == 304 || response.Headers.Location == null) return response;
                response.Dispose();
                if (hops >= 10) throw new DownloadException("The site sent the download round in circles");
                uri = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(uri, response.Headers.Location);
                if (uri.Scheme != "https" && uri.Scheme != "http") throw new DownloadException("The site sent the download somewhere it can't be fetched from");
            }
        }

        private static string VersionOf(HttpResponseMessage r)
        {
            var tag = r.Headers.ETag;
            if (tag != null && !tag.IsWeak) return tag.Tag;
            return r.Content.Headers.LastModified?.ToString("R");
        }

        private static string Plain(HttpStatusCode code)
        {
            switch ((int)code)
            {
                case 401:
                case 403: return "The site didn't allow it (you may need to sign in again)";
                case 404:
                case 410: return "The file isn't on the site any more";
                case 429: return "The site is busy; try again in a while";
                default: return (int)code >= 500 ? "The site had a problem (" + (int)code + ")" : "The site said no (" + (int)code + ")";
            }
        }

        private sealed class FileChangedException : Exception
        {
        }
    }

    /// <summary>A download that couldn't be finished, with the reason in plain words.</summary>
    public sealed class DownloadException : Exception
    {
        public DownloadException(string message) : base(message)
        {
        }
    }
}
