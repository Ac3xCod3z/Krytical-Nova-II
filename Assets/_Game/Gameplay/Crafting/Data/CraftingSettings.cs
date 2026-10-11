using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Crafting
{
    /// <summary>The ids of the items crafting is about. Items 9300 to 9499 of the utility table; never renumber.</summary>
    public static class CraftItemIds
    {
        // What the appearance services use up (EconomySettings names them too).
        public const ushort ChromaCell = 9300;
        public const ushort RestylePass = 9301;
        public const ushort TrainingPass = 9302;

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
