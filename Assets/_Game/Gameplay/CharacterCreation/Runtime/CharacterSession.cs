using System;
using KryticalNova.Core;
using KryticalNova.Networking;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// The character this player is playing right now, and the store it came from. The character select screen sets
    /// it before the zone loads; the zone's player reads it. Started straight in a zone (pressing Play in the Editor),
    /// it falls back to the last-played saved character, and to nothing if there is none (the prefab's own look).
    /// </summary>
    public static class CharacterSession
    {
        private static ICharacterStore store;
        private static CharacterProfile active;
        private static int activeSlot = -1;
        private static bool resolved;
        private static string accountId;

        private const string AccountKey = "kn.account.v1";

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            store = null;
            active = null;
            activeSlot = -1;
            resolved = false;
            accountId = null;
        }

        /// <summary>
        /// The id of this player's account: what its characters, and the things they share (the Deep Vault), are
        /// saved under on the server. Made once per machine profile and kept in PlayerPrefs beside the roster, until
        /// real sign-in replaces it.
        /// </summary>
        public static string AccountId
        {
            get
            {
                if (accountId != null) return accountId;
                string profile = NetworkConnectionManager.LocalProfile();
                string key = string.IsNullOrEmpty(profile) ? AccountKey : AccountKey + "." + profile;
                accountId = PlayerPrefs.GetString(key, string.Empty);
                if (!SaveKeys.IsId(accountId))
                {
                    accountId = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(key, accountId);
                    PlayerPrefs.Save();
                }
                return accountId;
            }
        }

        /// <summary>The character being played, or null.</summary>
        public static CharacterProfile Active => TryGetActive(out var profile) ? profile : null;

        /// <summary>Keeps the level shown on the select screen in step with the character being played.</summary>
        public static void RememberLevel(int level)
        {
            if (!TryGetActive(out var profile) || profile.level == level || level < 1) return;
            var roster = Store.Load();
            for (int slot = 0; slot < CharacterRoster.SlotCount; slot++)
            {
                var saved = roster.Get(slot);
                if (saved == null || saved.id != profile.id) continue;
                saved.level = level;
                Store.Save(roster);
                break;
            }
            profile.level = level;
        }

        /// <summary>
        /// Keeps the gear the select screen's doll wears in step with the character being played. Called on the
        /// player's own machine, which is told what its character wears whoever hosts the game.
        /// </summary>
        public static void RememberWorn(System.Collections.Generic.IReadOnlyList<WornPiece> worn)
        {
            if (!TryGetActive(out var profile) || worn == null || DollOutfit.Same(profile.worn, worn)) return;
            var pieces = new WornPiece[worn.Count];
            for (int i = 0; i < pieces.Length; i++) pieces[i] = worn[i];
            var roster = Store.Load();
            for (int slot = 0; slot < CharacterRoster.SlotCount; slot++)
            {
                var saved = roster.Get(slot);
                if (saved == null || saved.id != profile.id) continue;
                saved.worn = pieces;
                Store.Save(roster);
                break;
            }
            profile.worn = pieces;
        }

        /// <summary>
        /// The store used by menus and players. Each Multiplayer Play Mode clone, and each build started with its
        /// own "-profile", gets its own save.
        /// </summary>
        public static ICharacterStore Store
        {
            get
            {
                if (store == null) store = new PlayerPrefsCharacterStore(NetworkConnectionManager.LocalProfile());
                return store;
            }
            set => store = value;
        }

        public static int ActiveSlot => activeSlot;

        /// <summary>Choose the character to enter the world with, stamp it as played and save that.</summary>
        public static void Enter(CharacterRoster roster, int slot)
        {
            if (roster == null || !roster.IsUsed(slot)) return;
            roster.MarkPlayed(slot);
            Store.Save(roster);
            active = roster.Get(slot);
            activeSlot = slot;
            resolved = true;
        }

        public static void Clear()
        {
            active = null;
            activeSlot = -1;
            resolved = true;
        }

        public static bool TryGetActive(out CharacterProfile profile)
        {
            if (!resolved)
            {
                resolved = true;
                var roster = Store.Load();
                activeSlot = roster.PreferredSlot;
                active = roster.Get(activeSlot);
            }
            profile = active;
            return profile != null && profile.IsUsed && !profile.recipe.IsEmpty;
        }
    }
}
