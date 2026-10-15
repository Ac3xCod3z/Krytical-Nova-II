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
    /// format, so add to them and never rename one. A Hotbar slot of -1 is "none". What Magnet takes is a set of
    /// tick boxes and nothing a player types (owner, 9 Oct 2026: a typed item name can be got wrong).
    /// </summary>
    [Serializable]
    public sealed class AutopilotPreferences
    {
        public const int NoSlot = -1;

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
        // Unused since the Autobuff (15 Oct 2026) keeps the buffs: kept so older saves still parse.
        public int buffSlotA = NoSlot;
        public int buffSlotB = NoSlot;

        [Header("Stims")]
        public float stimHealthPercent = 60f;
        public float stimFluxPercent = 60f;

        [Header("Magnet")]
        public bool magnetOn = true;
        public bool magnetStims = true;
        public bool magnetMaterials = true;
        public bool takeCommon;
        public bool takeUncommon = true;
        public bool takeRare = true;
        public bool takeEpic = true;

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
        }

        /// <summary>Whether gear of this rarity is ticked. Anything above Epic goes with Epic.</summary>
        public bool TakesGear(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Common: return takeCommon;
                case ItemRarity.Uncommon: return takeUncommon;
                case ItemRarity.Rare: return takeRare;
                default: return takeEpic;
            }
        }

        private static int Slot(int slot) => slot >= 0 && slot < 14 ? slot : NoSlot;
    }
}
