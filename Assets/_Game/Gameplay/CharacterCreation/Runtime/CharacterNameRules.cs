using System;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// What a character name may be. One word of 3 to 16 letters and digits that starts with a letter (owner, 22 Oct 2026:
    /// "players can use numbers in their name"), capitalised for the player. On a server names are
    /// unique across the whole game, capitals ignored (online phase 4, milestone 5): the server checks every new name
    /// against all of them (<see cref="RosterRequest"/>). Playing on this PC alone, the only duplicate check is against
    /// the player's own slots (<see cref="CharacterRoster.IsNameTaken"/>).
    /// </summary>
    public static class CharacterNameRules
    {
        public const int MinLength = 3;
        public const int MaxLength = 16;

        public const string ReasonEmpty = "Enter a name.";
        public const string ReasonTooShort = "Names need at least 3 characters.";
        public const string ReasonTooLong = "Names can have at most 16 characters.";
        public const string ReasonLettersOnly = "Use letters and digits only: no spaces or symbols.";
        public const string ReasonLetterFirst = "Start the name with a letter.";
        public const string ReasonRepeats = "Too many repeated letters or digits.";
        public const string ReasonReserved = "That name is reserved.";
        public const string ReasonNotAllowed = "That name is not allowed.";
        public const string ReasonNoGm = "Names can't contain GM.";
        public const string ReasonTaken = "You already have a character with that name.";
        public const string ReasonTakenInGame = "Someone already has a character with that name. Try another.";
        public const string ReasonMustRename = "Another character took this name first. Choose a new name to play it again.";

        private static readonly string[] Reserved =
        {
            "admin", "administrator", "moderator", "gamemaster", "server", "system", "support", "developer",
            "player", "null", "none", "hegemony", "covenant", "krytical", "nova"
        };

        // Deliberately short: a real list belongs on the server. Matched as substrings of the lower-cased name.
        private static readonly string[] Blocked =
        {
            "fuck", "shit", "bitch", "cunt", "nigg", "fagg", "rape", "nazi", "hitler", "penis", "vagina", "whore", "slut"
        };

        /// <summary>Capitalises the way names are shown: first letter upper case, the rest lower case.</summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            string trimmed = raw.Trim();
            return char.ToUpperInvariant(trimmed[0]) + (trimmed.Length > 1 ? trimmed.Substring(1).ToLowerInvariant() : string.Empty);
        }

        /// <summary>
        /// True when "GM" stands as its own part of the name (owner, 13 Nov 2026, pick B: nobody may pass for one of the game's
        /// GMs, but a word like "Magma" stays allowed). A G followed by an M (digits, spaces and symbols between them ignored) counts
        /// when it starts or ends the letters ("GMBob", "BobGM"), when anything but a letter sits between the two or beside them
        /// ("g.m.Bob", "G_M", "[GM]", "G1M", "Ace9GMx"), or when both are capitals set off by small letters ("BobGMx"). Checked on the
        /// raw text, before the letters-only rule, so a dotted "G.M." gets this reason rather than the symbols one.
        /// </summary>
        public static bool LooksLikeGm(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            string text = raw.Trim();
            var letters = new System.Collections.Generic.List<int>(text.Length); // positions of the letters in text
            for (int k = 0; k < text.Length; k++) if (char.IsLetter(text[k])) letters.Add(k);
            for (int n = 0; n + 1 < letters.Count; n++)
            {
                int g = letters[n], m = letters[n + 1];
                if (char.ToLowerInvariant(text[g]) != 'g' || char.ToLowerInvariant(text[m]) != 'm') continue;
                bool first = n == 0, last = n + 1 == letters.Count - 1;
                if (first || last) return true;
                if (m != g + 1) return true;                         // a digit or symbol between G and M
                if (letters[n - 1] != g - 1) return true;            // ... or just before the G
                if (letters[n + 2] != m + 1) return true;            // ... or just after the M
                bool capitals = char.IsUpper(text[g]) && char.IsUpper(text[m]);
                if (capitals && (char.IsLower(text[g - 1]) || char.IsLower(text[m + 1]))) return true; // "BobGMx"
            }
            return false;
        }

        /// <summary>Checks a name and returns its normalised form. <paramref name="reason"/> is user-facing.</summary>
        public static bool Validate(string raw, out string normalized, out string reason)
        {
            normalized = Normalize(raw);
            reason = null;
            if (normalized.Length == 0) { reason = ReasonEmpty; return false; }
            if (LooksLikeGm(raw)) { reason = ReasonNoGm; return false; }
            foreach (char c in normalized)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) { reason = ReasonLettersOnly; return false; }
            if (!(normalized[0] >= 'A' && normalized[0] <= 'Z')) { reason = ReasonLetterFirst; return false; }
            if (normalized.Length < MinLength) { reason = ReasonTooShort; return false; }
            if (normalized.Length > MaxLength) { reason = ReasonTooLong; return false; }

            string lower = normalized.ToLowerInvariant();
            for (int i = 2; i < lower.Length; i++)
                if (lower[i] == lower[i - 1] && lower[i] == lower[i - 2]) { reason = ReasonRepeats; return false; }
            if (Array.IndexOf(Reserved, lower) >= 0) { reason = ReasonReserved; return false; }
            foreach (string word in Blocked)
                if (lower.Contains(word)) { reason = ReasonNotAllowed; return false; }
            return true;
        }
    }
}
