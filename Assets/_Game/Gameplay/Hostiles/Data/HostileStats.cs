using System.Collections.Generic;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>The numbers of one Hostile at one level and tier, as <see cref="HostileStatCurve"/> works them out.</summary>
    public struct HostileStats
    {
        public int Level;
        public HostileTier Tier;
        public float MaxHealth;
        public float Attack;
        public float Defense;
        public float HitRate;
        public float Evasion;
        public float CriticalRate;
        public int Experience;
        public int Creds;
        /// <summary>Metres at which an attack-on-sight Hostile notices a player.</summary>
        public float AggroRadius;
        public float WalkSpeed;
        public float RunSpeed;
        /// <summary>Seconds; for a Bruiser the shortest wait.</summary>
        public float RespawnSeconds;
        /// <summary>Seconds; the longest wait of a Bruiser. Equal to <see cref="RespawnSeconds"/> for the rest.</summary>
        public float RespawnSecondsMax;

        /// <summary>
        /// Gives a Hostile's stat sheet these numbers. A stat sheet with no class works its values out from
        /// attributes, so the attributes are emptied and each stat is a modifier that makes up the difference.
        /// Max HP is not set here: it is the <see cref="CombatEntity"/>'s own (SetMaxHealthOverride).
        /// </summary>
        public void ApplyTo(CharacterCombatStats sheet)
        {
            if (sheet == null) return;
            sheet.SetFallbackAttributes(0, 0, 0, 0);
            var modifiers = new List<StatModifier>(5);
            Add(modifiers, sheet, StatType.AttackPower, Attack);
            Add(modifiers, sheet, StatType.DefensePower, Defense);
            Add(modifiers, sheet, StatType.HitRate, HitRate);
            Add(modifiers, sheet, StatType.EvasionRate, Evasion);
            Add(modifiers, sheet, StatType.CriticalRate, CriticalRate);
            sheet.SetSourceModifiers(Source, modifiers);
        }

        /// <summary>The one owner of every modifier written by <see cref="ApplyTo"/>, so a second call replaces the first.</summary>
        private static readonly object Source = new object();

        private static void Add(List<StatModifier> modifiers, CharacterCombatStats sheet, StatType stat, float wanted)
        {
            float difference = Mathf.Max(0f, wanted) - sheet.GetBaseStatValue(stat);
            if (!Mathf.Approximately(difference, 0f)) modifiers.Add(new StatModifier(stat, ModifierType.Flat, difference, Source));
        }
    }
}
