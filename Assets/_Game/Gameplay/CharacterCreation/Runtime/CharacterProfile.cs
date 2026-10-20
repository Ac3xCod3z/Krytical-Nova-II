using System;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// One saved character: who it is, how it looks and what it was last seen wearing. The hidden <see cref="id"/> is what
    /// identifies it. On a server the roster is the server's and names are unique across the game (online phase 4,
    /// milestone 5); this PC keeps a copy of it for each server (<see cref="CharacterSession.UseStore"/>).
    /// </summary>
    [Serializable]
    public sealed class CharacterProfile
    {
        /// <summary>Unique ID (a GUID without dashes). Empty means the slot is unused.</summary>
        public string id;
        public string name;
        public CharacterRecipe recipe;
        public int level = 1;
        /// <summary>UTC ticks.</summary>
        public long created;
        public long lastPlayed;
        /// <summary>
        /// The gear the character wore when it was last played, for the select screen's doll. A copy for show only:
        /// the server's save is what the character really has. Empty for a character not played since this was kept.
        /// </summary>
        public WornPiece[] worn = Array.Empty<WornPiece>();
        /// <summary>The server asks for a new name before this character plays again (another took its name first).</summary>
        public bool mustRename;

        public bool IsUsed => !string.IsNullOrEmpty(id);

        /// <summary>The short tag shown after a name when two must be told apart (first four characters of the ID).</summary>
        public string Tag => IsUsed && id.Length >= 4 ? id.Substring(0, 4).ToUpperInvariant() : string.Empty;

        public static CharacterProfile Create(string characterName, in CharacterRecipe recipe)
        {
            long now = DateTime.UtcNow.Ticks;
            return new CharacterProfile
            {
                id = Guid.NewGuid().ToString("N"),
                name = characterName,
                recipe = recipe.Clone(),
                level = 1,
                created = now,
                lastPlayed = now
            };
        }
    }
}
