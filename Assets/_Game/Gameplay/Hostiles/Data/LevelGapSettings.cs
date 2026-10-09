using System;
using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// What the gap between a Hostile's level and a player's does: the colour of its name, the XP and drops a
    /// kill is worth, the player's chance to hit. Also the XP lost on a death while hunting. Change a colour or
    /// a percentage here, never in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Hostiles/Level Gap Settings")]
    public sealed class LevelGapSettings : ScriptableObject
    {
        /// <summary>One band of gaps. Gap = Hostile level minus player level.</summary>
        [Serializable]
        public struct Band
        {
            public string name;
            [Tooltip("Lowest gap of the band.")]
            public int fromGap;
            [Tooltip("Highest gap of the band.")]
            public int toGap;
            public Color nameColor;
            [Tooltip("XP percent for each gap of the band, from the lowest gap up. One value = the whole band.")]
            public float[] experiencePercent;
            [Tooltip("Creds and gear drop chance, percent of normal.")]
            [Range(0f, 100f)] public float dropPercent;
            [Tooltip("Off: no gear drops in this band, whatever the drop percent.")]
            public bool gearDrops;
            [Tooltip("Added to the player's chance to hit, in percent points (negative = harder to hit).")]
            public float hitChanceModifier;
        }

        [Tooltip("Lowest band first. A gap below the first band uses the first, above the last uses the last.")]
        public Band[] bands =
        {
            new Band { name = "Grey", fromGap = -200, toGap = -10, nameColor = Hex(0x92, 0xA2, 0xB2), experiencePercent = new[] { 0f }, dropPercent = 0f, gearDrops = false },
            new Band { name = "Blue", fromGap = -9, toGap = -6, nameColor = Hex(0x6E, 0xAA, 0xFF), experiencePercent = new[] { 8f, 13f, 22f, 37f }, dropPercent = 25f, gearDrops = false },
            new Band { name = "Teal", fromGap = -5, toGap = -3, nameColor = Hex(0x72, 0xDE, 0xC4), experiencePercent = new[] { 61f, 80f, 97f }, dropPercent = 75f, gearDrops = true },
            new Band { name = "White", fromGap = -2, toGap = 4, nameColor = Hex(0xE6, 0xED, 0xF5), experiencePercent = new[] { 100f }, dropPercent = 100f, gearDrops = true },
            new Band { name = "Purple", fromGap = 5, toGap = 9, nameColor = Hex(0xC8, 0x80, 0xFF), experiencePercent = new[] { 125f, 135f, 145f, 155f, 165f }, dropPercent = 100f, gearDrops = true, hitChanceModifier = -5f },
            new Band { name = "Red", fromGap = 10, toGap = 200, nameColor = Hex(0xFF, 0x5C, 0x52), experiencePercent = new[] { 175f }, dropPercent = 100f, gearDrops = true, hitChanceModifier = -15f },
        };

        [Header("Squads")]
        [Tooltip("A Squad member this many levels or more below the Squad's highest member is paid by the highest member's gap, not their own.")]
        [Min(1)] public int squadCarryLevels = 6;

        [Header("Death while hunting")]
        [Tooltip("Share of the current level's XP lost on a death to a Hostile. Never takes a level.")]
        [Range(0f, 100f)] public float deathExperienceLossPercent = 5f;
        [Tooltip("From this Prime level up a death costs nothing.")]
        [Min(1)] public int noDeathLossFromPrimeLevel = 33;

        [Header("Kill credit")]
        [Tooltip("The first player to hit a Hostile holds it. The hold ends after this many seconds without damage from them.")]
        [Min(1f)] public float tagTimeoutSeconds = 10f;

        private static Color Hex(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1f);
    }
}
