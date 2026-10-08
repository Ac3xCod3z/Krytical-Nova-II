using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>How an attempt at the forge ended.</summary>
    public enum GearWorkOutcome : byte
    {
        Success,
        /// <summary>It failed and the item paid for it (Temper lost, a Tuning stage lost) or nothing changed.</summary>
        Fail,
        /// <summary>It failed and a Failsafe took the damage.</summary>
        FailProtected,
        /// <summary>It failed and the item is gone.</summary>
        Broken,
    }

    /// <summary>The protection item a Tempering attempt is made with.</summary>
    public enum TemperProtection : byte { None, Failsafe, Advanced, Lucky }

    /// <summary>
    /// The rules of gear progression (Tempering, Fusion, Tuning, Reforge), free of networking and of chance: every
    /// roll is passed in, so the same inputs always give the same result and the rules can be tested. The server
    /// calls them with real rolls; the forge window calls them to show odds and reasons before anything is spent.
    /// Numbers come from <see cref="GearProgressionSettings"/>.
    /// </summary>
    public static class GearProgressionRules
    {
        public const string ReasonNotGear = "Only gear can be worked at the forge.";
        public const string ReasonNoSettings = "The forge is not set up.";

        // ---------------------------------------------------------------- shared

        /// <summary>Percent of success a character's Luck adds.</summary>
        public static float LuckBonus(float luck, float luckPerPercent, float cap) =>
            luckPerPercent <= 0f ? 0f : Mathf.Min(cap, Mathf.Max(0f, luck) / luckPerPercent);

        private static bool HasEffect(GearSlotEffect[] effects, EquipSlot slot)
        {
            if (effects == null) return false;
            foreach (var effect in effects) if (effect.slot == slot) return true;
            return false;
        }

        // ---------------------------------------------------------------- Tempering

        public static bool TryFindAlloy(TemperSettings settings, ushort itemId, out AlloyGrade alloy)
        {
            alloy = default;
            if (settings?.alloys == null || itemId == 0) return false;
            foreach (var candidate in settings.alloys)
            {
                if (candidate.itemId != itemId) continue;
                alloy = candidate;
                return true;
            }
            return false;
        }

        /// <summary>
        /// True once Temper can go past the soft cap. Until then Prime Alloy (the Alloy for that stretch) has no use.
        /// </summary>
        public static bool PrimeAlloyInUse(TemperSettings settings) => settings != null && settings.hardCap > settings.softCap;

        /// <summary>Which protection an item is, or None when it is not one.</summary>
        public static TemperProtection ProtectionOf(TemperSettings settings, ushort itemId)
        {
            if (settings == null || itemId == 0) return TemperProtection.None;
            if (itemId == settings.failsafeItemId) return TemperProtection.Failsafe;
            if (itemId == settings.advancedFailsafeItemId) return TemperProtection.Advanced;
            if (itemId == settings.luckyFailsafeItemId) return TemperProtection.Lucky;
            return TemperProtection.None;
        }

        /// <summary>Why this piece cannot be tempered with this Alloy and protection right now, or null.</summary>
        public static string TemperBlockedReason(GearProgressionSettings settings, ItemDefinition item, GearItemState state,
            AlloyGrade alloy, TemperProtection protection)
        {
            if (settings == null) return ReasonNoSettings;
            TemperSettings temper = settings.temper;
            if (item == null || !item.IsLadderGear || !HasEffect(temper.effects, item.equipSlot)) return "This cannot be tempered.";
            if (state.Temper >= temper.hardCap) return $"It is at {temper.hardCap}%: the highest Temper there is.";
            if (alloy.itemId == 0) return "Add an Alloy.";
            if (alloy.prime && !PrimeAlloyInUse(temper)) return "Prime Alloy has no use yet.";
            if (state.Temper >= temper.softCap && !alloy.prime) return $"From {temper.softCap}% only Prime Alloy works.";
            if (state.Temper < temper.softCap && alloy.prime) return $"Prime Alloy is for {temper.softCap}% and above.";
            if (state.Temper >= temper.advancedFailsafeFrom && protection != TemperProtection.Advanced)
                return $"From {temper.advancedFailsafeFrom}% an Advanced Failsafe is required.";
            return null;
        }

        /// <summary>Percent chance that an attempt succeeds.</summary>
        public static float TemperChance(TemperSettings settings, int current, int step, float luck, TemperProtection protection)
        {
            float chance = settings.baseChance - (current + step) + LuckBonus(luck, settings.luckPerPercent, settings.maxLuckBonus);
            if (protection == TemperProtection.Lucky) chance += settings.luckyFailsafeBonus;
            return Mathf.Clamp(chance, settings.minChance, settings.maxChance);
        }

        /// <summary>Percent chance that a failed attempt at this Temper % breaks the item.</summary>
        public static float BreakChance(TemperSettings settings, int current)
        {
            if (settings.breakBands == null) return 0f;
            foreach (var band in settings.breakBands)
            {
                if (current < band.from || current > band.to) continue;
                float t = band.to > band.from ? (current - band.from) / (float)(band.to - band.from) : 0f;
                return Mathf.Lerp(band.chanceFrom, band.chanceTo, t);
            }
            return 0f;
        }

        public static long TemperCost(GearProgressionSettings settings, ItemDefinition item, int current)
        {
            TemperSettings temper = settings.temper;
            double cost = item.itemLevel * (double)temper.credsPerItemLevel * settings.MultiplierOf(item.rarity);
            if (current >= temper.softCap) cost *= temper.softCapCostMultiplier;
            return (long)System.Math.Round(cost);
        }

        /// <summary>
        /// What one attempt does to the item. <paramref name="successRoll"/> and <paramref name="breakRoll"/> are
        /// 0-1. A Lucky Failsafe is spent on every attempt, an Advanced Failsafe on every fail, a plain Failsafe only
        /// when it stops a break.
        /// </summary>
        public static GearItemState ResolveTemper(TemperSettings settings, GearItemState state, AlloyGrade alloy, float chance,
            TemperProtection protection, float successRoll, float breakRoll, out GearWorkOutcome outcome, out bool protectionSpent)
        {
            protectionSpent = protection == TemperProtection.Lucky;
            if (successRoll * 100f < chance)
            {
                outcome = GearWorkOutcome.Success;
                // Ordinary Alloy tops out at the soft cap; Prime Alloy carries on to the hard cap.
                int cap = alloy.prime ? settings.hardCap : Mathf.Min(settings.hardCap, Mathf.Max(settings.softCap, state.Temper));
                return state.WithTemper(Mathf.Min(cap, state.Temper + alloy.step));
            }
            if (protection == TemperProtection.Advanced)
            {
                outcome = GearWorkOutcome.FailProtected;
                protectionSpent = true;
                return state;
            }

            GearItemState lowered = state.WithTemper(Mathf.Max(0, state.Temper - settings.failLoss));
            bool breaks = breakRoll * 100f < BreakChance(settings, state.Temper);
            if (!breaks)
            {
                outcome = GearWorkOutcome.Fail;
                return lowered;
            }
            if (protection == TemperProtection.Failsafe || protection == TemperProtection.Lucky)
            {
                outcome = GearWorkOutcome.FailProtected;
                protectionSpent = true;
                return lowered;
            }
            outcome = GearWorkOutcome.Broken;
            return state;
        }

        /// <summary>The glow band a Temper % falls in, or false below the first band.</summary>
        public static bool TryGetGlow(TemperSettings settings, int temper, out TemperGlowBand band)
        {
            band = default;
            bool found = false;
            if (settings?.glowBands == null || temper <= 0) return false;
            foreach (var candidate in settings.glowBands)
            {
                if (temper < candidate.from || (found && candidate.from < band.from)) continue;
                band = candidate;
                found = true;
            }
            return found;
        }

        public static TemperGlowLook GlowLookOf(TemperSettings settings, ItemRarity rarity)
        {
            int index = (int)rarity;
            if (settings?.glowLooks == null || settings.glowLooks.Length == 0) return new TemperGlowLook { intensity = 1f, width = 0.06f };
            return settings.glowLooks[Mathf.Clamp(index, 0, settings.glowLooks.Length - 1)];
        }

        // ---------------------------------------------------------------- Fusion

        /// <summary>
        /// The item level the material of a Fusion must have. A Rare takes a Rare of its own level. An Epic takes the
        /// closest Rare tier at or below its own level, read from the stat table's bands (0 when there is none).
        /// </summary>
        public static int FusionMaterialLevel(GearStatTable table, ItemDefinition target)
        {
            if (target == null) return 0;
            if (target.rarity != ItemRarity.Epic) return target.itemLevel;
            int best = 0;
            if (table?.bands == null) return 0;
            foreach (var band in table.bands)
            {
                if (band.rarity != ItemRarity.Rare || band.levels == null) continue;
                foreach (byte level in band.levels)
                    if (level <= target.itemLevel && level > best) best = level;
            }
            return best;
        }

        /// <summary>Why this target cannot be fused with this material, or null. A null material asks for one.</summary>
        public static string FusionBlockedReason(GearProgressionSettings settings, ItemDefinition target, GearItemState targetState,
            ItemDefinition material, GearItemState materialState)
        {
            if (settings == null) return ReasonNoSettings;
            FusionSettings fusion = settings.fusion;
            if (target == null || !target.IsLadderGear || !HasEffect(fusion.effects, target.equipSlot)) return "This cannot be fused.";
            if (target.rarity != ItemRarity.Rare && target.rarity != ItemRarity.Epic) return "Only Rare and Epic gear can be fused.";
            if (targetState.Fusion >= fusion.maxStage) return $"It is at FS{fusion.maxStage}: the last stage.";
            int needed = target.rarity == ItemRarity.Epic ? fusion.minTemperEpic : fusion.minTemperRare;
            if (targetState.Temper < needed) return $"Temper it to {needed}% or more first.";

            int level = FusionMaterialLevel(settings.statTable, target);
            if (material == null) return $"Add an untouched Rare of the same kind, item level {level}.";
            if (!material.IsLadderGear || material.rarity != ItemRarity.Rare) return "The material must be a Rare.";
            if (material.equipSlot != target.equipSlot) return "The material must be the same kind of piece.";
            if (target.equipSlot == EquipSlot.Weapon && material.weaponClass != target.weaponClass)
                return "The material must be a weapon of the same class.";
            if (material.itemLevel != level) return $"The material must be item level {level}.";
            if (!materialState.IsClean) return "The material must be untouched: no Temper, Fusion or Tuning.";
            return null;
        }

        /// <summary>Percent chance of reaching <paramref name="nextStage"/>.</summary>
        public static float FusionChance(FusionSettings settings, int nextStage, float luck)
        {
            if (nextStage <= settings.certainStages) return 100f;
            float chance = 100f - (nextStage - settings.certainStages) * settings.chanceLossPerStage
                           + LuckBonus(luck, settings.luckPerPercent, settings.maxLuckBonus);
            return Mathf.Clamp(chance, settings.minChance, 100f);
        }

        public static long FusionCost(FusionSettings settings, int nextStage) =>
            settings.firstStageCreds + settings.credsPerStage * Mathf.Max(0, nextStage - 1);

        /// <summary>The target after a successful Fusion: one stage up, and the Temper the fusing costs taken off.</summary>
        public static GearItemState ResolveFusion(FusionSettings settings, GearItemState target) =>
            target.WithFusion(target.Fusion + 1).WithTemper(Mathf.Max(0, target.Temper - settings.temperCostOnSuccess));

        // ---------------------------------------------------------------- Tuning

        public static bool TryGetBracket(TuningSettings settings, int nextStage, out TuningBracket bracket)
        {
            bracket = default;
            if (settings?.brackets == null) return false;
            foreach (var candidate in settings.brackets)
            {
                if (nextStage < candidate.fromStage || nextStage > candidate.toStage) continue;
                bracket = candidate;
                return true;
            }
            return false;
        }

        /// <summary>Why this piece cannot be tuned one stage further, or null.</summary>
        public static string TuneBlockedReason(GearProgressionSettings settings, ItemDefinition item, GearItemState state)
        {
            if (settings == null) return ReasonNoSettings;
            TuningSettings tuning = settings.tuning;
            if (item == null || !item.IsLadderGear || !HasEffect(tuning.effects, item.equipSlot)) return "This cannot be tuned.";
            if (item.rarity != ItemRarity.Rare && item.rarity != ItemRarity.Epic) return "Only Rare and Epic gear can be tuned.";
            if (state.Tune >= tuning.maxStage) return $"It is at Tune {tuning.maxStage}: the last stage.";
            if (!TryGetBracket(tuning, state.Tune + 1, out _)) return "This stage cannot be tuned yet.";
            return null;
        }

        /// <summary>Percent chance of reaching <paramref name="nextStage"/>.</summary>
        public static float TuneChance(TuningSettings settings, int nextStage)
        {
            if (nextStage <= settings.certainStages) return 100f;
            return Mathf.Clamp(100f - (nextStage - settings.certainStages) * settings.chanceLossPerStage, settings.minChance, 100f);
        }

        /// <summary>A stage up on a success. A fail leaves the early stages alone and costs the later ones a stage.</summary>
        public static GearItemState ResolveTune(TuningSettings settings, GearItemState state, bool success)
        {
            if (success) return state.WithTune(state.Tune + 1);
            return state.Tune >= settings.failDropsFromStage ? state.WithTune(state.Tune - 1) : state;
        }

        // ---------------------------------------------------------------- Reforge

        private static bool IsCoreSlot(EquipSlot slot) =>
            slot == EquipSlot.Weapon || slot == EquipSlot.Armor || slot == EquipSlot.Gloves || slot == EquipSlot.Boots
            || slot == EquipSlot.Ring || slot == EquipSlot.Amulet;

        /// <summary>Why this piece cannot be reforged, or null.</summary>
        public static string ReforgeBlockedReason(GearProgressionSettings settings, ItemDefinition item, GearItemState state)
        {
            if (settings == null) return ReasonNoSettings;
            if (item == null || !item.IsLadderGear || !IsCoreSlot(item.equipSlot)) return "This cannot be reforged.";
            if (item.rarity != ItemRarity.Rare) return "Only Rare gear can be reforged.";
            if (!state.IsClean) return "Only an untouched piece can be reforged: no Temper, Fusion or Tuning.";
            return null;
        }

        public static long ReforgeCost(ReforgeSettings settings, int itemLevel)
        {
            double tens = Mathf.Max(0, itemLevel - settings.firstLevel) / 10.0;
            return settings.baseCreds + (long)System.Math.Round(settings.credsPerTenLevels * tens);
        }

        public static float ReforgeChance(ReforgeSettings settings, float luck) =>
            Mathf.Clamp(settings.chance + LuckBonus(luck, settings.luckPerPercent, settings.maxLuckBonus), 0f, 100f);

        /// <summary>
        /// What a Rare may turn into: every other Rare of the same item level this character can wear, of any of the
        /// six core kinds.
        /// </summary>
        public static void CollectReforgeResults(GearDictionary dictionary, ItemDefinition item, FactionId faction, BodyFit body, List<ushort> into)
        {
            into.Clear();
            if (dictionary == null || item == null) return;
            foreach (var entry in dictionary.Entries)
            {
                if (entry.itemId == item.itemId || entry.rarity != ItemRarity.Rare || entry.itemLevel != item.itemLevel) continue;
                if (!IsCoreSlot(entry.slot)) continue;
                if (faction != FactionId.None && !FactionRules.Includes(entry.factions, faction)) continue;
                if (body != BodyFit.None && (entry.bodies & body) == 0) continue;
                into.Add(entry.itemId);
            }
        }

        // ---------------------------------------------------------------- what the state is worth

        /// <summary>
        /// Appends the stat modifiers an item's Temper, Fusion and Tuning give, tagged with <paramref name="source"/>.
        /// </summary>
        public static void AppendEffects(GearProgressionSettings settings, in ItemStack stack, ItemDefinition item, object source,
            List<StatModifier> results)
        {
            if (settings == null || item == null || !item.IsLadderGear) return;
            GearItemState state = GearItemState.Of(stack);
            if (state.IsClean) return;
            float rarity = settings.MultiplierOf(item.rarity);
            Append(settings.temper.effects, item, state.Temper, rarity, source, results);
            Append(settings.fusion.effects, item, state.Fusion, rarity, source, results);
            Append(settings.tuning.effects, item, state.Tune, rarity, source, results);
            if (state.Tune >= settings.tuning.maxStage) Append(settings.tuning.finalStageBonus, item, 1, rarity, source, results);
        }

        private static void Append(GearSlotEffect[] effects, ItemDefinition item, int steps, float rarity, object source, List<StatModifier> results)
        {
            if (effects == null || steps <= 0) return;
            foreach (var effect in effects)
            {
                if (effect.slot != item.equipSlot) continue;
                switch (effect.kind)
                {
                    case GearEffectKind.PercentOfItemStat:
                        float baked = GearStatResolver.Total(item, effect.stat);
                        if (baked > 0f) results.Add(new StatModifier(effect.stat, ModifierType.Flat, baked * effect.value * steps / 100f, source));
                        break;
                    case GearEffectKind.Flat:
                        results.Add(new StatModifier(effect.stat, ModifierType.Flat, effect.value * steps, source));
                        break;
                    case GearEffectKind.FlatTimesRarity:
                        results.Add(new StatModifier(effect.stat, ModifierType.Flat, effect.value * rarity * steps, source));
                        break;
                    case GearEffectKind.PercentOfWearer:
                        results.Add(new StatModifier(effect.stat, ModifierType.PercentAdd, effect.value * steps, source));
                        break;
                }
            }
        }
    }
}
