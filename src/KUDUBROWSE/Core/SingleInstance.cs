using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kudubrowse.Core
{
    /// <summary>
    /// Keeps one running copy of the browser for each Windows user: when it's started again (a link clicked in another
    /// app), the new copy hands the address to the one already running and closes.
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        public bool IsFirst { get; }
        public string PipeName { get; }

        public SingleInstance(string name = null)
        {
            name = name ?? DefaultName();
            PipeName = name;
            _mutex = new Mutex(true, @"Local\" + name, out var created);
            IsFirst = created;
        }

        /// <summary>A name for this Windows user (a short hash of the user name, so it's safe in a pipe name).</summary>
        public static string DefaultName()
        {
            using (var sha = SHA256.Create())
            {
                var who = Environment.UserDomainName + "\\" + Environment.UserName;
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(who.ToLowerInvariant()));
                return AppInfo.Name + "-" + string.Concat(hash.Take(8).Select(b => b.ToString("x2")));
            }
        }

        /// <summary>Listens for addresses from later copies; <paramref name="received"/> is called with each one's arguments.</summary>
        public void Listen(Action<string[]> received)
        {
            Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                        {
                            await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                            using (var reader = new StreamReader(server, Encoding.UTF8))
                            {
                                var text = await reader.ReadToEndAsync().ConfigureAwait(false);
                                if (text.Length > 64 * 1024) continue;
                                received(text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (IOException)
                    {
                        await Task.Delay(200).ConfigureAwait(false);
                    }
                }
            });
        }

        /// <summary>Hands the arguments to the running copy. False if it couldn't be reached.</summary>
        public static bool Send(string pipeName, string[] args, int timeoutMs = 3000)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                {
                    client.Connect(timeoutMs);
                    var data = Encoding.UTF8.GetBytes(string.Join("\n", args.Select(a => a.Replace("\n", " "))));
                    client.Write(data, 0, data.Length);
                    client.Flush();
                    return true;
                }
            }
            catch (Exception ex) when (ex is TimeoutException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            if (IsFirst)
            {
                try { _mutex.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
            _mutex.Dispose();
        }
    }
}
