using System;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// Random names in each lineage's flavour (see _Docs/Design/11_factions_lore.md): Hegemony names are short and
    /// corporate, robot names are designations that became names, skeleton names are old words for endings, Viking
    /// names are northern and Elven names flow. Every result passes <see cref="CharacterNameRules"/>.
    /// </summary>
    public static class CharacterNameGenerator
    {
        private struct Table
        {
            public string[] start, masculineEnd, feminineEnd;
        }

        // Indexed by lineage ID - 1: Civilian, Soldier, Robot, Skeleton, Viking, Elf.
        // Starts end in a vowel (or a soft consonant) and endings begin with a consonant, so any pair reads as a name.
        private static readonly Table[] Tables =
        {
            new Table
            {
                start = new[] { "Va", "Ke", "Mi", "Da", "So", "Ori", "Ly", "Te", "Ca", "Re", "Jo", "Ha", "Eli", "No", "Sy" },
                masculineEnd = new[] { "ren", "vin", "lan", "rek", "dric", "ton", "sler", "rin", "mon", "len" },
                feminineEnd = new[] { "ra", "lenne", "ria", "na", "lise", "ris", "dra", "vie", "lyn", "ssa" }
            },
            new Table
            {
                start = new[] { "Ka", "Bre", "Ro", "To", "Vi", "Ma", "Sta", "Dre", "Ga", "Ho", "Ja", "Ry", "Co", "Ba", "Za" },
                masculineEnd = new[] { "ne", "ker", "rick", "sen", "ston", "vok", "der", "lan", "rus", "gan" },
                feminineEnd = new[] { "ra", "ris", "lyn", "na", "ska", "vi", "dra", "tte", "len", "ya" }
            },
            new Table
            {
                start = new[] { "Axi", "Ci", "Ve", "Nu", "Octa", "Si", "Kry", "Ze", "He", "Vo", "Te", "Qua", "Sy", "Bi", "Arca" },
                masculineEnd = new[] { "tor", "pher", "tane", "ton", "dron", "trex", "ron", "xis", "lon", "mos" },
                feminineEnd = new[] { "tra", "phra", "la", "ra", "ris", "lix", "na", "lyx", "nia", "ssa" }
            },
            new Table
            {
                start = new[] { "Mo", "Ossi", "Gra", "Sepu", "Ve", "Cai", "Ne", "Tha", "Ma", "Du", "Umbra", "Cry", "Asha", "Vo", "Obi" },
                masculineEnd = new[] { "rtis", "sian", "ven", "then", "per", "ron", "cros", "nos", "gor", "lith" },
                feminineEnd = new[] { "rtia", "sia", "vena", "thra", "pera", "rna", "cria", "nyx", "ra", "lia" }
            },
            new Table
            {
                start = new[] { "Bjo", "Asa", "Ra", "Ha", "Si", "To", "Gu", "Ulf", "Frey", "Ei", "Hra", "Sva", "Inga", "Kna", "Thy" },
                masculineEnd = new[] { "rn", "var", "gnar", "vald", "mund", "leif", "grim", "stein", "rik", "bjorn" },
                feminineEnd = new[] { "strid", "na", "run", "hild", "dis", "ja", "gerd", "ny", "rid", "vor" }
            },
            new Table
            {
                start = new[] { "Ae", "Tha", "Syl", "Fae", "Ili", "Cae", "Lu", "Ela", "Ny", "Auri", "Ga", "Mi", "Vae", "Ithi", "Ari" },
                masculineEnd = new[] { "lion", "lar", "dir", "ron", "wyn", "thas", "rion", "las", "dil", "mir" },
                feminineEnd = new[] { "lindra", "wen", "yana", "riel", "thiel", "neth", "sae", "lia", "rwen", "nis" }
            },
        };

        // Valid by the rules, but nobody wants them suggested.
        private static readonly string[] Unfortunate = { "kack", "mince", "dick", "titt", "anal", "poo", "arse", "hore" };
        private const int MinSuggestedLength = 4;

        public static string Generate(byte lineage, BodyType bodyType, Random random)
        {
            if (random == null) random = new Random();
            var table = Tables[Math.Clamp(lineage - 1, 0, Tables.Length - 1)];
            var endings = bodyType == BodyType.Feminine ? table.feminineEnd : table.masculineEnd;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                string candidate = table.start[random.Next(table.start.Length)] + endings[random.Next(endings.Length)];
                if (candidate.Length < MinSuggestedLength || IsUnfortunate(candidate)) continue;
                if (CharacterNameRules.Validate(candidate, out string normalized, out _)) return normalized;
            }
            return "Wanderer";
        }

        private static bool IsUnfortunate(string candidate)
        {
            string lower = candidate.ToLowerInvariant();
            foreach (string word in Unfortunate)
                if (lower.Contains(word)) return true;
            return false;
        }
    }
}
