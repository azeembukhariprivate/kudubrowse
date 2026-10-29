using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kudubrowse.Core.Library
{
    /// <summary>
    /// Reads the rows of one table from an SQLite database file (the format Chrome keeps its history in). Read-only, and
    /// only what's needed for that: the file header, the table's pages and its rows (with long rows spread over extra
    /// pages). Follows the published SQLite file format.
    /// </summary>
    public sealed class SqliteReader
    {
        private readonly byte[] _data;
        private readonly int _pageSize, _usable;
        private readonly Encoding _text;

        public SqliteReader(byte[] data)
        {
            if (data.Length < 100 || Encoding.ASCII.GetString(data, 0, 16) != "SQLite format 3\0") throw new InvalidDataException("not an SQLite file");
            _data = data;
            var size = (data[16] << 8) | data[17];
            _pageSize = size == 1 ? 65536 : size;
            if (_pageSize < 512 || (_pageSize & (_pageSize - 1)) != 0) throw new InvalidDataException("bad page size");
            _usable = _pageSize - data[20];
            var enc = ReadInt(56, 4);
            _text = enc == 2 ? Encoding.Unicode : enc == 3 ? Encoding.BigEndianUnicode : Encoding.UTF8;
        }

        /// <summary>The table's rows, as name → value (long, double, string, byte[] or null).</summary>
        public IEnumerable<Dictionary<string, object>> Rows(string table)
        {
            var master = Scan(1).Select(r => r.Values).FirstOrDefault(v => v.Length >= 5 && v[0] as string == "table" && string.Equals(v[1] as string, table, StringComparison.OrdinalIgnoreCase));
            if (master == null) throw new InvalidDataException("no table " + table);
            var root = (int)(long)master[3];
            var columns = Columns(master[4] as string ?? string.Empty);
            foreach (var row in Scan(root))
            {
                var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < columns.Count; i++) d[columns[i]] = i < row.Values.Length ? row.Values[i] : null;
                d["rowid"] = row.RowId;
                yield return d;
            }
        }

        /// <summary>Column names from a CREATE TABLE statement, in order.</summary>
        public static List<string> Columns(string sql)
        {
            var open = sql.IndexOf('(');
            var close = sql.LastIndexOf(')');
            var list = new List<string>();
            if (open < 0 || close <= open) return list;
            var body = sql.Substring(open + 1, close - open - 1);
            var depth = 0;
            var start = 0;
            var parts = new List<string>();
            for (var i = 0; i <= body.Length; i++)
            {
                if (i == body.Length || (body[i] == ',' && depth == 0))
                {
                    parts.Add(body.Substring(start, i - start).Trim());
                    start = i + 1;
                }
                else if (body[i] == '(') depth++;
                else if (body[i] == ')') depth--;
            }
            var constraints = new[] { "PRIMARY", "UNIQUE", "CHECK", "FOREIGN", "CONSTRAINT" };
            foreach (var p in parts)
            {
                if (p.Length == 0) continue;
                var name = p.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim('"', '`', '[', ']');
                if (constraints.Contains(name.ToUpperInvariant())) continue;
                list.Add(name);
            }
            return list;
        }

        private struct Row
        {
            public long RowId;
            public object[] Values;
        }

        /// <summary>Walks a table's pages, left to right.</summary>
        private IEnumerable<Row> Scan(int rootPage)
        {
            var stack = new Stack<int>();
            stack.Push(rootPage);
            var visited = new HashSet<int>();
            while (stack.Count > 0)
            {
                var page = stack.Pop();
                if (!visited.Add(page) || page < 1 || (long)page * _pageSize > _data.Length) continue; // damaged links are skipped
                var start = (page - 1) * _pageSize;
                var h = page == 1 ? 100 : 0;
                var type = _data[start + h];
                var cells = ReadInt(start + h + 3, 2);
                if (type == 0x05)
                {
                    var children = new List<int>();
                    for (var i = 0; i < cells; i++)
                    {
                        var cell = start + ReadInt(start + h + 12 + i * 2, 2);
                        children.Add(ReadInt(cell, 4));
                    }
                    children.Add(ReadInt(start + h + 8, 4));
                    for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
                }
                else if (type == 0x0D)
                {
                    for (var i = 0; i < cells; i++)
                    {
                        var cell = start + ReadInt(start + h + 8 + i * 2, 2);
                        Row row;
                        try { row = ReadLeafCell(cell); }
                        catch (Exception ex) when (ex is IndexOutOfRangeException || ex is ArgumentException || ex is InvalidDataException) { continue; }
                        yield return row;
                    }
                }
            }
        }

        private Row ReadLeafCell(int pos)
        {
            var payloadSize = ReadVarint(ref pos);
            var rowId = ReadVarint(ref pos);
            var payload = Payload(pos, payloadSize);
            return new Row { RowId = rowId, Values = Record(payload) };
        }

        /// <summary>The row's bytes: the part on this page, then any overflow pages.</summary>
        private byte[] Payload(int pos, long size)
        {
            if (size > 64 * 1024 * 1024) throw new InvalidDataException("row too big");
            var p = (int)size;
            var x = _usable - 35;
            int local;
            if (p <= x) local = p;
            else
            {
                var m = ((_usable - 12) * 32 / 255) - 23;
                var k = m + ((p - m) % (_usable - 4));
                local = k <= x ? k : m;
            }
            var result = new byte[p];
            Buffer.BlockCopy(_data, pos, result, 0, local);
            var done = local;
            var next = local < p ? ReadInt(pos + local, 4) : 0;
            var guard = 0;
            while (done < p && next != 0 && guard++ < 100000)
            {
                var start = (next - 1) * _pageSize;
                var n = Math.Min(_usable - 4, p - done);
                Buffer.BlockCopy(_data, start + 4, result, done, n);
                done += n;
                next = ReadInt(start, 4);
            }
            if (done < p) throw new InvalidDataException("row cut short");
            return result;
        }

        private object[] Record(byte[] r)
        {
            var pos = 0;
            var headerSize = (int)ReadVarint(r, ref pos);
            var types = new List<long>();
            while (pos < headerSize) types.Add(ReadVarint(r, ref pos));
            var values = new object[types.Count];
            pos = headerSize;
            for (var i = 0; i < types.Count; i++)
            {
                var t = types[i];
                switch (t)
                {
                    case 0: values[i] = null; break;
                    case 1: values[i] = (long)(sbyte)r[pos]; pos += 1; break;
                    case 2: values[i] = SignedInt(r, pos, 2); pos += 2; break;
                    case 3: values[i] = SignedInt(r, pos, 3); pos += 3; break;
                    case 4: values[i] = SignedInt(r, pos, 4); pos += 4; break;
                    case 5: values[i] = SignedInt(r, pos, 6); pos += 6; break;
                    case 6: values[i] = SignedInt(r, pos, 8); pos += 8; break;
                    case 7: values[i] = BitConverter.Int64BitsToDouble(SignedInt(r, pos, 8)); pos += 8; break;
                    case 8: values[i] = 0L; break;
                    case 9: values[i] = 1L; break;
                    default:
                        if (t >= 12)
                        {
                            var len = (int)((t - (t % 2 == 0 ? 12 : 13)) / 2);
                            if (t % 2 == 0)
                            {
                                var b = new byte[len];
                                Buffer.BlockCopy(r, pos, b, 0, len);
                                values[i] = b;
                            }
                            else values[i] = _text.GetString(r, pos, len);
                            pos += len;
                        }
                        break;
                }
            }
            return values;
        }

        private static long SignedInt(byte[] b, int pos, int n)
        {
            long v = (sbyte)b[pos];
            for (var i = 1; i < n; i++) v = (v << 8) | b[pos + i];
            return v;
        }

        private int ReadInt(int pos, int n)
        {
            var v = 0;
            for (var i = 0; i < n; i++) v = (v << 8) | _data[pos + i];
            return v;
        }

        private long ReadVarint(ref int pos) => ReadVarint(_data, ref pos);

        private static long ReadVarint(byte[] b, ref int pos)
        {
            long v = 0;
            for (var i = 0; i < 9; i++)
            {
                var c = b[pos++];
                if (i == 8) return (v << 8) | c;
                v = (v << 7) | (long)(c & 0x7F);
                if ((c & 0x80) == 0) return v;
            }
            return v;
        }
    }
}
