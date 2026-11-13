# Defensive stats rework: Defense, Block and Crit Resistance (build 17 item 8)

Status: **DESIGN, no code yet** (owner, 13 Nov 2026: research first, one design doc, then code). Every number below is **PROPOSED**.
The owner picks before the code phase.

## 1. Today's formula (what the game does now)

One formula serves both netcodes: `Core/Runtime/CombatMath.cs` `Resolve` (the DOTS server and `CombatCalculator.CalculateAttackInternal` →
`CombatCalculator.cs:72` both call it). Numbers come from `CombatStatTuning.asset`.

| step | file:line (CombatMath.cs) | rule | number |
|---|---|---|---|
| hit | `Resolve` step 1 | `Hit / (Hit + Evasion)`, + modifier, clamped | 10 - 95 % |
| Backlash (reflection) | step 2 | only with Heavenly Repulsion up: reflects `AP x skill` | buff's chance |
| shield shatter | step 3 | attacker buff | buff's chance |
| **Defense** | step 4 | `max(1, AP x skill - Defense)`: **subtractive** | — |
| crit | step 5 | `roll <= CriticalRate`; x(1.5 + CritDmg %); Critical Defense (capes) trims only the bonus part | base crit 15, x1.5 |
| elemental | step 6 | `max(0, EleAtk - EleDef)`, ignores Defense | — |

Stats from points (`CombatStatTuning.asset`): Str → 1 AP, 2 Hit; Agi → 1 DP, 2 Evasion; Vit → 1 Evasion, 20 HP; Spi → 1 AP.
Gear (the Hostile curve's reference player, `HostileStatCurve.asset`): AP 5.2 x level x rarity, DP 4.85 x level x rarity, Hit 3.5 x level,
Evasion 0.7 x level; rarity 1.0 at level 1 → 1.7 at 112. Points: 5 at the start + 5 a level.

**Why Defense feels pointless:** subtraction is all or nothing. Against a plain hit, a high-Defense build is almost immune (thousands of hits
to kill); against a skill (x3) the same Defense is nearly nothing, because the multiplier is applied before Defense is taken off. Skills are
what decides a fight, so in practice Defense does little and HP wins. Nothing resists a crit except the cape's Critical Defense, which only
trims the bonus.

## 2. Proposed model (one pass)

All numbers go in `CombatStatTuning.asset` (new fields), never in code; the change stays inside `CombatMath.Resolve` so the DOTS server
gets it for free (one shared formula).

1. **Defense curve (diminishing, never immunity):** `damage = AP x skill x K / (K + Defense x (1 - penetration))`, `K = 200 + 12 x attacker
   level` (K = 1,544 at 112). Defense equal to K halves a hit; double K cuts two thirds. Penetration 0 now (a field, for later skills).
2. **Block roll (new):** `chance = 30 % x DP / (DP + K)`, cap 30 %. A blocked hit loses 60 % and shows **BLOCKED** (a new look in
   `HitLookSettings.asset`, `GameFeelPresenter` beside `Missed`). It is rolled after the hit roll, so evasion and block stack.
3. **Crit resistance:** the attacker's crit rate is cut by `resist = min(60 %, 35 % x DP/(DP+K) + 25 % x 2 x Eva/(Eva+Hit) + cape
   Critical Defense)`. No defence: crits land at their rate; well geared: a third or less. The cape's bonus-trim stays as it is.
4. **Evasion and Backlash:** higher Evasion adds a small Backlash chance: `+5 % x Eva/(Eva+Hit)`, cap +5 points, only when the Backlash
   buff is up (it stays a buff skill).
5. Elemental stays outside Defense (Guard / Ele Def handle it).

## 3. Time-to-kill tables (hits to kill, expected values: hit chance, block and crit averaged)

Builds (all free points into): **STR**; **VIT**; **AGI**; **DEF** = Vit/Agi half each + Defense-tempered armour (gear DP x1.6);
**MIX** = the Hostile curve's reference player (50 / 20 / 20 / 10); **STR+C** = 80 % Str / 20 % Agi + a ring with crit (7.2: +20 at 112).
Same level both sides, Epic-ish gear by level. Script: `Tools~` not committed; the tables are its output.

Rock-paper-scissors read at 112, skills (x3): proposed **DEF takes 5.8 Strength skills to die vs 0.7 for a STR build** (Defense beats raw
Strength), **VIT outlasts everything** but hits like a feather, STR+C kills the evasive AGI build fastest. Not yet good: AGI is still weak
against STR (1.5 hits). The owner's "Agility beats slow heavy hitters" needs the hit / evasion side tuned (attack speed, a dodge window),
which this doc leaves open: pick in the morning.

### Level 30, skill multiplier x1: hits to kill (expected)

Current:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 4.2 | 28.0 | 38.4 | 902.4 | 10.8 | 5.7 |
| VIT | 29.6 | 279.1 | 2603.6 | 6669.7 | 1070.5 | 522.9 |
| AGI | 29.6 | 279.1 | 2603.6 | 6669.7 | 1070.5 | 522.9 |
| DEF | 29.6 | 279.1 | 2603.6 | 6669.7 | 1070.5 | 522.9 |
| MIX | 6.6 | 49.1 | 1556.4 | 4165.7 | 20.3 | 10.4 |
| STR+C | 5.0 | 34.5 | 530.1 | 3562.4 | 13.7 | 7.1 |

Proposed:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 2.9 | 19.7 | 5.9 | 16.9 | 6.6 | 3.5 |
| VIT | 6.2 | 59.2 | 21.1 | 55.8 | 18.0 | 8.8 |
| AGI | 6.2 | 59.2 | 21.1 | 55.8 | 18.0 | 8.8 |
| DEF | 6.2 | 59.2 | 21.1 | 55.8 | 18.0 | 8.8 |
| MIX | 3.7 | 27.7 | 8.9 | 24.6 | 9.0 | 4.6 |
| STR+C | 3.2 | 22.2 | 6.9 | 19.3 | 7.4 | 3.8 |

### Level 30, skill multiplier x3: hits to kill (expected)

Current:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 0.8 | 5.5 | 1.6 | 4.6 | 1.9 | 1.0 |
| VIT | 2.0 | 19.2 | 8.1 | 22.7 | 6.0 | 2.9 |
| AGI | 2.0 | 19.2 | 8.1 | 22.7 | 6.0 | 2.9 |
| DEF | 2.0 | 19.2 | 8.1 | 22.7 | 6.0 | 2.9 |
| MIX | 1.1 | 8.1 | 2.6 | 7.4 | 2.6 | 1.3 |
| STR+C | 0.9 | 6.3 | 1.9 | 5.4 | 2.1 | 1.1 |

Proposed:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 1.0 | 6.6 | 2.0 | 5.6 | 2.2 | 1.2 |
| VIT | 2.1 | 19.7 | 7.0 | 18.6 | 6.0 | 2.9 |
| AGI | 2.1 | 19.7 | 7.0 | 18.6 | 6.0 | 2.9 |
| DEF | 2.1 | 19.7 | 7.0 | 18.6 | 6.0 | 2.9 |
| MIX | 1.2 | 9.2 | 3.0 | 8.2 | 3.0 | 1.5 |
| STR+C | 1.1 | 7.4 | 2.3 | 6.4 | 2.5 | 1.3 |

### Level 60, skill multiplier x1: hits to kill (expected)

Current:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 3.4 | 27.7 | 40.8 | 6341.4 | 10.0 | 4.7 |
| VIT | 29.6 | 357.9 | 4323.0 | 12728.5 | 4649.4 | 2022.5 |
| AGI | 29.6 | 357.9 | 4323.0 | 12728.5 | 4649.4 | 2022.5 |
| DEF | 29.6 | 357.9 | 4323.0 | 12728.5 | 4649.4 | 2022.5 |
| MIX | 5.5 | 49.7 | 2500.0 | 7727.3 | 19.4 | 8.9 |
| STR+C | 4.0 | 33.6 | 2043.8 | 6434.6 | 12.5 | 5.8 |

Proposed:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 2.4 | 19.8 | 5.2 | 17.4 | 6.2 | 2.9 |
| VIT | 4.9 | 59.7 | 18.7 | 58.2 | 16.6 | 7.2 |
| AGI | 4.9 | 59.7 | 18.7 | 58.2 | 16.6 | 7.2 |
| DEF | 4.9 | 59.7 | 18.7 | 58.2 | 16.6 | 7.2 |
| MIX | 3.0 | 27.8 | 7.7 | 25.3 | 8.4 | 3.8 |
| STR+C | 2.6 | 21.9 | 5.9 | 19.5 | 6.8 | 3.1 |

### Level 60, skill multiplier x3: hits to kill (expected)

Current:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 0.6 | 5.2 | 1.3 | 4.3 | 1.6 | 0.7 |
| VIT | 1.5 | 17.9 | 6.4 | 21.7 | 5.1 | 2.2 |
| AGI | 1.5 | 17.9 | 6.4 | 21.7 | 5.1 | 2.2 |
| DEF | 1.5 | 17.9 | 6.4 | 21.7 | 5.1 | 2.2 |
| MIX | 0.8 | 7.5 | 2.1 | 6.9 | 2.2 | 1.0 |
| STR+C | 0.7 | 5.7 | 1.5 | 4.9 | 1.7 | 0.8 |

Proposed:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 0.8 | 6.6 | 1.7 | 5.8 | 2.1 | 1.0 |
| VIT | 1.6 | 19.9 | 6.2 | 19.4 | 5.5 | 2.4 |
| AGI | 1.6 | 19.9 | 6.2 | 19.4 | 5.5 | 2.4 |
| DEF | 1.6 | 19.9 | 6.2 | 19.4 | 5.5 | 2.4 |
| MIX | 1.0 | 9.3 | 2.6 | 8.4 | 2.8 | 1.3 |
| STR+C | 0.9 | 7.3 | 2.0 | 6.5 | 2.3 | 1.0 |

### Level 90, skill multiplier x1: hits to kill (expected)

Current:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 3.1 | 27.4 | 39.6 | 9255.9 | 9.6 | 4.3 |
| VIT | 28.1 | 374.6 | 6029.2 | 18790.4 | 6657.8 | 2760.4 |
| AGI | 28.1 | 374.6 | 6029.2 | 18790.4 | 6657.8 | 2760.4 |
| DEF | 28.1 | 374.6 | 6029.2 | 18790.4 | 6657.8 | 2760.4 |
| MIX | 5.0 | 49.3 | 3444.9 | 11290.4 | 18.8 | 8.2 |
| STR+C | 3.6 | 32.5 | 2747.1 | 9175.6 | 11.8 | 5.3 |

Proposed:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 2.2 | 19.7 | 4.8 | 17.5 | 6.0 | 2.7 |
| VIT | 4.3 | 57.5 | 16.9 | 57.2 | 15.5 | 6.4 |
| AGI | 4.3 | 57.5 | 16.9 | 57.2 | 15.5 | 6.4 |
| DEF | 4.3 | 57.5 | 16.9 | 57.2 | 15.5 | 6.4 |
| MIX | 2.7 | 27.3 | 7.1 | 25.2 | 8.0 | 3.5 |
| STR+C | 2.3 | 21.3 | 5.3 | 19.3 | 6.4 | 2.8 |

### Level 90, skill multiplier x3: hits to kill (expected)

Current:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 0.6 | 4.8 | 1.1 | 4.0 | 1.4 | 0.6 |
| VIT | 1.2 | 16.2 | 5.2 | 19.4 | 4.4 | 1.8 |
| AGI | 1.2 | 16.2 | 5.2 | 19.4 | 4.4 | 1.8 |
| DEF | 1.2 | 16.2 | 5.2 | 19.4 | 4.4 | 1.8 |
| MIX | 0.7 | 6.9 | 1.8 | 6.4 | 2.0 | 0.9 |
| STR+C | 0.6 | 5.2 | 1.2 | 4.5 | 1.5 | 0.7 |

Proposed:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 0.7 | 6.6 | 1.6 | 5.8 | 2.0 | 0.9 |
| VIT | 1.4 | 19.2 | 5.6 | 19.1 | 5.2 | 2.1 |
| AGI | 1.4 | 19.2 | 5.6 | 19.1 | 5.2 | 2.1 |
| DEF | 1.4 | 19.2 | 5.6 | 19.1 | 5.2 | 2.1 |
| MIX | 0.9 | 9.1 | 2.4 | 8.4 | 2.7 | 1.2 |
| STR+C | 0.8 | 7.1 | 1.8 | 6.4 | 2.1 | 0.9 |

### Level 112, skill multiplier x1: hits to kill (expected)

Current:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 3.0 | 27.2 | 38.0 | 11393.7 | 9.4 | 4.2 |
| VIT | 26.6 | 370.7 | 7277.9 | 23236.5 | 8130.9 | 3302.7 |
| AGI | 26.6 | 370.7 | 7277.9 | 23236.5 | 8130.9 | 3302.7 |
| DEF | 26.6 | 370.7 | 7277.9 | 23236.5 | 8130.9 | 3302.7 |
| MIX | 4.8 | 48.9 | 4138.0 | 13903.6 | 18.3 | 7.9 |
| STR+C | 3.4 | 31.7 | 3242.9 | 11106.6 | 11.3 | 5.0 |

Proposed:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 2.2 | 19.5 | 4.6 | 17.5 | 5.9 | 2.6 |
| VIT | 4.0 | 55.8 | 16.0 | 56.0 | 14.8 | 6.0 |
| AGI | 4.0 | 55.8 | 16.0 | 56.0 | 14.8 | 6.0 |
| DEF | 4.0 | 55.8 | 16.0 | 56.0 | 14.8 | 6.0 |
| MIX | 2.6 | 26.8 | 6.8 | 25.0 | 7.8 | 3.3 |
| STR+C | 2.2 | 20.8 | 5.1 | 19.0 | 6.1 | 2.7 |

### Level 112, skill multiplier x3: hits to kill (expected)

Current:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 0.5 | 4.6 | 1.0 | 3.8 | 1.4 | 0.6 |
| VIT | 1.1 | 15.0 | 4.6 | 17.8 | 4.0 | 1.6 |
| AGI | 1.1 | 15.0 | 4.6 | 17.8 | 4.0 | 1.6 |
| DEF | 1.1 | 15.0 | 4.6 | 17.8 | 4.0 | 1.6 |
| MIX | 0.6 | 6.6 | 1.6 | 6.0 | 1.9 | 0.8 |
| STR+C | 0.5 | 4.9 | 1.1 | 4.2 | 1.4 | 0.6 |

Proposed:

| attacker \ defender | STR | VIT | AGI | DEF | MIX | STR+C |
|---|---|---|---|---|---|---|
| STR | 0.7 | 6.5 | 1.5 | 5.8 | 2.0 | 0.9 |
| VIT | 1.3 | 18.6 | 5.3 | 18.7 | 4.9 | 2.0 |
| AGI | 1.3 | 18.6 | 5.3 | 18.7 | 4.9 | 2.0 |
| DEF | 1.3 | 18.6 | 5.3 | 18.7 | 4.9 | 2.0 |
| MIX | 0.9 | 8.9 | 2.3 | 8.3 | 2.6 | 1.1 |
| STR+C | 0.7 | 6.9 | 1.7 | 6.3 | 2.0 | 0.9 |



## 4. Open picks for the owner (morning)

- A) Take the proposed numbers as they are and start the code (CombatMath + CombatStatTuning fields + BLOCKED look + tests).
- B) Raise Block (cap 40 %, cut 70 %) to make tanks tankier.
- C) Change the AGI answer: Evasion also cuts crit damage taken / adds a dodge, so Agility beats slow heavy hitters.

## 5. Code phase (after the pick)

`CombatMath.Resolve` (new steps 4b block, 5 crit resist), `CombatTuning` + `CombatStatTuningData` fields, `DamageResult.IsBlocked`, the DOTS
hit path reading `IsBlocked` for the popup ring, `HitLookSettings` BLOCKED, `HostileStatCurve` re-check (its 4-6 hits a kill test), tests:
`CombatMathTests` (formula to the bit, both netcodes), a new table test of the curve.
