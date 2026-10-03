# Twelve Sky 2 — Core Systems Implementation Plan & Progress Tracker

> **Scope:** Character Stats Progression (Module 01) & Skills / Combat Architecture (Module 02).  
> **Approach:** Incremental, phased implementation. Each phase builds upon the existing player controller, resources, navigation, hotbar, and UI Toolkit HUD without regressions.

---

## Overall Roadmap & Phase Status

| Phase | Description | Status |
| :--- | :--- | :--- |
| **Phase 1** | **Character Stats & Derived Combat Stats Architecture** | **Completed** |
| **Phase 2** | **Combat Calculator & Damage Pipeline** | **Completed** |
| **Phase 3** | **Tier 1 Skills: Self-Buffs, Shields, Chi Recovery & Master Buffs** | **Completed** |
| **Phase 4** | **Targeting, Support Skills & Formations (Tier 2)** | Pending |
| **Phase 5** | **Combat Skills, Weapon Lines & Crowd Control (Tier 3)** | Pending |

---

## Phase 1: Character Stats & Derived Combat Stats Architecture

### Goals:
Establish the dual-layer stat architecture described in Module 01: Primary Attributes (STR, VIT, AGI, CHI) dynamically producing Derived Combat Stats (AP, DP, Hit Rate, Evasion, Max HP, Max Chi, Critical Rate, Critical Damage, Stun Resist, Move Speed %) with flat and percentage modifier support (`StatModifier`). Connect this to the Character Sheet UI in `ProgressionHUD`.

### Detailed Checklist:
- [x] **1.1 Core Stat Enums & Modifiers (`StatType.cs` / `StatModifier.cs`)**
  - Define `StatType`: Primary Attributes (`Strength`, `Vitality`, `Agility`, `Spirit`), Derived Combat Stats (`AttackPower`, `DefensePower`, `HitRate`, `EvasionRate`, `MaxHP`, `MaxChi`), and Secondary Stats (`CriticalRate`, `CriticalDamagePercent`, `ElementalAttack`, `ElementalDefense`, `AcupressureStunResistance`, `Luck`, `MoveSpeedPercent`).
  - Implement `StatModifier` supporting `Flat`, `PercentAdd` (additive %), and `PercentMult` (multiplicative %) with source tracking (`Source`).
- [x] **1.2 Character Stat Configuration (`CharacterClassData` & `CombatStatTuningData` ScriptableObjects)**
  - Faction / base class configuration asset (`GuanyinBladeClass.asset`) providing base attributes (10 STR, 10 VIT, 10 AGI, 10 CHI), base HP (100), base Chi (50), base weapon AP (15), base weapon DP (5).
  - Dedicated designer tuning asset (`CombatStatTuning.asset`) providing Inspector control over all scaling formulas, ratios, base critical rate (15%), base move speed (100%), and hit/evade clamps.
  - Stat definitions (`StatDefinitionData` ScriptableObjects in `Assets/_Game/Gameplay/Progression/Data/Assets/StatDefinitions/`) storing designer-friendly names, abbreviations, min/max limits, percentages, and descriptions.
  - Update `CharacterResourceData.asset` formulas: 1 Vitality = 20 HP, 1 Spirit = 20 Chi.
- [x] **1.3 Runtime Character Stats Component (`CharacterCombatStats.cs`)**
  - Attach to Player; maintain allocated stats and active `StatModifier` collection.
  - Implement dynamic formula calculations matching Module 01:
    - $\text{Attack Power (AP)} = \text{STR} + \text{Spirit} + \text{Weapon Base AP}$
    - $\text{Defense Power (DP)} = \text{AGI} + \text{Armor DP}$
    - $\text{Hit Rate} = \text{STR} \times 2 + \text{Item Hit Rate}$
    - $\text{Evasion Rate} = (\text{AGI} \times 2) + (\text{VIT} \times 1) + \text{Item Dodge}$
    - $\text{Max HP} = (\text{VIT} \times 20) + \text{Base HP} + \text{Gear HP}$
    - $\text{Max Chi} = (\text{Spirit} \times 20) + \text{Base Chi} + \text{Gear Chi}$
    - $\text{Critical Rate} = 15\% \text{ baseline} + \text{Modifiers}$
    - $\text{Move Speed \%} = 100\% \text{ baseline} + \text{Modifiers}$
    - $\text{Stun Resistance} = \text{Modifiers}$
  - Fire `OnStatsRecalculated` event on allocation or modifier change.
  - Connect with `PlayerCharacterProgression` and `PlayerResources` so point allocation immediately updates derived stats and health/chi pools.
  - **Core Attributes Primary & Secondary Sub-Stat Scaling Architecture:**
    - Derived sub-stats scale dynamically from **Total Effective Attributes** ($\text{Allocated Base} + \text{Gear/Buff Modifiers}$):
      | Attribute | Primary Stat Scaling | Secondary Stat Scaling |
      | :--- | :--- | :--- |
      | **Strength (STR)** | **+1 Attack Power (AP)** | **+2 Attack Rate (Hit Rate)** |
      | **Vitality (VIT)** | **+20 Health Points (HP)** | **+1 Evade (Dodge)** |
      | **Agility (AGI/DEX)** | **+2 Evade (Dodge)** | **+1 Defense (DP)** |
      | **Chi (Spirit/INT)** | **+20 Chi (Mana)** | **+1 Attack Power (AP)** |
    - Modifiers placed directly on primary attributes (`StatType.Strength`, `Vitality`, `Agility`, `Spirit`) via gear or enchantments propagate and scale both their Primary and Secondary derived sub-stats automatically.
- [x] **1.4 Character Sheet UI Updates (`ProgressionHUD.uxml`, `.uss`, `.cs`)**
  - Expand the Character Window UI to cleanly display Derived Combat Stats:
    - Attack Power (AP)
    - Defense Power (DP)
    - Hit Rate
    - Evasion Rate
    - Critical Rate %
    - Stun Resistance %
    - Move Speed %
  - Display attribute values with active gear bonuses: e.g. `10 (+10)`.
  - Comprehensive tooltips on stat allocation buttons detailing exact Primary & Secondary gains per point for +1, +5, and +10 steps.
  - Playtest Tool added: "Test: Equip Gear (+10 All Stats)" toggle to immediately verify gear-driven attribute scaling and baseline return.
  - Keep existing +1/+5/+10 step buttons and press-and-hold allocation working smoothly.
  - Test & verify in Play Mode.

---

## Phase 2: Combat Calculator & Damage Pipeline

### Goals:
Implement the deterministic combat resolution engine from Module 01 & 02 that calculates hit/evasion checks, physical damage mitigation, critical strike multipliers, elemental damage bypass, and master defensive procs.

### Detailed Checklist:
- [x] **2.1 Combat Calculation Engine (`CombatCalculator.cs`)**
  - Calculate hit chance: $\text{Clamp}\left(\frac{\text{Hit}}{\text{Hit} + \text{Evade}} \times 100\%, 10\%, 95\%\right)$ using `CombatStatTuningData` clamps.
  - Handle evasion / miss outcome (`IsEvaded = true`, 0 damage).
  - Physical damage formula: $\max(1, \text{Raw AP} \times \text{SkillMultiplier} - \text{Target DP})$.
  - Critical strike roll: $1.5 \times (1 + \text{CritDamageBonus}\%)$ based on attacker critical rate and tuning multiplier.
  - Elemental damage: $\max(0, \text{EleAttack} - \text{EleDefense})$ (bypasses physical DP).
  - Reactive proc hooks: Heavenly Repulsion (100% reflect back to attacker) & Heavenly Destruction (shield shatter proc).
- [x] **2.2 Targetable Entity / Combat Dummy (`IDamageable`, `CombatEntity.cs`)**
  - Interface `IDamageable` and component `CombatEntity` for all entities (Player, NPCs, Monsters, Dummies) that receive damage.
  - Configured via ScriptableObjects (`DummyTargetClass.asset`, `CombatStatTuning.asset`).
  - Interactive Combat Dummy placed in `SampleScene.unity` next to player at `(442, 1, 305.6)`. Left-clicking the dummy initiates a direct combat strike with directional facing.
  - Added playtest HUD buttons ("Test: Strike Combat Dummy" and "Test: Reset Dummy Health").
- [x] **2.3 Floating Combat Feedback / World Text (`CombatFeedbackManager.cs`)**
  - Visual damage feedback projected from world coordinates into UI Toolkit (`.floating-combat-text`, `.crit`, `.evade`, `.reflected`).
  - Animated rising and fading float text with spread jitter.

---

## Phase 3: Tier 1 Skills: Self-Buffs, Shields, Chi Recovery & Master Buffs

### Goals:
Build out the Tier 1 skill architecture from Module 02. Implement self-buffs that spend Chi, apply timed `StatModifier` instances, manage absorption shields, channeled Chi recovery, and execute reactive master procs. Note: Weapon stance buffs (Lightning Flash, Divine Protection, Soaring Phoenix) are deferred to Phase 5.

### Detailed Checklist:
- [x] **3.1 Unified Skill Definition Architecture (`SkillDefinitionSO.cs`)**
  - ScriptableObject schema supporting cast time, Chi cost, duration, short cooldowns, target types (`Self`, `SingleTarget`, `AreaOfEffect`, `Party`), execution types (`Instant`, `Channeled`, `Passive`), and effect payloads.
  - Compatible with hotbar slotting and shortcut keys (1–10).
- [x] **3.2 Active Buff & Status Controller (`CharacterBuffManager.cs`)**
  - Tracks active buffs, durations, tick timers, and shield capacities.
  - Automatically cleans up expired modifiers and invokes recalculations.
  - UI HUD status bar showing active buff badges and remaining durations.
- [x] **3.3 General Utility Self-Buffs & Channeled Chi Recovery**
  - **Spiritual Rage:** Instant self-buff, increases Critical Rate (+30% Critical Rate for 180s, 1s cooldown, Self).
  - **Heavenly Serenity:** Channeled HP and Chi regen over 20s, scaling with rank (replaces passive chi recovery; interrupts on taking damage or moving, 1s cooldown, Self).
  - Remove passive Chi recovery from `PlayerResources` / `CharacterResourceData` to align with Twelve Sky 2 meditation model.
- [x] **3.4 Absorption Shield Logic & Master Rare Drop Skills**
  - All skills configured with Self target type and short cooldowns (1–2s):
    - **Heavenly Shield:** Absorbs incoming damage up to 150% Max HP for 60s.
    - **Heavenly Blow:** +75% Critical / Deadly Blow chance for 180s.
    - **Garuda's Prayer:** +100% Luck for 240s.
    - **Heavenly Repulsion (HR):** 10% chance on incoming hit to take 0 damage and reflect 100% damage back.
    - **Heavenly Destruction (HD):** 10% chance per hit to instantly shatter enemy's Heavenly Shield and 50% chance to dispel stance buffs.
    - **Reverse Flow (RF):** +20% Stun (Acupressure) resistance for 180s.
  - Master skills are designated as Rare Drop Book skills (`requiresSkillBook = true`); displayed in the Skills UI with clear lock note ("Requires Rare Skill Book Drop") until acquired.
- [x] **3.5 UI Integration in Skills Window & Hotbar**
  - Skills window sections for Movement, General Utility (trainer skills with step allocation +1, +5, +10), and Rare Master Skills (with drop book lock indicators).
  - Ability to drag learned buffs to hotbar slots and activate via keys 1–10.
  - Playtest tools button to toggle unlock for drop book skills during testing.

### Phase 3 Deliverables & Implementation Summary:
- **Created ScriptableObject Schema:** `Assets/_Game/Gameplay/Progression/Data/SkillDefinitionSO.cs`
  - Encapsulates skill identity, category (`GeneralUtility`, `RareMasterSkill`, `WeaponStance`, etc.), targeting (`Self`, `SingleTarget`, `AreaOfEffect`, `Party`), execution type (`Instant`, `Channeled`, `Passive`), Chi costs, cooldowns, duration, channeled tick formulas, absorption shield multipliers, reflection procs, and offensive shield shatter/dispel procs.
- **Created Runtime Systems:**
  - `CharacterBuffManager.cs`: Controls active buff lifetimes, absorption shields, channeled meditation ticks, reflection defense, and shield shatter/dispel hooks.
  - `PlayerCombatSkills.cs`: Tracks learned skill ranks, skill book unlock states, cooldown clocks, and validation/casting pipelines.
- **Updated Combat & Player Controllers:**
  - `PlayerSkillHotbar.cs`: Unified 10-slot hotbar supporting both `MovementSkillId` and `SkillDefinitionSO` with slot cooldown badges and Chi check highlights.
  - `PointAndClickPlayer.cs`: Integrated with `CharacterBuffManager` to proc offensive shield shatter / stance dispel on attacks and interrupt channeled skills upon movement.
  - `CombatEntity.cs`: Integrated with `CharacterBuffManager` so incoming damage is absorbed by active absorption shields before depleting health pools, and checks for defensive reflection.
  - `PlayerResources.cs` & `CharacterResources.asset`: Passive Chi regeneration disabled (`0 Chi/s`) to reflect the Twelve Sky 2 meditation model.
- **Asset Directory Structure (`Assets/_Game/Gameplay/Progression/Data/Assets/`):**
  - `Skills/SelfBuffSkills/`: The 8 Phase 3 self-buff and master skills.
  - `Skills/MovementSkills/`: `Jump.asset`, `Run.asset`, `Movement.asset`, `JumpMotion.asset`.
  - `CharacterClasses/`: `GuanyinBladeClass.asset`, `DummyTargetClass.asset`.
  - `CharacterData/`: `CharacterStats.asset`, `CharacterProgression.asset`, `CharacterResources.asset`.
  - `CombatTuning/`: `CombatStatTuning.asset`.
  - `StatDefinitions/`: 13 designer stat definition assets.
  - `Materials/`: `DummyMaterial.mat`.
- **Created 8 Phase 3 Skill Assets (`Assets/_Game/Gameplay/Progression/Data/Assets/Skills/SelfBuffSkills/`):**
  1. `Skill_SpiritualRage.asset`: +30% Critical Rate for 180s (15 Chi, 1.0s CD, Trainer Skill, Ranks 1-20).
  2. `Skill_HeavenlySerenity.asset`: Channeled HP + Chi regen (1% → 5% Max HP/s and 2% → 10% Max Chi/s for 20s, 0 Chi, 1.0s CD, Trainer Skill, Ranks 1-20).
  3. `Skill_HeavenlyShield.asset` (HS): Level 60 Drop Item. Absorbs incoming physical damage up to 111% - 150% Max HP for 60s (765 - 1010 Chi, 2.0s CD, Ranks 1-30, 1-30 skill points). Elemental damage bypasses the shield directly to health!
  4. `Skill_HeavenlyBlow.asset` (HB): Level 60 Drop Item. Increases chance of deadly blow by 12% - 75% multiplicatively (765 - 1010 Chi, 64s - 180s duration, 1.0s CD, Ranks 1-30, 1-30 skill points).
  5. `Skill_GarudasPrayer.asset` (GP): Level 70 Drop Item. Multiplies luck by +13% - +100% (891 - 1136 Chi, 66s - 240s duration, 1.0s CD, Ranks 1-30, 1-30 skill points).
  6. `Skill_HeavenlyRepulsion.asset` (HR): Level 80 Craftable / Drop Item. 1% - 10% chance on incoming hit to take 0 physical damage and reflect 100% raw physical damage back to attacker (1471 - 1871 Chi, 61s - 180s duration, 1.0s CD, Ranks 1-100, 50 skill points to learn, 100 points to max). Reflected damage bypasses target's Heavenly Shield. Requires Archives: *Fire Dragon Archive, Munro's Finale, Torn Spirit Finale, Sun Archive*.
  7. `Skill_HeavenlyDestruction.asset` (HD): Level 80 Craftable / Drop Item. 1% - 20% chance per strike to shatter enemy's Heavenly Shield and 50% chance to dispel enemy buffs (1417 - 1871 Chi, 61s - 180s duration, 1.0s CD, Ranks 1-100, 50 skill points to learn, 100 points to max). Requires Archives: *Bloody Sky Archive, Demon Gate Archive, Marble Archive, Demon God's Archive*.
  8. `Skill_ReverseFlow.asset` (RF): Level 80 Craftable / Drop Item. +1% - +20% Stun (Acupressure) resistance (1417 - 1871 Chi, 61s - 180s duration, 1.0s CD, Ranks 1-100, 50 skill points to learn, 100 points to max). Stacks additively on top of passive Acupressure resistance. Requires Archives: *Snow Legend, Northern Wind Legend, Gods Legend, Moon Wolf's Legend*.
- **Combat Resolution Rules Verified:**
  - Elemental damage bypasses Heavenly Shield and damages health directly.
  - Heavenly Repulsion reflected physical damage bypasses the attacker's Heavenly Shield directly into health.
  - Unabsorbed damage reaching health immediately interrupts channeled skills (Heavenly Serenity).
- **UI Toolkit Expansion (`ProgressionHUD.uxml`, `ProgressionHUD.uss`, `ProgressionHUD.cs`):**
  - Grouped sections for Movement Skills, Tier 1 Utility Buffs (Trainer), and Tier 1 Master Skills (Rare Drop Books & Craftable Archives).
  - Displays dynamic rank scaling (Chi cost, duration, and effect values for current rank and next rank).
  - Displays Level requirements (Level 60, 70, 80) and required Crafting Archives.
  - Floating Active Buffs HUD bar above the hotbar showing remaining durations and shield HP.
  - Development tools toggle button to unlock/lock rare drop book skills during testing.
- **Verification Completed:**
  - Automated test passed: Spiritual Rage applies flat +30% crit rate (15% -> 45%).
  - Automated test passed: Heavenly Serenity channels Chi restoration.
  - Automated test passed: Heavenly Shield absorbs up to 150% Max HP (tested 50 dmg absorbed from 450 shield HP to 400 shield HP).
  - Automated test passed: Drop book lock gates verified.
  - Automated test passed: Hotbar drag & assignment verified.
  - Zero compiler errors, zero warnings.

---

## Phase 4: Targeting, Support Skills & Formations (Tier 2)

### Goals:
Enable ally/party targeting, targeted buffs, healing, Chi restoration, stun cleansing, and party leader formation auras from Module 02.

### Detailed Checklist:
- [ ] **4.1 Entity Targeting System (Left-click target selection, Target Frame UI)**
- [ ] **4.2 Direct Support Skills**
  - **Thunderous Roar:** +100% Damage for 30s.
  - **Azure Wall:** +10% Defense & Elemental Resistance for 180s.
  - **Raising the Dead:** Single-target ally HP restoration.
  - **Spirit's Return:** Single-target ally Chi restoration.
  - **Finger of Darkness:** Passive +40% Stun resistance + active 80% chance to cleanse Stun.
- [ ] **4.3 Party Formation Auras**
  - **Oval Formation:** +40% Dodge Chance & +40% Move Speed within 20m.
  - **5-Star Formation:** +40% Hit Chance within 20m.
  - **Tiger Army Formation:** 200% HP Shield to party members within 20m for 120s.

---

## Phase 5: Combat Skills, Weapon Lines & Crowd Control (Tier 3)

### Goals:
Implement offensive attack execution, animations, range/hitbox checks, crowd control states, weapon stance buffs, and weapon progression skill lines.

### Detailed Checklist:
- [ ] **5.1 Weapon Stance Buffs (Deferred from Phase 3)**
  - **Lightning Flash** (Guanyin Blade): +20% Physical Damage for 180s.
  - **Divine Protection** (Guanyin Sword): +50% Physical Defense for 180s.
  - **Soaring Phoenix** (Guanyin Marble): +20% Dodge / +20% Move Speed for 180s.
- [ ] **5.2 Crowd Control State Machine (`Stunned`, `Immobilized`)**
  - Stunned state preventing movement and skill activation with Acupressure resistance mitigation.
  - **Finger of Light:** 15m range, minor damage, 50% stun chance (10s duration).
- [ ] **5.3 Weapon Skill Progression Engine**
  - Single-hit, Multi-hit, and AoE skill execution lines.
  - Skill level progression scaling damage multiplier, lowering Chi cost or cooldown.
- [ ] **5.4 Consumable Skill Book / Archive Drops**
  - Learning higher tier skills (Level 60, Level 80 War God, 9th/10th skills) via item unlocks.
