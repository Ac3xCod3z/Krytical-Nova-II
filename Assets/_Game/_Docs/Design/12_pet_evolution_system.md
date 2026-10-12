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
| 8001 | Emberwing | Scout Drone | **its own bird** (10 Oct 2026) | Flux (`MaxChi`) | 2,000 |
| 8002 | Drone Core | Guardian Drone | **its own drone** (10 Oct 2026) | Health (`MaxHP`) | 2,200 |
| 8003 | Tidecaller | Orb Companion | **its own egg** (10 Oct 2026) | Defense (`DefensePower`) | 2,200 |
| 8004 | Kitsune Spirit | Mantis Drone | **its own fox** (9 Oct 2026) | Attack (`AttackPower`) | 1,100 |

All four pets now have a model of their own; no Synty drone stands in for a pet any more.

- **The Kitsune Spirit's own model** (9 Oct 2026): `Characters/Pets/KitsuneSpirit/Pet_KitsuneSpirit_S1.prefab` (the fox of the Blender pipeline: 2,079 triangles, 47 bones, coloured from the shared swatch in `Characters/Shared/Swatch/`). It is the pet item's model, so all five stages show it until a stage has art of its own. A pet's own model is the `Model` of its row in `PetAssetBuilder`; its icon is retaken with **Krytical Nova ▸ Items ▸ Capture Icons (Selected Items)**.
- **It stands on clouds** (9 Oct 2026, owner): the fox does not bob up and down as the drones do; it keeps its height beside its owner, and a small cloud under each paw is what holds it up: a few white cartoon puffs (3.5 to 6 cm) that swell under the paw and drift slowly straight back from the fox, some 8 cm in two seconds, with only a slight waver, as they shrink away. `PetDefinition.bobs` (off for the fox), `footEffect` (`VFX/GameFeel/PFX_Pet_Cloud.prefab`, built by **Krytical Nova ▸ GameFeel ▸ Rebuild Pet Cloud**; its numbers are in `GameFeelBuilder.ShapePetCloud`) and `footBones` (the four toe bones). `PetFollower` wears one cloud per named bone and moves it with the paw, so a lifted paw takes its cloud with it; with no bone named there is one under the middle of the body. In `PetAssetBuilder` a row with `Feet` is a pet that stands.
- **Walk and run** (9 Oct 2026, owner): a pet whose controller has a true/false parameter `Walk` or `Run` walks or runs on the spot while its owner covers ground, so it seems to go along with them; the follower is what moves it. It walks while the owner goes `PetSettings.walkFromOwnerSpeed` (0.3 m/s) or faster, runs from `runFromOwnerSpeed` (7 m/s, where the owner's own run starts) and drops back to a walk below `runToWalkOwnerSpeed` (5). The speed is measured from the owner's movement on each machine (`PetFollower.OwnerGait`), so nothing is sent. The fox's **Walk** is a 1 s loop and its **Run** a 0.5 s gallop; they play at their own pace and are not matched to the ground, since it is carried on its clouds. While it walks or runs it does not take its turn at the other animations, and one forced by a GM is cut short by the gait.
- **The Drone Core's own model** (10 Oct 2026): `Characters/Pets/DroneCore/Pet_DroneCore_S1.prefab` (5,918 triangles, 6 bones, 0.30 m, its own painted texture rather than the shared swatch). It hovers and bobs as the stand-in did, with no clouds, at 0.45 of the size every pet is brought to (`petScale` on the item, `Scale` of its row: a 0.25 m cube at base size), because a cube at the common 0.55 m dwarfed its owner. Six clips: the movement loop **FrontRoll** (1 s, rolls forward top first) and five that play once from rest and take their turn like the fox's: **AxisSpin** 3.96 s, **AxisSpinDiagonal** 4.04 s, **EyeLook** 5.71 s, **Mad** 3.13 s, **ShutterTurn** 2.29 s. The two spins are longer than a third of the 10 s turn, so it is busy more of the time than the fox.
- **One movement loop** (10 Oct 2026, owner): a model with no walk and run of its own names the true/false parameter that holds its movement loop in `PetDefinition.moveLoop` (`FrontRoll`; `MoveLoop` of its row in `PetAssetBuilder`). It holds that loop whenever its owner moves, played at `moveLoopWalkSpeed` (0.6) while the owner walks and `moveLoopRunSpeed` (1.5) while the owner runs, on the same owner speeds as the fox's walk and run. The speed is the whole Animator's and goes back to 1 at rest.
- **The Drone Core's four evolutions** (11 Oct 2026, owner): from 25 % growth it is a model of its own at each stage: `Characters/Pets/DroneCore_25` (a ring light and a barrel lens), `_50` (a cage frame and a cannon under it), `_75` (plating and side pods), `_100` (the Gunship, with its own texture and emission map). They are the `model` of stages 1 to 4 of `PetDef_DroneCore` (`StageModels`), sized by `StageSizes` 0.28 / 0.47 / 0.53 / 0.63 m longest side, which keeps the artist's proportions at the scale the base cube is shown at (cubes of 0.28, 0.31, 0.35 and 0.38 m; my choice, not yet seen by the owner). Each has the base form's clips (the photo is **BarrelTurn** instead of ShutterTurn) and more: **CannonFire** from 50 %, **MinigunFire** at 50 and 75 %, and at 100 % **SpinShots**, **FakeoutBlast** and the swarm (**Disassemble**, **SwarmHover**, **Assemble**). The data chips are worn at every stage.
  - *Rest pose.* The 100 % file holds the model flown apart into its swarm, and its resting state has no clip, so it would rest in pieces. `PetDefinition.restPose` (`EyeLook`) names the animation whose first frame it rests in: `PetAnimations.Pose` puts the model there before its Animator starts, and it is then measured as posed (`PetPlacement.TryMeasurePosed`), because the file's bounds cover the swarm (1.4 m). Better fixed in the export; this holds until it is.
  - *Animations that follow one another.* `PetDefinition.followOns`: Disassemble, then SwarmHover twice round, then Assemble (`PetAnimations.Continue`). The ones that follow take no turn of their own; `gm.pet.anim` can still play any of them alone.
  - *What it says.* `PetDefinition.lines`: in FakeoutBlast it says "Ka...", "Me...", "Ha!", "...just kidding" and "Die!" on frames 1, 13, 27, 45 and 65 of 130, in the pet bubble (`PetFollower.Spoke`, shown by `PetChatterDirector`), for any pet in sight.
  - *Effects on the gun.* A `PetEffect` with `alongBone` turns with its bone and looks down it, its offset in the bone's frame in metres of the model (the cannon's length puts it at the muzzle: 0.137, 0.170, 0.178 m). One with `endsWithAnimation` is cleared at once when its animation is cut short (the beam, the blur round the barrel).
  - *The effects* (sent to the owner as a contact sheet of sixteen on 11 Oct 2026: yes to all, with the pod rockets and the spent rounds added on their word; those two additions are not yet seen). Built by **Krytical Nova ▸ GameFeel ▸ Rebuild Drone Core Effects** (`GameFeelBuilder.DroneCore.cs`) as `VFX/GameFeel/PFX_Pet_Drone*.prefab`. Shots, smoke, shells, steam, arcs, sparks, the gathering power and the body of the beam are POLYGON Particle FX effects made over for URP; the flashes, shock rings, the blur sleeve, the beam's core, the hoops and the scan lines are meshes drawn with `KryticalNova/FX/Pet Drone Energy` (`VFX/GameFeel/Shaders/FX_PetDroneEnergy.shader`), which uses the All In 1 Shader Nodes library's Twist UV, Scroll Texture, Radial Pattern Lines, Hologram and Glow. BarrelTurn: camera flash on frame 34. EyeLook: a cone of scan lines from the lens. AxisSpin and AxisSpinDiagonal: two hoops of streaks. Mad: steam from its top. CannonFire: flash, ring and tracer on frames 12 and 30; on the 75 % a rocket also leaves each pod arm (`pod.L`, `pod.R`) on those frames, flies about two metres and bursts eight frames later (`PFX_Pet_DroneRocket`, `PFX_Pet_DroneBoom`; owner, 11 Oct 2026). MinigunFire: blur sleeve and tracers from frame 14 to 60, and spent rounds thrown out at the breech, twelve a second (owner, 11 Oct 2026). SpinShots: blur sleeve and eight small shots. FakeoutBlast: power gathers to frame 29, a puff on 31, the beam from 72 to 110. Disassemble and Assemble: a shell of scan lines on the burst (10) and the snap (36), arcs while the blocks are out, a ring and sparks on the lock (39).

- **Emberwing's own model** (10 Oct 2026): `Characters/Pets/Emberwing/Pet_Emberwing_S1.prefab` (2,730 triangles, 29 bones, painted from the shared swatch). It flies beside its owner and bobs as the stand-in did. Eight clips: **Idle** (4 s, perched with wings folded), **Fly** (2.5 s, flapping upright on the spot), the movement set **MoveStart** 0.83 s → **Move** (3.33 s loop, level flight with one roll) → **MoveStop** 0.92 s, and three that play once and take their turn: **Squawk** 1.79 s, **WingSpread** 3.04 s, **BarrelRoll** 2.17 s. Its movement loop is `Move`, at the Drone Core's speeds (0.6 at a walk, 1.5 at a run).
- **A loop to rest in** (10 Oct 2026; my choice, not yet seen by the owner): Emberwing hovers at head height, where its perched Idle would sit on nothing, so it waits in **Fly** instead. `PetDefinition.restLoop` (`RestLoop` of its row in `PetAssetBuilder`) names the true/false parameter that holds the loop; `PetAnimations` holds it while the pet is at rest, lets go when the owner moves or an animation plays once, and takes over again when that one is back in the default state. Stopping therefore goes straight from Move into Fly instead of waiting out the loop and MoveStop. Empty the field and it waits perched, as the controller was made.
- **An effect of its own** (10 Oct 2026; the two looks are my proposal, not yet seen by the owner): as the fox has its clouds, a pet can wear effects that mark it out. `PetDefinition.effects` (`Effects` of its row in `PetAssetBuilder`) lists them: a prefab and the bones of the model it is worn on, one copy a bone (or one on the middle of the body). `PetFollower.SetEffects` wears them as it wears the clouds, a child of the pet that follows the bone's place and never its turning (the Drone Core tumbles), but leaves the particles as the effect made them, so these two trail in the world. They are tinted with the growth stage's glow colour, so they change as the pet grows. Local on every machine, nothing is sent. Built by **Krytical Nova ▸ GameFeel ▸ Rebuild Pet Effects** (`GameFeelBuilder.ShapePetCinders`, `ShapePetCrumbs`) into `VFX/GameFeel/`, solid and inked like the fox's puffs:
  - **Emberwing: cinders.** Small flat flakes (about 4 cm) shed from both wing tips (`wing.L.004`, `wing.R.004`) and the end of the tail (`tail.001`), left behind in the air. They tumble, rise a little as hot ash does, and shrink away within a second. A trickle while it hangs in the air (4 a second), a stream when it flies (3.5 a metre).
  - **Drone Core: data chips.** Little cubes (about 2.5 cm) dropped behind the body (`body`) while it moves, 2.2 a metre, which hang where they were dropped, blink twice and go out after about a second. None while it rests.
  - Not built: a burst on the Emberwing's BarrelRoll and WingSpread, and the Drone Core's sweep of light from its lens at rest.
- **Effects that belong to an animation** (10 Oct 2026, asked for by the owner for the Tidecaller; the looks are mine and wait on review): a `PetEffect` may name an `animation` (a state of the pet's Animator). It is then not worn all the time: with moments (`at`, shares of the animation from 0 to 1) it plays once as the animation passes each; with none it plays for a stretch of it (`during`; 0 to 0 = all of it) and stops letting particles go when the stretch ends. With no bone named it stands at a place on the pet that does not move with the animation (`anchor`: Body, Feet or Top, moved by `offset` in footprints), so dust stays on the floor while the pet hops. `PetAnimations.TryGetPlaying` says what the Animator is in and how far through; `PetFollower.PlayAnimationEffects` plays them. A row of `PetAssetBuilder` gives the moments as frames of the clip (`Frames(last, ...)`). Local on every machine.
  - **Tidecaller**, built by **Krytical Nova ▸ GameFeel ▸ Rebuild Tidecaller Effects** (`GameFeelBuilder.Pets.cs`), solid and inked like the fox's puffs; the wave, the bubble and the Zs are meshes made by the builder (`VFX/GameFeel/Meshes`):
    - *Hop dust* (`PFX_Pet_HopDust`): a ring of 7 or 8 small puffs thrown out along the floor from under it. **HopSpin** at frame 7 of 35 (it leaves the floor) and 23 (it comes down); **VictoryBounce** at its three landings, frames 17, 32 and 50 of 75.
    - *Rolling dust* (`PFX_Pet_RollDust`): while **Move** plays, puffs raised under it, 7 a metre and 4 a second, that keep about half its speed and so run after it for a moment before they shrink away.
    - *Zs in a bubble* (`PFX_Pet_Zzz`): while it dozes in **WobbleNap** (frames 20 to 105 of 149; it starts awake at 110), a white thought bubble 0.7 of its width stands over its top with two little bubbles leading down to it, and three Zs, each bigger than the last, pop up in it one after the other. Bubble and letters face the camera from any side.
    - *The wave* (`PFX_Pet_TideWave`): **TideCall** leans back from frame 10 and heaves forward at 38 and again at 55 of 95. The wave is called at frame 21 and lasts 1.6 s, in the pet's own space: it gathers behind the egg (0.55 s), runs forward past it steepening and whitening at the crest (to 1.05 s; no taller than the egg's brow, so its face shows over it), throws its lip forward, curls over a hollow face and brings the lip down in front of the egg at 1.42 s, which is frame 55, and falls in on itself into a heap of foam that sinks away. It never lies flat (the owner's word on the first version, which ended as a sheet on the floor).
      - *How.* A flip book of meshes, about fifteen a second, which suits the cut-out look: the swell, then seventeen shapes of the break, each a one-particle system that shows for its moment and is carried forward at the wave's speed (`WaveStage`, `WaveAt`). The shapes are blends between six outlines of a slice through the wave (`WaveOutlines`: swell, steep, lip thrown, curl, lip down, heap), swept across and falling away to nothing at the two ends; all seventeen are in one asset, `VFX/GameFeel/Meshes/SM_FX_Wave.asset`.
      - *The shatter*, where the lip lands, 1.25 footprints ahead of the egg, in six parts from the floor up: foam that rolls out along the floor in a half ring ahead of the wave; a heap of foam thrown up over the spot; twenty or so shards of water (flat kites, `SM_FX_WaterShard`, in the water's two blues and white) flung up and forward, tumbling, gone before they reach the floor; small drops thrown high and a few fat ones low; and a mist of small foam that hangs over it for a moment after.
    - Not done: sound for any of them, and nothing for CrackCry or Tantrum.
- **Tidecaller's own model** (10 Oct 2026): `Characters/Pets/Tidecaller/Pet_Tidecaller_S1.prefab` (6,336 triangles, 17 bones; an egg with a face). It keeps its own look and does not use the shared swatch: seven materials of its own and one eye texture. It hovers and bobs beside its owner as the stand-in did, at 0.55 of the size every pet is brought to (a 0.30 m egg at base size). Ten clips: **Idle** (8 s: breathes, looks about, blinks), the movement set **MoveStart** 0.71 s → **Move** (1.33 s loop, lying on its side and rolling) → **MoveStop** 0.96 s, and six that play once and take their turn: **CrackCry** 5.79 s, **HopSpin** 1.46 s, **Tantrum** 3.63 s, **TideCall** 3.96 s, **VictoryBounce** 3.13 s, **WobbleNap** 6.21 s. Its movement loop is `Move`, at the Drone Core's speeds (0.6 at a walk, 1.5 at a run); it leaves the roll only at the end of a turn, so it rights itself up to a second or two after its owner stops. The faces (glare, tears, smile, the crack) are scaled out of the shell by the clips themselves.
- **The Tidecaller's four evolutions** (10 Oct 2026, owner): from 25 % growth the egg is a swimmer, a different creature at each stage: `Characters/Pets/Tidecaller_25` (plain, with fins), `_50` (stone shell plates), `_75` (spikes and the blue band), `_100` (cracks and the blue orb). They are the `model` of stages 1 to 4 of `PetDef_Tidecaller` (`StageModels` of its row in `PetAssetBuilder`). They share one rig and five clips, which are not the egg's: **Idle** (6 s, a slow swim on the spot), **Move** (2 s loop), and three that play once and take their turn: **TideCall** 5.13 s, **SwimFlip** 4.13 s (a backward somersault), **Ram** 3.46 s.
  - *Size.* The egg is 0.30 m; each form after it is 0.1 m longer on its longest side: 0.40, 0.50, 0.60, 0.70 m (`StageSizes` of the row; the builder turns each into the stage's `scale`).
  - *Glow that breathes.* The 50, 75 and 100 % forms have an emission map (the light blue lines; on the 100 % the yellow eyes too). `PetFollower` finds every material of a pet model with an emission map switched on and swings its emission colour between `PetSettings.emissionBreath` (0.55 and 2.4 times the material's own) `emissionBreathsPerSecond` (0.3) times a second, through a property block, so the material assets are not touched. Any later pet with an emission map breathes the same way.
  - *Effects by stage.* A `PetEffect` now says which growth stages it is worn at (`stages`, first to last) and how big it is against the pet's footprint (`size`). The egg's five effects are stage 0 only; the swimmers' four are stages 1 to 4. `PlayerPet` hands the follower the list for the stage the pet is at.
  - *TideCall* uses the egg's wave (`PFX_Pet_TideWave`), called at frame 47 of 123 so that it gathers behind the pet while it is reared up and breaks in front on its second beat (frame 81). At 0.55 of its made size: the swimmers are much wider than the egg and the full wave buried their owner's head.
  - *Water.* While **Move** plays, thin ribbons of water start on a ring ahead of the pet's nose and stream back past its sides and off behind it (`PFX_Pet_TideFlow`); during the turn of **SwimFlip** (frames 20 to 70 of 99) ribbons run round a hoop in the plane it turns over in (`PFX_Pet_TideSwirl`). Both are particle trails drawn with the shader `KryticalNova/FX/Pet Water Flow` (`VFX/GameFeel/Shaders/FX_PetWaterFlow.shader`, material `VFX/Materials/M_FX_PetWaterFlow.mat`): cut-out ribbons that taper at both ends, with an edge that wavers, bands of lighter water running down them and a rim of foam. Built by **Krytical Nova ▸ GameFeel ▸ Rebuild Tidecaller Swimmer Effects**.
  - *Ram.* `PetDefinition.chargeAnimation` (`Ram`) and `chargeBone` (`body`) name a charge. While that state plays the pet turns to face its owner instead of the way they face, and `PetFollower` moves it towards them in step with the clip: the clip itself only carries the body about a quarter of the pet's length, so the follower adds whatever is left between its nose and its owner (it reads the clip's own reach off the model once, when the pet comes out). At the strike (frame 22 of 83) a small comic burst with a few stars pops at its nose (`PFX_Pet_RamBump`). During a charge only the owner's own movement can set the pet swimming, not the lunge.
- **Rim and sparkles, made far smaller** (10 Oct 2026, owner): the rim of light is 1.2 to 3 cm wide (was 5 to 11) at a brightness of 0.18 to 0.4 (was 0.7 to 1.7), and a pet wears it only from `PetSettings.rimFromStage` (2 = 50 % growth). The sparkles are sized to the pet that wears them (`PetFollower.sparkleReach`: born on its surface, 3 to 6 % of its length each), 1.5 to 5 a second (was 3 to 16). They used to come out up to a whole pet-length across whatever the effect said: the follower multiplied their size at run time, which on a size given as a range sets the top of the range. That is gone.
- **Animations** (9 Oct 2026): a pet model with an Animator rests in the controller's looping state and has one animation per trigger of that controller, named after the trigger (`PetAnimations`; nothing is written per pet). The fox has eight clips (model updated 9 Oct 2026 with Walk, Run and cleaner ear weights): **Idle** (6 s, loops) and five that play once and blend back to it: **Blink** 0.46 s, **LookAround** 2.46 s, **PawLift** 1.46 s, **SpinChase** 2.13 s, **TailCurl** 1.29 s. A pet plays them in turn, one every `PetSettings.animationCycleSeconds` (10 s; 0 = it only rests). Each machine keeps its own turn, so two players may see a different one at the same moment; an animation forced by a GM is sent to everyone (`PlayerPet.ServerPlayAnimation`) and the turn-taking waits a full interval after it.
- **A pet is calm while its owner fights** (owner, 10 Oct 2026, to save frame time when most is going on): in a fight it rests, walks and runs and does nothing else. No animation is played once, by turn or on demand (a GM's is refused too), so there is no charge and none of the effects that belong to those animations, and one under way is cut back to rest. Whether a player is in a fight is `PlayerCombatState` (Gameplay/Combat): decided by the server and told to every machine, true from a blow given or taken, an attack skill cast, an attack ordered or a duel, until 6 s (`lingerSeconds` on the player prefab) have gone by without any of it. `PlayerPet` passes it to `PetFollower.SetCombatCalm` on every machine, so everyone sees the same.

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
