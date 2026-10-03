# Twelve Sky 2 — Module 02: Skills & Combat Architecture

## 1. Core Philosophy & System Overview

The skill system in Twelve Sky 2 decouples character identity from static classes. Combat roles (Offensive, Defensive, Ranged) are determined dynamically by a player's equipped weapon type and allocated Stat Points (Strength, Agility, Vitality, Spirit).

```
                        ┌──────────────────────────────────┐
                        │        CHARACTER CLASS           │
                        └────────────────┬─────────────────┘
                                         │
                 ┌───────────────────────┼───────────────────────┐
                 ▼                       ▼                       ▼
       ┌───────────────────┐   ┌───────────────────┐   ┌───────────────────┐
       │ OFFENSIVE ROLE    │   │  DEFENSIVE ROLE   │   │    RANGED ROLE    │
       │ High Burst/Damage │   │ High Shield/Tank  │   │ High Dodge/Kite   │
       └─────────┬─────────┘   └─────────┬─────────┘   └─────────┬─────────┘
                 │                       │                       │
       ┌─────────┴─────────┐   ┌─────────┴─────────┐   ┌─────────┴─────────┐
       │ Faction Weapons:  │   │ Faction Weapons:  │   │ Faction Weapons:  │
       │ • Guanyin: Blade  │   │ • Guanyin: Sword  │   │ • Guanyin: Marble │
       │ • Fujin: Katana   │   │ • Fujin: D. Blade │   │ • Fujin: Lute     │
       │ • Jinong: Spear   │   │ • Jinong: L.Blade │   │ • Jinong: Sceptre │
       └───────────────────┘   └───────────────────┘   └───────────────────┘
```

---

## 2. Tiered Skill Architecture & Implementation Guide

The skill system is organized into three distinct tiers based on mechanical execution complexity.

### Tier 1: Self Buffs & Stances
Implement these first. They follow a simple pattern: deduct Chi, apply a temporary stat modifier to the `PlayerState` / `CombatStats` component, and trigger a local particle/visual effect.

#### 1.1 General Utility Self Buffs
* **Spiritual Rage (Level 1 Trainer Skill):**
  * **Type:** Self Buff (Instant Cast, 0s Cooldown).
  * **Effect:** Increases Critical Success Rate and enables critical damage multipliers.
* **Heavenly Serenity (Level 1 Trainer Skill):**
  * **Type:** Channeled Self Buff / Regen.
  * **Effect:** Restores HP and Chi (MP) every second for 20 seconds. Per second: 1% → 5% of Max HP and 2% → 10% of Max Chi
    from rank 1 to rank 20. Costs no Chi. Interrupts on receiving damage or moving.

#### 1.2 Faction Weapon Stance Buffs (Level 1 Trainer Skills)
* **Lightning Flash (Guanyin Blade / Offensive):** +20% Physical Damage (180s duration).
* **Divine Protection (Guanyin Sword / Defensive):** +50% Physical Defense (180s duration).
* **Soaring Phoenix (Guanyin Marble / Ranged):** +20% Dodge / +20% Move Speed (180s duration).

#### 1.3 Rare Drop & Master Skill Self Buffs
* **Heavenly Shield (Rare Drop):** Absorbs incoming damage up to 150% Max HP for 60s.
* **Heavenly Blow (Rare Drop):** +75% Critical / Deadly Blow Chance for 180s.
* **Garuda's Prayer / Song (Rare Drop):** +100% Drop Luck / Luck Stat Increase for 240s.
* **Heavenly Repulsion (HR) (Craftable Archive OR Ultra-Rare Drop):**
  * **Type:** Self Buff with Defensive Reactive Proc (180s duration).
  * **Mechanic:** 10% chance on incoming physical hit to take 0 damage and reflect 100% raw physical damage back to attacker (bypasses target's Heavenly Shield).
* **Heavenly Destruction (HD) (Craftable Archive OR Ultra-Rare Drop):**
  * **Type:** Self Buff with Offensive Proc (180s duration).
  * **Mechanic:** 10% chance per strike to instantly shatter target's Heavenly Shield and 50% chance to strip stance buffs.
* **Reverse Flow (RF) (Craftable Archive OR Ultra-Rare Drop):**
  * **Type:** Self Buff (180s duration).
  * **Mechanic:** +20% Stun (Acupressure) Resistance. Stacks additively with passive *Finger of Darkness*.

---

### Tier 2: Team-Wide Buffs, Cleanse, & Formations
Implement these second. They require targeted entity selection, group networking state, or continuous aura triggers centered on the Party Leader.

#### 2.1 Direct Support Skills
* **Thunderous Roar:** Targetable/Self Buff giving +100% Damage (30s duration).
* **Azure Wall:** Targetable/Self Buff giving +10% Defense & Elemental Resistance (180s duration).
* **Raising the Dead:** Single-target ally HP restoration.
* **Spirit's Return:** Single-target ally Chi (MP) restoration.
* **Finger of Darkness (Active Cleanse & Passive):**
  * **Passive:** +40% Stun Resistance.
  * **Active:** 80% chance to remove Stun status from targeted ally.

#### 2.2 Party Leader Formations (Auras & Zones)
* **5-Star Formation:** Party aura granting +40% Hit Chance within 20m.
* **Oval Formation:** Party aura granting +40% Dodge Chance & +40% Move Speed within 20m.
* **Tiger Army Formation:** Instant party buff applying 200% HP Shield to party members within 20m for 120s.
* **Slaughter / Slicing Wind Formations:** Area-of-Effect persistent hazard zone dealing damage ticks to enemies within 15m.
* **Stunning Blow Formation:** Area control zone with a 90% chance to stun enemies within 12m.

---

### Tier 3: Offensive Combat & Crowd Control Skills
Implement these last. They require hit detection, line-of-sight checks, animation timing, damage equations, and state machine transitions (e.g., Applying Stun state).

#### 3.1 Crowd Control
* **Finger of Light:** Single-target active skill. Deals minor damage with a 50% chance to apply Stun State to enemy for 10s (resisted by target's Stun Resistance from *Finger of Darkness* + *Reverse Flow*).

#### 3.2 Weapon Skill Progression Tiers
Every faction (Guanyin/Dragon, Fujin/Snake, Jinong/Tiger) follows an identical tiering architecture for each weapon line:

| Tier Level | Acquisition Source | Skill Type / Purpose |
| :--- | :--- | :--- |
| **Level 1** | Faction Skill Trainer | Starter Single/Multi/AoE & Stance Buff |
| **Level 30** | Faction Skill Trainer | Tier 2 Single/Multi |
| **Level 60** | Monster Drop Item | Tier 3 Single/Multi |
| **Level 80** | Monster Drop Item | War God (WG) Primary Farming AoE |
| **9th Skill** | High-Level World Drop | Endgame PvP Single-Hit Burst |
| **10th Skill** | High-Level World Drop | Endgame PvP Multi-Hit / Combo |

##### Structural Skill Map (Example: Guanyin Faction)
* **Offensive Weapon Line (Blade):**
  * *1st Buff:* Lightning Flash (Lvl 1) — +20% Damage (180s).
  * *Single-Hit Line:* Haeseo Stroke (Lvl 1) $\rightarrow$ Devil's Strike (Lvl 30) $\rightarrow$ Sunray's Stroke (Lvl 60 Drop).
  * *Multi-Hit Line:* Gentleman's Pen (Lvl 1) $\rightarrow$ Flash of Rage (Lvl 30) $\rightarrow$ Phantom Thunder (Lvl 60 Drop).
  * *AoE Line:* Fury Strike (Lvl 1) $\rightarrow$ Spirit of Pegasus (Lvl 80 War God Drop).
* **Defensive Weapon Line (Sword):**
  * *1st Buff:* Divine Protection (Lvl 1) — +50% Defense (180s).
  * *Single-Hit Line:* Crane Atop Pine (Lvl 1) $\rightarrow$ Whirlwind (Lvl 30) $\rightarrow$ Fire Blast Sword (Lvl 60 Drop).
  * *Multi-Hit Line:* Autumn Blizzard (Lvl 1) $\rightarrow$ Dragon Blade (Lvl 30) $\rightarrow$ Supernova (Lvl 60 Drop).
  * *AoE Line:* Solar Flare (Lvl 1) $\rightarrow$ Fiery Dragon (Lvl 80 War God Drop).
* **Ranged Weapon Line (Marble):**
  * *1st Buff:* Soaring Phoenix (Lvl 1) — +20% Dodge / +20% Speed (180s).
  * *Single-Hit Line:* Tiger's Roar (Lvl 1) $\rightarrow$ Dragon Typhoon (Lvl 30) $\rightarrow$ Dragon Thunder (Lvl 60 Drop).
  * *Multi-Hit Line:* Dragon's Roar (Lvl 1) $\rightarrow$ Four Gods' Blows (Lvl 30) $\rightarrow$ Shadowmere's Step (Lvl 60 Drop).
  * *AoE Line:* Hyeonmu's Fist (Lvl 1) $\rightarrow$ Thunderous Dragon (Lvl 80 War God Drop).

---

## 3. Master-Tier Craftable Skill Archives (Endgame PvP Trifecta)

Requires learning cost of 50 SP and a total of 100 SP to max out. Crafted by combining 4 specific world drop archives (or acquired via ultra-rare drops). Duration is 180 seconds when fully maxed out.

| Master Skill | Acronym | Required Crafting Archives | Operational Mechanic / Max Level Effect | Strategic Counter / Purpose |
| :--- | :--- | :--- | :--- | :--- |
| **Heavenly Repulsion** | **HR** | • Fire Dragon Finale<br>• Munro's Finale<br>• Torn Spirit Finale<br>• Sun Finale | 10% chance to reflect 100% physical damage back to attacker. Takes 0 physical damage on proc. Bypasses opponent's Heavenly Shield. | Defensive Counter-suit against raw physical burst damage dealers. |
| **Heavenly Destruction** | **HD** | • Bloody Sky Archive<br>• Demon Gate Archive<br>• Marble Archive<br>• Demon God's Archive | 10% chance per hit to instantly shatter/destroy target's Heavenly Shield. 50% chance to dispel standard weapon buffs. | Ultimate Shield-Breaker to counter tanks & defensive setups. |
| **Reverse Flow** | **RF** | • Snow Legend<br>• Northern Wind Legend<br>• Gods Legend<br>• Moon Wolf's Legend | +20% Acupressure (Stun) Resistance. Stacks additively with passive Finger of Darkness (+40%). | Stun Immunity build layer to prevent PvP lockouts. |

---

## 4. Technical Mechanics & Systems Implementation

### 4.1 Combat Resources
* **Health Points (HP):** Scaled via Vitality stat ($1\text{ Vit} = 20\text{ HP}$).
* **Chi / Spirit (MP):** Scaled via Spirit stat ($1\text{ Spirit} = 20\text{ Chi}$). Consumed by all skills and buffs.
* **Skill Points (SP):** Earned upon level-up. Applied directly into individual skills to raise damage multipliers, lower cooldowns, or increase buff percentages.

### 4.2 Master Buff Execution Mechanics

$$\text{Physical Hit} \longrightarrow \begin{cases}  \text{If HR Procs (10\%):} & \text{Target Damage = 0}, \quad \text{Attacker takes 100\% Raw Damage (Bypasses Shield)} \\ \text{If HD Procs (10\%):} & \text{Target Heavenly Shield = Destroyed}, \quad \text{Dispel Weapon Buffs (50\%)} \\ \text{Standard Hit:} & \text{Apply standard damage formula} \end{cases}$$

$$\text{Total Stun Resistance} = \text{Finger of Darkness Passive (40\%)} + \text{Reverse Flow Buff (20\%)} + \text{Gear Stun Resistance}$$

---

## 5. Combat Loops

### 5.1 PvE Loop (Farming / Grinding)
1. **Prep:** Cast Weapon Self-Buff (Damage/Def) + Thunderous Roar + Garuda's Prayer.
2. **Pulling:** Pull 5–10 mobs using Ranged / Single-hit skills.
3. **Execution:** Rotate War God (Lvl 80) AoE skill to clear groups efficiently.
4. **Sustain:** Activate Heavenly Serenity during downtime to restore Chi pools.

### 5.2 PvP Loop (Holy Stone Battles / Faction War)
1. **Prep:** Activate Heavenly Shield + Heavenly Blow + Master Buffs (HR, HD, RF) + Formations (Oval or Tiger Army).
2. **Engagement:** Initiate engagement with Spiritual Rage for critical rate boost, followed by tactical positioning.
3. **Control & Shield Break:**
   * Execute Finger of Light (50% Stun baseline — defended against by enemy Reverse Flow).
   * Engage with multi-hits to attempt Heavenly Destruction (HD) proc to shatter the enemy's Heavenly Shield.
4. **Burst:** Chain 9th (Single-Hit) & 10th (Multi-Hit) High-Tier Skills inside the opening window.
5. **Counterplay & Defense:** Rely on Heavenly Repulsion (HR) (10% chance to return 100% physical damage) and Reverse Flow (RF) to prevent lock-down loops during enemy counter-attacks.

---

## 6. Development Checklist & Implementation Tracker

- [ ] **Tier 1 (Self Buffs):**
  - [ ] Base `SelfBuff` scriptable action logic.
  - [ ] Resource System: HP and Chi / Spirit consumption & regeneration loop.
  - [ ] Spiritual Rage: Instant self-buff, critical chance boost calculation.
  - [ ] Heavenly Serenity: Over-time Chi restoration buff tick (cancel on hit).
  - [ ] Stance Buffs: Lightning Flash (+20% Dmg), Divine Protection (+50% Def), Soaring Phoenix (+20% Dodge/Spd).
  - [ ] Shield Buffer Logic (Heavenly Shield: 150% Max HP capacity).
  - [ ] HR Reflect Trigger Handler: 10% proc, 0 damage taken, 100% reflection, bypass shield.
  - [ ] HD Shield Shatter Handler: 10% proc on hit, shatter shield, 50% dispel stance buffs.
  - [ ] RF Stun Resistance Handler: +20% additive stun resist calculation.
- [ ] **Tier 2 (Team & Formations):**
  - [ ] Targeted Ally Cast Logic (Thunderous Roar, Azure Wall, Raising the Dead, Spirit's Return).
  - [ ] Finger of Darkness: Passive stun resist (+40%) + active ally stun cleanse (80% chance).
  - [ ] Party Aura Trigger System (5-Star, Oval, Tiger Army, Hazard Zones, Stunning Blow Zone).
- [ ] **Tier 3 (Combat Skills):**
  - [ ] Finger of Light: Single-target 50% stun chance applied to enemy unit.
  - [ ] Weapon Skill Progression Engine (Single-hit, Multi-hit, AoE hitboxes and animations).
  - [ ] Item Drop Skill Unlocks: Unlocking Lvl 60, Lvl 80 (War God AoE), 9th, and 10th skills via consumable item books.

---

## 7. Unity ScriptableObject Serialized Database Schemas

The following serialized JSON database represents the Unity ScriptableObject definitions for the skill system:

```json
{
  "$schema": "Unity_ScriptableObject_SkillDatabase_Schema",
  "description": "Serialized database representation for Twelve Sky 2 skills, organized as Unity ScriptableObjects.",
  "skills": [
    {
      "so_name": "SO_Skill_SpiritualRage",
      "skill_id": "utility_spiritual_rage",
      "displayName": "Spiritual Rage",
      "tierCategory": "Tier1_SelfBuff",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 1,
      "acquisitionType": "TrainerNPC",
      "spCostToLearn": 1,
      "maxSkillPoints": 20,
      "resourceCost": {
        "chiCost": 20,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 0.0,
      "durationSeconds": 180.0,
      "targetType": "Self",
      "effectData": {
        "buffType": "CritRateIncrease",
        "critRateBonusPercent": 30.0,
        "enablesCritMultiplier": true
      }
    },
    {
      "so_name": "SO_Skill_HeavenlySerenity",
      "skill_id": "utility_heavenly_serenity",
      "displayName": "Heavenly Serenity",
      "tierCategory": "Tier1_SelfBuff",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 1,
      "acquisitionType": "TrainerNPC",
      "spCostToLearn": 1,
      "maxSkillPoints": 20,
      "resourceCost": {
        "chiCost": 0,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 0.0,
      "durationSeconds": 20.0,
      "targetType": "Self",
      "effectData": {
        "buffType": "ChanneledChiRegen",
        "chiRegenPerTickPercent": 5.0,
        "tickIntervalSeconds": 2.0,
        "breakOnDamageTaken": true
      }
    },
    {
      "so_name": "SO_Skill_LightningFlash",
      "skill_id": "stance_guanyin_blade_lightning_flash",
      "displayName": "Lightning Flash",
      "tierCategory": "Tier1_SelfBuff",
      "faction": "Guanyin",
      "weaponRequired": "Blade",
      "minLevel": 1,
      "acquisitionType": "TrainerNPC",
      "spCostToLearn": 1,
      "maxSkillPoints": 20,
      "resourceCost": {
        "chiCost": 60,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 5.0,
      "durationSeconds": 180.0,
      "targetType": "Self",
      "effectData": {
        "buffType": "WeaponStanceDamage",
        "damageBonusPercent": 20.0
      }
    },
    {
      "so_name": "SO_Skill_HeavenlyShield",
      "skill_id": "heavenly_shield",
      "displayName": "Heavenly Shield",
      "tierCategory": "Tier1_SelfBuff",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 1,
      "acquisitionType": "ItemDrop",
      "spCostToLearn": 20,
      "maxSkillPoints": 50,
      "resourceCost": {
        "chiCost": 200,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 120.0,
      "durationSeconds": 60.0,
      "targetType": "Self",
      "effectData": {
        "buffType": "AbsorptionShield",
        "shieldCapacityMaxHpMultiplier": 1.5
      }
    },
    {
      "so_name": "SO_Skill_HeavenlyBlow",
      "skill_id": "heavenly_blow",
      "displayName": "Heavenly Blow",
      "tierCategory": "Tier1_SelfBuff",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 1,
      "acquisitionType": "ItemDrop",
      "spCostToLearn": 20,
      "maxSkillPoints": 50,
      "resourceCost": {
        "chiCost": 180,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 60.0,
      "durationSeconds": 180.0,
      "targetType": "Self",
      "effectData": {
        "buffType": "StatBoost",
        "criticalChanceBonusPercent": 75.0
      }
    },
    {
      "so_name": "SO_Skill_GarudasPrayer",
      "skill_id": "garudas_prayer",
      "displayName": "Garuda's Prayer",
      "tierCategory": "Tier1_SelfBuff",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 1,
      "acquisitionType": "ItemDrop",
      "spCostToLearn": 20,
      "maxSkillPoints": 50,
      "resourceCost": {
        "chiCost": 150,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 300.0,
      "durationSeconds": 240.0,
      "targetType": "Self",
      "effectData": {
        "buffType": "LuckBoost",
        "dropRateLuckBonusPercent": 100.0
      }
    },
    {
      "so_name": "SO_Skill_Master_HR",
      "skill_id": "master_heavenly_repulsion",
      "displayName": "Heavenly Repulsion",
      "acronym": "HR",
      "tierCategory": "Tier1_SelfBuff",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 1,
      "acquisitionType": "CraftableOrUltraRareDrop",
      "craftingRecipeArchives": [
        "archive_fire_dragon_finale",
        "archive_munros_finale",
        "archive_torn_spirit_finale",
        "archive_sun_finale"
      ],
      "spCostToLearn": 50,
      "maxSkillPoints": 100,
      "resourceCost": {
        "chiCost": 300,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 240.0,
      "durationSeconds": 180.0,
      "targetType": "Self",
      "effectData": {
        "buffType": "DefensiveDamageReflectProc",
        "procChancePercent": 10.0,
        "triggerCondition": "OnIncomingPhysicalDamage",
        "procReflectPercent": 100.0,
        "procDamageTakenMultiplier": 0.0,
        "bypassesTargetShield": true
      }
    },
    {
      "so_name": "SO_Skill_Master_HD",
      "skill_id": "master_heavenly_destruction",
      "displayName": "Heavenly Destruction",
      "acronym": "HD",
      "tierCategory": "Tier1_SelfBuff",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 1,
      "acquisitionType": "CraftableOrUltraRareDrop",
      "craftingRecipeArchives": [
        "archive_bloody_sky",
        "archive_demon_gate",
        "archive_marble",
        "archive_demon_god"
      ],
      "spCostToLearn": 50,
      "maxSkillPoints": 100,
      "resourceCost": {
        "chiCost": 300,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 240.0,
      "durationSeconds": 180.0,
      "targetType": "Self",
      "effectData": {
        "buffType": "OffensiveShieldShatterProc",
        "procChancePercent": 10.0,
        "triggerCondition": "OnOutgoingAttackHit",
        "shattersTargetShield": true,
        "dispelStanceBuffChancePercent": 50.0
      }
    },
    {
      "so_name": "SO_Skill_Master_RF",
      "skill_id": "master_reverse_flow",
      "displayName": "Reverse Flow",
      "acronym": "RF",
      "tierCategory": "Tier1_SelfBuff",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 1,
      "acquisitionType": "CraftableOrUltraRareDrop",
      "craftingRecipeArchives": [
        "archive_snow_legend",
        "archive_northern_wind_legend",
        "archive_gods_legend",
        "archive_moon_wolf_legend"
      ],
      "spCostToLearn": 50,
      "maxSkillPoints": 100,
      "resourceCost": {
        "chiCost": 300,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 240.0,
      "durationSeconds": 180.0,
      "targetType": "Self",
      "effectData": {
        "buffType": "StunResistanceBoost",
        "stunResistanceBonusPercent": 20.0,
        "stacksWithSkill": "utility_finger_of_darkness"
      }
    },
    {
      "so_name": "SO_Skill_ThunderousRoar",
      "skill_id": "support_thunderous_roar",
      "displayName": "Thunderous Roar",
      "tierCategory": "Tier2_TeamBuffAndFormation",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 10,
      "acquisitionType": "TrainerNPC",
      "spCostToLearn": 1,
      "maxSkillPoints": 20,
      "resourceCost": {
        "chiCost": 100,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 60.0,
      "durationSeconds": 30.0,
      "targetType": "SelfOrAlly",
      "effectData": {
        "buffType": "DamageIncrease",
        "damageMultiplierBonusPercent": 100.0
      }
    },
    {
      "so_name": "SO_Skill_AzureWall",
      "skill_id": "support_azure_wall",
      "displayName": "Azure Wall",
      "tierCategory": "Tier2_TeamBuffAndFormation",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 10,
      "acquisitionType": "TrainerNPC",
      "spCostToLearn": 1,
      "maxSkillPoints": 20,
      "resourceCost": {
        "chiCost": 80,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 10.0,
      "durationSeconds": 180.0,
      "targetType": "SelfOrAlly",
      "effectData": {
        "buffType": "DefensiveAndElementalBoost",
        "defenseBonusPercent": 10.0,
        "elementalResistPercent": 10.0
      }
    },
    {
      "so_name": "SO_Skill_RaisingTheDead",
      "skill_id": "support_raising_the_dead",
      "displayName": "Raising the Dead",
      "tierCategory": "Tier2_TeamBuffAndFormation",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 20,
      "acquisitionType": "TrainerNPC",
      "spCostToLearn": 1,
      "maxSkillPoints": 10,
      "resourceCost": {
        "chiCost": 200,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 5.0,
      "durationSeconds": 0.0,
      "targetType": "SingleAlly",
      "effectData": {
        "buffType": "DirectHeal",
        "baseHealAmount": 500,
        "spiritScalingMultiplier": 1.5
      }
    },
    {
      "so_name": "SO_Skill_SpiritsReturn",
      "skill_id": "support_spirits_return",
      "displayName": "Spirit's Return",
      "tierCategory": "Tier2_TeamBuffAndFormation",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 20,
      "acquisitionType": "TrainerNPC",
      "spCostToLearn": 1,
      "maxSkillPoints": 10,
      "resourceCost": {
        "chiCost": 50,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 5.0,
      "durationSeconds": 0.0,
      "targetType": "SingleAlly",
      "effectData": {
        "buffType": "DirectChiRestore",
        "baseChiRestoreAmount": 400
      }
    },
    {
      "so_name": "SO_Skill_FingerOfDarkness",
      "skill_id": "utility_finger_of_darkness",
      "displayName": "Finger of Darkness",
      "tierCategory": "Tier2_TeamBuffAndFormation",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 1,
      "acquisitionType": "TrainerNPC",
      "spCostToLearn": 1,
      "maxSkillPoints": 20,
      "resourceCost": {
        "chiCost": 50,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 15.0,
      "durationSeconds": 0.0,
      "targetType": "SingleAlly",
      "effectData": {
        "passiveStunResistPercent": 40.0,
        "activeCleanseStatus": "Stunned",
        "cleanseSuccessChancePercent": 80.0
      }
    },
    {
      "so_name": "SO_Skill_Formation_Oval",
      "skill_id": "formation_oval",
      "displayName": "Oval Formation",
      "tierCategory": "Tier2_TeamBuffAndFormation",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 1,
      "acquisitionType": "PartyLeaderRequirement",
      "spCostToLearn": 0,
      "maxSkillPoints": 1,
      "resourceCost": {
        "chiCost": 0,
        "chiCostPerSecond": 12
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 0.0,
        "durationSeconds": 0.0,
      "targetType": "PartyAura",
      "effectData": {
        "auraRadiusMeters": 20.0,
        "dodgeChanceBonusPercent": 40.0,
        "moveSpeedBonusPercent": 40.0
      }
    },
    {
      "so_name": "SO_Skill_FingerOfLight",
      "skill_id": "utility_finger_of_light",
      "displayName": "Finger of Light",
      "tierCategory": "Tier3_CombatAndCC",
      "faction": "Common",
      "weaponRequired": "Any",
      "minLevel": 1,
      "acquisitionType": "TrainerNPC",
      "spCostToLearn": 1,
      "maxSkillPoints": 20,
      "resourceCost": {
        "chiCost": 80,
        "chiCostPerSecond": 0
      },
      "castTimeSeconds": 0.0,
      "cooldownSeconds": 20.0,
      "durationSeconds": 10.0,
      "targetType": "SingleEnemy",
      "effectData": {
        "actionType": "CrowdControlStun",
        "rangeMeters": 15.0,
        "statusApplied": "Stunned",
        "stunProcChancePercent": 50.0
      }
    }
  ]
}
```