# Pet Evolution System — Synty-Aligned Creature Concepts

Exported from the Claude doc "Pet Evolution System — Synty-Aligned Creature Concepts" (5 Oct 2026). Owner: Ac3.

4 original creatures in one shared Synty POLYGON + Sidekick aesthetic. Each has one stat that grows with a 0–100% growth meter, a 5-stage visual evolution, and a hunger meter that switches the stat bonus off at 0%.

## System Format

Each creature is a single row, not a chain of separately-leveled stages. Level Requirement is the player's level, and "Level 1 or higher" means any player can use the pet. Each creature grows one stat: Attack, Flux (Chi in code), Defense or Health. There is no Awakening range for now.

Each creature also has 5 visual evolution stages (baseline, 25%, 50%, 75%, 100%): it doesn't change level, it visibly matures/intensifies as its stat climbs — the same way the reference's butterfly, bee, totem and skull each show 5 escalating forms.

The four creatures below aren't one reskin per pack — they're built to sit together as one roster, each borrowing silhouette and material cues from a different cluster of the Synty library (monster-scale, relic/bone, mechanical, winged) so the set reads as cohesive even though it spans fantasy and sci-fi assets.

## How pets grow

Each pet earns its own XP, separate from the player's, and that XP fills its growth meter from 0% to 100%. For this round of pets, 100% growth takes 100,000 pet XP, so each 1% is 1,000 XP. A pet at 0% hunger gains no XP.

| Source | Pet XP | Notes |
| --- | --- | --- |
| Hostile (mob) kills | 0.04 per kill | Lowest amount, most consistent source |
| PvP events such as war (later) | 50 to each pet on the winning team or teams | 25 to pets on losing teams; players with no pet get nothing |

Pacing target: about 2,000 hours of grinding alone to reach 100%. At 0.04 XP per kill that is 2,500,000 kills, about 2,083 hours at one kill every 3 seconds (50 XP an hour). PvP events shorten this: a win is worth about an hour of grinding and a loss half an hour.

Pets evolve at 25%, 50%, 75% and 100% growth: those are the points where the pet changes to its next visual stage. The stat is paid out separately from the stages: every 1% of growth gives the player 1/100 of the pet's max value, so only a pet at 100% gives the full stat. The 25/50/75/100% columns in each creature's table show the stat the player has at each evolution point. The max values are high on purpose: the last evolution stage takes a very long time to reach.

| Pet | Stat | Per 1% growth | Max at 100% |
| --- | --- | --- | --- |
| Tidecaller | Defense | 22 | 2,200 |
| Drone Core | Health | 22 | 2,200 |
| Kitsune Spirit | Attack | 11 | 1,100 |
| Emberwing | Flux | 20 | 2,000 |

Kitsune Spirit's max is half the others on purpose: Attack is the most valuable stat.

## Hunger

Each pet has a hunger meter. It drains 0.05% per minute on the server clock, only while the player is online: 3% an hour, about 33 hours of play from full to empty. At 0% hunger the owner stops receiving the pet's stat bonus and the pet gains no XP until it is fed. A Pet Ration, sold in NPC shops, restores hunger to 100%.

## As built (5 Oct 2026)

Code in `Assets/_Game/Gameplay/Pets/`; the numbers above are fields of `Pets/Data/Assets/PetSettings.asset` and of each `PetDef_*.asset`, built by **Krytical Nova ▸ Items ▸ 7. Build Pets** (`PetAssetBuilder`).

- **The four pets replace the five cosmetic drones.** All Common, usable from level 1, no Legendary. Item ids are kept:

| Id | Pet | Was | Stand-in model | Stat | At 100 % |
| --- | --- | --- | --- | --- | --- |
| 8001 | Emberwing | Scout Drone | Quadcopter | Flux (`MaxChi`) | 2,000 |
| 8002 | Drone Core | Guardian Drone | Guardian Drone | Health (`MaxHP`) | 2,200 |
| 8003 | Tidecaller | Orb Companion | Orb | Defense (`DefensePower`) | 2,200 |
| 8004 | Kitsune Spirit | Mantis Drone | Mantis Drone | Attack (`AttackPower`) | 1,100 |

- **Growth and hunger travel with the item** (`PetItemState` packed into `ItemStack.data`): a pet keeps them in the Pack, the Vault, a trade, a Kiosk and on the ground. Only the pet that is out gets hungry and earns XP.
- **The stat** is `max × growth % ÷ 100`, added to the owner's stats while the pet is out and fed (`PlayerPet`). Growth is replicated to everyone, so every machine derives the same stat and stage.
- **Stages:** until the real models exist each stage is the stand-in a size step larger with a stronger glow in the doc's palette. The glow (changed 6 Oct 2026; the orbiting ribbons are gone) is a rim of light round the pet's own outline, never a tint on its body, plus soft sparkles drifting off it; there is no ring on the ground, since pets fly. Both grow with the stage: rim brightness 0.9 to 2.4 and 3 to 16 sparkles a second (`PetSettings.rimIntensity`, `sparkleRate`; effect `PFX_Pet_Glow`, shader `TemperGlow`, the same rim a tempered weapon wears). A starving pet loses its glow with its bonus. Crossing 25, 50, 75 or 100 % plays the evolution moment: a FEEL burst on the pet, a banner for the owner ("EMBERWING EVOLVED · STAGE 2 OF 5") and a chat line with the new bonus.
- **Pet XP:** 0.04 per Hostile kill (only the combat dummy counts as one today). `PlayerPet.ServerAwardEventXp(won)` gives the 50 / 25 for a PvP event; nothing calls it yet.
- **Hunger:** 0.05 % a minute while online. At 0 % the bonus and XP stop. **Pet Ration** (item 9010) refills it; it has no shop yet, so it is GM-only (`item.give Pet_Ration`).
- **Where the player reads it:** the pet's tooltip, anywhere the item is shown (stage pips, growth, hunger, current bonus). Nothing is added to the Equipment window.
- **Kiosk price cap** for a pet rises with its growth: from the Common cap at 0 % to 20,000,000 at 100 % (`EconomySettings`).
- **GM:** `gm.pet`, `gm.pet.growth <0-100>`, `gm.pet.hunger <0-100>`, `gm.pet.feed`, `gm.pet.xp <n>`, `gm.pet.event <win|loss>`; the Pets tab of the GM Tools window.
- **Not built:** the real stage models, pet XP from wars, a Pet Ration shop, Legendary pets.

## Open questions

- [x] Hunger drain: built at 0.05% per minute (3% an hour), as written above. It is one field of `PetSettings` if 8% an hour is wanted.
- [ ] PvP event rewards of 50 / 25 pet XP are proposed (about an hour / half an hour of grinding), and the 2,000-hour target assumes one kill every 3 seconds. Confirm both.
- [ ] Pet Ration price, and which NPC sells it (GM-only until then)

## Tidecaller

A baby kaiju that never outgrows its owner's shoulder — all the Kaiju Pack's spiky, amphibious monster-styling compressed into a fist-to-forearm-sized companion. Growth reads through surface detail and a faint tidal aura, not literal size, so it always stays perch-ready. The roster's tankiest, "wow factor" pick.

**Visual Evolution**

- Base (Spawnling): smooth egg-shaped body, single blinking eye, no spikes
- 25%: first dorsal spike pair, tiny fin-like limbs
- 50%: second spike row, faint bioluminescent seams along the belly
- 75%: compact armored shell plates, fins widen into small propulsion fins
- 100% (Apex, still pocket-sized): full spike crest, glowing tide-line cracks, floats on a thin aura disc — never grows past perch size

| Level Requirement | Defense 25% | 50% | 75% | 100% |
| --- | --- | --- | --- | --- |
| Level 1 or higher | 550 | 1,100 | 1,650 | 2,200 |

**Art Direction Brief**

Proportions: compact ovoid body, overall size capped at roughly head-to-forearm length at every stage (~15–20cm in-world scale), fin span ≈ 0.6× body length, floats ~5cm above its perch on the aura disc at 100% only.

| Element | Material / finish | Hex |
| --- | --- | --- |
| Hide | Satin, faintly damp-looking | #2F4858 |
| Belly bioluminescence | Emissive | #5AD8C6 |
| Armor plates | Semi-gloss | #8E9AAF |
| Aura disc | Emissive, translucent | #3FA7D6 |
| Eye | Emissive | #FFD23F |

## Drone Core

A boxy little robot buddy built from the Mech Pack's chunky panel-lines, shrunk to palm size and softened with the Sci-Fi Robots Sidekick's rounder, friendlier proportions. Bolts on visible armor plates and a cannon pod as it grows, always small enough to hover at shoulder height.

**Visual Evolution**

- Base: single-rotor cube drone, bare frame, one optic lens
- 25%: plating added to two faces, scanner ring lights up
- 50%: small cannon pod mounts underneath
- 75%: full plating wrap, second cannon pod added
- 100%: compact "gunship" silhouette, running-light trim, thruster glow — still fits in one hand

| Level Requirement | Health 25% | 50% | 75% | 100% |
| --- | --- | --- | --- | --- |
| Level 1 or higher | 550 | 1,100 | 1,650 | 2,200 |

**Art Direction Brief**

Proportions: cube-ish chassis ≈ 1:1:1 width:height:depth, cannon pods ≈ 15% of body width each, rotor span ≈ 1.3× body width (kept tight for a shoulder-scale silhouette).

| Element | Material / finish | Hex |
| --- | --- | --- |
| Panel plating | Matte, olive-drab | #5A5F3D |
| Warning stripe | Flat, decal | #F2C230 / #1A1A1A |
| Joints | Brushed metal | #9AA0A6 |
| Core / thruster glow | Emissive | #4FD1FF |

## Kitsune Spirit

A fox spirit wrapped in paper talismans, drawing on the Samurai Warriors' restrained lacquer-and-gold palette. Gains two tails per growth stage, from one up to nine — tails stay thin and weightless-looking (spectral, not furred mass) so the whole creature still reads as a small, shoulder-riding companion even at full tail count.

**Visual Evolution**

- Base (1 tail): small, slightly translucent fox form, one plain talisman around the neck
- 25% (3 tails): talisman wraps extend down the forelegs, faint ember glow at tail tips
- 50% (5 tails): fur gains a soft gradient, talisman seals glow
- 75% (7 tails): ears gain gold tipping, tails fan in a loose arc behind it
- 100% (9 tails): full nine-tail fan, kept thin/spectral so it trails rather than bulks up, talisman crown, soft flame-wisp trim

| Level Requirement | Attack 25% | 50% | 75% | 100% |
| --- | --- | --- | --- | --- |
| Level 1 or higher | 275 | 550 | 825 | 1,100 |

**Art Direction Brief**

Proportions: kit-sized body (small fox, not full-grown adult), each tail ≈ 0.8× body length but near-weightless/thin so the silhouette stays compact when curled around a shoulder; ears ≈ 20% of head height.

| Element | Material / finish | Hex |
| --- | --- | --- |
| Fur (base / tips) | Matte, soft gradient | #E8C9A0 / #9C5B3C |
| Talisman paper | Matte | #F4EDE1 |
| Talisman ink | Matte | #2E2A26 |
| Tail flame-wisps | Emissive | #FF8A3D |
| Eyes | Emissive | #FFD23F |

## Emberwing

A winged mystic beast — the Knights Pack's griffon-style heraldry, the Viking raven's silhouette and the Samurai pack's restrained lacquer-and-gold palette, folded into one creature with the Elven Warriors' elegance. The roster's showpiece, with the most dramatic silhouette change across its evolution.

**Visual Evolution**

- Base: small feathered glider, dull plumage, folded wings
- 25%: wingspan doubles, first gold filigree appears along feather edges
- 50%: a second, smaller pair of wings sprouts; plumage brightens
- 75%: talons gild over, a crest of longer feathers forms
- 100% (Ascendant): four full wings in a lacquer-and-gold pattern, trailing ribbon-like tail feathers

| Level Requirement | Flux 25% | 50% | 75% | 100% |
| --- | --- | --- | --- | --- |
| Level 1 or higher | 500 | 1,000 | 1,500 | 2,000 |

**Art Direction Brief**

Proportions: wingspan ≈ 2.2× body length at full growth, slender bird-like body kept at the player's shoulder at every stage like the rest of the roster, lower wing pair present from 50% growth and tail ribbons at 100% only (roughly 1× body length each once grown).

| Element | Material / finish | Hex |
| --- | --- | --- |
| Lacquer plumage | Glossy | #7A1F3D |
| Filigree trim | Metallic gold | #D4AF37 |
| Under-feather | Matte, soft | #F2E6D8 |
| Tail ribbons | Satin / cloth shader | #2E86AB |
