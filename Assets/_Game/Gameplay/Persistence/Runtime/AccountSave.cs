using System;
using System.Collections.Generic;

namespace KryticalNova.Persistence
{
    /// <summary>One system's document inside a save (see <see cref="KryticalNova.Core.ISaveParticipant"/>).</summary>
    [Serializable]
    public sealed class SavePart
    {
        public string key;
        public string json;
    }

    /// <summary>
    /// Everything the server keeps for one account: what its characters share, and each character. One document, so
    /// a save is one write and the Deep Vault can never disagree with the characters that fill it.
    /// </summary>
    [Serializable]
    public sealed class AccountSave
    {
        /// <summary>2: kept in parts (online phase 4, milestone 2: <see cref="ServerSave.WriteAccount"/>); 1: one document.</summary>
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;
        public string accountId;
        /// <summary>Account-wide documents (the Deep Vault, the daily trade allowance).</summary>
        public List<SavePart> parts = new List<SavePart>();
        public List<CharacterSave> characters = new List<CharacterSave>();

        public CharacterSave Find(string characterId)
        {
            if (characters == null) return null;
            foreach (CharacterSave character in characters)
                if (character != null && character.characterId == characterId) return character;
            return null;
        }

        public string Get(string key) => SaveParts.Get(parts, key);
        /// <summary>True when the document changed.</summary>
        public bool Set(string key, string json) => SaveParts.Set(parts ??= new List<SavePart>(), key, json);
    }

    /// <summary>One character on the server. The header fields are what lists show without opening the parts.</summary>
    [Serializable]
    public sealed class CharacterSave
    {
        public string characterId;
        public string name;
        public int faction;
        public int level = 1;
        /// <summary>UTC ticks of the last change that was written.</summary>
        public long savedUtc;
        /// <summary>The map the character was on and where it stood when last saved (online phase 4; used once there is more than one map).</summary>
        public string map;
        public float x, y, z;
        public List<SavePart> parts = new List<SavePart>();

        public string Get(string key) => SaveParts.Get(parts, key);
        /// <summary>True when the document changed.</summary>
        public bool Set(string key, string json) => SaveParts.Set(parts ??= new List<SavePart>(), key, json);
    }

    internal static class SaveParts
    {
        public static string Get(List<SavePart> parts, string key)
        {
            if (parts == null) return null;
            foreach (SavePart part in parts)
                if (part != null && part.key == key) return string.IsNullOrEmpty(part.json) ? null : part.json;
            return null;
        }

        public static bool Set(List<SavePart> parts, string key, string json)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] == null || parts[i].key != key) continue;
                if (string.IsNullOrEmpty(json))
                {
                    parts.RemoveAt(i);
                    return true;
                }
                if (parts[i].json == json) return false;
                parts[i].json = json;
                return true;
            }
            if (string.IsNullOrEmpty(json)) return false;
            parts.Add(new SavePart { key = key, json = json });
            return true;
        }
    }
}
