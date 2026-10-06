using System.Collections.Generic;
using KryticalNova.Progression;

namespace KryticalNova.Items
{
    /// <summary>
    /// Turns one equipped item into the stat modifiers it grants: the item's baked stats, plus what its own Temper,
    /// Fusion and Tuning add (read from the <see cref="ItemStack"/>; Cores will join them here). Pure, so it runs
    /// the same on the server, the owner and every observer.
    /// </summary>
    public static class GearStatResolver
    {
        /// <summary>Appends the modifiers of <paramref name="definition"/> to <paramref name="results"/>, tagged with <paramref name="source"/>.</summary>
        /// <param name="progression">The gear progression numbers; null gives the baked stats alone.</param>
        public static void Resolve(in ItemStack stack, ItemDefinition definition, object source, List<StatModifier> results,
            GearProgressionSettings progression = null)
        {
            if (definition == null || stack.IsEmpty) return;
            GearProgressionRules.AppendEffects(progression, stack, definition, source, results);
            Append(definition.baseStats, source, results);
            Append(definition.secondaryStats, source, results);
            if (definition.statModifiers != null)
                foreach (var m in definition.statModifiers)
                    results.Add(new StatModifier(m.stat, m.modType, m.value, source));
        }

        /// <summary>The total an item gives to one stat (flat modifiers only): what a tooltip compares.</summary>
        public static float Total(ItemDefinition definition, StatType stat)
        {
            if (definition == null) return 0f;
            return Sum(definition.baseStats, stat) + Sum(definition.secondaryStats, stat) + Sum(definition.statModifiers, stat);
        }

        /// <summary>
        /// The same total for one item as it is now, its Temper, Fusion and Tuning counted in (flat modifiers only).
        /// </summary>
        public static float Total(in ItemStack stack, ItemDefinition definition, StatType stat, GearProgressionSettings progression)
        {
            float total = Total(definition, stat);
            if (progression == null || definition == null) return total;
            Scratch.Clear();
            GearProgressionRules.AppendEffects(progression, stack, definition, null, Scratch);
            foreach (var modifier in Scratch)
                if (modifier.Stat == stat && modifier.ModType == ModifierType.Flat) total += modifier.Value;
            Scratch.Clear();
            return total;
        }

        // Main thread only, like everything that reads items.
        private static readonly List<StatModifier> Scratch = new List<StatModifier>(8);

        private static void Append(ItemStatModifier[] stats, object source, List<StatModifier> results)
        {
            if (stats == null) return;
            foreach (var m in stats) results.Add(new StatModifier(m.stat, m.modType, m.value, source));
        }

        private static float Sum(IReadOnlyList<ItemStatModifier> stats, StatType stat)
        {
            float total = 0f;
            if (stats == null) return 0f;
            for (int i = 0; i < stats.Count; i++)
                if (stats[i].stat == stat && stats[i].modType == ModifierType.Flat) total += stats[i].value;
            return total;
        }
    }
}
