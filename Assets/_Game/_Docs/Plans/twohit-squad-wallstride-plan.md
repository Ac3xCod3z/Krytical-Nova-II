# Plan: Two-hit attacks, T key, Squad buffs, the Redline charge on beat, Wallstride (wall-run and trees)

## Context
The owner asked for four things:
- a two-hit attack for every class at levels 1, 30, 60 and 80, on the same rules as the other attacks;
- a fifth letter key, **T**, on the hotbar;
- Stance buffs that also go to Squad members once the skill is at rank 20;
- a new rule for the Redline charge on the three-hit skills, judged against the Cadence beat;
- wall-running and jumping into trees, so a player can use the terrain to break off a fight.

### What the code has today
- **Two-hit kind is half there.** `SkillKinds` already knows TwoHit and the Arsenal has a TWO-HIT column, but no two-hit skill exists. All 108 class skills come from `BladeSkillBuilder` + `ClassSkillBuilder`, keyed by `ClassSkillSlot`.
- **The beat lives elsewhere.** Cadence is only on the parked branch `music/cadence`. Merging it into this branch gives 7 conflicts.
- **Buffs are per character only.** They are `CharacterBuffManager` entries mirrored by RPC, and a re-cast already refreshes rather than stacks. The Squad is `PlayerParty.ServerGetMembers`.
- **Movement is click-to-move on a NavMesh.** The server owns it. Jump, vault and death each switch the agent off and drive the body through Motion Warping; there is no climbing code.
- **Trees.** Terrain trees have colliders on everywhere. Our Synty packs ship almost no LODs: 11 prefabs in total, and none of them trees. Nothing in the world uses an LOD group, so Phase 4b builds them.
- **No Unity in this container.** Code and builders get written here, but the builder menus, tests and play checks have to run in the owner's Editor. The run order is in Verification.

### Decisions taken with the owner
- Merge Cadence first, without breaking anything.
- T becomes hotbar slot 15, and the Kiosk window moves to **J** (free; away from the combat keys).
- The charge rule covers every class's three-hit skills: S3, S5, Lv60 Multi and S7. The basic combo is unchanged.
- **The press decides.**
  - A three-hit skill pressed on Cadence while Redline is charged: all three hits crit with Redline's power, and the charge is spent after the third hit.
  - Pressed off the beat (or with no beat): the charge goes on the first hit, as today.
  - A hit the target evades **spends the charge** and that hit is lost. On an on-beat cast, the hits after it are plain hits.
- Wall-run and tree perch start from a click on the surface, and also happen automatically when a run or jump meets a surface that qualifies. Both need the new **Wallstride** passive: character level 60+, Run rank 20 and Jump rank 20.
- Animations: stand-ins from clips already in the project, held in a `WallRunAnimationSet` asset so a real parkour pack drops in later.

---

## Phase 0: bring Cadence in (one merge commit, nothing else in it)
1. `git merge origin/music/cadence` into `claude/modest-cray-vcpw2b` (a merge commit; no rebase).
2. Resolve the 7 conflicts by keeping **both** sides' additions, never dropping a line from main:
   - `HostileNet.cs`, `GmToolsWindow.cs`, `BuildSceneOrder.cs`: code from both sides, merged by hand.
   - `Player.prefab`: keep main's prefab and re-add only the Cadence components (`PlayerCadence`, `PlayerCombatState`) with their own fileIDs. Check there are no duplicate fileIDs and that every `m_Component` entry has its block.
   - `EditorBuildSettings.asset`: main's scene list plus the Cadence test arena. The arena stays out of builds, as that branch intends.
   - `ProjectOrganization.md` and `01_Systems_Build_List.md`: keep both, and renumber rows if both sides added one.
3. After the merge:
   - grep for the files that touched the same APIs: `PlayerMeleeCombat.ResolveHit` (Cadence adds `damageScale`), `PlayerSkillHotbar.SkillKeyPressed`, `PlayerCombatSkills`.
   - Check that every new `.cs` has a `.meta` and sits under an asmdef/asmref.
   - Check that the Cadence tests' asmdef references still resolve.
   - Check that `PlayerSystemsSetup` rows from both sides are present.

## Phase 1: the two-hit attacks and the T key

### New slots
`Gameplay/Progression/Data/WeaponClassId.cs`:
- Append `TwoHit1 = 13, TwoHit30 = 14, Lv60TwoHit = 15, Lv80TwoHit = 16` (append only).
- `SlotCount = 16` and extend `Slots[]` in enum order.

Every table that is indexed by slot:
- `SkillDefinitionSO.RankKey` slot-key array;
- `ClassSkillBuilder` arrays: `MonobladeIds`, `GreatSwordClips`, `BowClips`, `ClassSeed.names`, `Describe()`, the feel borrow, `SkillName()`;
- `ProgressionHUD.ClassLinks`: add a two-hit line, TwoHit1 → TwoHit30 → Lv60TwoHit → Lv80TwoHit;
- `GameFeelBuilder.Skills[]`: the two-hit pair borrows the S2/S3 feel by `presentationId`.

### Monoblade templates
New rows in `BladeSkillBuilder`, with ids `Monoblade_TwoHit1`, `Monoblade_TwoHit30`, `Monoblade_Lv60TwoHit` and `Monoblade_Lv80TwoHit`. Each number is the midpoint of that tier's single and three-hit rows, so it sits between them. Ranks follow from `pointsToMax` as now.

| Tier | Flux | Damage % | Bonus % | Points / ranks | Cooldown | How learned |
| --- | --- | --- | --- | --- | --- | --- |
| L1 | 11–175 | 213–527 | — | 30 / 30 | 2.25 s | Instructor |
| L30 | 108–246 | 233–620 | — | 35 / 35 | — | Instructor |
| L60 | 125–263 | 241–629 | 100–105 | 38 / 38 | — | Skill Chip |
| L80 | 143–281 | 250–638 | 101–110 | 40 / 40 | — | Skill Chip (Prime) |

- All tiers: `hitCount = 2` and new `DoubleHitTimes {0.35, 0.7}`, used by both builders.

### Names
Proposed, faction-themed and checked against existing names. They go into the `04_Weapon_Classes.md` §3 tables.

| Class | L1 | L30 | L60 | L80 |
| --- | --- | --- | --- | --- |
| Monoblade | Double Entry | Split Decision | Countersign | Double Jeopardy |
| Glaive | Baton Pair | Summons | Double Warrant | Due Process |
| Carbine | Double Tap | Controlled Pair | Crossfire | Twin Verdict |
| Harvester | Twin Reap | Bale and Bind | Gleaning Pass | Final Yield |
| Servo Claws | One-Two Piston | Crankshaft | Twin Clamp | Dual Overload |
| Arc Caster | Twin Spark | Dipole | Alternating Current | Double Discharge |
| Longaxe | Two Ravens | Brother's Bite | Twinblood | Fenris Jaws |
| Claymore | Oath and Answer | Twin Vows | Shield and Sword | Kinsguard |
| Warbow | Two Feathers | Paired Flight | Twin Talons | Wind and Thunder |

Before writing them, read them against the Lore Bible Fixed Canon.

### Animations
One cast clip per tier per class. Clips apply only to new assets, so later Inspector choices survive a rebuild.
- **Katana** (Monoblade, plus the stand-ins for Carbine, Arc Caster and Servo Claws): `Skill_F`, `Skill_I`, `Dash_Attack_ver_A`, `Revenge_Guard_Attack_ver_A`.
- **Great sword** (Glaive, Harvester, Longaxe, Claymore): `Skill_G_1`, `Skill_I`, `Skill_K`, `Revenge_Guard_Attack`.
- **Warbow** has only thin choices: `Stand_Shoot_Reload` at different speeds, and `Leaping` for L80. This is noted under "What is left".
- Hit times are tuned per clip in the builder.

### Skill Chips and drops (L60 and L80 only)
The L1 and L30 skills come from the Instructor, like S2 to S5.
- `CraftingSettings.CraftItemIds`: new range `TwoHitChipFirst = 9510`, 2 per class, so 9510–9527.
  - `SkillChip()` routes the two new slots there, and `SkillChips()` yields them.
  - Existing chip ids do not move.
- `UtilityItemGenerator.SkillChipRows()`: rows `Chip_<Class>_Lv60TwoHit` (Rare) and `Chip_<Class>_Lv80TwoHit` (Epic). Both share `AttackChipIcon`.
- `CraftingBuilder`:
  - `LinkChips` covers the new slots.
  - `IsPrimeChip` includes `Lv80TwoHit`.
  - `AddDrops` adds L60 rows (level 60+, 0.1 %) and L80 rows (level 113+, 0.02 %) to `HostileSettings.materialDrops`.
- GM tools `gm.chips` and `GiveSkillChips` pick the new chips up through `SkillChips()`.
- The unlock bitmask grows to 8 + 3×16 = 56 entries, still under 64. Add an `Assert` there so a later growth fails loudly.

### Icons
Through the Icon Library only:
1. "Give Icons To All Without One" (36 new skills from the class sets, about 38 free per set).
2. Bake All.
3. **Check Icons** must come back clean.

The chips share the attack-chip picture. `IconAssetsTests` follows `SlotCount`.

### The T key
- `InputSystem_Actions.inputactions`: add `SlotT` (`<Keyboard>/t`) to `SkillCommands`, and move the `Kiosk` binding to `<Keyboard>/j`.
- `PlayerSkillHotbar`: `SlotCount = 15`, plus `SlotActionNames`/`DefaultKeyLabels` "T".
- `KeyBindings.PlayerKeys`: add Hotbar 15.
- `ProgressionHUD.uxml`: add `slot-14`.
- `FactionHudBuilder.LetterSlots = 5`, then rebuild the faction HUDs.
- Hint texts: `ProgressionHUD`, `HudTyping`, `GuideLibrary.asset` (both the T key and the Kiosk key).
- Update `05_Economy_And_Social.md` (Kiosk on J).
- `PlayerHotbarMemory`: make sure a saved 14-slot hotbar loads into 15 slots (pad, don't reject). Add a test.
- Tests to update: `FactionHudTests` (15), `KeyBindingsTests`, `SkillChipTests`, `WeaponClassTests` (name tables and slot count).

## Phase 2: Redline on the three-hit skills
In `PlayerSkillAttacks.ExecuteRoutine` (server):
- When the skill is a three-hit kind (`SkillKinds.Of == ThreeHit`, with all of S3, S5, Lv60Multi and S7 being 3 hits) and a `consumedOnNextHit` buff is active, ask `PlayerCadence` whether this cast's press was on Cadence.
  - Add a small getter there, next to the `damageScale` it already hands out: `ServerCastWasOnCadence(skill)`.
- **On Cadence:**
  - Each hit resolves with Redline's modifiers still on and with `forceCrit = true`.
  - A new optional `forceCrit` on `CombatCalculator.CalculateAttack` and `PlayerMeleeCombat.ResolveHit` skips the crit roll, but only after the evasion roll.
  - After the third hit resolves, `ConsumeNextHitBuffs()`.
- **Off the beat or no beat:** the charge is spent on the first hit, as today.
- **Evasion:** an evaded hit calls `ConsumeNextHitBuffs()` right away, so the charge is lost on the miss.
  - On an on-beat cast, the hits after it are plain.
  - The basic combo and the single- and two-hit skills keep "spent on the first hit that lands".
- A pure rule class `RedlineRules` (`Gameplay/Combat/Runtime`) decides `{KeepCharged, CritAndKeep, SpendNow}` per hit from (kind, onCadence, evaded, hitIndex, hitCount). The routine only asks it. EditMode tests cover the table.
- Update the Redline tooltip in `SkillKinds`/`BladeSkillBuilder` and the Cadence design doc 17 ("charged skills earn nothing" stays true for Redline itself).

## Phase 3: Stance buffs shared with the Squad
- **Scope:** the nine Stance buffs (`classSlot == Stance`): offense, defense, and the ranged evade/move speed buffs. Guard and the shared self-buffs stay self-only.
- **Settings:** `SquadSettings` gets `buffShareRadius = 20` and `buffShareRank = 20`. `SkillDefinitionSO` gets a `sharesWithSquad` flag, which `ClassSkillBuilder` sets on Stance.
- **The interface:** a new `ISquadBuffTargets` in Progression, implemented by `PlayerParty`. It returns the living members in the same scene within the radius, excluding self (like `PlayerSponsor.IsNear`). Progression does not reference Social.
- **Casting:** in `PlayerCombatSkills.ExecuteCastInternal` (server), after the self-apply, when `sharesWithSquad && rank >= buffShareRank`:
  - for each member, call `member.ServerReceiveSquadBuff(skillId, rank)`;
  - each receiver applies it on the server and sends `SquadBuffClientRpc` to all, which does remove-then-apply (unlike `SyncBuffClientRpc`).
  - Autobuff casts share too, since they go through the same path.
- **No stacking:**
  - The same skill id refreshes the timer, as now.
  - Keep the **higher** rank of the existing and the incoming buff, so a low-rank re-cast restarts the timer without lowering the bonus.
  - Different Stances (offense + defense) are different ids and both apply, which is the intent.
- **Weapon swaps:** `ActiveBuff` gets `FromSquadmate`. `OnActiveClassChanged` leaves received buffs alone, so a received Bulwark Protocol survives the receiver swapping weapons.
- **Late joiners:** `SyncBuffClientRpc` already carries every active buff.
- **Move speed actually applied:** `StatType.MoveSpeedPercent` is shown but never reaches the agent. `PointAndClickPlayer.ApplyMovementSettings` multiplies the agent speed by `1 + MoveSpeedPercent/100` (server), re-applied when stats change. Without this the ranged Stance's move speed does nothing.
- **Tooltip:**
  - `SkillKinds.Describe` adds the line "Rank 20+: also given to Squad members within 20 m. The same Stance does not stack; casting it again restarts the timer."
  - Below rank 20 it shows greyed as "(from rank 20)".
  - The numbers are read from `SquadSettings`, not typed.
  - The buff-bar badge tooltip says "from <name>" for a received buff.
- **Tests:** `SquadBuffRules` (pure static: qualifies by rank and flag, within range, which rank to keep).

## Phase 4: Wallstride: wall-run, jump to a wall, tree perch

### The passive
- **Wallstride** is a passive in the Strides tab. It opens by itself when character level ≥ 60, Run rank 20 and Jump rank 20 (the thresholds live in `TraversalSettings`).
- `PlayerMovementSkills` computes it on the server and carries it in `MovementSkillState`.
- The Arsenal shows it locked with the three conditions until they are met.

### Surfaces
- A new marker component `ClimbableSurface { kind: Wall | Tree, maxHeight }`.
- Builders add it:
  - `StarterTownBuilder` building facades and walls, outskirts cliffs and large rocks;
  - town GameObject trees, with a `TreePerch` child at the crown;
  - the tree proxies below.
- Nothing is climbable unless marked, so fences, NPCs and Kiosks never catch a player.

### `WallRunRules.Evaluate` (pure static, tested)
It returns `CanRunUpWall` or a `CantRunUpWall` reason. Checks, in order:
1. Wallstride is open.
2. The player is not dead, casting, stunned or carrying a cast lock.
3. Run is the selected stride, or a jump is in flight.
4. The surface has `ClimbableSurface`.
5. The surface is steep: its normal is within 70°–110° of up. Shallower is ordinary NavMesh walking.
6. The approach is within 45° of head-on (travel direction · −normal).
7. Speed is at least the minimum (no wall-run from a standstill unless clicked).
8. There is a top within `maxClimbHeight`: either a NavMesh sample on top for a mantle, or a `TreePerch` point. With no top, the result is a wall-kick back down.
9. There is clearance for the capsule at the top.
10. Flux is paid (a cost per climb in `TraversalSettings`).

Every number lives in `TraversalSettings`.

### The motor
`PlayerWallRunMotor` (server-authoritative, built like `PlayerJumpMotor` and `PlayerVaultMotor`).
- **Start by click:** with Run or Jump selected, a click on a marked surface (ClimbableSurface colliders join `raycastLayers`) plans the approach.
- **Start automatically:** the auto checks run when a move or jump would end at a marked surface:
  - a route blocked by it;
  - the `IsSegmentClear` hit during jump planning, which plans a jump that catches the wall instead of refusing.
- **Phases,** one Motion Warping chain driven by the server and replayed on clients by `WallRunStartedClientRpc` (as the jump does):
  1. **Plant:** the foot meets the wall, warped to the contact point.
  2. **Run-up:** the body tilts to the wall normal, and speed scales with the Run rank.
  3. **Mantle** onto a NavMesh top (`RestoreAgent`), or **perch** at a tree point, or **kick-off** to a sampled landing behind.
- **Jump into a wall:** the jump arc is cut at the contact point and blends into Plant. Time is matched so the feet meet the wall at the same speed as the arc, with no snap.
- **Perched** (trees or high ledges with no NavMesh):
  - The agent stays off and the body is held at the perch.
  - Self and Squad buffs can be cast.
  - Melee attacks are refused.
  - Ranged skills work.
  - A ground click is a jump down through the Jump motor.
- **Death:** `ReleaseForDeath()` pushes the body 0.6 m off the wall normal. `PlayerDeathMotor.FindLanding` samples from there, so the body falls clear of the wall. It drops with the same `HeightCurve` used for a death in the air. From a perch, it drops beside the trunk.
- **Hooks:** `PlayerDeathMotor.OnKilled`, `PointAndClickPlayer.RecoverLostAgent` (skipped while wall-running or perched) and the stall watcher.
- **Hostiles:** check how a Hostile handles a target above its reach. Its melee must not hit through the height gap. If it is stuck, its existing leash returns it home. Fix only what play shows.

### Animations (stand-ins)
`WallRunAnimationSet` (Data asset, per faction like `VaultAnimationSet`) with clips:
- Plant: vault plant `AS_Vault_*`;
- Run-up: `AS_Run_Loop_*`, the root tilted by the motor;
- Mantle: a vault top segment;
- Kick-off and drop-down: jump pieces;
- Perch idle: crouch/idle.

Foot IK is off on the wall. The owner can swap clips later with no code change.

### Trees: colliders only near players
- `TreeProxyField` (World/Runtime), one per Terrain.
  - On load it buckets `terrainData.treeInstances` into a 16 m grid.
  - Each tree prototype has a `TreeProfile` (trunk radius, height, perch height) built by `FoliageAssetBuilder`.
- Every 0.25 s it gathers trees within 35 m of each player and activates pooled `CapsuleCollider` + `ClimbableSurface(Tree)` + `TreePerch` proxies. Proxies that are out of range go back to the pool.
  - On the server, it covers every player.
  - On a client, it covers the local player only, so clicks can target a tree.
  - The pool is capped, and the field allocates nothing per frame.
- `ZoneTerrainGenerator.EnsureTerrainObject` turns `TerrainCollider.enableTreeColliders` off once proxies exist.
  - Baked NavMesh is unaffected.
  - Projectiles near players still hit proxies.
- **LOD and billboards:** handled by Phase 4b. The proxy radius is read from `WorldLodSettings` and is always below the distance where a tree drops to its impostor. A validator flags any setting that breaks this, so a tree with a collider is always a full mesh.
- **Performance:** about 900 trees in Zone01, a grid lookup per player 4× per second, and at most about 60 live proxies per player. Confirm in the Profiler.

### Networking
- Remote clients follow `NetworkTransform` plus the start RPC replay.
- Late joiners get the current state, wall-running or perched, through a `NetworkVariable` pose, like `PlayerDeathMotor.netPose`.
- Test with a second client, not only as host.

## Phase 4b: LOD for every mesh drawn at a distance (owner, 17 Oct: "no matter what we need LOD", "for all meshes at a distance")

### Scope
Everything with a `MeshRenderer` or `SkinnedMeshRenderer` that can be seen from far away:
- environment: trees, bushes, rocks and cliffs;
- town buildings, walls and props;
- world objects: Kiosks, WorldDrops, furnishings;
- characters: players, Hostiles, NPCs and pets.

Left out, because each already has its own distance handling or is never far away:
- the GPU grass, which is already distance-faded by `GrassRenderer`;
- particles and VFX;
- UI and preview models (the `UIPreview` layer);
- first-person / close-up held weapons.

### Characters
Sidekick characters are assembled from parts at runtime, so their LODs can't be prefab-only.
- `CharacterLodBuilder` (Editor) makes LOD1/LOD2 versions of each Sidekick part mesh, keeping bone weights and blend shapes on LOD1 and dropping blend shapes on LOD2.
- The runtime combine step picks the matching part LOD for each level and puts the combined meshes in an `LODGroup`.
- Hostiles, NPCs and pets that are fixed prefabs get `LODGroup` variants like the props.
- Animation also costs less with distance:
  - beyond the LOD1 threshold, Animancer/Animator update at a reduced rate (an interval in `WorldLodSettings`);
  - beyond cull, `cullingMode = CullUpdateTransforms` (server-side hit logic does not depend on the bones);
  - your own character and your current target are never reduced.
- `OccluderFader` already looks up an `LODGroup` (`Rendering/Runtime/OccluderFader.cs:283`). Check that it fades every LOD's renderers, not just LOD0.

### What is true today
- Checked in the repo: only 11 prefabs under `Assets/Synty` have a `LODGroup`. Ten are piles in PolygonSamuraiEmpire, and one is `PNB_Core/SM_Env_Grass_01`.
- The trees in use (`SM_Gen_Env_Tree_01..03`, `Tree_Pine_01..03`) are single meshes with no LOD meshes in the FBX.
- `Rendering-Bible.md` already says "Nothing in the world has an LOD group." So the LODs have to be made by us.
- The vendor packs stay untouched. Everything is generated into first-party folders.

### `WorldLodSettings`
A Data asset in `Environment/Rendering/Data` that holds every distance; nothing is typed in code.
- **Per category** (Tree, Bush, Rock, Building, Prop, Small prop, Character, Creature):
  - screen-height thresholds for LOD0, LOD1 and LOD2 (impostor, trees only), and the cull threshold;
  - a decimation ratio per LOD;
  - the cross-fade mode (dither, using `PNB_Core/LODNoise` style dithering in URP).
- **Per quality tier:** a multiplier (Low / Medium / High / Ultra), applied through `QualitySettings.lodBias` and `maximumLODLevel`. The settings menu tier dictates how near or far LODs switch, which is what the owner asked for.
- **The tree collider proxy radius** lives here too (see Phase 4).

### `EnvironmentLodBuilder`
Editor tool in `Environment/Rendering/Editor`, menu **Krytical Nova ▸ Rendering ▸ Build LODs**. It runs the environment and the characters (`CharacterLodBuilder`).
1. **Find the assets:**
   - the meshes the world builders use: the `FoliageAssetBuilder` variants, the trees, bushes and rocks scattered by `ZoneTerrainGenerator`, and the Synty prefabs that `StarterTownBuilder` places;
   - every prefab registered in `DefaultNetworkPrefabs` and `GameplayPrefabsSetup.SpawnedPrefabs`.
2. **LOD1 and LOD2 meshes.**
   - First choice is Unity 6's built-in Mesh LOD generation on the model importer (6000.2+), if it is available for these models. I'll check in the Editor that it works with terrain trees and the GPU Resident Drawer.
   - Otherwise, a first-party simplifier (edge-collapse with the swatch UVs kept, since Synty colour comes from one palette texture) writes `*_LOD1.asset` / `*_LOD2.asset` meshes into `Environment/Rendering/Generated/`.
3. **Trees** also get a last level: a baked **impostor**, a crossed-quad or octahedral card textured from a render of the tree. That is what is drawn far away, the "billboard".
   - Unity Terrain only makes billboards for SpeedTree, so the impostor is our own LOD level inside the tree's `LODGroup`. Terrain trees with an `LODGroup` are supported and stay instanced.
   - The wind shader runs on LOD0 and LOD1 only.
4. **Prefab variants with `LODGroup`.**
   - It writes `<Name>_LOD.prefab` variants into `Environment/<Category>/Prefabs/`, with `LODGroup` thresholds read from `WorldLodSettings`.
   - Colliders stay on the root, so LOD switches never change physics.
   - For trees, the collider is removed from the variant entirely; the Phase 4 proxies provide it.
5. **Re-pointing the builders.**
   - `ZoneTerrainGenerator` tree prototypes and the `StarterTownBuilder` placements use the `_LOD` variants.
   - Run "Rescatter Trees and Rocks" and rebuild the town.
   - The terrain's `treeDistance` / `treeBillboardDistance` are set from `WorldLodSettings` (`treeBillboardDistance` is no longer relied on).
6. **Re-runnable:** an asset that already has generated LODs is skipped unless its source mesh changed.

### Checks
- `ValidateProjectOrganization` and a new **Rendering ▸ Check LODs** step flag any world-placed or spawned mesh (scenes plus the prefabs in `DefaultNetworkPrefabs`) without an `LODGroup`, unless it is on the exclusion list above, and an impostor distance below the tree proxy radius.
- Profiler / Frame Debugger before and after in Zone01 and the town: triangle count, draw calls, and GPU Resident Drawer batches, recorded in the Rendering Bible.

## Phase 5: docs and the Encyclopedia
- `01_Systems_Build_List.md`:
  - Rows 8, 16, 23, 24 and 27 (Status, columns, "What is left").
  - Glossary rows: Two-hit attack, Wallstride, Wall-run, Perch, Squad buff share.
  - Change log lines.
- `04_Weapon_Classes.md`: §2 slot table, §3 names, §8 as built (two-hit tier, Redline on beat, Squad Stance).
- `05_Economy_And_Social.md`: Kiosk on J, chip ids 9510–9527.
- `17_music_and_cadence.md`: the Redline rule.
- `10_core_systems_implementation_plan.md` traversal section.
- `Rendering-Bible.md`: the tree LOD finding.
- Run `build_encyclopedia.py`, read the live Encyclopedia artifact, and publish.

## Commits
One commit per phase on `claude/modest-cray-vcpw2b`, pushed with `git push -u origin claude/modest-cray-vcpw2b`. No PR unless asked.

## Verification
There is no Unity in this container. Code is checked by review and grep here; the rest needs the owner's Editor.
1. **Editor, in this order:**
   1. Progression ▸ Build Blade Skills
   2. Build Class Skills
   3. Items ▸ 5. Generate Utility Items
   4. Items ▸ 11. Build Crafting Recipes
   5. Icons ▸ Icon Library: Give Icons To All Without One, then Bake All
   6. Check Icons (must be clean)
   7. Faction HUD build
   8. Setup ▸ Player Prefab
   9. Project ▸ Validate Project Organization

   I'll add one menu, **Krytical Nova ▸ Progression ▸ Build Two-Hit Skills (all steps)**, that chains steps 1–4 and 6.
2. **Test Runner, EditMode:** all suites, plus the new `RedlineRules`, `SquadBuffRules`, `WallRunRules` and hotbar-memory padding tests.
3. **Play, host plus a second client:**
   - Cast each two-hit tier per class; the T key works and Kiosk opens on J.
   - GM `gm.chips` gives the new chips; reading one unlocks the skill.
   - Redline + a three-hit skill pressed on beat: three crits, the charge spent after the third. Pressed off the beat: spent on the first hit. Against an evasive Hostile: an evaded hit spends it.
   - In a Squad: a rank-20 Stance reaches a member within 20 m and not one at 25 m. A second caster restarts the timer without stacking. A received buff survives a weapon swap. The ranged Stance makes the member move faster.
   - At level 60 with Run and Jump at 20: click a town wall and run up and mantle; jump into a wall and catch it; jump into a tree and perch; get killed on the wall or in the tree and fall clear; the second client sees all of it. Below the requirements, a clear refusal reason.
   - Profiler in Zone01: the tree proxy field costs well under 0.1 ms and allocates nothing per frame.
   - LOD: walk away from a tree and see LOD0 → LOD1 → impostor with a dither fade, no popping.
   - Change the quality tier and see the switch distances move.
   - Triangle and draw-call counts drop against the recorded baseline.
   - A tree you can jump into is always a full mesh.
