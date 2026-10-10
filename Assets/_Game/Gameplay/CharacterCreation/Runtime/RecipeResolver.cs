using KryticalNova.Items;
using KryticalNova.Items.Visuals;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// Turns a <see cref="CharacterRecipe"/> into the part lists <see cref="CharacterAppearance"/> merges. The rules
    /// live here so every peer applies them the same way:
    ///  - a recipe only chooses the head. The body is the lineage's bare body; anything else is equipped gear.
    ///    A menu doll that knows no gear is resolved dressed: it stands in the basic outfit, the look of level-one gear.
    ///  - helmets and masks are never taken from a recipe (a skeleton's helm arrives with its armor piece).
    ///  - a feminine body gets its species' chest wrap (the appearance adds it only under a bare torso).
    /// </summary>
    public static class RecipeResolver
    {
        public static bool Resolve(in CharacterRecipe recipe, CharacterCreationDatabase database, ResolvedAppearance result,
            bool dressed = false)
        {
            result.Clear();
            if (database == null || database.Parts == null) return false;
            var lineage = database.GetLineage(recipe.lineage);
            if (lineage == null) return false;

            var catalog = database.Parts;
            for (int i = 0; i < recipe.PartCount; i++)
            {
                ushort id = recipe.parts[i];
                if (id == 0) continue;
                if (!catalog.TryGet(id, out var entry)) { result.missing++; continue; }
                if (!IsChoosable(entry.region)) continue;
                if (PartLibrary.TryLoad(catalog, id, out var part)) result.fixedParts.Add(part);
                else result.missing++;
            }

            foreach (ushort id in lineage.BodyParts(dressed))
            {
                if (PartLibrary.TryLoad(catalog, id, out var part)) result.defaultParts.Add(part);
                else result.missing++;
            }

            result.shape = recipe.ToShape();
            if (recipe.bodyType == BodyType.Feminine && lineage.wrapPartId != 0)
                PartLibrary.TryLoad(catalog, lineage.wrapPartId, out result.wrap);

            return result.fixedParts.Count + result.defaultParts.Count > 0;
        }

        /// <summary>
        /// A recipe may only name head parts. Regions owned by an equip slot (the body, attachments, helmets and
        /// masks) come from the bare body or from gear, and the wrap follows the body type.
        /// </summary>
        public static bool IsChoosable(BodyRegion region) =>
            region != BodyRegion.None && region != BodyRegion.Wrap && !EquipSlots.IsSlotRegion(region);
    }
}
