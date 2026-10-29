using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Kudubrowse.Core
{
    /// <summary>One part of a file fetched on its own connection: bytes <see cref="Start"/> to <see cref="End"/>, <see cref="Done"/> of them arrived.</summary>
    [DataContract]
    public sealed class DownloadPiece
    {
        [DataMember(Name = "start", Order = 1)] public long Start { get; set; }
        [DataMember(Name = "end", Order = 2)] public long End { get; set; }
        [DataMember(Name = "done", Order = 3)] public long Done { get; set; }

        public long Length => End - Start + 1;
        public bool Finished => Done >= Length;
    }

    public enum DownloadState
    {
        Running,
        Paused,
        Done,
        Failed,
        Cancelled
    }

    /// <summary>A download as it's kept in the list (and in downloads.json, so the list is there next time).</summary>
    [DataContract]
    public sealed class DownloadRecord
    {
        [DataMember(Name = "id", Order = 1)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [DataMember(Name = "url", Order = 2)] public string Url { get; set; }
        [DataMember(Name = "file", Order = 3)] public string File { get; set; }
        [DataMember(Name = "referrer", Order = 4, EmitDefaultValue = false)] public string Referrer { get; set; }
        [DataMember(Name = "room", Order = 5, EmitDefaultValue = false)] public string Room { get; set; }
        [DataMember(Name = "total", Order = 6)] public long Total { get; set; } = -1;
        [DataMember(Name = "received", Order = 7)] public long Received { get; set; }
        [DataMember(Name = "state", Order = 8)] public string StateText { get; set; } = "running";
        [DataMember(Name = "started", Order = 9)] public DateTime Started { get; set; } = DateTime.UtcNow;
        /// <summary>Fetched by the browser itself (in pieces when the site allows it) rather than by the engine.</summary>
        [DataMember(Name = "own", Order = 10)] public bool Own { get; set; }
        [DataMember(Name = "pieces", Order = 11, EmitDefaultValue = false)] public List<DownloadPiece> Pieces { get; set; }
        /// <summary>The site's tag for this version of the file, so a resumed download isn't mixed with a newer file.</summary>
        [DataMember(Name = "version", Order = 12, EmitDefaultValue = false)] public string Version { get; set; }
        [DataMember(Name = "error", Order = 13, EmitDefaultValue = false)] public string Error { get; set; }

        /// <summary>From a private window: never written to the file.</summary>
        [IgnoreDataMember] public bool Private { get; set; }

        [IgnoreDataMember]
        public DownloadState State
        {
            get => Enum.TryParse(StateText, true, out DownloadState s) ? s : DownloadState.Failed;
            set => StateText = value.ToString().ToLowerInvariant();
        }

        public string FileName => Path.GetFileName(File ?? string.Empty);
        public string PartFile => File + DownloadRules.PartExtension;

        /// <summary>Fetched in more than one piece at a time.</summary>
        public bool InPieces => Pieces != null && Pieces.Count > 1;
    }

    /// <summary>The downloads list kept in %LocalAppData%\KUDUBROWSE\downloads.json (newest first, at most 300).</summary>
    public static class DownloadList
    {
        public const int Max = 300;

        [DataContract]
        private sealed class Stored
        {
            [DataMember(Name = "downloads", Order = 1)] public List<DownloadRecord> Downloads { get; set; }
        }

        /// <summary>
        /// Reads the list. Downloads that were still arriving when the browser closed are paused when they can carry on
        /// (the browser's own, with the part already there), otherwise marked as not finished, ready to try again.
        /// </summary>
        public static List<DownloadRecord> Load(string file)
        {
            try
            {
                if (!System.IO.File.Exists(file)) return new List<DownloadRecord>();
                List<DownloadRecord> list;
                using (var stream = System.IO.File.OpenRead(file))
                    list = ((Stored)new DataContractJsonSerializer(typeof(Stored)).ReadObject(stream))?.Downloads ?? new List<DownloadRecord>();
                list = list.Where(r => r != null && !string.IsNullOrEmpty(r.Url) && !string.IsNullOrEmpty(r.File)).ToList();
                foreach (var r in list.Where(r => r.State == DownloadState.Running || r.State == DownloadState.Paused))
                {
                    if (r.Own && r.Pieces != null && System.IO.File.Exists(r.PartFile)) r.State = DownloadState.Paused;
                    else
                    {
                        r.State = DownloadState.Failed;
                        r.Error = "Stopped when the browser was closed";
                    }
                }
                return list;
            }
            catch (Exception ex) when (ex is IOException || ex is SerializationException || ex is UnauthorizedAccessException || ex is InvalidCastException)
            {
                return new List<DownloadRecord>();
            }
        }

        public static void Save(string file, IEnumerable<DownloadRecord> records)
        {
            var stored = new Stored { Downloads = records.Where(r => !r.Private).Take(Max).ToList() };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var temp = file + ".new";
                using (var stream = System.IO.File.Create(temp))
                    new DataContractJsonSerializer(typeof(Stored)).WriteObject(stream, stored);
                if (System.IO.File.Exists(file)) System.IO.File.Replace(temp, file, null);
                else System.IO.File.Move(temp, file);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Best-effort: the list just won't come back this time.
            }
        }
    }
}
