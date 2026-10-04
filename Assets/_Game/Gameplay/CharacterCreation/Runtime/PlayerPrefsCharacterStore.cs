using System;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// Saves the roster as one JSON string in PlayerPrefs (same conventions as HudPreferences: "kn." prefix, a
    /// version in the key, PlayerPrefs.Save on every write).
    /// </summary>
    public sealed class PlayerPrefsCharacterStore : ICharacterStore
    {
        public const string BaseKey = "kn.characters.v1";

        private readonly string key;

        /// <param name="profile">Optional suffix that keeps several local players apart (Multiplayer Play Mode clones).</param>
        public PlayerPrefsCharacterStore(string profile = null)
        {
            key = string.IsNullOrEmpty(profile) ? BaseKey : BaseKey + "." + profile;
        }

        public CharacterRoster Load()
        {
            string json = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(json)) return new CharacterRoster();
            try
            {
                var roster = JsonUtility.FromJson<CharacterRoster>(json) ?? new CharacterRoster();
                roster.Normalize();
                return roster;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Characters] Saved characters could not be read and were ignored: {e.Message}");
                return new CharacterRoster();
            }
        }

        public void Save(CharacterRoster roster)
        {
            if (roster == null) return;
            roster.Normalize();
            PlayerPrefs.SetString(key, JsonUtility.ToJson(roster));
            PlayerPrefs.Save();
        }

        public void DeleteAll()
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }
}
