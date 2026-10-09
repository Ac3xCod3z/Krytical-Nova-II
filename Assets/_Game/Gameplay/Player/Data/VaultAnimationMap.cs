using System;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>Which body a set of vault animations was made for.</summary>
    public enum VaultBody : byte
    {
        /// <summary>Both body types.</summary>
        Any = 0,
        Masculine = 1,
        Feminine = 2,
    }

    /// <summary>
    /// Which vault animations a character uses: a set per faction and body type, and the default set for everyone
    /// else. The obstacle's height picks the style inside the set. A set that has no animation for a height falls
    /// back to the next set in line for that same height (the faction's set for both bodies, then the default), never
    /// to an animation of another height. Every machine resolves it from what it already knows of a player (faction
    /// and body type are replicated), so all of them play the same clip.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Player/Vault Animation Map", fileName = "VaultAnimationMap")]
    public sealed class VaultAnimationMap : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public FactionId faction;
            [Tooltip("Any: both body types of the faction, unless another entry names one of them.")]
            public VaultBody body;
            public VaultAnimationSet set;

            public Entry(FactionId faction, VaultBody body, VaultAnimationSet set)
            {
                this.faction = faction; this.body = body; this.set = set;
            }
        }

        [Tooltip("Used by anyone no entry covers, and for any height an entry's set has no animation for. Must have all three heights.")]
        public VaultAnimationSet defaultSet;
        public Entry[] entries = Array.Empty<Entry>();

        /// <summary>True when the default set can answer for every height: the map can always resolve.</summary>
        public bool IsUsable => defaultSet != null && defaultSet.IsComplete;

        public static VaultBody BodyOf(BodyType body) => body == BodyType.Feminine ? VaultBody.Feminine : VaultBody.Masculine;

        /// <summary>
        /// The style a character of this faction and body uses for an obstacle of this height, and which set it came
        /// from. False only when not even the default set has one (<see cref="IsUsable"/> is false).
        /// </summary>
        public bool TryResolve(FactionId faction, BodyType body, VaultHeight height, out VaultStyle style, out VaultAnimationSet from)
        {
            from = Find(faction, BodyOf(body));
            if (from == null || !from.Has(height)) from = Find(faction, VaultBody.Any);
            if (from == null || !from.Has(height)) from = defaultSet;
            if (from == null || !from.Has(height))
            {
                style = default;
                from = null;
                return false;
            }
            style = from.Get(height);
            return true;
        }

        public bool TryResolve(FactionId faction, BodyType body, VaultHeight height, out VaultStyle style) =>
            TryResolve(faction, body, height, out style, out _);

        /// <summary>The set an entry names for exactly this faction and body, or null.</summary>
        public VaultAnimationSet Find(FactionId faction, VaultBody body)
        {
            if (entries == null || faction == FactionId.None) return null;
            foreach (Entry entry in entries)
                if (entry.faction == faction && entry.body == body && entry.set != null) return entry.set;
            return null;
        }
    }
}
