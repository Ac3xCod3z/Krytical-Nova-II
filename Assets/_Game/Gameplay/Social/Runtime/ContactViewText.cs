using System.Collections.Generic;
using System.Text;
using Unity.Collections;

namespace KryticalNova.Social
{
    /// <summary>
    /// A Contacts window as text (M8 8.4.2): what NGO's owner ViewRpc carried as a <see cref="ContactView"/> array, for the entity netcode's
    /// pages. One line per entry, tab between the fields (kind, id, name, online, level, faction, weapon class, favourite, last seen, note);
    /// a backslash, tab or line break inside a field is escaped. Plain C#.
    /// </summary>
    public static class ContactViewText
    {
        private const int Fields = 10;

        public static string Encode(IReadOnlyList<ContactView> view)
        {
            var text = new StringBuilder();
            if (view == null) return string.Empty;
            for (int i = 0; i < view.Count; i++)
            {
                ContactView line = view[i];
                if (i > 0) text.Append('\n');
                text.Append((byte)line.kind).Append('\t');
                Escape(text, line.id.ToString()); text.Append('\t');
                Escape(text, line.name.ToString()); text.Append('\t');
                text.Append(line.online ? '1' : '0').Append('\t');
                text.Append(line.level).Append('\t').Append(line.faction).Append('\t').Append(line.weaponClass).Append('\t');
                text.Append(line.favourite ? '1' : '0').Append('\t').Append(line.lastSeenUtc).Append('\t');
                Escape(text, line.note.ToString());
            }
            return text.ToString();
        }

        public static bool TryDecode(string text, out ContactView[] view)
        {
            view = System.Array.Empty<ContactView>();
            if (string.IsNullOrEmpty(text)) return true;
            var lines = new List<ContactView>();
            foreach (string row in text.Split('\n'))
            {
                string[] f = row.Split('\t');
                if (f.Length != Fields || !byte.TryParse(f[0], out byte kind) || kind > (byte)ContactKind.Blocked || !ushort.TryParse(f[4], out ushort level)
                    || !byte.TryParse(f[5], out byte faction) || !byte.TryParse(f[6], out byte weaponClass) || !long.TryParse(f[8], out long lastSeen)) return false;
                var line = new ContactView
                {
                    kind = (ContactKind)kind, online = f[3] == "1", level = level, faction = faction, weaponClass = weaponClass, favourite = f[7] == "1", lastSeenUtc = lastSeen,
                };
                line.id.CopyFromTruncated(Unescape(f[1]));
                line.name.CopyFromTruncated(Unescape(f[2]));
                line.note.CopyFromTruncated(Unescape(f[9]));
                lines.Add(line);
            }
            view = lines.ToArray();
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
