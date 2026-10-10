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

| Id | Pet | Was | Model | Stat | At 100 % |
| --- | --- | --- | --- | --- | --- |
| 8001 | Emberwing | Scout Drone | Quadcopter | Flux (`MaxChi`) | 2,000 |
| 8002 | Drone Core | Guardian Drone | **its own drone** (10 Oct 2026) | Health (`MaxHP`) | 2,200 |
| 8003 | Tidecaller | Orb Companion | Orb | Defense (`DefensePower`) | 2,200 |
| 8004 | Kitsune Spirit | Mantis Drone | **its own fox** (9 Oct 2026) | Attack (`AttackPower`) | 1,100 |

Emberwing and Tidecaller are still shown with the Synty drone that stands in for them.

- **The Kitsune Spirit's own model** (9 Oct 2026): `Characters/Pets/KitsuneSpirit/Pet_KitsuneSpirit_S1.prefab` (the fox of the Blender pipeline: 2,079 triangles, 47 bones, coloured from the shared swatch in `Characters/Shared/Swatch/`). It is the pet item's model, so all five stages show it until a stage has art of its own. A pet's own model is the `Model` of its row in `PetAssetBuilder`; its icon is retaken with **Krytical Nova ▸ Items ▸ Capture Icons (Selected Items)**.
- **It stands on clouds** (9 Oct 2026, owner): the fox does not bob up and down as the drones do; it keeps its height beside its owner, and a small cloud under each paw is what holds it up: a few white cartoon puffs (3.5 to 6 cm) that swell under the paw and drift slowly straight back from the fox, some 8 cm in two seconds, with only a slight waver, as they shrink away. `PetDefinition.bobs` (off for the fox), `footEffect` (`VFX/GameFeel/PFX_Pet_Cloud.prefab`, built by **Krytical Nova ▸ GameFeel ▸ Rebuild Pet Cloud**; its numbers are in `GameFeelBuilder.ShapePetCloud`) and `footBones` (the four toe bones). `PetFollower` wears one cloud per named bone and moves it with the paw, so a lifted paw takes its cloud with it; with no bone named there is one under the middle of the body. In `PetAssetBuilder` a row with `Feet` is a pet that stands.
- **Walk and run** (9 Oct 2026, owner): a pet whose controller has a true/false parameter `Walk` or `Run` walks or runs on the spot while its owner covers ground, so it seems to go along with them; the follower is what moves it. It walks while the owner goes `PetSettings.walkFromOwnerSpeed` (0.3 m/s) or faster, runs from `runFromOwnerSpeed` (7 m/s, where the owner's own run starts) and drops back to a walk below `runToWalkOwnerSpeed` (5). The speed is measured from the owner's movement on each machine (`PetFollower.OwnerGait`), so nothing is sent. The fox's **Walk** is a 1 s loop and its **Run** a 0.5 s gallop; they play at their own pace and are not matched to the ground, since it is carried on its clouds. While it walks or runs it does not take its turn at the other animations, and one forced by a GM is cut short by the gait.
- **The Drone Core's own model** (10 Oct 2026): `Characters/Pets/DroneCore/Pet_DroneCore_S1.prefab` (5,918 triangles, 6 bones, 0.30 m, its own painted texture rather than the shared swatch). It hovers and bobs as the stand-in did, with no clouds, at 0.45 of the size every pet is brought to (`petScale` on the item, `Scale` of its row: a 0.25 m cube at base size), because a cube at the common 0.55 m dwarfed its owner. Six clips: the movement loop **FrontRoll** (1 s, rolls forward top first) and five that play once from rest and take their turn like the fox's: **AxisSpin** 3.96 s, **AxisSpinDiagonal** 4.04 s, **EyeLook** 5.71 s, **Mad** 3.13 s, **ShutterTurn** 2.29 s. The two spins are longer than a third of the 10 s turn, so it is busy more of the time than the fox.
- **One movement loop** (10 Oct 2026, owner): a model with no walk and run of its own names the true/false parameter that holds its movement loop in `PetDefinition.moveLoop` (`FrontRoll`; `MoveLoop` of its row in `PetAssetBuilder`). It holds that loop whenever its owner moves, played at `moveLoopWalkSpeed` (0.6) while the owner walks and `moveLoopRunSpeed` (1.5) while the owner runs, on the same owner speeds as the fox's walk and run. The speed is the whole Animator's and goes back to 1 at rest.
- **Animations** (9 Oct 2026): a pet model with an Animator rests in the controller's looping state and has one animation per trigger of that controller, named after the trigger (`PetAnimations`; nothing is written per pet). The fox has eight clips (model updated 9 Oct 2026 with Walk, Run and cleaner ear weights): **Idle** (6 s, loops) and five that play once and blend back to it: **Blink** 0.46 s, **LookAround** 2.46 s, **PawLift** 1.46 s, **SpinChase** 2.13 s, **TailCurl** 1.29 s. A pet plays them in turn, one every `PetSettings.animationCycleSeconds` (10 s; 0 = it only rests). Each machine keeps its own turn, so two players may see a different one at the same moment; an animation forced by a GM is sent to everyone (`PlayerPet.ServerPlayAnimation`) and the turn-taking waits a full interval after it.

- **Growth and hunger travel with the item** (`PetItemState` packed into `ItemStack.data`): a pet keeps them in the Pack, the Vault, a trade, a Kiosk and on the ground. Only the pet that is out gets hungry and earns XP.
- **The stat** is `max × growth % ÷ 100`, added to the owner's stats while the pet is out and fed (`PlayerPet`). Growth is replicated to everyone, so every machine derives the same stat and stage.
- **Stages:** until the real models exist each stage is the stand-in a size step larger with a stronger glow in the doc's palette. The glow (changed 6 Oct 2026; the orbiting ribbons are gone) is a rim of light round the pet's own outline, never a tint on its body, plus soft sparkles drifting off it; there is no ring on the ground, since pets fly. Both grow with the stage: the halo from 0.7 to 1.7 bright and from 5 to 11 cm wide, breathing deeper and faster as it grows, and 3 to 16 sparkles a second (`PetSettings.rimIntensity`, `rimWidth`, `rimPulse`, `rimPulseSpeed`, `rimPulseWidth`, `sparkleRate`; effect `PFX_Pet_Glow`). The halo is the one a tempered weapon wears, drawn on the screen by the Glow Halo renderer feature (rebuilt 7 Oct 2026, Rendering Bible section 6e), so it hugs the pet's outline whatever its model. A starving pet loses its glow with its bonus. Crossing 25, 50, 75 or 100 % plays the evolution moment: a FEEL burst on the pet, a banner for the owner ("EMBERWING EVOLVED · STAGE 2 OF 5") and a chat line with the new bonus.
- **Where it hovers** (changed 7 Oct 2026): beside its owner's head, on the side and at the height the pet item's `petOffset` gives. How far out is no longer that offset's length. The pet's nearest edge is kept `PetSettings.ownerHalfWidth` + `followGap` from the owner's centre line (0.18 m + 0.07 m = 0.25 m) at base size, and its underside stays at the height it has at base size, so a pet that grows moves out and up, never in or down. A grown pet stands further off still: the reach of its own glow is added, and `PetSettings.followGapGrowth` (0.25 m) for every whole size it has grown past base. At stage 5 that puts its nearest edge about 0.50 m from the owner's centre line, so neither the pet nor its light sits on the owner's shoulder. One pet can sit closer or further with `PetDefinition.followGapAdjust` (0 for all four today). The spacing is worked out from each model's own bounds (`PetPlacement`), so real stage models need no numbers of their own, and a pet's size no longer depends on which way its owner faced when it came out. On a turn the pet swings round its owner, by their back on a turn-about, instead of passing through them. A pet within `PetSettings.cameraClearance` (0.6 m) of the camera shrinks out of view until the camera has room again; at the normal closest zoom (3 m) that never happens. The follow lag is unchanged up to a point: at a run the pet trails by about the owner's speed × 0.35 s, but never by more than `PetSettings.followMaxTrail` (3 m, since 10 Oct 2026). Without that limit the fast Run ranks (rank 14 and up, 23 m/s and more) left it over 8 m back, which is where it is put in place as after a portal: it jumped to its owner two or three times a second. Past the limit it keeps pace, and it is only put in place when its owner really was (a portal, a spawn). A pet with a walk and a run now goes by the faster of its owner's speed and its own, so it runs in after its owner stops instead of sliding in at rest.
- **Pet XP:** 0.04 per Hostile kill (only the combat dummy counts as one today). `PlayerPet.ServerAwardEventXp(won)` gives the 50 / 25 for a PvP event; nothing calls it yet.
- **Hunger:** 0.05 % a minute while online. At 0 % the bonus and XP stop. **Pet Ration** (item 9010) refills it; it has no shop yet, so it is GM-only (`item.give Pet_Ration`).
- **Where the player reads it:** the pet's tooltip, anywhere the item is shown (stage pips, growth, hunger, current bonus). Nothing is added to the Equipment window.
- **Kiosk price cap** for a pet rises with its growth: from the Common cap at 0 % to 20,000,000 at 100 % (`EconomySettings`).
- **Reactions** (8 Oct 2026). A pet reacts to what its owner does, in a comic speech bubble beside it.
  - *Who says what.* The Drone Core speaks in words; Emberwing, Kitsune Spirit and Tidecaller in emoji (drawings of
    the Cartoon Emotion Pack, redrawn in ink with Colour Bible fills by **Krytical Nova ▸ Items ▸ 9. Bake Pet Emoji**).
    What each pet says is its `PetChatterSet` (`Pets/Data/Assets/Chatter/PetChatter_*.asset`, on `PetDefinition.chatter`):
    ten lines, or eight emoji, for each of seven moments. It never says the same one twice running.
  - *The moments*, most important first: you die, a rare drop, you level up, you kill a Bruiser, you land a critical
    hit, your HP is low, you stand idle for a long time. When two come together the more important one is reacted to.
  - *How often* (`Pets/Data/Assets/PetChatterSettings.asset`): 8 s of quiet after any bubble (a death is never held
    back); the same moment again only after 20 s (rare drop), 10 s (Bruiser), 25 s (critical hit), 45 s (low HP),
    120 s (idle); a bubble stays 2.6 s plus 0.045 s a character, 4.5 s at most. Low HP is under 25 % and warns again
    once the HP has been back over 50 %; idle is 75 s without moving or fighting; a rare drop is an item of Rare or
    better picked up off the ground.
  - *Who sees it:* only the pet's owner. Nothing is sent for a bubble, and **Settings ▸ Pet chatter** switches it off.
  - *Where the bubble goes* (`PetBubbleController`, `PetBubblePlacement`): to the left of the pet, following it. It
    never lies over the player, a namecard, a HUD panel (the HP bar among them) or the target and its marker: it is
    shifted up or down, then put on the pet's other side, then above it. "Beside the pet" is measured from the
    pet's own box turned as the pet is (`PetFollower.TryGetBodyBox`), not from its glow or its sparkles.
  - *How it looks* (`ComicBubble`, `.comic-bubble` in `ProgressionHUD.uss`): paper, an ink outline, a hard shadow,
    Ben-Day dots in its shaded corner, a tail toward the pet.
  - *How it is noticed* (`PetChatterDirector` on the game feel prefab, local only): deaths and critical hits come from
    the game feel moments; levels, HP, idling and pick-ups are watched on the local player. Another system reports a
    moment with `PetChatterMoments.Raise`. **Nothing reports a Bruiser kill yet** (there are no Bruisers), and items
    do not drop from kills yet, so today a rare drop is a rare item picked up; `gm.pet.say <died|drop|level|bruiser|crit|lowhp|idle>` plays any of them.
- **GM:** `gm.pet`, `gm.pet.growth <0-100>`, `gm.pet.hunger <0-100>`, `gm.pet.feed`, `gm.pet.xp <n>`, `gm.pet.event <win|loss>`, `gm.pet.say <moment>`, `gm.pet.anims` (lists the animations of the pet that is out), `gm.pet.anim <name>` (plays one now, for everyone); the Pets tab of the GM Tools window.
- **Not built:** the real stage models, pet XP from wars, a Pet Ration shop, Legendary pets, other players seeing a pet's bubbles (`PetChatterSettings.audience` has only *owner only*).

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
