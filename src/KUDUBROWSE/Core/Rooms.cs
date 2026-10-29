using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kudubrowse.Core
{
    /// <summary>A room: tabs in it share their logins and site data, kept apart from other rooms.</summary>
    public sealed class Room
    {
        public string Id { get; set; } // the engine's profile name; "Default" is the first room
        public string Name { get; set; }
        public string Color { get; set; } // #RRGGBB
        public bool IsDefault => Id == Rooms.DefaultId;
    }

    /// <summary>The rooms, kept in %LocalAppData%\KUDUBROWSE\rooms.txt (a line each: id, name, colour).</summary>
    public sealed class Rooms
    {
        public const string DefaultId = "Default";
        public static readonly string[] Colors = { "#0E8C7E", "#2563EB", "#D97706", "#DB2777", "#7C3AED", "#16A34A", "#DC2626", "#64748B" };

        private readonly List<Room> _rooms = new List<Room>();
        public string File { get; }
        public event Action Changed;

        private Rooms(string file)
        {
            File = file;
        }

        public IReadOnlyList<Room> All => _rooms;
        public Room Default => _rooms.First(r => r.IsDefault);

        public Room Find(string id) => _rooms.FirstOrDefault(r => r.Id == id) ?? Default;

        public static Rooms Load(string file)
        {
            var r = new Rooms(file);
            try
            {
                if (System.IO.File.Exists(file))
                    foreach (var line in System.IO.File.ReadAllLines(file, Encoding.UTF8))
                    {
                        var p = line.Split('\t');
                        if (p.Length < 3 || !ValidId(p[0]) || r._rooms.Any(x => x.Id == p[0])) continue;
                        r._rooms.Add(new Room { Id = p[0], Name = p[1], Color = p[2] });
                    }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            if (!r._rooms.Any(x => x.IsDefault)) r._rooms.Insert(0, new Room { Id = DefaultId, Name = "Personal", Color = Colors[0] });
            if (r._rooms.Count == 1 && !System.IO.File.Exists(file)) r._rooms.Add(new Room { Id = "room-1", Name = "Work", Color = Colors[1] });
            return r;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File));
                System.IO.File.WriteAllLines(File, _rooms.Select(x => x.Id + "\t" + Clean(x.Name) + "\t" + x.Color), Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            Changed?.Invoke();
        }

        public Room Add(string name)
        {
            var n = 1;
            while (_rooms.Any(x => x.Id == "room-" + n)) n++;
            var room = new Room { Id = "room-" + n, Name = string.IsNullOrWhiteSpace(name) ? "Room " + (n + 1) : name.Trim(), Color = Colors[_rooms.Count % Colors.Length] };
            _rooms.Add(room);
            Save();
            return room;
        }

        public void Rename(Room room, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            room.Name = name.Trim();
            Save();
        }

        /// <summary>Removes a room from the list (the first room can't be removed). Its site data is deleted by the caller.</summary>
        public bool Remove(Room room)
        {
            if (room.IsDefault || !_rooms.Remove(room)) return false;
            Save();
            return true;
        }

        /// <summary>The engine accepts letters, digits and a few signs in profile names; ours are "Default" or "room-N".</summary>
        public static bool ValidId(string id) => id == DefaultId || (id != null && id.StartsWith("room-") && int.TryParse(id.Substring(5), out var n) && n > 0);

        private static string Clean(string s) => (s ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
