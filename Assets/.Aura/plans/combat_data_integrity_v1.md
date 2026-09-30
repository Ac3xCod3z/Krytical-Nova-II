# Combat Data Integrity & Server-Authoritative Strikes

## Objective
Give Max HP/Chi a single owner, stop Luck from feeding the combat pipeline while keeping it as an enhancement-only stat, and make the server validate melee combo order and cadence instead of trusting a client-supplied `comboStep`.

## Changes
- 'Assets/Scripts/Progression/Data/CombatStatTuningData.cs' - add `healthPerLevel` / `chiPerLevel` so level scaling is tunable
- 'Assets/Scripts/Progression/Data/CharacterResourceData.cs' - remove `baseHealth`, `healthPerVitality`, `baseChi`, `chiPerSpirit`
- 'Assets/Scripts/Progression/CharacterCombatStats.cs' - read HP/Chi level scaling from tuning data
- 'Assets/Scripts/Progression/PlayerResources.cs' - delete the `resourceData` formula fallback branch
- 'Assets/Scripts/Progression/CombatCalculator.cs' - remove the Luck term from the crit roll
- 'Assets/Scripts/Combat/PlayerMeleeCombat.cs' - server-side combo-sequence and cadence validation
- 'Assets/GameData/Progression/CombatTuning/CombatStatTuning.asset' - set the two new level fields
- 'Assets/GameData/Progression/CharacterData/CharacterResources.asset' - re-serialized after field removal

## Relevant Assets and Quirks
- **Correction to my earlier report:** `CharacterResources.asset` stores `healthPerVitality: 20` and `chiPerSpirit: 20`, which already agree with `CharacterCombatStats`. There is no value conflict. The defects are *duplicated ownership* plus the misleading C# defaults `DefaultHealthPerVitality = 10f` / `DefaultChiPerSpirit = 5f`, which contradict the asset and only apply when the asset is missing.
- `CharacterCombatStats.GetBaseStatValue` hardcodes `(level - 1) * 15f` HP and `(level - 1) * 10f` Chi. Neither number exists in any asset, so both are un-tunable today.
- `CharacterResourceData` must stay referenced: `PlayerResources.EnsureInitialized` logs an error and disables the component when it is null. Only its fields shrink.
- `StatType.Luck` keeps its 50 baseline in `CharacterCombatStats`, and `SkillDefinitionSO.GetStatModifiers` keeps emitting the Garuda's Prayer `PercentMult` Luck modifier. Only combat consumption is removed.
- `CombatCalculator.CalculateAttackInternal` is the deterministic entry point with injected dice rolls - its signature must stay compatible for `PointAndClickPlayer` and `ProgressionHUD.TestAttackDummy`.
- `PlayerMeleeCombat` already holds `combo1Clip/2/3` and `strike1Speed/2/3`, so per-step minimum intervals derive from `clip.length / speed` with no new data.
- Removing serialized fields from a ScriptableObject preserves the remaining same-named fields.

## Steps
- [ ] 1. Add `healthPerLevel` (default 15) and `chiPerLevel` (default 10) to `CombatStatTuningData`, with `OnValidate` clamping matching the existing style [depends: none]
- [ ] 2. Remove `baseHealth`, `healthPerVitality`, `baseChi`, `chiPerSpirit` and their constants/validation from `CharacterResourceData` [depends: none]
- [ ] 3. Replace the hardcoded `15f` / `10f` per-level constants in `CharacterCombatStats.GetBaseStatValue` with the two new tuning fields, keeping 15/10 as null-safe fallbacks [depends: 1]
- [ ] 4. In `PlayerResources.RecalculateMaximums`, delete the `resourceData.baseHealth ... chiPerSpirit` branch and make a missing `CharacterCombatStats` an explicit `Debug.LogError` [depends: 2]
- [ ] 5. In `CombatCalculator.CalculateAttackInternal`, reduce the crit roll input to `attacker.GetStatValue(StatType.CriticalRate)` alone, with a comment that Luck is reserved for enhancement and loot systems [depends: none]
- [ ] 6. In `PlayerMeleeCombat`, add server-only `expectedComboStep` / `lastAcceptedStrikeTime`; in `ExecuteAuthoritativeStrike` reject, via an `OnCombatMessage` line, any strike whose `comboStep` is not the expected next step or whose elapsed time is under `clip.length / speed * 0.7`; advance state only on accept [depends: none]
- [ ] 7. Set `healthPerLevel: 15` and `chiPerLevel: 10` on `CombatStatTuning.asset`, then confirm `CharacterResources.asset` still loads with four fields gone [depends: 1, 2]

## Verify
- Play Mode, HUD "Grant test XP": Max HP and Max Chi must rise by exactly +15 / +10 per level, identical to pre-change behaviour.
- Toggle "Test: Equip Gear (+10 All Stats)": Max HP must rise by 200 (10 VIT x 20), proving Vitality still owns the formula.
- Cast `Skill_GarudasPrayer`: the `combat-stat-crit` readout must not move. Cast Spiritual Rage: crit must still read 15% -> 45%.
- Strike the Combat Dummy: the 3-hit combo loops and damage lands normally.
- Host/client pair: the server must log a rejection when the same `comboStep` is sent twice in a row, and reject a cadence-violating replay.
- Console shows zero errors/warnings after the `CharacterResourceData` field removal.
