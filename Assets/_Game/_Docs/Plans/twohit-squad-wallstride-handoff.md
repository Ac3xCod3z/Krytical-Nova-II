# Hand-off: two-hit attacks, Squad Stances, Redline on the beat, Wallstride, LOD

Written 17 Oct 2026 by the cloud session that built this on branch `claude/modest-cray-vcpw2b`, for the session on
the owner's computer that carries it on. **Nothing here has been compiled or run yet.** The cloud container had no
Unity, so expect compile errors on first open and fix them before anything else.

## What is on the branch

| Commit | What |
| --- | --- |
| Merge `music/cadence` | Cadence M0 to M4 brought into the main line. Conflicts were resolved by hand; the server build now uses `BuildSceneOrder.Shipped` too. |
| Two-hit attacks | Slots `Lv1TwoHit`, `Lv30TwoHit`, `Lv60TwoHit`, `Lv80TwoHit` for all nine classes (names in `04_Weapon_Classes.md` §3). Skill Chips 9510–9527 for the level 60 and 80 ones. T is hotbar slot 15; Kiosk moved to J. Menu **Progression ▸ Build Two-Hit Skills (all steps)**. |
| Redline on the beat | `RedlineRules`. A three-hit skill pressed on Cadence while Redline is charged lands three crits. An evaded hit spends the charge. |
| Squad Stances | `SquadBuffRules`, `ISquadBuffTargets` (implemented by `PlayerParty`). From rank 20, a Stance reaches Squad members within 20 m (`SquadSettings`). The same Stance never stacks; a recast restarts the timer at the higher rank. The Move Speed stat now actually moves the agent (`MovementSettings` limits 0.5x–1.5x), which makes Boots' Move Speed count too. |
| Wallstride | `PlayerWallRunMotor`, `WallRunRules`, `WallstrideSettings`, `ClimbableSurface`, `TreeProxyField`. Opens at level 60 with Run and Jump at rank 20. Covers clicking a wall or tree, an automatic climb on a run, a jump that catches a wall, perching in trees, and death on a wall. Hostiles and melee skills can't reach a player far above them. |
| LOD part 1 | `WorldLodSettings`, `CharacterLod`, `GeneratedLod`. **Not wired in yet.** `EnvironmentLodBuilder` (the mesh simplifier, LODGroups and quality biases) is still to write. The plan is Phase 4b of `twohit-squad-wallstride-plan.md`. |

## Order to run on the owner's computer

1. Open the project and let it compile. Fix every error.
2. **Krytical Nova ▸ Setup ▸ Player Prefab**. This adds `PlayerWallRunMotor` with its settings and stand-in clips.
3. **Krytical Nova ▸ Progression ▸ Build Two-Hit Skills (all steps)**. It runs these in order:
   - the blade and class builders
   - utility items
   - crafting
   - icons
   - Check Icons
   - the faction HUD
4. **Krytical Nova ▸ Project ▸ Validate Project Organization**.
5. Test Runner: run every EditMode test. The new ones are `RedlineRulesTests`, `SquadBuffRulesTests` and `WallRunRulesTests`. Updated ones are `SkillChipTests`, `WeaponClassTests`, `FactionHudTests`, `KeyBindingsTests`, `MovementSettingsTests` and `IconAssetsTests`.
6. Play as host plus a second client, following the Verification list in `twohit-squad-wallstride-plan.md` (next to this note). **Record it with Unity Recorder** (the owner asked for a video of the testing).

## Next: disintegrate a player killed off the ground (owner, 17 Oct 2026)

- **When:** a player killed on a wall, perched in a tree, or in the middle of a jump disintegrates instead of falling. Nothing is left where they were, and they go straight to the respawn timer.
- **Their pet disintegrates with them.**
- **Look:** different from the Hostiles' dissolve (`HostileCorpse` + `HostileDissolve.shader`). The owner wants it more unique. Build it from:
  - Cartoon FX (`_ThirdParty/JMO Assets/Cartoon FX Remaster`, `_ThirdParty/Cartoon FX Pack v2`)
  - All In 1 Shader (`Plugins/AllIn1ShaderNodes`, the Shader Graph nodes)
  - FEEL (`_ThirdParty/Feel`)
- **Hook:** `PlayerDeathMotor.OnKilled`. When `PlayerWallRunMotor.IsBusy` or `PlayerJumpMotor.IsBusy`, play the disintegrate on every machine instead of the fall:
  - through an RPC, and through `netPose` for late joiners;
  - hide the body and the pet until `Revive`.
- Look at it in the Editor while it is built; it can't be judged blind.

Still to do after that: finish LOD (Phase 4b), then the docs and the Encyclopedia (Phase 5).

## Later: walls as a second ground (owner, 17 Oct 2026) — not built, a future iteration

Wallstride today runs **up** a surface and over its top (or into a tree). The owner's goal for a later iteration is a
full system where a wall is a second plane to move on: run all around a wall (up, across, along it), and use the game's
systems there as on the ground (skills, targeting, buffs, combat). Nothing of that is built or planned in detail yet;
today's motor (one Motion Warping route per climb) is the first step and should not be stretched to fake it.

## Wallstride as built on the owner's computer (17 Oct 2026)

- The climb is one KINEMATION Motion Warping route of legs (plant, climb, over the edge in two legs, or a kick-off or a
  drop as arcs), each eased so the speed carries across every hand-off; Animancer fades clip into clip on the same clock.
- Its clips are in-place copies of the stand-ins (`Characters/Player/Animations/Wallstride`, made by
  `WallstrideClipCopies`): the vendor run and the vault copies carry their travel in the pose, which walked the body 4 m
  into the wall. The originals are untouched.
- The body stays `wallStandOff` (0.38 m) out from the face's outermost part; measured on every bone, every frame: hands and
  feet come within 5 to 12 cm and never pass it. Max climb speed 8 m/s (was 14).
- A jump catches a surface up to 65° from head-on (`maxJumpApproachAngle`), a run 45°; the plant is longer the wider the
  catch, so the turn to face the wall eases in.
- No climb starts, ends or lands on a building's indoor NavMesh. Vault goes up to 2.2 m, so it meets Wallstride's 2.2 m.
- Still to do: real climbing clips (the owner chose to keep stand-ins for now); roofs solid and walkable with jump links;
  the disintegrate for deaths in the air, on a wall, in a tree or on a roof.
