using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// The nine weapon classes: what each is called, whose it is, and its ten skills by slot. Built by the class
    /// skill builder. The creator's Discipline step, the skill system, the GM tools and the starter kit all read
    /// this one asset, so a class is defined in exactly one place.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Items/Weapon Class Catalog")]
    public sealed class WeaponClassCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public WeaponClassId id;
            public string displayName;
            public FactionId faction;
            public WeaponClassRole role;
            [Tooltip("One line for the creator card: what the class is.")]
            [TextArea] public string summary;
            [Tooltip("What the class's Stance does, for the creator card.")]
            public string stanceSummary;
            [Tooltip("The class's ten skills in slot order: S1 to S8, Stance, Guard.")]
            public SkillDefinitionSO[] skills;

            public bool IsValid => id != WeaponClassId.None;

            public SkillDefinitionSO SkillIn(ClassSkillSlot slot)
            {
                int index = (int)slot - 1;
                return skills != null && index >= 0 && index < skills.Length ? skills[index] : null;
            }
        }

        [SerializeField] private Entry[] classes = Array.Empty<Entry>();
        [Tooltip("Skills every class can use (self-buffs).")]
        [SerializeField] private SkillDefinitionSO[] sharedSkills = Array.Empty<SkillDefinitionSO>();

        [NonSerialized] private readonly Dictionary<FactionId, SkillDefinitionSO[]> byFaction = new Dictionary<FactionId, SkillDefinitionSO[]>();

        public IReadOnlyList<Entry> Classes => classes;
        public IReadOnlyList<SkillDefinitionSO> SharedSkills => sharedSkills;

        public bool TryGet(WeaponClassId id, out Entry entry)
        {
            foreach (var candidate in classes)
            {
                if (candidate.id != id) continue;
                entry = candidate;
                return true;
            }
            entry = default;
            return false;
        }

        /// <summary>The faction's three classes, in role order (offense, defense, ranged).</summary>
        public void GetClasses(FactionId faction, List<Entry> results)
        {
            results.Clear();
            foreach (var role in new[] { WeaponClassRole.Offense, WeaponClassRole.Defense, WeaponClassRole.Ranged })
                if (TryGet(WeaponClassRules.ClassOf(faction, role), out var entry)) results.Add(entry);
        }

        /// <summary>
        /// Every skill a character of this faction can have: the shared skills, then its three class kits in class and
        /// slot order. The order is the same on every machine, which the skill system relies on. The array is cached:
        /// do not modify it.
        /// </summary>
        public SkillDefinitionSO[] SkillsFor(FactionId faction)
        {
            if (byFaction.TryGetValue(faction, out var cached)) return cached;
            var list = new List<SkillDefinitionSO>();
            foreach (var skill in sharedSkills) if (skill != null) list.Add(skill);
            foreach (var entry in classes)
            {
                if (entry.faction != faction || entry.skills == null) continue;
                foreach (var skill in entry.skills) if (skill != null) list.Add(skill);
            }
            var result = list.ToArray();
            byFaction[faction] = result;
            return result;
        }

        /// <summary>Editor builder entry point.</summary>
        public void SetContents(Entry[] classEntries, SkillDefinitionSO[] shared)
        {
            classes = classEntries ?? Array.Empty<Entry>();
            sharedSkills = shared ?? Array.Empty<SkillDefinitionSO>();
            byFaction.Clear();
        }

        private void OnEnable() => byFaction.Clear();
        private void OnValidate() => byFaction.Clear();
    }
}
