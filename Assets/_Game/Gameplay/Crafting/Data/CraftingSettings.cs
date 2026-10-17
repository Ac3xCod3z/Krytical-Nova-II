using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Crafting
{
    /// <summary>The ids of the items crafting is about. Items 9300 to 9499 and 9510 to 9527 of the utility table; never renumber.</summary>
    public static class CraftItemIds
    {
        // What the appearance services use up (EconomySettings names them too).
        public const ushort ChromaCell = 9300;
        public const ushort RestylePass = 9301;
        public const ushort TrainingPass = 9302;
        /// <summary>What dyeing one weapon uses up.</summary>
        public const ushort PrismCell = 9303;

        // The four colours a Chroma Cell is made of: Hostile drops, and the Outfitter's shelf, one a day.
        public const ushort CyanCell = 9310;
        public const ushort MagentaCell = 9311;
        public const ushort YellowCell = 9312;
        public const ushort BlackCell = 9313;

        // Dossiers: each teaches one skill, and is made of the four Pages that follow it.
        public const ushort BacklashDossier = 9400;
        public const ushort IronNerveDossier = 9410;
        public const ushort ShatterpointDossier = 9420;
        public const int PagesPerDossier = 4;

        // Zenith Dossiers: one for each weapon class's Zenith technique (the level-80 leap; Twelve Sky 2: War God).
        // They have no Pages: each is dropped whole, in the Prime grounds only.
        public const ushort ZenithMonoblade = 9430;
        public const ushort ZenithGlaive = 9431;
        public const ushort ZenithCarbine = 9432;
        public const ushort ZenithHarvester = 9433;
        public const ushort ZenithServoClaws = 9434;
        public const ushort ZenithArcCaster = 9435;
        public const ushort ZenithLongaxe = 9436;
        public const ushort ZenithClaymore = 9437;
        public const ushort ZenithWarbow = 9438;

        // Skill Chips (Twelve Sky 2: skill books). Four a weapon class, in the order of the classes: the level 60
        // single hit, the level 60 multi hit, the level 80 single hit (slot 6), the level 80 multi hit (slot 7).
        public const ushort SkillChipFirst = 9440;
        public const int SkillChipsPerClass = 4;
        // The three skills every class shares that open with a chip.
        public const ushort AegisChip = 9480;
        public const ushort KillerInstinctChip = 9481;
        public const ushort HotStreakChip = 9482;
        // The Skill Chips of the two-hit attacks (16 Oct 2026): two a weapon class, in the order of the classes, the
        // level 60 one then the level 80 one. A range of their own, so the chips above keep their ids.
        public const ushort TwoHitChipFirst = 9510;
        public const int TwoHitChipsPerClass = 2;

        /// <summary>The chip of a class skill, or 0 for a slot that has none.</summary>
        public static ushort SkillChip(KryticalNova.Progression.WeaponClassId weaponClass, KryticalNova.Progression.ClassSkillSlot slot)
        {
            int index = System.Array.IndexOf(KryticalNova.Progression.WeaponClasses.All, weaponClass);
            if (index < 0) return 0;
            switch (slot)
            {
                case KryticalNova.Progression.ClassSkillSlot.Lv60Single: return (ushort)(SkillChipFirst + index * SkillChipsPerClass);
                case KryticalNova.Progression.ClassSkillSlot.Lv60Multi: return (ushort)(SkillChipFirst + index * SkillChipsPerClass + 1);
                case KryticalNova.Progression.ClassSkillSlot.S6: return (ushort)(SkillChipFirst + index * SkillChipsPerClass + 2);
                case KryticalNova.Progression.ClassSkillSlot.S7: return (ushort)(SkillChipFirst + index * SkillChipsPerClass + 3);
                case KryticalNova.Progression.ClassSkillSlot.Lv60TwoHit: return (ushort)(TwoHitChipFirst + index * TwoHitChipsPerClass);
                case KryticalNova.Progression.ClassSkillSlot.Lv80TwoHit: return (ushort)(TwoHitChipFirst + index * TwoHitChipsPerClass + 1);
                default: return 0;
            }
        }

        /// <summary>True for a slot whose chip is of level 80 and drops only in the Prime grounds.</summary>
        public static bool IsPrimeChipSlot(KryticalNova.Progression.ClassSkillSlot slot) =>
            slot == KryticalNova.Progression.ClassSkillSlot.S6 || slot == KryticalNova.Progression.ClassSkillSlot.S7
            || slot == KryticalNova.Progression.ClassSkillSlot.Lv80TwoHit;

        /// <summary>The slots of a class that open with a Skill Chip.</summary>
        public static readonly KryticalNova.Progression.ClassSkillSlot[] SkillChipSlots =
        {
            KryticalNova.Progression.ClassSkillSlot.Lv60Single, KryticalNova.Progression.ClassSkillSlot.Lv60Multi,
            KryticalNova.Progression.ClassSkillSlot.S6, KryticalNova.Progression.ClassSkillSlot.S7,
            KryticalNova.Progression.ClassSkillSlot.Lv60TwoHit, KryticalNova.Progression.ClassSkillSlot.Lv80TwoHit,
        };

        /// <summary>Every Skill Chip: the class chips in class order, then the three shared ones.</summary>
        public static System.Collections.Generic.IEnumerable<ushort> SkillChips()
        {
            foreach (KryticalNova.Progression.WeaponClassId weaponClass in KryticalNova.Progression.WeaponClasses.All)
                foreach (KryticalNova.Progression.ClassSkillSlot slot in SkillChipSlots)
                    yield return SkillChip(weaponClass, slot);
            yield return AegisChip;
            yield return KillerInstinctChip;
            yield return HotStreakChip;
        }

        /// <summary>Every Zenith Dossier, in the order of the weapon classes: three a faction (Hegemony, Null, Covenant).</summary>
        public static readonly ushort[] ZenithDossiers =
        {
            ZenithMonoblade, ZenithGlaive, ZenithCarbine, ZenithHarvester, ZenithServoClaws, ZenithArcCaster, ZenithLongaxe, ZenithClaymore, ZenithWarbow,
        };

        /// <summary>The id of a Dossier's Page, 1 to <see cref="PagesPerDossier"/>.</summary>
        public static ushort PageOf(ushort dossier, int page) => (ushort)(dossier + page);
    }

    /// <summary>One thing a recipe uses up.</summary>
    [Serializable]
    public struct CraftIngredient
    {
        public ushort itemId;
        [Min(1)] public int count;

        public CraftIngredient(ushort itemId, int count)
        {
            this.itemId = itemId;
            this.count = count;
        }
    }

    /// <summary>One thing the Fabricator can make: what it uses up, what comes out, and who may ask for it.</summary>
    [Serializable]
    public struct CraftRecipe
    {
        [Tooltip("Stable ID sent over the network. Never reuse or renumber; 0 means none.")]
        public ushort id;
        [Tooltip("The heading the recipe is listed under in the craft window.")]
        public string group;
        public ushort resultItemId;
        [Min(1)] public int resultCount;
        public CraftIngredient[] ingredients;
        [Tooltip("Creds the Fabricator takes for one craft. 0 = the materials are the whole cost.")]
        [Min(0)] public long creds;
        [Tooltip("The level a character needs to ask for it. 0 = any.")]
        [Min(0)] public int requiredLevel;

        public bool IsValid => id != 0 && resultItemId != 0 && ingredients != null && ingredients.Length > 0;
    }

    /// <summary>
    /// Every recipe the Fabricator knows. Hand-editable: the builder (Krytical Nova ▸ Items ▸ 11. Build Crafting
    /// Recipes) only adds the recipes this asset lacks, so a tuned cost or level survives a re-run. Recipe IDs are
    /// append-only.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Crafting/Crafting Settings")]
    public sealed class CraftingSettings : ScriptableObject
    {
        public List<CraftRecipe> recipes = new List<CraftRecipe>();
        [Tooltip("The most of one recipe a single request may make.")]
        [Min(1)] public int maxPerRequest = 99;

        public bool TryGet(ushort id, out CraftRecipe recipe)
        {
            if (id != 0)
                foreach (CraftRecipe candidate in recipes)
                {
                    if (candidate.id != id) continue;
                    recipe = candidate;
                    return recipe.IsValid;
                }
            recipe = default;
            return false;
        }
    }
}
