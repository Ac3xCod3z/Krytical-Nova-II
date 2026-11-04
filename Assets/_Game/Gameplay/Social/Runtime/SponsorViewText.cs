using System.Collections.Generic;
using System.Text;
using Unity.Collections;

namespace KryticalNova.Social
{
    /// <summary>
    /// A Sponsor window's links as text (M8 8.4.4): what NGO's owner ViewRpc carried as a <see cref="SponsorLinkView"/> array, for the entity
    /// netcode. One line per link, tab between the fields (character id, name, level, sponsor, online, near). Ids are hex and names are
    /// cleaned chat names, so neither holds a tab or a line break. Plain C#.
    /// </summary>
    public static class SponsorViewText
    {
        public static string Encode(IReadOnlyList<SponsorLinkView> view)
        {
            var text = new StringBuilder();
            if (view == null) return string.Empty;
            for (int i = 0; i < view.Count; i++)
            {
                SponsorLinkView line = view[i];
                if (i > 0) text.Append('\n');
                text.Append(Clean(line.characterId.ToString())).Append('\t').Append(Clean(line.name.ToString())).Append('\t').Append(line.level).Append('\t')
                    .Append(line.isSponsor ? '1' : '0').Append('\t').Append(line.online ? '1' : '0').Append('\t').Append(line.near ? '1' : '0');
            }
            return text.ToString();
        }

        public static bool TryDecode(string text, out SponsorLinkView[] view)
        {
            view = System.Array.Empty<SponsorLinkView>();
            if (string.IsNullOrEmpty(text)) return true;
            var lines = new List<SponsorLinkView>();
            foreach (string row in text.Split('\n'))
            {
                string[] f = row.Split('\t');
                if (f.Length != 6 || !int.TryParse(f[2], out int level)) return false;
                var line = new SponsorLinkView { level = level, isSponsor = f[3] == "1", online = f[4] == "1", near = f[5] == "1" };
                line.characterId.CopyFromTruncated(f[0]);
                line.name.CopyFromTruncated(f[1]);
                lines.Add(line);
            }
            view = lines.ToArray();
            return true;
        }

        private static string Clean(string value) => (value ?? string.Empty).Replace('\t', ' ').Replace('\n', ' ');
    }
}
