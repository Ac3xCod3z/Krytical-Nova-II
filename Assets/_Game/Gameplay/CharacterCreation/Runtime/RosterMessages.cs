using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// What a game asks a server about its characters (online phase 4, milestone 5: the roster on the server). A
    /// dedicated server keeps which characters an account has: the game lists them, creates and deletes them, and
    /// renames one the server asks to be renamed, by asking. The server checks every name and look, and names are unique
    /// across the whole game (capitals ignored). Sent as JSON; the network carries it as text.
    /// </summary>
    [Serializable]
    public sealed class RosterRequest
    {
        public const string List = "list";
        public const string Create = "create";
        public const string Delete = "delete";
        public const string Rename = "rename";

        public string op;
        /// <summary>Delete and rename: which character.</summary>
        public string characterId;
        /// <summary>Create and rename: the name wanted.</summary>
        public string name;
        /// <summary>Create: the slot wanted (the server takes the first free one when it is not).</summary>
        public int slot = -1;
        /// <summary>Create: the look.</summary>
        public CharacterRecipe recipe;
        /// <summary>
        /// List: the looks this PC keeps of its characters. The server takes one only for a character of this account it
        /// has no look for (one made before looks were saved), checked like a new one.
        /// </summary>
        public List<RosterLook> looks = new List<RosterLook>();

        public string ToJson() => JsonUtility.ToJson(this);

        public static RosterRequest FromJson(string json)
        {
            try { return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<RosterRequest>(json); }
            catch (Exception) { return null; }
        }
    }

    /// <summary>A look this PC keeps of a character (see <see cref="RosterRequest.looks"/>).</summary>
    [Serializable]
    public sealed class RosterLook
    {
        public string id;
        public CharacterRecipe recipe;
    }

    /// <summary>The server's answer: done or why not, and the account's characters as they are now.</summary>
    [Serializable]
    public sealed class RosterReply
    {
        public bool ok;
        /// <summary>Why not, in the words the player reads; empty when done.</summary>
        public string message = string.Empty;
        /// <summary>Create: the new character's id.</summary>
        public string characterId = string.Empty;
        public List<RosterCharacter> characters = new List<RosterCharacter>();

        public string ToJson() => JsonUtility.ToJson(this);

        public static RosterReply FromJson(string json)
        {
            try { return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<RosterReply>(json); }
            catch (Exception) { return null; }
        }

        public static RosterReply No(string message) => new RosterReply { ok = false, message = message ?? string.Empty };
    }

    /// <summary>One character of the account, as the select screen shows it.</summary>
    [Serializable]
    public sealed class RosterCharacter
    {
        public string id;
        public int slot = -1;
        public string name;
        public int level = 1;
        public CharacterRecipe recipe;
        /// <summary>UTC ticks.</summary>
        public long created;
        public long lastPlayed;
        /// <summary>Another character took this name first (names became unique): it is renamed before it plays again.</summary>
        public bool mustRename;
    }
}
