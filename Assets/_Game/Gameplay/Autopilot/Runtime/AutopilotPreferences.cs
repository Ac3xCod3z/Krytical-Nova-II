using System;
using KryticalNova.Items;
using UnityEngine;

namespace KryticalNova.Autopilot
{
    /// <summary>What Autopilot does when the Pack is full.</summary>
    public enum WhenPackFull : byte { KeepHunting = 0, Stop = 1 }

    /// <summary>What Autopilot does when no Stim is left.</summary>
    public enum WhenStimsOut : byte { Stop = 0, KeepHunting = 1 }

    /// <summary>What Autopilot does when another player attacks.</summary>
    public enum WhenAttacked : byte { SwitchOff = 0, FightBack = 1 }

    /// <summary>
    /// One character's Autopilot and Magnet choices. Saved with the character as JSON; the fields are the save
    /// format, so add to them and never rename one. A Hotbar slot of -1 is "none".
    /// </summary>
    [Serializable]
    public sealed class AutopilotPreferences
    {
        public const int NoSlot = -1;
        private const int MaxNames = 24, MaxNameLength = 40;

        [Header("Where")]
        public bool wholeZone = true;
        public float huntRadius = 25f;

        [Header("Which Hostiles")]
        public bool skipTagged = true;
        public bool skipGrey = true;
        public bool skipRed = true;
        public bool includeBruisers;

        [Header("How (Hotbar slots)")]
        public int attackSlot = NoSlot;
        public int areaSlot = NoSlot;
        public int gatherCount = 1;
        public int buffSlotA = NoSlot;
        public int buffSlotB = NoSlot;

        [Header("Stims")]
        public float stimHealthPercent = 60f;
        public float stimFluxPercent = 60f;

        [Header("Magnet")]
        public bool magnetOn = true;
        public bool magnetStims = true;
        public ItemRarity minGearRarity = ItemRarity.Uncommon;
        public string[] alwaysTake = Array.Empty<string>();
        public string[] neverTake = Array.Empty<string>();

        [Header("When things go wrong")]
        public WhenPackFull whenPackFull = WhenPackFull.KeepHunting;
        public WhenStimsOut whenStimsOut = WhenStimsOut.Stop;
        public WhenAttacked whenAttacked = WhenAttacked.SwitchOff;

        public string ToJson() => JsonUtility.ToJson(this);

        /// <summary>Preferences from JSON, with every value brought inside its limits. Bad or empty JSON gives the defaults.</summary>
        public static AutopilotPreferences FromJson(string json, AutopilotSettings settings)
        {
            AutopilotPreferences preferences = null;
            if (!string.IsNullOrEmpty(json))
            {
                try { preferences = JsonUtility.FromJson<AutopilotPreferences>(json); }
                catch (ArgumentException) { preferences = null; }
            }
            preferences ??= new AutopilotPreferences();
            preferences.Sanitise(settings);
            return preferences;
        }

        public void Sanitise(AutopilotSettings settings)
        {
            Vector2 radius = settings != null ? settings.huntRadius : new Vector2(10f, 40f);
            huntRadius = Mathf.Clamp(huntRadius, radius.x, radius.y);
            gatherCount = Mathf.Clamp(gatherCount, 1, settings != null ? settings.maxGatherCount : 8);
            stimHealthPercent = Mathf.Clamp(stimHealthPercent, 0f, 95f);
            stimFluxPercent = Mathf.Clamp(stimFluxPercent, 0f, 95f);
            attackSlot = Slot(attackSlot);
            areaSlot = Slot(areaSlot);
            buffSlotA = Slot(buffSlotA);
            buffSlotB = Slot(buffSlotB);
            if (!Enum.IsDefined(typeof(ItemRarity), minGearRarity)) minGearRarity = ItemRarity.Uncommon;
            alwaysTake = Names(alwaysTake);
            neverTake = Names(neverTake);
        }

        private static int Slot(int slot) => slot >= 0 && slot < 14 ? slot : NoSlot;

        private static string[] Names(string[] names)
        {
            if (names == null) return Array.Empty<string>();
            var kept = new System.Collections.Generic.List<string>(names.Length);
            foreach (string name in names)
            {
                string trimmed = (name ?? string.Empty).Trim();
                if (trimmed.Length == 0 || kept.Count >= MaxNames) continue;
                kept.Add(trimmed.Length > MaxNameLength ? trimmed.Substring(0, MaxNameLength) : trimmed);
            }
            return kept.ToArray();
        }
    }
}
