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

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            store = null;
            active = null;
            activeSlot = -1;
            resolved = false;
        }

        /// <summary>The store used by menus and players. Each Multiplayer Play Mode clone gets its own save.</summary>
        public static ICharacterStore Store
        {
            get
            {
                if (store == null) store = new PlayerPrefsCharacterStore(NetworkConnectionManager.EditorCloneProfile());
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
