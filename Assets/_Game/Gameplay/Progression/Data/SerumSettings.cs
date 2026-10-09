using System;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>The item ids of the Serums. Named here so the settings asset and the item generator agree.</summary>
    public static class SerumIds
    {
        public const ushort Strength = 9200;
        public const ushort Vitality = 9201;
        public const ushort Agility = 9202;
        public const ushort Spirit = 9203;
        public const ushort ElementalStrike = 9204;
        public const ushort ElementalWard = 9205;
    }

    /// <summary>One stat a Serum raises, and by how much for each one taken.</summary>
    [Serializable]
    public struct SerumGain
    {
        public StatType stat;
        [Min(0f)] public float perUse;

        public SerumGain(StatType stat, float perUse)
        {
            this.stat = stat; this.perUse = perUse;
        }
    }

    /// <summary>One kind of Serum: the item that is it, and what a character keeps for each one taken.</summary>
    [Serializable]
    public struct SerumKind
    {
        [Tooltip("The item a player uses. Never change it once characters have taken some: their count is saved under it.")]
        public ushort itemId;
        [Tooltip("Shown on the Character Sheet (\"Strength Serum (37/100) +111 Attack Power\").")]
        public string displayName;
        public SerumGain[] gains;

        public SerumKind(ushort itemId, string displayName, params SerumGain[] gains)
        {
            this.itemId = itemId; this.displayName = displayName; this.gains = gains;
        }
    }

    /// <summary>
    /// Serums (Twelve Sky 2: Elixirs): items that raise a stat for good, each kind up to a number of times per
    /// character. Which kinds there are, what each gives and how many a character can take. Nothing in code may
    /// hard-code one of these numbers.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Progression/Serum Settings", fileName = "SerumSettings")]
    public sealed class SerumSettings : ScriptableObject
    {
        /// <summary>How many kinds a character's record has room for. More needs a wider record (<see cref="SerumState"/>).</summary>
        public const int MaxKinds = 8;

        [Tooltip("How many of one kind a character can take.")]
        [Min(1)] public int baseCap = 100;
        [Tooltip("The later system: with this on, a character can go on to the expansion cap. Off for now, like Tempering beyond 120%.")]
        public bool expansionEnabled;
        [Tooltip("How many of one kind a character can take once the expansion is on (the base cap included).")]
        [Min(1)] public int expansionCap = 200;

        [Tooltip("The kinds, in the order the Character Sheet lists them. Append: a character's record is kept by item id.")]
        public SerumKind[] serums = Defaults();

        /// <summary>How many of one kind a character can take now.</summary>
        public int Cap => expansionEnabled ? Mathf.Max(baseCap, expansionCap) : baseCap;

        /// <summary>Twelve Sky 2's six, at its values.</summary>
        public static SerumKind[] Defaults() => new[]
        {
            new SerumKind(SerumIds.Strength, "Strength Serum", new SerumGain(StatType.AttackPower, 3f)),
            new SerumKind(SerumIds.Vitality, "Vitality Serum", new SerumGain(StatType.MaxHP, 20f)),
            new SerumKind(SerumIds.Agility, "Agility Serum", new SerumGain(StatType.HitRate, 2f), new SerumGain(StatType.EvasionRate, 2f)),
            new SerumKind(SerumIds.Spirit, "Spirit Serum", new SerumGain(StatType.MaxChi, 25f)),
            new SerumKind(SerumIds.ElementalStrike, "Elemental Strike Serum", new SerumGain(StatType.ElementalAttack, 10f)),
            new SerumKind(SerumIds.ElementalWard, "Elemental Ward Serum", new SerumGain(StatType.ElementalDefense, 10f)),
        };

        private void OnValidate()
        {
            if (serums != null && serums.Length > MaxKinds)
                Debug.LogError($"[Serums] {serums.Length} kinds, but a character's record holds {MaxKinds}. The ones past that are ignored.", this);
        }
    }
}
