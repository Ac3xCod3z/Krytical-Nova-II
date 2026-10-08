# Comic effects: marks, words and the look of a critical hit on a player

As built, 8 Oct 2026 (polish pass, workstream E). The comic layer of the game feel is made from the **Cartoon FX Pack
v2** (`Assets/_ThirdParty/Cartoon FX Pack v2`, read only). This page is the audit of where the pack was used, what of
it is used now and for what, and the rules for adding to it.

## 1. The rule

**Shape, word and lettering say who hit. Colour says what kind of hit it was.**

- Every weapon class owns one shape of impact mark, one font and three words. Nobody else uses them.
- Every kind of hit owns one colour ramp, the same for every class, so a hit reads the same in a crowd whoever dealt it.
- A moment that is not a plain hit (a death, the forge, a level gained, a find, a pet's evolution) has a word of its
  own, and no effect is shown for two different moments.
- A critical hit by one player on another shows a look that nothing else in the game uses.

## 2. Before (the audit)

The pack has 117 effects (55 explosions, 28 impacts, 34 texts) and 37 fonts. Seven effects were in use:

| Pack effect | Was used for |
| --- | --- |
| Impact 024 | every normal hit |
| Impact 028 | every heavy hit, every death, every critical death, and the hits of three skills |
| Impact 023 | every other skill's hit |
| Text 020 "POW!" | a critical hit, a thrown body landing, and a forge success |
| Text 022 "WHAM!" | a critical death, one skill, and a forge break |
| Text 021 "ZAP!" | two skills |
| Text 012 "SMASH!" | built, never shown |

So one word stood for a critical hit, a landing and the forge, and every class hit with the same star. Everything else
the game feel shows (dust, auras, casts, heals, the Nova) is Synty POLYGON Particle FX and still is: section 7.

## 3. Hits: one look per weapon class

A hit shows the attacker's class **mark** at the point of the hit, in the colours of the kind of hit. A heavy hit also
shows a **word** above it. Light hits (the first two strikes of a combo) show the mark alone.

| Class | Mark (light / heavy) | Lettering | Heavy hit | Critical hit | Signature skill | Blade trail |
| --- | --- | --- | --- | --- | --- | --- |
| Monoblade | a crossed cut (Impact 009 / 023) | Shojumaru | SHING! | SLASH! | SLICE! | `#4F9BF0` |
| Glaive | a sweep (012 / 026) | Black Ops One | SWISH! | THWACK! | WHOOSH! | `#E04FB8` |
| Carbine | a muzzle star (010 / 024) | Bungee | BANG! | BLAM! | RATATAT! | `#4FE3F0` |
| Harvester | streaks of a scythe (001 / 015) | Creepster | SHRED! | REAP! | CULL! | `#F03A2E` |
| Servo Claws | a claw's rake (011 / 025) | Rubik Glitch | RAKE! | REND! | RIP! | `#39D1B4` |
| Arc Caster | rays (013 / 027) | Rubik Wet Paint | ZAP! | KRAKL! | BZZZT! | `#9B6BFF` |
| Longaxe | a star (007 / 021) | Caesar Dressing | CHOP! | KRAK! | THOOM! | `#FF7A3C` |
| Claymore | a cloud (002 / 016) | Berkshire Swash | KLANG! | BASH! | KRASH! | `#FFC93C` |
| Warbow | a ring (006 / 020) | Jolly Lodger | THWIP! | THUNK! | TWANG! | `#6FD06A` |
| no class (a creature, a GM command) | a puff (014 / 028) | none | none | none | none | none |

The fonts follow the factions: Hegemony's are clean and military, Null's broken and eerie, Covenant's carved and old.
The heavy mark is the same shape as the light one with a coloured fringe, half as large again.

Which word:

- **Critical hit:** the class's critical word, whatever dealt it.
- **Signature skill** (slots S4, S6, S8): the class's skill word.
- **Heavy hit** (a combo's finisher, any other skill): the class's heavy word.
- **A blow thrown back** (Backlash): BACKLASH! instead (Knewave), and **a barrier broken** (Shatterpoint): SHATTER!
  instead (Frijole).
- One attacker shows one word in 0.45 s; a critical word is never held back by an ordinary one.
- Of fights the local player is no part of: at most 5 words on screen at once, and only within 26 m of the camera.
  Marks are drawn within 48 m.

Sizes: a light mark is 0.9 m across and a heavy one 1.35 m (a shape of thin lines up to 1.6 times that). A heavy
word's capitals are 0.26 m tall and the word at most 1.35 m wide, a skill word 0.29 m / 1.5 m, a critical word
0.33 m / 1.7 m. A word sits 0.8 m above the hit and, like the mark, 0.35 m nearer the camera than the hit, so the body
that was hit does not hide it.

## 4. Kinds of hit: one colour ramp each

A ramp is four colours: core (the white flash), fill (the letters), back (the burst behind them) and ink.

| Kind | When | Core | Fill | Back |
| --- | --- | --- | --- | --- |
| Normal | anything else | `#FFFFFF` | `#FFC93C` | `#FF9E66` |
| Elemental | at least 35 % of the damage was the Elemental part (`CombatStatTuningData.elementalHitShare`) | `#EFFFFF` | `#4FE3F0` | `#4C52BF` |
| Critical | a critical hit | `#FFF3C4` | `#F03A2E` | `#FFC93C` |
| Shattered | the blow broke a barrier | `#FFFFFF` | `#ADE0F2` | `#1F6ECC` |
| Reflected | the blow was thrown back | `#FFFFFF` | `#E04FB8` | `#7A4FE0` |

Ink is `#14122B` everywhere. When a hit is several of these, the rarest wins: Reflected, then Shattered, then
Critical, then Elemental. An evaded hit shows nothing in the world. The server sends the kind with the hit as one byte
(`PlayerMeleeCombat.SpawnImpactVfxClientRpc`); it is for the picture only.

## 5. A critical hit on another player

Shown when one player lands a critical hit on another (not on a creature, a dummy or oneself, and not when the blow was
thrown back). The class mark is drawn as usual; in place of the class's word comes a panel half as large again as any
other word of a hit (capitals 0.5 m, up to 2.6 m wide), with screen feedback of its own on the screens of the two
players: a camera jolt, a chromatic slam, a field-of-view punch, a lens punch, and both hold their pose for 0.14 s. The
one who was hit gets 60 % of the screen's part. The Screen shake setting switches the screen's part off. One attacker
shows it at most once in 0.3 s.

Three looks are built. `HitLookSettings.pvpCritStyle` picks the one in use.

| Look | What it is |
| --- | --- |
| **Impact frame** (in use) | "CRIT!" in white Permanent Marker on a burst of ink, magenta speed lines through the hit. The only thing in the game drawn without colour, which is what makes it unmistakable. |
| Splash panel | "KABLAM!" in yellow Bangers on a red and white burst with ink smoke. The classic full-page panel. |
| Faction strike | The attacker's faction speaks: "DENIED!" (Hegemony, Black Ops One, cyan), "ERASED!" (Null, Rubik Glitch, red) or "JUDGED!" (Covenant, Caesar Dressing, gold). |

`gm.fx.pvpcrit [splash|frame|faction|default]` shows a look on the nearest target and keeps it on this screen until the
game is closed, to compare them. `gm.fx.hit [normal|elemental|crit|shatter|backlash] [light|heavy]` shows a hit of the
weapon in hand on the nearest target. Neither hits anything.

## 6. Other moments

| Moment | From the pack |
| --- | --- |
| A death | an ink cloud (Explosion 022) and "K.O.!" (Explosion 040, Luckiest Guy, white on ink) |
| A critical death | the ink cloud and "WHAM!" (Text 022, Bangers) |
| A thrown body lands | "THUD!" (Text 016, Titan One) |
| The forge: it worked | "FORGED!" (Text 029, Alfa Slab One, gold) |
| The forge: a Failsafe held | "SAVED!" (Text 027, Carter One, cyan) |
| The forge: the piece broke | "CRACK!" (Text 012, Frijole, red) |
| A level gained | a ring of puffs (Explosion 006) and "LEVEL UP!" (Text 011, Bungee Shade) in the colours of the character's faction |
| A drop taken | a puff where it lay (Impact 005) |
| A Rare find | "!!" over the finder's head (Text 033, Rare's yellow) |
| An Epic find | "!!!" over the finder's head (Text 034, Epic's purple) |
| A pet's evolution | "EVOLVED!" (Text 001, Slackey) in the colour of the stage it grew into |

A level gained and a find are new moments (`IGameFeelSink.LevelUp`, `PickedUp`); every client sees them. The one who
gained the level also gets a small lift of the camera and a chromatic shimmer. Their sound slots (Level Up, Rare Find)
are on the prefab and empty, like the rest.

## 7. What is not from the pack

Buffs, heals, casts, movement, selection, the Nova and Standings keep their Synty POLYGON effects. Each is a prefab of
its own under `VFX/GameFeel`, but some are the same POLYGON effect in another colour and size:

| POLYGON effect | Used for |
| --- | --- |
| FX_Cartoony_Rings_01 | the click ring, a hostile selection, a landing, a critical kill's shockwave, two rings of the Nova |
| FX_LevelUp_01 | the power-up cast, the forge's success, a pet's evolution, the Nova's column |
| FX_MagicBlast_02 | the buff cast, the forge's Failsafe, two bursts of the Nova |
| FX_LightRayRound_01 | a respawn, the forge's rays, a pet's rays, the Nova's rays |
| FX_Sparkles_Large_01 | the forge's sparkles, a pet's sparkles, the Nova's sparkles, one aura |
| FX_Explosion_Body_01 | a death, a critical death, the forge's break |
| FX_Sparks_01 | a hit's sparks, the forge's fail, a Nova that did not take |
| FX_Heal_01 | health restored (green), Flux restored (cyan) |
| FX_Healing_Cirle_02, FX_Electricity_01, FX_Swirl_03 | two or three buff auras each, in different colours |

These were not part of this pass. If they should be told apart as the hits now are, the pack's unused smoke clouds
(below) are the material for it.

## 8. What of the pack is used, and what is left

Used as the source of an effect of ours: 53 of 117 (it was 7), and 20 of the 37 fonts.

- **Impacts, 21 of 28.** Not used: 003, 004, 008, 017, 018, 019, 022 (small puffs and a very thin ring).
- **Texts, 23 of 34.** Not used: 002, 005, 006, 007, 008, 009, 015, 025, 028, 031, 032.
- **Explosions, 9 of 55:** 006, 022, 040, 041, 042, 043, 044, 047, 055. Not used: the other smoke clouds and debris
  (001 to 039), the coloured smokes (048 to 053) and three word panels (045, 046, 054).
- **Fonts not used:** Alien Block, Bagel Fat One, Bahiana, Blaka, Bokor, Caveat Brush, Erica One, Faster One,
  Gochi Hand, Indie Flower, Major Mono Display, Monoton, Potta One, Road Rage, Rubik Vinyl, Supermercado One, VT323.
  (Monoton, Faster One and Major Mono Display were tried for the Arc Caster and could not be read at the size of a hit.)

A text of the pack is an animation (how the letters fly in, how the burst moves); several of our words are made from
the same one with another word and font. The Impact frame's speed lines are the same pack impact as the Arc Caster's
light mark (rays), five times the size and in magenta: the pack has one shape of rays.

## 9. How they are made

- **Builder:** `GameFeelBuilder.Comic.cs`, menu **Krytical Nova ▸ GameFeel ▸ Build Comic Effects** (a few minutes) and
  **Rebuild Comic Feel** (the players on `PFX_GameFeel.prefab` only). The tables at the top of the file are the whole
  assignment: `ClassComics`, `OtherMarks`, `OtherWords`.
- A **mark** is a copy of one of the pack's impacts. A **word** is made by the pack's own text tool, driven on a copy
  of ours: it is given the word and the font and rebuilds the effect letter by letter. The builder then spaces the
  letters by their real widths, holds the smallest letter to 55 % of the largest, gives a word at least 0.55 s and a
  mark at least 0.18 s, measures the effect on its particles and sizes it to the table.
- **The pack is never changed.** Its prefabs are instantiated and its fonts read; every material a word needs is a copy
  in `VFX/Materials/Comic/<word>/` (the tool rewrites the materials of what it rebuilds).
- Output: `VFX/GameFeel/Comic/PFX_Mark_*`, `PFX_MarkHeavy_*`, `PFX_Word_*`, `PFX_PvpCrit_*`, each with a `ComicFx` that
  says which layer takes which colour of a ramp, so one pooled copy can be shown in any kind's colours.
- **Settings:** `GameFeel/Data/Assets/HitLookSettings.asset` holds the ramps, which effect each class has, the word
  rules (height, scatter, gap, budget, distances), `markScale` and `wordScale` (resize without rebuilding), the look in
  use for a critical hit on a player, the factions' colours and the pool counts. An asset that exists keeps its
  numbers when the effects are rebuilt.
- **Lettering is drawn over the finished frame.** Every comic effect is on the `ComicLettering` layer, which the PC
  renderer draws in a pass of its own after the ink, the sun shafts and the glow (`ComicLetteringFeature`, Rendering
  Bible section 2). Drawn with the other transparents, a word had the outline of whoever stood behind it inked across
  its letters and the light of their weapon on top.

## 10. Pool and cost

- Everything is pooled (`NetworkCombatVFXPool`) and made ready when a scene starts, a few copies a frame: 4 of each
  mark, 2 of each word, 16 hit sparks, 12 of a basic attack's spark. The pool takes an effect back when its particles
  are done.
- **The local player's own fight is always shown**: if no copy is ready, one is made. **Anyone else's** marks, words
  and sparks are shown only when a copy lies ready and are left out otherwise, so a crowd cannot make the pool grow
  in the middle of a fight.
- Measured in the Editor on the Zone01 host (1163 × 1067), 30 hits a second by other players for 6 s: 0 effects
  created, 47 words shown and about as many held back by the budget; mean frame 6.6 ms with the comic layer against
  6.0 ms without, 95 % of frames under 12.1 ms against 11.1 ms. The comic layer's own calls allocate nothing (0 B a
  hit). A real fight with the dummy, real pick-ups and every moment on this page: 0 effects created.
- The ready pool is 338 sleeping copies with about 2,000 particle systems: about 107 MB by the Editor's count, 86 MB of
  it the comic effects (a word is one particle system a letter layer). `warmWords` and `warmImpacts` on the settings
  trade that against the rule above.
- Tests: `GameFeel/Tests/Editor/HitLookTests.cs` (the rules, that every class has its five effects, that no effect is
  shared between two moments, the three looks of the critical hit on a player, the lettering layer and its pass, how
  long a mark is seen, when the pool takes an effect back).

## 11. Not checked yet

A second client was not available for this pass (the Multiplayer Play Mode clone was held up by a dialog), so the
kinds of hit, a real critical hit on a player, a level gained and a find as another client sees them were checked on
the host only, through the game's own paths and previews.
