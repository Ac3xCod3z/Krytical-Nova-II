using System.Collections.Generic;
using System.Text;
using Unity.Collections;

namespace KryticalNova.Social
{
    /// <summary>
    /// A Crew window as text (M8 8.4.5): what NGO's owner ViewRpc carried (<see cref="CrewHeader"/> + <see cref="CrewMemberView"/> array),
    /// for the entity netcode's pages. Line 1 the header (exists, name, symbol, colour, member cap, my rank, announcement), then one line per
    /// member (id, name, rank, title, level, weapon class, online, last seen); tab between the fields, a backslash, tab or line break inside
    /// one escaped. Plain C#.
    /// </summary>
    public static class CrewViewText
    {
        public static string Encode(in CrewHeader header, IReadOnlyList<CrewMemberView> members)
        {
            var text = new StringBuilder();
            text.Append(header.exists ? '1' : '0').Append('\t');
            Escape(text, header.name.ToString()); text.Append('\t');
            text.Append(header.symbol).Append('\t').Append(header.colour).Append('\t').Append(header.memberCap).Append('\t').Append((byte)header.myRank).Append('\t');
            Escape(text, header.announcement.ToString());
            if (members != null)
                foreach (CrewMemberView m in members)
                {
                    text.Append('\n');
                    Escape(text, m.id.ToString()); text.Append('\t');
                    Escape(text, m.name.ToString()); text.Append('\t');
                    text.Append((byte)m.rank).Append('\t');
                    Escape(text, m.title.ToString()); text.Append('\t');
                    text.Append(m.level).Append('\t').Append(m.weaponClass).Append('\t').Append(m.online ? '1' : '0').Append('\t').Append(m.lastSeenUtc);
                }
            return text.ToString();
        }

        public static bool TryDecode(string text, out CrewHeader header, out CrewMemberView[] members)
        {
            header = default;
            members = System.Array.Empty<CrewMemberView>();
            if (string.IsNullOrEmpty(text)) return false;
            string[] rows = text.Split('\n');
            string[] h = rows[0].Split('\t');
            if (h.Length != 7 || !byte.TryParse(h[2], out byte symbol) || !byte.TryParse(h[3], out byte colour) || !ushort.TryParse(h[4], out ushort cap)
                || !byte.TryParse(h[5], out byte myRank)) return false;
            header = new CrewHeader { exists = h[0] == "1", symbol = symbol, colour = colour, memberCap = cap, myRank = (CrewRank)myRank };
            header.name.CopyFromTruncated(Unescape(h[1]));
            header.announcement.CopyFromTruncated(Unescape(h[6]));
            var list = new List<CrewMemberView>(rows.Length - 1);
            for (int i = 1; i < rows.Length; i++)
            {
                string[] f = rows[i].Split('\t');
                if (f.Length != 8 || !byte.TryParse(f[2], out byte rank) || !ushort.TryParse(f[4], out ushort level) || !byte.TryParse(f[5], out byte weaponClass)
                    || !long.TryParse(f[7], out long seen)) return false;
                var m = new CrewMemberView { rank = (CrewRank)rank, level = level, weaponClass = weaponClass, online = f[6] == "1", lastSeenUtc = seen };
                m.id.CopyFromTruncated(Unescape(f[0]));
                m.name.CopyFromTruncated(Unescape(f[1]));
                m.title.CopyFromTruncated(Unescape(f[3]));
                list.Add(m);
            }
            members = list.ToArray();
            return true;
        }

        private static void Escape(StringBuilder into, string value)
        {
            foreach (char c in value ?? string.Empty)
            {
                if (c == '\\') into.Append("\\\\");
                else if (c == '\t') into.Append("\\t");
                else if (c == '\n') into.Append("\\n");
                else into.Append(c);
            }
        }

        private static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0) return value;
            var text = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\' || i + 1 >= value.Length) { text.Append(c); continue; }
                char next = value[++i];
                text.Append(next == 't' ? '\t' : next == 'n' ? '\n' : next);
            }
            return text.ToString();
        }
    }
}
