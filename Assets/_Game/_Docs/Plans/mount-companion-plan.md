# Mount Companion plan

**Status: proposed 13 Nov 2026, waiting for the owner's review.** Owner's scope (13 Nov 2026, final answers on the day
list): phases 1 and 2 first (follow + hunger + FREE stance; companion HP + Mount Stim + knock-out); the owner tests them
before phases 3 to 5 are built. So phases 1-2 are written out in full below; phases 3-5 are an outline only.

Row 43 of `../Reference/12Sky/01_Systems_Build_List.md` (Pets and mounts; the Mount rows are glossary lines 93-110).
Item 19 of the owner's day list of 13 Nov 2026. Inspired by the chocobo companion of FFXIV, kept our own.

## What it is for

You can call your selected mount out to walk beside you instead of riding it. It follows you at a walk or a run, keeps
giving its stats, and gets hungry faster while it is out; when its hunger bar runs dry it goes home. It has its own HP,
can be knocked out (then it rests for a while before it can be called again) and is healed with a **Mount Stim**. Later
it fights with you in one of three combat stances and earns a little more mount XP than riding does.

## 1. What exists today (research)

All line numbers checked on 13 Nov 2026 in this checkout. The day list's WHERE pointers are right:
`AutopilotController.cs` Step L121, Fight L226, Pick L277, Allowed L290 have not moved.

**The mount rules** (`Gameplay/Mounts`):

- `MountSettings.cs` holds every number. Growth: `xpToFullGrowth` 100,000 (L15), `xpPerHostileKill` 0.04 (L17).
  Hunger: `hungerDrainPercentPerMinute` 0.05 (L21), "only while ridden"; warning at 20 % (L23); the server writes hunger
  to the owner every 10 s (L25). Riding speed walk 6-9 m/s, run 20-34 m/s by growth (L29, L32).
- At 0.05 % a minute a full bar lasts **2,000 minutes (33 h 20 min) of riding**. One Mount Feed (item 9013, 50 Creds at
  the Apothecary, a placeholder) fills it back to 100 % (`MountRules.Feed`, L103).
- `MountRules.cs`: three stages at 0 / 50 / 100 % growth (L41-60); each of the mount's two stats pays 1/100 of its
  maximum per 1 % of growth (`StatBonus`, L63); a starving mount (0 % hunger) gives no stats, earns no XP and rides at
  its slowest (L33 of `MountState.cs`, L67 of `MountRules.cs`). `Drain` keeps the fraction in a carry so slow ticks lose
  nothing (L92-100).
- `MountState.cs`: XP in hundredths, hunger as a deficit in thousandths of a percent (0 = full). A new mount is all
  zeroes. This "deficit, 0 = full" style is what lets new saved fields default safely (section 4.7).
- `PlayerMounts.cs` decides everything on the server body: select (L209), ride toggle (L218), dismount at once on
  death, a jump, a vault or a wall run (L230, L322, L356), hunger drain only while `phase == Riding` (L358-379), kill XP
  for the selected mount ridden or not (L383-389), the save under key `mounts` (L613-648). Nobody comes back into the
  world in the saddle (L636).
- **Important for item 19:** the two stats already apply whenever the mount is *selected and fed*, ridden or not
  (`Show`, L569-577, no phase check). The riding speed applies only while riding (L582). So "the riding bonus still
  applies while out" is already true for the stats today; see owner pick 4.
- `MountRider.cs` draws the mount model under the rider on every view (not on a server body, L75), with the
  appear / vanish effect (`MountMaterialiser`) and the stage clips idle / walk / run (`MountStageLook`, L25).
- `MountWindowController.cs` (UI, key N): four boxes, a RIDE / GET OFF button (L184, L286).

**How the DOTS game carries mounts** (no `Mounts/Entities` folder yet):

- `Core/Runtime/Bodies/IBodyMounts.cs`: `MountOpKind` Select = 1, Ride = 2 (L6-10); the body interface the systems
  read; `IMountOwnerLink` for the owner-only boxes.
- `Gameplay/Persistence/Entities/Runtime/MountSystems.cs`: `MountOpRpc { Op, Box }` (L13), `MountBookRpc` (four XP and
  four hunger values, L19), `MountServerSystem` runs requests on the server body and copies the shown state into the
  player ghost's `PlayerMountTag` (L77-117); `MountClientSystem` mirrors it on every view (L123-159).
- `PlayerMountTag` (`Player/Entities/Runtime/PlayerComponents.cs` L300): Selected, Phase, Growth, Fed, Running,
  EndsRunning, all bytes, on the player ghost.
- `PlayerRoleTable.cs`: `PlayerMounts`, `MountFeeding`, `MountRider` are rule behaviours (L78) kept on remote views
  (L91).

**Pets** (the follow and XP pattern): `Gameplay/Items/Runtime/Visuals/PetFollower.cs` is presentation only. Each view
damps the pet towards a spot by its owner's head (`followSmoothTime` 0.2 s, snaps when more than 8 m away, L25-30) and
picks walk or run from the owner's speed. The pet is not a ghost; only growth and fed ride `PlayerPetTag`. That is fine
for a floating pet that nothing can hit. It is **not** enough for a companion that has HP, can be hit in PvP and will
later fight, so the companion must have a server position (section 2).

**Autohunt** (`Gameplay/Autopilot/Runtime/AutopilotController.cs`): runs on the owner's own view. `Fight` (L226) keeps
the target while `Allowed` (L290: alive, inside the zone or radius, the level-gap and held / grey / red / Bruiser
filters) holds, else `Pick` (L277) takes the nearest allowed Hostile view. Phase 3 reuses these rules, on the server.

**Threat** (`Gameplay/Hostiles/Entities/Runtime/HostileMindSystems.cs`): `HostileThreatSystem` (L19) turns damage,
heals (half) and taunts (lift above the top) into a `ThreatEntry` per **GhostId** (L48-52, buffer in
`HostileComponents.cs` L97), decays it, keeps six, and switches target above 110 % in reach and 130 % out of reach.
Because threat is keyed by GhostId, a companion that is its own ghost can sit in that table as it is (phase 4).
Hostiles have single-target strikes only (`HostileAttackSystem.cs`: no area attack today).

**Items:** Mount Feed 9013 (`ItemUseEffect.FeedMount` = 11). The **Rider Booster** (9503, built 12 Nov 2026) already
doubles the mount XP of your kills for an hour: that *is* the "Double Mount XP item" of the day list (pick in phase 5).
HP Stims 9001-9009 and % Stims 9041-9049 heal the player only. Free today: item id 9014, `ItemUseEffect` 14.

### What we borrow from FFXIV's chocobo, and what we change

| FFXIV companion | Krytical Nova |
|---|---|
| The mount you ride is also the companion. | Same: the selected mount box. Riding and being out are one or the other. |
| A summon item sets a timer; more items add time. | No summon item. The **hunger bar is the only timer**; Mount Feed refills it. |
| Free / Defender / Attacker / Healer stances. | FREE / TANK / DPS / HEALER, set in the Mount window. One at a time. |
| Not in PvP. | **FREE is allowed in PvP**; the three combat stances are PvE only. |
| Knocked out: just summon again. | Knocked out: it goes home and **rests (cooldown)** before it can be called. |
| Levels and a skill tree from battle XP. | Our growth meter and three stages; XP like pets, a bit more when it fights beside you. No skill tree. |
| Heal it with your own spells. | Only a **Mount Stim** heals it; your Stims and skills never do (phases 1-2). |

## 2. Design that fits the layers

- **Layers.** New code goes in `Gameplay/Mounts/Entities/` (Simulation): `Runtime`, `Editor`, `Tests`, assembly
  `KryticalNova.Mounts.Entities`. It may use Core, Networking, Mounts, Player.Entities and (phase 2) Combat.Entities,
  all its own layer or lower, one way. Hostiles never learn about the companion; in phase 4 the companion writes a
  `Taunt` brain event into the Hostile's buffer (Mounts.Entities -> Hostiles.Entities, one way; Mounts already uses
  Hostiles). The Mount window (UI, Presentation) reads `PlayerMounts` as today. `dependency_map.py --check` and
  `compile_check.py` (and `--server`) at every step.
- **The server decides.** Summon, dismiss, follow, hunger, HP, knock-out and the cooldown are all server rules. The
  owner's view only asks (through `MountOpRpc`) and presents. **No prediction** of the companion anywhere, not even on
  the owner's view: it is an interpolated ghost for everyone. A summon shows about 100-150 ms after the press, hidden by
  the appear effect.
- **Its own ghost, not part of the player ghost.** It moves on its own, has HP others see, and must have its own
  GhostId for hits and threat. Putting it on the player ghost would send its position at the player's 30 Hz rate even
  when it stands still and tie its relevancy to the player's. The ghost has **no `GhostOwner`**, so the
  one-owned-ghost lint (`OneOwnedGhostLintTests`) stays true; the owner is a field.
- **A light server body, like a Hostile's.** From phase 2 the companion needs HP that the game's own hit rules can
  reach (enemy players' area skills in PvP, later Hostile strikes). The proven way in this game is the Hostile's: a
  server-only body GameObject with a `CombatEntity` and a collider, linked to its ghost and registered in
  `CombatTargetRegistry` (as `HostileBodyLinkServerSystem` does). Phase 1 needs no body; phase 2 adds it. The perf
  phase can later move bodies to pure entities together with the Hostiles' (section 6).
- **`PlayerMounts` changes only a little.** It is the server body's rule behaviour in the DOTS game, and it owns the
  book (XP, hunger) and the save. It gets: an "out" state beside the ride phase, hunger drain while out, the companion
  HP lost per box, the knock-out time, the stance. Everything that moves, spawns or replicates is new code in
  `Mounts/Entities`. The pure rules go in a new `CompanionRules` (Mounts/Runtime, no networking, testable).

### The companion ghost (`MountCompanionGhost`, interpolated, made by a builder)

| Component | GhostFields | Notes |
|---|---|---|
| `LocalTransform` | position, quantised 100 (1 cm) | rotation sent as the yaw below, no scale |
| `CompanionView` | `int OwnerGhostId`, `byte Box`, `byte Stage`, `ushort YawCentideg`, `byte SpeedStep` (0.25 m/s steps), `byte State` (Out, Warping, KnockedOut, Leaving), `byte Stance`, `ushort HpPermille` | all a view needs to draw, animate and show the HP bar |
| `FxEvent` ring (capacity 4) | Appear, Vanish, Warp, Hurt, KnockedOut | one-off moments as ring entries with a tick, never a flipping bool |
| server only: `CompanionFollow` + `DynamicBuffer<TrailPoint>` (16) | none | follow state and the owner's breadcrumb trail |

- The prefab name and GUID are the ghost type and never change. The builder `MountCompanionAssetsBuilder`
  (`Mounts/Entities/Editor`, safe to rerun) makes the prefab and its holder in the ghost subscene; then re-bake and check
  the re-bake for no diff.
- Send rate 15 Hz, importance 40 (below Hostiles' 60). A companion at rest sends nothing.
- Relevancy (ships OFF): when on, a companion is relevant to a connection exactly when its owner's ghost is
  (`RelevancyFill` follows the owner). Its tests use their own reason number, never 1.
- The owner's book gains per-box HP lost and the cooldown end (owner-only, through `MountBookRpc`), so the Mount
  window can show HP and the rest timer of a mount that is at home.
- `PlayerMountTag` gains one byte `Out` (1 = this player's companion is out), so every view knows not to draw a mount
  under the rider, and the window knows the button state.

## 3. Phase 1: follow + hunger + FREE stance (detail)

**Summon and dismiss.**
- New `MountOpKind` values: `Companion = 3` (toggle out / home), `Stance = 4` (the box byte carries the stance). Same
  `MountOpRpc`, same `CallKind.Move` limit as the Ride key.
- A key (proposed **B**, rebindable, beside V and N) and a CALL / SEND HOME button in the Mount window.
- Refused with a message (the existing `Notify`): no mount selected; mount not built; starving ("Feed it first");
  knocked out ("Resting: 3:12 left"); dead; mid-jump, vault or wall run; inside a Crew Hall or other instance where
  mounts are not allowed (the same rule as riding, `RideBlockedReason`).
- Riding and out are exclusive. V while out: the companion runs to you, vanishes, and the normal mount-up plays.
  Calling it out while riding: you get off first (the normal dismount) and it forms beside you as your feet land.
- Choosing another box in the window while out sends it home first (as riding refuses today, L212).
- It goes home (no cooldown) on your death, on a map change, travel or Recall, on logout, and when hunger hits 0.
  It never comes back out by itself after a relog (as nobody comes back in the saddle, L636).

**Follow (server, every 3rd tick, 10 Hz).** All numbers in `MountSettings`, new header "Companion"; PROPOSED:
- The owner's ghost position is sampled every 0.25 s into a 16-point trail (4 s of path). The player's path is on the
  ground, so walking the trail keeps the companion on walkable ground with **no NavMesh query while moving**.
- **Rest spot:** 2.5 m behind and 1.5 m to the left of the owner (the pet floats on the right), facing the owner's
  way. Checked once with `NavMesh.SamplePosition` (1 m) when the owner stops; if off the mesh, the nearest trail point.
- **Keeps** at least 1.2 m from the owner. Other players and Hostiles pass through it (no blocking collider).
- **Speed:** the owner's speed, up to the mount's own run speed at its growth (20-34 m/s). Walk or run clip from that
  speed as the pets do (walk below 7 m/s).
- **Catch-up:** more than 8 m behind its spot, it goes 25 % faster until it is within 3 m.
- **Warp-back:** more than 30 m away, or the owner moved more than 10 m between two trail samples (portal, recall,
  GM teleport), or no progress for 3 s: it vanishes and forms at the rest spot (Warp ring entry, the materialise
  effect). At most one warp per 2 s.
- **Drawn at the real ground height:** each view snaps the model's feet to the ground under the interpolated position
  (a downward ray, as the player's foot grounding does), so it never floats or sinks on a slope.

**Hunger while out.** `PlayerMounts.Update` drains while `Riding` **or** out. While out the rate is
`companionHungerDrainPercentPerMinute`, PROPOSED **0.5 % a minute**: a full bar lasts 200 min (3 h 20 min) out, ten
times the riding rate (pick 1). The 20 % warning and the 10 s book writes work as for riding. At 0 % it says
"Your Brambleback is starving and went home." and goes home; feeding it lets you call it again.

**Stats while out.** Unchanged: the two stats whenever selected and fed (pick 4). No riding speed on foot.

**FREE stance.** The only stance in phases 1-2, and the default. The companion never attacks, never takes threat and
is never the target of a Hostile. Allowed everywhere riding is allowed, PvP zones, duels and Crossfire included. The
stance row in the window shows TANK / DPS / HEALER greyed with "Coming later".

**Presentation (every view).** A pooled `CompanionPresenter` per companion ghost: the stage model from
`MountDefinition.Stage(stage).model`, its idle / walk / run / idle extras, clip speed from `SpeedStep` as
`MountRider` scales it, the mount's own appear / vanish look and summon / vanish sounds (`soundSet`). The owner's name
plate gets a small mount line ("Brambleback, out", hunger bar) only on the owner's own screen; others see the model
only (phase 2 adds an HP bar for everyone). Uses the mount's 2K textures; no new art.

## 4. Phase 2: companion HP + Mount Stim + knock-out (detail)

**4.1 HP.** Max HP = the owner's max HP x (0.6 + 0.4 x growth / 100), PROPOSED: 60 % of yours at 0 % growth, 100 %
at full growth. It scales with your level and needs no curve of its own. Its defence is the owner's defence (no gear
of its own). HP is held as **permille lost** per box (0 = full), so a level-up or a growth step never hurts it.

**4.2 What hurts it in FREE** (pick 2, recommended a): enemy players' area skills that cover it in PvP; Hostiles
never (no threat in FREE, and they have no area attacks). A friendly player's skills never hurt it. GM:
`gm.mount.hurt <percent>`, `gm.mount.heal`, `gm.mount.rest <seconds>`, with buttons in the GM window's Mounts tab.

**4.3 The server body.** On summon the server takes a pooled body (`CompanionBody`: `CombatEntity`, capsule collider
sized from the stage's bounds, faction of the owner) and links it to the ghost, as Hostile bodies are linked. The body
stands where the follow put the ghost; the ghost shows the body's HP. On dismiss or knock-out the body goes back to the
pool. A hit on it writes a `Hurt` ring entry (the hit number and a flinch on every view).

**4.4 Regen.** None while out. While at home it regains 2 % of max HP a minute (PROPOSED), counted from the saved
time, so a relog cannot skip it and an offline rest counts.

**4.5 Knock-out.** At 0 HP: a KnockedOut ring entry (the mount's vanish with a stagger, no corpse), it goes home, and
the box rests for `companionKnockOutSeconds`, PROPOSED **300 s** (pick 3). When the rest ends it is at 30 % HP and
may be called again (regen continues from there). You may still **ride** a resting mount (it is resting from the
fight, not hurt): pick 3 c is the stricter option. The window shows the rest timer on the box.

**4.6 Mount Stim (new item).**
- Item **9014** "Mount Stim", Common, stack 99, glyph "MOUNT HP", icon from the Icon Library (its own picture).
- `ItemUseEffect.HealMount = 14`; one handler `MountStimUse : IItemUseHandler` in `Mounts/Runtime`, beside
  `MountFeeding` (one handler per effect). A new row in `PlayerSystemsSetup` and in `PlayerRoleTable` Rules: real on
  the server body, a stub on the own view, stripped on other views.
- Heals **40 % of the companion's max HP** (PROPOSED) on the selected mount, out or at home. Never heals the player and
  is never drunk by Autopilot's Stim thresholds. Refused while knocked out ("Resting"), when at full HP, with no mount.
  10 s between two.
- Sold by the Apothecary, PROPOSED 60 Creds (a placeholder, next to Mount Feed's 50). Bought with `PlayerWallet` as
  every shop item is; no new Creds source or sink.

**4.7 Saves.** `MountSaveEntry` (`MountNet.cs` L106) gains `hpLost` (permille, 0 = full) and `restUntil` (UTC seconds,
0 = not resting); `MountSave` gains `stance` (0 = FREE). These are **added fields**, and each one's missing value
(0) means the right thing: full HP, not resting, FREE. No existing field changes meaning, so **no `IMigratingSave`**
is needed and `version` stays 1. "Out" is never saved. Still one document (`mounts`, character scope), so no
`SaveTogether`. Proved by a test that loads a save written before this change.

**4.8 UI (Mount window).** On the selected box: an HP bar (green, under hunger), the rest timer when resting. In the
footer: CALL [B] / SEND HOME [B] beside RIDE [V], enabled from the same reasons the server uses. Under the boxes: the
stance row (FREE lit; the other three greyed in phases 1-2). The detail goes in tooltips, not in the boxes.

## 5. Phases 3-5 (outline only, after the owner approves 1-2)

- **Phase 3, DPS and HEALER (PvE only).** In a PvP zone, a duel or Crossfire the stance falls back to FREE with a
  message, and comes back when you leave. Target logic: the `Allowed` filters of `AutopilotController` (L290) moved into
  a pure `HuntFilter` both use, run on the server against the Hostiles' entity data, inside 12 m of the owner; your
  current target first, else the Hostile attacking you, else nothing (it never starts a fight alone). DPS: strikes on
  its own timer, damage from the owner's attack x a stance factor, a small self damage buff, its damage adds **no
  threat** (never aggro). HEALER: no damage; a heal over time on you and a regen buff, never threat (the heal is
  removed from the Hostile's heal-threat sum). Kill credit stays the owner's.
- **Phase 4, TANK (PvE only).** A taunt every N s that writes `BrainEventKind.Taunt` for the companion's GhostId into
  the Hostile's events (`HostileMindSystems.cs` L50), a self defence buff, and the Hostile's sense data learns
  companion entries so it can chase and strike the companion body. Strikes on it are rolled by the existing
  `HostileStrikeBridge` rules.
- **Phase 5, XP and items.** A kill with the companion out in a combat stance and having helped (hit, healed or taunted)
  gives the selected mount 1.5x the kill XP (PROPOSED; riding stays 1x), like the pets' rule of XP from kills. The
  Double Mount XP item: reuse the Rider Booster (9503) rather than add a second item (the owner picks then).

## 6. Performance budget

- **Cap:** one companion per player (it is your selected mount). Worst case The Scar, 1,000 players, 1,000 companions.
- **Server per companion:** follow at 10 Hz in a Burst `ISystem` over the trail buffer, no NavMesh while moving, one
  `SamplePosition` per stop: about 1-2 microseconds per companion per update, about 1-2 ms a second for 1,000. The
  phase 2 body sync is a managed bridge like the Hostiles' (about 5 microseconds per companion out); it moves to
  entities in the perf phase with them. Hunger is the existing `PlayerMounts.Update` path, no new cost.
- **Bandwidth:** position (delta-compressed), yaw, speed step and state: about 10 bytes a snapshot when moving at
  15 Hz, about 150 bytes a second per moving companion in view, 0 at rest. 30 moving companions in view: about 4.5 KB/s
  to one client. Ring entries are rare.
- **Client:** one skinned mount model per companion in view. `companionDrawCap` PROPOSED 30 nearest drawn in full,
  beyond 60 m the pose is not updated (Animancer culling); further ones hidden. Pooled presenters, no per-frame
  allocations. Checked against the performance log (60 fps target), never guessed.

## 7. Files

**New:**
- `Gameplay/Mounts/Runtime/CompanionRules.cs` (pure: follow spot, catch-up, warp test, HP, regen, rest, Stim heal).
- `Gameplay/Mounts/Runtime/MountStimUse.cs` (the item handler).
- `Gameplay/Mounts/Entities/Runtime/KryticalNova.Mounts.Entities.asmdef`
- `Gameplay/Mounts/Entities/Runtime/CompanionComponents.cs` (`CompanionView`, `CompanionFollow`, `TrailPoint`).
- `Gameplay/Mounts/Entities/Runtime/CompanionServerSystems.cs` (spawn / despawn from the body's out state, trail,
  follow, warp, ring entries; phase 2: body link and HP sync).
- `Gameplay/Mounts/Entities/Runtime/CompanionPresentSystem.cs` + `CompanionPresenter.cs` (client, pooled).
- `Gameplay/Mounts/Entities/Editor/MountCompanionAssetsBuilder.cs`; prefab
  `Gameplay/Mounts/Entities/Prefabs/MountCompanionGhost.prefab` and its holder in the ghost subscene.
- `Gameplay/Mounts/Entities/Tests/Editor/` (`CompanionRulesTests`, `CompanionWorldTests`, `MountSaveCompanionTests`).
- `Gameplay/Mounts/Prefabs/ServerBody/CompanionBody.prefab` (phase 2).

**Changed:**
- `Core/Runtime/Bodies/IBodyMounts.cs` (two `MountOpKind` values; `ShownOut`, HP and rest on the interface).
- `Core/Runtime/ItemUseEffect.cs` (`HealMount = 14`).
- `Gameplay/Mounts/Data/MountSettings.cs` (Companion header: every number above).
- `Gameplay/Mounts/Runtime/PlayerMounts.cs` (out state, drain while out, HP lost, rest, stance, save fields, refusals).
- `Gameplay/Mounts/Runtime/MountNet.cs` (`MountBook` and `MountSaveEntry` fields).
- `Gameplay/Mounts/Runtime/MountRider.cs` (no mount model under the rider while out).
- `Gameplay/Persistence/Entities/Runtime/MountSystems.cs` (`MountBookRpc` HP and rest; `PlayerMountTag.Out`).
- `Gameplay/Player/Entities/Runtime/PlayerComponents.cs` (`PlayerMountTag.Out`).
- `UI/Runtime/MountWindowController.cs` (HP bar, rest timer, CALL button, stance row).
- `Gameplay/Items/Editor/UtilityItemGenerator.cs` (Mount Stim row), `Gameplay/Economy/Editor/VendorStockBuilder.cs`
  (Apothecary), `Tools/Editor/Setup/PlayerSystemsSetup.cs`, `PlayerRoleTable.cs`.
- GM commands and the GM window's Mounts tab; the key binding (B) in the input settings.
- Docs: `01_Systems_Build_List.md` (row 43, glossary rows Mount Companion, Companion stance, FREE stance, Mount Stim,
  Knock-out rest, change log), `05_Economy_And_Social.md` (the Stim's price), Encyclopedia rebuild and publish.

## Evals (written and approved before any code)

Phase 1:
- [ ] Follow math: the rest spot, the 1.2 m gap, catch-up above 8 m, warp above 30 m or a 10 m jump in the trail, one
  warp per 2 s. Proved by: `CompanionRulesTests` (each shown failing once).
- [ ] Hunger drains at the out rate only while out, at the riding rate only while riding, never at home; at 0 it goes
  home. Proved by: `CompanionRulesTests` + `CompanionWorldTests.StarvingGoesHome`.
- [ ] Refusals: no mount, starving, dead, airborne, riding exclusive, another box while out. Proved by:
  `CompanionWorldTests` (server world).
- [ ] Two clients: a summon appears on both clients within 0.1 m of the server's spot after interpolation; walking,
  running and a Recall look the same on both; dismiss removes it on both. Proved by: `CompanionWorldTests` in a
  `KnTestWorld` (server + two clients), then the second-client test in Play.
- [ ] One owned ghost per connection still holds. Proved by: `OneOwnedGhostLintTests`.
- [ ] Drawn on the ground on slopes and stairs in Zone01 (spots picked in edit mode beforehand). Proved by: owner in
  Play, screenshots / GIF.

Phase 2:
- [ ] HP scales with the owner's max HP and growth; a level-up keeps the share lost. Proved by: `CompanionRulesTests`.
- [ ] An enemy player's area skill in PvP hurts it; a friendly one and Hostiles in FREE never do. Proved by: second
  client in Play (Player2 enemy faction).
- [ ] Knock-out at 0 HP: goes home on both clients, cannot be called for 300 s (a relog does not skip it), comes back at
  30 %. Proved by: `CompanionWorldTests.KnockOutRests` + `MountSaveCompanionTests.RestSurvivesRelog`.
- [ ] Mount Stim heals 40 % of the companion only, never the player, refused while resting or full. Proved by:
  `CompanionRulesTests` + `CompanionWorldTests.StimHealsOnlyTheMount`.
- [ ] Saves: a `mounts` save written before this change loads with full HP, no rest, FREE, nothing lost (XP and hunger
  equal). Proved by: `MountSaveCompanionTests.OldSaveLoads`; Check Saves Against This Build on a copy of the real saves.
- [ ] Mount Stim item: Check Icons clean, Apothecary sells it, `PlayerRoleTableTests` green. Proved by: `kn_check`,
  Check Icons, the lint test.
- [ ] Performance: 30 companions out (bots) on the measure server and in view, perf log at the target. Proved by: the
  perf log, never on the real server.

## Steps (one PR each)

- [ ] 1. `CompanionRules` + the `MountSettings` Companion header + rules tests. Evals: follow math, hunger rates.
- [ ] 2. The ghost, its builder and the server follow / spawn systems; `PlayerMounts` out state and drain;
  `MountOpKind.Companion`. Evals: refusals, hunger, two clients (KnTestWorld), lint.
- [ ] 3. Presentation (presenter, ground snap, appear / vanish, clips) + the Mount window CALL button and FREE row + the
  key. Evals: two clients in Play, ground on slopes. **Owner tests phase 1 on two clients and approves.**
- [ ] 4. HP, the server body, regen, knock-out and rest, save fields, GM commands. Evals: HP, area hit in PvP,
  knock-out, saves.
- [ ] 5. Mount Stim (item, handler, Apothecary, icon) + the window's HP bar and rest timer. Evals: Stim, item checks,
  performance. **Owner tests phase 2 on two clients and approves.** Then the Encyclopedia update.
- [ ] 6-8. Phases 3, 4, 5, each planned in detail here first and approved before code.

## The owner's picks

Multiple choice, the recommended option first. Answers recorded here with the date.

1. **Hunger while out:** a) 0.5 % a minute, a full bar lasts 3 h 20 min out (10x riding); b) 0.2 % a minute, 8 h 20
   min out (4x riding); c) 1 % a minute, 1 h 40 min out (20x riding).
2. **What can hurt the companion in FREE stance:** a) only enemy players' area skills in PvP (plus GM for tests);
   b) also enemy players clicking it and hitting it directly in PvP; c) nothing in FREE: its HP only matters in the
   combat stances (then phase 2 is tested with the GM commands alone).
3. **Knock-out rest:** a) 5 minutes, back at 30 % HP, it can still be ridden while resting; b) 2 minutes, the rest
   the same; c) 5 minutes and it cannot be ridden either until the rest ends.
4. **The two stats while the mount is at home:** a) keep today's rule: the stats apply whenever the mount is selected
   and fed, ridden, out or at home (the companion adds nothing to stats); b) change it: the stats apply only while you
   ride it or it is out, so a mount left at home gives nothing.

## Done when

Each phase is tested on two clients (`KnTestWorld` tests and a second client in Play) and the owner approves it in the
game. For phases 1-2: the companion follows, catches up and warps back on both screens, drains hunger at the picked
rate and goes home at 0, is hurt and knocked out by the picked sources, rests the picked time, is healed only by the
Mount Stim, an old save loads unchanged, the perf log holds the target, and the Encyclopedia shows the new rows.

## As built

(Empty until a step lands.)
