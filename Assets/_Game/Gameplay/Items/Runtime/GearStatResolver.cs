using System.Collections.Generic;
using KryticalNova.Progression;

namespace KryticalNova.Items
{
    /// <summary>
    /// Turns one equipped item into the stat modifiers it grants. Today that is the item's baked stats; Tempering,
    /// Fusion, Tuning and Cores will add to the result here, from the <see cref="ItemStack"/>, without any caller
    /// changing. Pure, so it runs the same on the server, the owner and every observer.
    /// </summary>
    public static class GearStatResolver
    {
        /// <summary>Appends the modifiers of <paramref name="definition"/> to <paramref name="results"/>, tagged with <paramref name="source"/>.</summary>
        public static void Resolve(in ItemStack stack, ItemDefinition definition, object source, List<StatModifier> results)
        {
            if (definition == null || stack.IsEmpty) return;
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
