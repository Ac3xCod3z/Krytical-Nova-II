using System;
using System.Collections.Generic;
using KryticalNova.Economy;

namespace KryticalNova.Crafting
{
    /// <summary>
    /// The rules of crafting, free of networking so they can be tested: whether a recipe can be made so many times
    /// by this character, what that uses up, and how many times what the player has would stretch. The window and
    /// the server both ask here, so the window never offers a craft the server would refuse.
    /// </summary>
    public static class CraftRules
    {
        public const string ReasonNoRecipe = "The Fabricator does not know that recipe.";
        public const string ReasonNoCreds = "You don't have enough Creds.";
        public const string ReasonNoRoom = "Your Pack has no room for it.";

        /// <summary>
        /// Why a recipe cannot be made <paramref name="times"/> times (shown to the player), or null.
        /// <paramref name="have"/> counts an item across everything the player can pay from.
        /// </summary>
        public static string BlockedReason(in CraftRecipe recipe, int times, int level, long creds, Func<ushort, int> have,
            Func<ushort, string> nameOf = null)
        {
            if (!recipe.IsValid || times < 1) return ReasonNoRecipe;
            if (recipe.requiredLevel > 0 && level < recipe.requiredLevel) return $"Requires level {recipe.requiredLevel}.";
            for (int i = 0; i < recipe.ingredients.Length; i++)
            {
                CraftIngredient ingredient = recipe.ingredients[i];
                if (ingredient.itemId == 0 || ingredient.count <= 0) continue;
                long need = Needed(recipe, ingredient.itemId) * times;
                if (have == null || have(ingredient.itemId) < need)
                    return $"You need {need} {(nameOf != null ? nameOf(ingredient.itemId) : "of a material")}.";
            }
            if (!TryCreds(recipe, times, out long fee) || creds < fee) return ReasonNoCreds;
            return null;
        }

        /// <summary>How many of an item one craft uses up, the same item listed twice counted twice.</summary>
        public static long Needed(in CraftRecipe recipe, ushort itemId)
        {
            long need = 0;
            if (recipe.ingredients == null) return 0;
            foreach (CraftIngredient ingredient in recipe.ingredients)
                if (ingredient.itemId == itemId && ingredient.count > 0) need += ingredient.count;
            return need;
        }

        /// <summary>How many times the player's materials and Creds would make the recipe (the level aside).</summary>
        public static int MaxCrafts(in CraftRecipe recipe, long creds, Func<ushort, int> have, int most)
        {
            if (!recipe.IsValid || have == null) return 0;
            long crafts = Math.Max(0, most);
            foreach (CraftIngredient ingredient in recipe.ingredients)
            {
                if (ingredient.itemId == 0 || ingredient.count <= 0) continue;
                crafts = Math.Min(crafts, have(ingredient.itemId) / Needed(recipe, ingredient.itemId));
            }
            if (recipe.creds > 0) crafts = Math.Min(crafts, creds / recipe.creds);
            return (int)Math.Max(0, crafts);
        }

        /// <summary>What <paramref name="times"/> crafts use up, as costs the player's item sources can take.</summary>
        public static void Costs(in CraftRecipe recipe, int times, List<ItemCost> costs)
        {
            costs.Clear();
            if (recipe.ingredients == null) return;
            foreach (CraftIngredient ingredient in recipe.ingredients)
                if (ingredient.itemId != 0 && ingredient.count > 0)
                    costs.Add(new ItemCost(ingredient.itemId, ingredient.count * times));
        }

        /// <summary>The Creds for so many crafts. False when the sum would overflow.</summary>
        public static bool TryCreds(in CraftRecipe recipe, int times, out long fee)
        {
            fee = 0;
            if (recipe.creds <= 0 || times <= 0) return true;
            if (recipe.creds > long.MaxValue / times) return false;
            fee = recipe.creds * times;
            return true;
        }
    }
}
