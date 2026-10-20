using System;
using KryticalNova.Core;
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

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            store = null;
            active = null;
            activeSlot = -1;
            resolved = false;
            LocalAccount.ForgetMadeUp();
            LocalAccount.VerifiedId = null;
        }

        /// <summary>The id of this player's account (<see cref="LocalAccount.Id"/>).</summary>
        public static string AccountId => LocalAccount.Id;

        /// <summary>The account Unity's sign-in proves, set while joining a server (<see cref="LocalAccount.VerifiedId"/>).</summary>
        public static string VerifiedAccountId
        {
            get => LocalAccount.VerifiedId;
            set => LocalAccount.VerifiedId = value;
        }

        /// <summary>The id the game made up once per machine profile (<see cref="LocalAccount.MadeUpId"/>).</summary>
        public static string MadeUpAccountId => LocalAccount.MadeUpId;

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
        /// Keeps the look the select screen shows in step with the character being played. The server holds the
        /// look that counts (it changes at the Stylist, the Reshaper and the Coach); this is the player's own copy.
        /// </summary>
        public static void RememberRecipe(in CharacterRecipe recipe)
        {
            if (!TryGetActive(out var profile) || recipe.IsEmpty || profile.recipe.Equals(recipe)) return;
            var roster = Store.Load();
            for (int slot = 0; slot < CharacterRoster.SlotCount; slot++)
            {
                var saved = roster.Get(slot);
                if (saved == null || saved.id != profile.id) continue;
                saved.recipe = recipe.Clone();
                Store.Save(roster);
                break;
            }
            profile.recipe = recipe.Clone();
        }

        /// <summary>
        /// The store used by menus and players. Each Multiplayer Play Mode clone, and each build started with its
        /// own "-profile", gets its own save.
        /// </summary>
        public static ICharacterStore Store
        {
            get
            {
                if (store == null) store = new PlayerPrefsCharacterStore(LocalProfile.Name());
                return store;
            }
            set => store = value;
        }

        /// <summary>
        /// Shows and plays from another store: this PC's own characters, or this PC's copy of a server's roster (online
        /// phase 4, milestone 5). The character chosen before is forgotten.
        /// </summary>
        public static void UseStore(ICharacterStore next)
        {
            if (next == null || ReferenceEquals(store, next)) return;
            store = next;
            active = null;
            activeSlot = -1;
            resolved = false;
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
