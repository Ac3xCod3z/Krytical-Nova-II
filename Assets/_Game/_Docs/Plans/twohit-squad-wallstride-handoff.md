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
| LOD part 1 | `WorldLodSettings`, `CharacterLod`, `GeneratedLod`. **Not wired in yet.** `EnvironmentLodBuilder` (the mesh simplifier, LODGroups and quality biases) is still to write. The plan is Phase 4b of the plan file. |

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
6. Play as host plus a second client, following the Verification list in the plan file. **Record it with Unity Recorder** (the owner asked for a video of the testing).

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
