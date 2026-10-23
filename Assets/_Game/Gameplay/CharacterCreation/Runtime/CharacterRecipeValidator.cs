using System.Collections.Generic;
using KryticalNova.Items;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// Server-side check of a recipe a client submitted: the lineage must belong to the claimed faction and every
    /// part must be one that lineage and body type are allowed to wear. Pure, so it is unit-tested.
    /// </summary>
    public static class CharacterRecipeValidator
    {
        public const string ReasonNoDatabase = "Character data is not loaded.";
        public const string ReasonUnknownLineage = "Unknown lineage.";
        public const string ReasonWrongFaction = "That lineage does not belong to this faction.";
        public const string ReasonTooManyParts = "Too many parts.";
        public const string ReasonUnknownPart = "Unknown part.";
        public const string ReasonForbiddenRegion = "Only head parts can be chosen; the body comes from gear.";
        public const string ReasonPartNotAllowed = "A part is not available to this lineage.";
        public const string ReasonDuplicateRegion = "Two parts for the same body region.";
        public const string ReasonMissingRegion = "A required body part is missing.";
        public const string ReasonUnknownPalette = "Unknown colour palette.";
        public const string ReasonBadSkinTone = "Unknown skin tone.";
        public const string ReasonBadColor = "A colour is not available to this lineage.";
        public const string ReasonBadBodySlider = "A body slider is out of range.";

        public static bool Validate(in CharacterRecipe recipe, CharacterCreationDatabase database, out string reason)
        {
            reason = null;
            if (database == null || database.Parts == null) { reason = ReasonNoDatabase; return false; }
            var lineage = database.GetLineage(recipe.lineage);
            if (lineage == null) { reason = ReasonUnknownLineage; return false; }
            if (lineage.faction != recipe.faction) { reason = ReasonWrongFaction; return false; }
            // None is a character saved before classes existed; it plays as its faction's offense class.
            if (recipe.weaponClass != Progression.WeaponClassId.None && !WeaponClassRules.BelongsTo(recipe.weaponClass, recipe.faction))
            {
                reason = WeaponClassRules.ReasonWrongDiscipline;
                return false;
            }
            if (recipe.PartCount > CharacterRecipe.MaxParts) { reason = ReasonTooManyParts; return false; }
            if (recipe.bust > 100 || recipe.butt > 100 || recipe.defined > 100 || recipe.height > 100)
            {
                reason = ReasonBadBodySlider;
                return false;
            }

            var setup = lineage.SetupFor(recipe.bodyType);
            var seen = new HashSet<BodyRegion>();
            for (int i = 0; i < recipe.PartCount; i++)
            {
                ushort id = recipe.parts[i];
                if (id == 0) continue;
                if (!database.Parts.TryGet(id, out var entry)) { reason = ReasonUnknownPart; return false; }
                if (!RecipeResolver.IsChoosable(entry.region)) { reason = ReasonForbiddenRegion; return false; }
                if (!setup.Allows(entry.region, id)) { reason = ReasonPartNotAllowed; return false; }
                if (!seen.Add(entry.region)) { reason = ReasonDuplicateRegion; return false; }
            }
            foreach (var options in setup.regions)
                if (!options.optional && !seen.Contains(options.region)) { reason = ReasonMissingRegion; return false; }

            if (recipe.palette != 0 && (database.Palettes == null || !database.Palettes.TryGet(recipe.palette, out _)))
            {
                reason = ReasonUnknownPalette;
                return false;
            }
            if (recipe.skinTone > setup.skinTones.Count) { reason = ReasonBadSkinTone; return false; }
            if (recipe.ColorCount > CharacterRecipe.MaxColors) { reason = ReasonBadColor; return false; }
            for (int i = 0; i < recipe.ColorCount; i++)
                if (!setup.OffersChannel(recipe.colors[i].channel)) { reason = ReasonBadColor; return false; }
            return true;
        }
    }
}
