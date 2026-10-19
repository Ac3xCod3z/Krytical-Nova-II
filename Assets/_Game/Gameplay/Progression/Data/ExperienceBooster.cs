using System;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>One XP booster: the item, what it adds while it runs, and how long one of it lasts.</summary>
    [Serializable]
    public struct ExperienceBooster
    {
        [Tooltip("The item that is used up (a utility item, 9500 and up).")]
        public ushort itemId;
        [Tooltip("Added to the XP of the character's Hostile kills while it runs, in percent (50 = x1.5).")]
        [Range(0, 500)] public int characterPercent;
        [Tooltip("Added to the XP of the pet's Hostile kills while it runs, in percent (100 = x2).")]
        [Range(0, 500)] public int petPercent;
        [Tooltip("Minutes one of it runs for. Using another adds as many.")]
        [Min(1)] public int minutes;
    }

    /// <summary>
    /// Where the XP boosters are listed, with how long a character may hold of one and whether its time runs offline.
    /// <c>EconomySettings</c> is the one (the boosters are Nova Store stock, kept with the economy's numbers); the boosts
    /// read it through this, so Progression need not know the economy.
    /// </summary>
    public interface IExperienceBoosterTable
    {
        ExperienceBooster[] Boosters { get; }
        int MaxHours { get; }
        bool TimeRunsOnlyOnline { get; }
    }
}
