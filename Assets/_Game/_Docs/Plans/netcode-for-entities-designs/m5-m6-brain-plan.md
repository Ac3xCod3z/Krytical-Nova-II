# M5 / M6: the Brain (new monster AI) and the same Brain for world NPCs

Author: planning session "M5/M6 brain plan", 5-6 Nov 2026, for the Master (Nova) and the owner's review. Planning and research
only: no game code written, nothing compiled or run. Builds on `monster-ai-design.md` (29 Oct, 20 sections), which stays the
detailed component reference; this plan **changes** that design where the owner's new goals ask for it (section 4) and adds the
NPC use, the numbers and the step list.

Tags: **[code]** read in the NGO game or the clone with file:line; **[web]** from research, source in section 13; **[U]** not
verified, must be checked before code; **to confirm** = was a working pick; the owner confirmed every pick (Q1-Q8 = A) on 6 Nov 2026.

Owner's words (6 Nov 2026): learn how Emerald does it, then decide how we do it; the brain's framework must also run regular
world NPCs later; monsters must be **smarter and respond faster** than Emerald ("it felt laggy"); research how others do mob AI
at scale and living-world NPCs.

---------------------------------------------------------------------------------------------------

## 0. The plan in one screen

| # | Choice | Recommended | Instead of |
|---|---|---|---|
| D1 | Brain style | **Modes + scores**: a small state machine of modes (Idle, Patrol, Alert, Engage, Return, Flee, Dead) and, inside a mode, a **score** picks *who* to fight and *which* attack (utility AI, the Guild Wars 2 way) | Old design: plain state machine with a weighted dice for attacks. Behaviour trees, GOAP/HTN planners |
| D2 | One framework, two uses | **Brain core** (sense grid, think clock with AI LOD, moving and steering, paths, dice, debug) + **minds** on top: Monster mind now, Townsfolk mind later | Two separate systems |
| D3 | How fast it thinks | **Engaged monsters think every tick (30 Hz)**; near 10 Hz; far awake 2 Hz; asleep 0. Sight aggro lands in **<= 0.15 s**, a hit turns it **the same tick**, an attack starts **the tick it is in reach and facing** | Emerald: sight scan once a second, attacks wait for the animator |
| D4 | Paths | Straight line first; when blocked, a **budgeted path job on Unity's NavWorld** (Unity 6.5+, job-safe) after a one-day spike; fallback the baked grid of the old design | Emerald's NavMeshAgent per monster; 29 Oct default "NavMesh.CalculatePath queue" |
| D5 | Attacks that feel fair and quick | Server starts the attack at once and sends **AttackStartTick** on the ghost; the client plays the clip **caught up** by the time already gone and shows the warning; the server lands the hit at the windup's end where the player *is*; every attack's **visible warning >= 0.35 s** | Emerald: swing starts after an animator change, an RPC tells clients, the hit comes from a clip event |
| D6 | Threat | A **threat table** per monster (damage, heals on its foes, taunts); switches target only when another player beats the current one by **10 % in melee reach / 30 % out of it**; first-hit still holds the kill and the loot | Emerald: nearest target, sticky; no table |
| D7 | Packs | **Attack slots** (Kingdoms of Amalur's stage manager): each player has a weighted ring of slots and an attack budget, so about 2-3 monsters swing at once and the rest wait just outside and rotate in, **assist** pulls same-zone neighbours within 6 m when one is hit; both per species switches | Emerald: no group play (off in the game) |
| D8 | Hit reactions | Monsters **flinch visually** but a flinch never cancels a swing already started; heavy hits or stuns can (poise) | Emerald: every hit blocks the next attack (stun-lock, "passive mobs") |
| D9 | World NPCs | Same brain core. **Talk-to NPCs** (vendors, guards, quest givers) are server ghosts with a **schedule stack**; **ambient crowds** are client-only, grown from a seed, with AC Unity-style crowd LOD | All NPCs as server ghosts |
| D10 | Budget | **< 0.3 ms per tick for Zone01's 102 monsters; < 2 ms per tick for 2,000 awake monsters** across worker threads (The Scar later) | Emerald: full Update + Animator + NavMeshAgent per monster on the server |

---------------------------------------------------------------------------------------------------

## 1. How Emerald does it today (NGO main game) and why it feels laggy

### 1.1 What runs where [code]
- **Server/host only** runs the brain: `HostileNet` wakes `HostileBrainLink` where the game is decided; clients keep every Emerald
  part off and replay what the server reports (`HostileNet.cs:12-17`, prefab parts off `HostileAssetBuilder.cs:545-547`).
- **Every awake Hostile runs all of Emerald each frame** from `EmeraldSystem.Update` (`EmeraldSystem.cs:143-152`): animation,
  movement, behaviours, detection, combat, in that order, as MonoBehaviour code on the main thread. The dedicated server runs at
  30 frames a second (`DedicatedServerOptimizer.cs:14,37`; NGO `TickRate: 30` in `NetworkManager.prefab:58`).
- **The server animates every Hostile**: the Animator is on, culling "Always Animate" (`m_CullingMode: 0` in the prefabs), because
  Emerald reads its own animator states to decide (below). A headless server pays full skinned-animation state evaluation.
- **Movement** is a `NavMeshAgent` per Hostile (`NavMeshDriven`, `HostileAssetBuilder.cs:562`); stopping distance = attack range
  (`:567`).
- **Health** is the game's `CombatEntity`; Emerald's health is a shadow kept in step (`HostileBrainLink.cs:186-206`). **Hits on
  players** come from Emerald's melee ability through `KNEmeraldPlayerBridge.DamageCharacterController` -> `HostileBrainLink.Strike`,
  which re-rolls with the game's `CombatCalculator` and the reach-above / wall rules (`HostileBrainLink.cs:237-260`).
- **Network**: position by `NetworkTransform` (interpolated, `Interpolate: 1`, full floats); animation state by a NetworkVariable
  sent at most every 0.1 s (`HostileNet.cs:34,432-446`); attack start, strike and flinch by **ClientRpcs** (`HostileNet.cs:482,497,
  558-575`); threat note to the target player every 0.5 s (`:36`). Zones tick every 0.5 s (`HostileZone.cs:113-115`).

### 1.2 Why it feels laggy: the delay chain, with the numbers in our prefabs [code]

| # | Where the time goes | Value in the game | File:line |
|---|---|---|---|
| L1 | **Sight scan** (Physics.OverlapSphere) only every `DetectionFrequency` | **1.0 s** (Emerald default, never changed by the builder) -> a player who walks into range waits **0-1.0 s, 0.5 s on average** before the monster reacts | `EmeraldDetection.cs:17,179-189`; prefab `DetectionFrequency: 1` |
| L2 | **Can I reach you?** NavMesh.CalculatePath only every `CalculatePathSeconds` | **1.0 s**; a monster that lost reach keeps chasing up to 1 s, one that regains it waits up to 1 s | `EmeraldMovement.cs:15,155,1205-1216` |
| L3 | **Attack waits for the animator**: an attack may only start while the animator says Idle or Moving, never in a turn, a hit reaction or a transition | the 0.35 s cooldown only counts in those states | `EmeraldBehaviors.cs:290-297`, `EmeraldCombat.cs:19` |
| L4 | **Must face you within 30 degrees** first, turning at sensitivity 2 / stationary turn speed 10 | a player who side-steps resets the turn; the swing never comes | `EmeraldCombat.cs:203-206`, `EmeraldCombatManager.cs:542`; prefab `CombatAngleToTurn: 30` |
| L5 | **Must be inside the NavMeshAgent's stopping distance**, which is measured along the agent's remaining path (lags the target, which moves every frame) | attack range 1.8-2.6 m | `EmeraldCombatManager.cs:533`, `EmeraldBehaviors.cs:317-323` (cancels a triggered attack if the path says too far) |
| L6 | **Every hit taken blocks the next attack** (Hit trigger, InternalHit, IsGettingHit) | a fast player stun-locks a monster: it looks passive, then "wakes up" late | `EmeraldCombatManager.cs:529,537-538` |
| L7 | **The attack is announced only after the animator has entered the attack state** (polled next frame) | trigger -> transition -> next Update -> `OnStartAttackAnimation` -> `AttackClientRpc` | `EmeraldAnimation.cs:104-107`, `HostileBrainLink.cs:223-226` |
| L8 | **The hit is an animation event** on the clip at the windup time | 0.25 s (light) / 0.55 s (heavy) after the state starts; Emerald's own gate 4 m / 75 degrees | `HostileAssetBuilder.cs:325,384-386`; `HostileMelee.asset` `MaxDamageDistance: 4`, `MaxDamageAngle: 75` |
| L9 | **Clients see two clocks**: the body position arrives interpolated (NGO buffer, ~0.1 s+ behind) but the attack RPC is played on arrival | the swing plays on a body that is drawn ~0.1 s in the past; a 0.25 s windup leaves the player **~0.1 s** to see it; the number can land before the swing reads | `HostileNet.cs:562-570`; prefab `Interpolate: 1` |
| L10 | **Fight-back aggro**: Emerald itself waits 0.6 s before turning on an unseen attacker (our link forces it at once, but only after Emerald's parts have started, the frame after waking) | 0 s in practice, 1 frame after wake | `HostileBrainLink.cs:184,195-196` |
| L11 | **Obstruction / line of sight** raycast every 0.1 s, give up after 1.5 s obstructed | extra raycasts per monster | `EmeraldDetection.cs:22-24,473` |

**Sum for the owner's feel:** walk into a level-10+ monster's range and it reacts after **~0.5 s on average (up to 1 s)**; once in
reach it may wait for its animator, a 30-degree turn and a 0.35 s cooldown that does not count while it flinches; then clients
see the swing ~0.1 s late on a body ~0.1 s in the past. Fighting it, each of your hits flinches it and cancels its next swing.
That is "laggy and not responsive" in numbers. None of it is a network-bandwidth problem; it is decision timing.

**Server cost (why 102 monsters is already heavy):** per awake monster per frame: Emerald's five updates in managed code, an
Animator evaluation, a NavMeshAgent, an OverlapSphere each second, a raycast every 0.1 s, a NetworkVariable compare every 0.1 s,
three ClientRpcs per attack. All single-threaded on the main thread. No AI LOD inside an awake zone (only zone sleep).

### 1.3 What we keep from it (rules, not code)
The roster, levels, behaviours (FightsBack / AttacksOnSight), aggro radius formula, walk/run speeds, three attacks per species
with windup/multiplier/weight, reach-above 3 m, the wall rule, first-hit kill credit with 10 s release, leash 15 m past the zone
edge, zone sleep (ends fights, heals), corpse 3 s + 1.4 s dissolve, respawn timers, Bruisers, GM commands. Full list:
`monster-ai-design.md` section 1 and the **M5.0 NGO rules table** (step list) which is written before any code (owner rule).

---------------------------------------------------------------------------------------------------

## 2. What others do (short; sources in section 13)

**Mob AI at scale**
- **AI LOD and time-slicing** are universal: think often near players, rarely far, not at all with nobody around; spread heavy
  work (repaths, rescoring) over ticks with a fixed budget, keep movement every tick [web: arch 3]. Assassin's Creed Unity: 3 tiers
  (full AI within 12 m, "puppet" 12-40 m, no autonomy beyond), at most 40 full AIs at once [web: npc 3].
- **Utility AI at MMO scale**: Guild Wars 2: Heart of Thorns runs its creatures on Dave Mark's Infinite Axis Utility System
  (GDC 2015 "AI at Massive Scale"): every action scored by curves over considerations, best score wins; cheap with early-out
  [web: arch 1].
- **Threat**: WoW pulls aggro off the current target only at **110 % of its threat in melee range, 130 % at range**; leash/evade:
  the mob runs home **immune and regenerating**, also when the target is unreachable; "social" mobs pull same-type neighbours
  [web: R-mob 2]. GW2: no hard tanking, a break bar instead of taunts on big foes [web: R-mob 2].
- **Packs**: Kingdoms of Amalur's "stage manager" (Game AI Pro ch. 28, read in full): the player carries an 8-slot ring with a
  **grid capacity** (e.g. 12) and an **attack capacity** (e.g. 10); a soldier weighs 4, a troll 8; each attack has a cost (lunge 5,
  swing 3); slots are released right after the attack so attackers rotate; slot-less mobs wait outside the outer ring (free
  flanking); difficulty only changes the capacities. Doom 2016: separate melee/ranged token pools, bigger with difficulty.
  Vermintide/Darktide: slots around each player also save performance [web: R-mob 3].
- **Crowds**: Supreme Commander 2 flow-field tiles (1 m cells, 10x10 sectors, shared and cached, time-sliced rebuilds) replaced
  per-unit A* that stalled at ~1,000 units; Left 4 Dead: steer to a look-ahead point + local avoidance, repath only when drifted,
  an "active area set" around players spawns/despawns the population [web: R-mob 1, 4].
- **Responsiveness**: human reaction ~200-300 ms; telegraph strength should scale with damage; **Netcode for Entities draws
  interpolated ghosts 2 ticks back by default** (`InterpolationTimeNetTicks = 2`); the common fix for server mobs is to replicate
  {attack, start tick} and play the clip from the right offset, resolving the hit at the window's end on the server [web: R-mob 5].
  FFXIV resolves at cast end, so its telegraph must cover the round trip [web: R-mob 5].
- **Ticks in MMOs**: EVE 1 Hz with time dilation; WoW batched at 400 ms until ~2014; FFXIV ~0.3 s position tick (players'
  measurement). Our 30 Hz server tick is already far faster than the MMOs that feel "slow" [web: R-mob 1].

**ECS / DOTS**
- Behaviour trees fit ECS badly (pointer trees, divergent walks); Unity's own Behavior package lost its team in Feb 2025 and AI
  Planner never left preview: **do not depend on either** [web: arch 1].
- Best Burst fit: modes as enum components + utility scoring in batches over blob curves; HTN later for routines [web: arch 1].
- **NavWorld** (`Unity.AI.Navigation.LowLevel.NavWorld`, Unity 6.5+) is the new job-safe NavMesh query API; the old
  NavMeshQuery is deprecated [web: arch 2]. This answers the old design's open item ("NavWorld page 404").
- V Rising (Unity DOTS, 60-player servers, 30 Hz) is the closest shipped game; nothing public about its mob AI [web: arch 2].
- Perception: one shared hash grid rebuilt per tick, physics only for line of sight on the few candidates [web: arch 3].
- Determinism: per-entity `Random.CreateFromIndex` state in a component, server tick not deltaTime, pure static scorers testable
  without a World [web: arch 4].

**Living-world NPCs**
- **Schedules as a stack**: Bethesda's package stack (first package whose time and conditions pass runs; near NPCs re-check often,
  far ones rarely); Stardew/Ultima VII schedules as plain data (time, place, activity) [web: npc 1].
- **Smart objects**: objects advertise what they offer ("dumb people, smart objects", The Sims); Bethesda furniture/idle markers;
  a sandbox picks reachable markers near the NPC [web: npc 2].
- **Crowds and LOD**: AC Unity ~10,000 crowd NPCs with 40 full AIs; Unreal Mass: 4 LOD levels each with a distance and a max count;
  Kingdom Come 2: far NPCs keep their state but only choose behaviour [web: npc 1, 3].
- **Multiplayer**: GW2 sends every NPC in a large radius (server entities filtered by interest); a seeded client-only crowd is
  possible but risks drift; nothing primary confirms MMO towns run client-only crowds [web: npc 4].

---------------------------------------------------------------------------------------------------

## 3. The Brain framework

### 3.1 Shape: a core and minds

```
KryticalNova.Brains            (Simulation layer; Burst; server world, and client world for client-only crowds)
  Sense      players + brains copied once per tick into hash grids; sight/hearing/aggro queries; line-of-sight on the few
  Clock      think tiers (AI LOD), per-brain next-think tick, staggered; budgets per tick for paths and rescoring
  Move       steering (seek/arrive/separation/avoid), speed, turn rate, ground height; paths through PathService
  PathService  straight line first, queued path jobs (NavWorld, spike) with a per-tick node budget; results as waypoint buffers
  Score      utility curves (blob), considerations, early-out pickers; pure static Burst functions
  Dice       per-entity Random state (CreateFromIndex(stable id ^ world seed)), tick based
  Debug      state dump, gizmos, replay recorder
KryticalNova.Monsters          (Simulation; uses Brains, Combat.Entities queues, Progression rules) = the Monster mind
KryticalNova.Townsfolk         (Simulation; uses Brains, World) = the Townsfolk mind (later)
KryticalNova.Monsters.Presentation / Townsfolk.Presentation  (Presentation; client views; excluded from the Linux server)
```
Layer check: Brains, Monsters and Townsfolk are all Simulation; the minds use the core, never the reverse; nothing in Brains
names a mind. Rewards cross into economy code only through the existing managed bridge (CLAUDE.md ledger rules untouched).
**No new machinery** (CLAUDE.md): no event bus, no DI, no BT package; data stays ScriptableObjects baked to blobs; numbers live in
settings assets (`MonsterSettings`, later `TownsfolkSettings`).

### 3.2 The brain loop (each server tick, every system Burst `ISystem` + `IJobEntity`, parallel)
1. **SenseBuild**: player snapshot (ghost players: feet, faction, alive, on neutral ground, on a wall, ignored) and brain
   positions into two grids (players 16 m cells, brains 4 m cells).
2. **ThinkClock**: per brain, `think = tick >= NextThinkTick`; `NextThinkTick += TierInterval[tier]`; staggered by stable id so
   each tick carries the same share. Tier from the nearest player distance (from the grid) and "in a fight".
3. **Events in** (always processed, every tick, regardless of tier): damage taken, assist calls, GM commands. **A hit is never
   delayed by the think clock**: the hit brain is bumped to tier 0 and thinks this tick.
4. **Decide** (only brains with `think`): the mind's mode machine; inside the mode the scorer picks target and action.
5. **Act**: start attacks (write `AttackStartTick`), choose destinations, request paths.
6. **Move** (every tick for every awake brain that moves): steering toward the destination; separation; ground height.
7. **Resolve**: attacks whose windup ends this tick are tested and rolled; blows go to the `HostileStrike` queue.
8. **Out**: ghost fields written only when changed (Netcode serialises what jobs write).

### 3.3 Think tiers (AI LOD) - numbers

| Tier | When | Think (decide) | Move | Sense scan | Paths |
|---|---|---|---|---|---|
| 0 Engaged | has a target, or took/dealt damage in the last 5 s, or a player within 12 m | **every tick (30 Hz, 33 ms)** | every tick | every 2nd tick for new threats | repath when the target moved > 1.5 m, at most every 0.3 s |
| 1 Near | a player within 40 m | every 3rd tick (10 Hz) | every tick | **every 3rd tick (aggro reaction <= 0.1 s + tick)** | patrol paths only |
| 2 Far | zone awake, nearest player 40-120 m | every 15th tick (2 Hz) | every tick, or snap-walk at 2 Hz if nobody sees it (relevancy) | every 15th tick | none |
| 3 Asleep | zone asleep (no player within radius + 40 m) | none (enableable `BrainActive` off) | none | none | none |

Reaction targets vs Emerald (server time, before network):

| Moment | Emerald (our prefabs) | Brain target |
|---|---|---|
| Player walks into aggro range (AttacksOnSight) | 0-1.0 s, avg 0.5 s (L1) | **<= 0.10 s** scan + optional per-species "notice" 0-0.2 s (Alert mode; data, default 0.1 s) |
| Player hits a monster that was idle | 1 frame after its parts started (L10) | **same tick** (event path) |
| Target in reach and facing -> swing starts | after animator Idle/Moving + 30 deg + 0.35 s cooldown not counting in flinch (L3-L6) | **same tick** it is in reach (+ slack 0.3 m) and within 60 deg; turn rate 540 deg/s while engaged |
| Player steps out of reach mid-windup | Emerald cancels on path distance (L5) | **commit point** at 60 % of the windup: before it, the monster tracks (turns) and can cancel; after it, the swing is locked and lands only if the player is still in reach at the windup end (so dodging works and reads) |
| Monster loses reach (unreachable / above reach) | up to 1 s (L2) | next think tick (<= 33 ms engaged); give up after `unreachableSeconds` 6 s as today |
| Target dies / goes to neutral ground | next detection | same tick (event) |

### 3.4 Modes and scores (D1)
- Modes (byte): `Idle, Patrol, Alert, Engage, Return, Flee, Dead` (+ `Despawn`). Transitions are a small table, testable as a pure
  function (`BrainRules.NextMode`). Engage has sub-phases `Approach, Windup, Recover, Reposition`.
- **Scores (utility)** only where a choice exists, all over blob curves with early-out:
  - *Target pick* (on think in Engage, and when threat changes): threat (main), distance, "already engaged by N allies"
    (token pressure), on-wall/out-of-reach (big minus), low health (small plus, species flag "finisher").
  - *Action pick* (when an attack may start): each ability scored by range fit, cooldown ready, target's health, own health,
    the species weight (keeps today's 45/40/15 feel as the base). Replaces the plain weighted dice; with all curves flat it gives
    exactly the dice (parity test).
  - *Townsfolk* reuse the same scorer for "which smart object / activity" (section 6).
- Cost: monsters have 3 abilities and 1-6 threat entries; scoring is ~20 multiply-adds per think. At 2,000 brains with 30 % in
  tier 0: 600 x 30 Hz scorings, ~0.05 ms per tick on one core [estimate, U].

### 3.5 Threat, kill credit, leash (D6)
- `ThreatEntry` buffer (capacity 6, as the old design): damage dealt x 1.0, healing done on its foes x 0.5 split over the monsters
  that hold the healed player, taunt sets top + 10 %. Decays 5 %/s after 5 s without contact.
- Target switch: only if a new top beats the current target by **10 % (in melee reach) or 30 % (out of reach)** - the WoW rule,
  stops ping-pong [web: R-mob]. **First hit still holds the kill and the drops** (unchanged NGO rule; separate `TagHolder`).
- Leash: zone radius + 15 m (unchanged), plus "unreachable for 6 s". Return mode: runs home, cannot be damaged, heals to full,
  threat cleared, tag released (NGO rule). Players see it as "evade".

### 3.6 Packs: tokens, surround, assist (D7, per species)
- **Attack slots (the Amalur stage manager)**: each player carries an 8-slot ring with a **grid capacity** (default 12) and an
  **attack capacity** (default 10, separate melee and ranged pools as Doom). A normal monster weighs 4 on the ring, a Bruiser 8;
  each ability has an attack cost (light 3, heavy 5). A monster must hold a slot and pay the attack cost to start a swing; the cost
  is returned right after Recover, so attackers rotate (no monster waits > 2 s). Slot-less monsters wait outside the outer ring
  (3.5-5 m) in front of free slots and feint, which gives flanking for free. One Burst job per tick per engaged player assigns
  slots. Difficulty and species only change numbers in `MonsterSettings` / the archetype [web: R-mob 3]. It limits *who swings*, never who can be hit: **player area skills still hit every monster in range with
  no cap** (owner rule, area-skills-no-target-cap).
- **Assist (social aggro)**: when a monster is hit or aggroes, same-zone monsters of the same species within `assistRadius`
  (default 6 m; Bruisers 0) join with a small threat. Off in Emerald today; turning it on makes zones more dangerous: owner pick.
- **Flee**: data present, off by default (old design), a species flag.

### 3.7 Movement and paths (D4)
- Kinematic movement (no physics bodies), steering every tick: seek/arrive to destination, separation within 1.2 m from the
  brain grid, ground height from the nav data. Engaged turn 540 deg/s (Emerald effectively ~180-360).
- **Straight line first** (most zone ground is open). When the line is blocked: **PathService** queues a request, a Burst job
  answers within a per-tick node budget (start 8 searches/tick, 2,000 nodes each); result = up to 16 waypoints. Repath rules in 3.3.
- **Which nav data (spike before code, 1 day)**: (A, recommended) **NavWorld** from the zone's existing NavMesh, queried in
  jobs; (B) the old design's baked 2.5D grid tiles + Burst A* (no engine dependency, the most testable); (C) the 29 Oct default:
  main-thread `NavMesh.CalculatePath` queue. The spike proves A in a Linux IL2CPP+Burst server build and in EditMode (a test
  NavMesh). If A fails either, B. Tests of brain logic never need a NavMesh: PathService has a test double (straight line + boxes).
- Crowds chasing one target (The Scar, events): a **flow field** per hot target is the later upgrade when > 30 brains chase one
  goal [web: R-mob]; not built in M5/M6.
- Wallstride / players on walls: unchanged rules (reach-above 3 m, the wall line test) in Resolve, not in paths.

### 3.8 Attacks that feel quick and fair on clients (D5)
Server (truth):
1. Tick T: the brain starts ability *i*: writes `AttackStartTick = T`, `AttackIndex = i`, `AttackTarget` ghost id. Facing is
   tracked until the commit point (60 % of windup), then locked.
2. Tick T + windup: Resolve checks target alive, not on neutral ground, height <= reach-above, wall rule, distance <= range +
   0.3 m slack, angle (cone abilities), then rolls `CombatMath` (Burst port, parity-tested with `CombatCalculator`) and appends a
   `HostileStrike` (step 9 queue; real attacker key, so reflection now hits the monster).
3. Players are predicted, so the server's player position at T + windup is the player's real position: **a dodge the player
   sees on their own screen is a dodge the server sees** (no rewind needed for monster -> player).

Client (looks):
1. The ghost snapshot carries `AttackStartTick`/`AttackIndex`. The client's monster is drawn at the interpolation tick (~2-3
   ticks behind). On arrival the view starts the clip **at the time already gone** (`(interpTick - AttackStartTick) / 30 s`), so
   swing and body agree (fixes L9). Heavy attacks show the "!" and Feel swing exactly as NGO `ShowAttack`.
2. **Warning time the player gets** = windup - (half RTT + interpolation delay). With 0.25 s windups, ~50 ms half RTT and
   Netcode's default 2-tick interpolation (66 ms at 30 Hz) plus snapshot spacing, that is ~0.1 s, under the 200-300 ms a human
   needs: too short to read. So: **every attack's visible warning >= 0.35 s** = data windups raised where needed
   (light 0.25 -> 0.40 s, heavy 0.55 s stays), *and* light attacks get a short pre-swing "tell" (head turn / weight shift) that
   the client plays from `Alert->Engage`. Responsive feel comes from the **monster starting at once**, not from short windups.
3. Hit numbers come from `HostileStrikeRpc` (step 9, reliable) at T + windup on the server; the client shows the number when it
   arrives and the impact on the clip's impact frame (whichever is later, no number before the swing).
4. **Player -> monster**: already built in M4: the server resolves player swings on `CombatTarget` ghosts with `PositionHistory`
   (16 ticks) at the tick the player saw (`PositionHistoryRules.HitTestAtTick`). Monsters become `CombatTarget` ghosts
   (`Kind = Hostile`) and get a `PositionHistory` ring: lag compensation for free.

### 3.9 Hit reactions (D8)
- Flinch = client-only animation from a `HitCounter` ghost byte (old design). It **never cancels** the server's windup or
  cooldowns. `Poise` per species (data): heavy player hits or stun skills (Lockup) interrupt a windup only if they exceed it.
  Fixes L6 (stun-lock -> passive monsters).

### 3.10 Death, de-rez, loot, XP
- Unchanged from `monster-ai-design.md` section 10: one idempotent `MonsterKilledEvent` -> managed bridge -> the existing
  `HostileKillRewards` rules (XP by level gap, Squad split, pet credit), `HostileLoot`, `PlayerLoot.ServerPlaceAt` (drops held
  20 s for the holder). ECS never touches wallet, ledger, SaveTogether.
- Owner rules: **airborne deaths de-rez** (a monster killed off solid ground disintegrates, never falls: the view picks the de-rez
  instead of the death clip when `OnGround` was false at death); dissolve otherwise as today.
- Player killed by a monster: 5 % XP loss once per death via the stand-in attacker path of step 9, now the real monster key.

### 3.11 Ghost and network
- Interpolated ghosts, never predicted; quantised position (5 cm), yaw byte, `Mode`/`Anim` byte, `HealthPct` byte,
  `AttackStartTick`, `AttackIndex`, `HitCounter`, `Flags` (in combat, stunned, dead, on ground), static species/level/tier.
  ~14 B per changed monster per send.
- **Relevancy**: monsters within 150 m of a player; **importance** by distance (`GhostDistanceImportance` tiles). Send at full
  rate while engaged with that client, reduced far away. Netcode's spatial tiles also feed the think tiers (one grid, two uses).
- No RPC per attack (L7/L9 gone): attack, flinch and death travel on the ghost. Only the hit player's number is an RPC (step 9).
- Bandwidth: Zone01 worst case all 102 relevant x 10-30 Hz x 14 B = 14-43 KB/s per client; normal play 30 relevant ~5-12 KB/s.

### 3.12 Counts and budget (D10)

| Place | Monsters (awake / alive) | Players | Target brain cost per 30 Hz tick |
|---|---|---|---|
| Zone01 today | 102 / 102 (cap 300) | 1-20 | **< 0.3 ms** (measure; Emerald today: whole managed Update + Animator per monster) |
| A normal map server later | up to 600 / 1,000 | up to 100 | < 1 ms |
| The Scar (later, 1,000+ players) | 2,000 awake | 1,000+ | **< 2 ms across worker threads** (~6 % of 33 ms), main thread < 0.3 ms |
| Mobile client crowds (later) | 0 server brains; 200 client crowd agents | - | < 1 ms client, jobs on all cores |

Per-brain memory ~600 B + 6 threat entries; 2,000 brains ~1.5 MB. These are design estimates, **[U] until profiled** on the Linux
build (soak in M5.7 and M6.6).

### 3.13 Testing it without a game (deterministic enough)
- Pure static rules (`BrainRules`, `MonsterRules`, `Score`, `CombatMath`) tested with plain EditMode tests.
- World tests (`KnTestWorld`-style, as M4): create the server world's systems, feed a fake player snapshot, step ticks, assert.
  Same seeds -> same results after 1,000 ticks (determinism test). No `Time.time`, no `System.Random`, writes only to own entity,
  shared outputs merged in a stable order.
- **Reaction-time tests** (the owner's main ask as numbers): aggro <= 4 ticks after entering range; turn on attacker in 0
  ticks; swing starts the tick reach+facing hold; dodge before the commit point cancels; a flinch never cancels a windup.
- Zero GC in the brain loop over 1,000 ticks; Burst compile of every job in a Linux server build (`lib_burst_generated` present).

---------------------------------------------------------------------------------------------------

## 4. Changes to the 29 Oct design (`monster-ai-design.md`)

| Section there | Change here | Why |
|---|---|---|
| 0/3 Brain: plain FSM + weighted dice | Modes + scorer (utility) for target and ability; still one byte mode | "smarter"; parity kept (flat curves = the dice) |
| 3 Scan every 4th tick everywhere | Think tiers 0-3 (3.3); engaged every tick | "respond faster"; scale to The Scar |
| 2 Assist/Flee off | Tokens + surround + assist as species switches (owner pick) | "smarter" packs |
| 7 Nav: baked grid only (NavWorld 404) | NavWorld spike first, grid as fallback | NavWorld now documented for 6.5+ |
| 9.1 Monster hits at windup tick | + commit point, + min visible warning, + client catch-up | fairness and read |
| 2 Hit reaction = flinch | + Poise; flinch never cancels a swing | fixes stun-lock |
| 9.2 Player -> monster via an NGO adapter | Through M4's `CombatTarget` + `PositionHistory` + `MeleeResolveServerSystem` (already built) | M4 landed this |
| 9.1 player damage via managed bridge | Through M4.9's `HostileStrike` queue (built) | M4.9 landed this |
| (none) | Brains core shared with Townsfolk | owner's NPC ask |
| 10 Rewards via a new ECS kill event | **Hidden server body per Hostile** (CombatEntity + HostileNet + `EcsHostileBrain : IHostileBrain`, no Emerald/Animator): health, tag, XP, loot, death penalty run in unchanged NGO code; the brain is ECS | port first, HIGH rules untouched; ECS vitals in the performance phase |

**Build steps, files, components, tests and hand tests: `m5-m6-implementation-plan.md` (6 Nov 2026).**

---------------------------------------------------------------------------------------------------

## 5. Step list: M5 (the Brain + one monster)

Sizes: S = one session-day, M = 2-3, L = 4-5. Every step: its tests green, `dependency_map.py --check`, `compile_check.py` and
`--server`, `graphify update .`, a line in the migration plan checklist; the milestone ends with the 2-client test and the
Encyclopedia update (ground rules, migration plan section 0). Runs follow the overnight/RAM rules of the DOTS sessions.

| Step | What | Size | Tests |
|---|---|---|---|
| **M5.0** | **NGO rules table** for Hostiles (owner rule, before code): every number and rule of section 1 with file:line, the `HostileNet` surface and its consumers (`graphify affected "HostileNet"`), the step-9 queue contract. Owner/Nova sign-off | S | none (doc) |
| M5.1 | **Brains core skeleton**: components, ThinkClock tiers, Sense grids, Dice, Score (curves in blobs), `BrainRules`; `KryticalNova.Brains` asmdef | M | `ThinkClockTests` (stagger, tier change, a hit bumps to tier 0), `SenseGridTests`, `ScoreTests` (curves, early-out), determinism 1,000 ticks |
| M5.2 | **Nav spike** (NavWorld in a job + Linux IL2CPP/Burst build + EditMode test NavMesh); decide A or B | S | spike report; PathService test double |
| M5.3 | **Monster mind P0 "Prowler"** (stand-in clips, as the old slice): modes, target/ability scores, threat table, leash/return, attack windup/commit/resolve into the `HostileStrike` queue | L | `MonsterBrainTests` scenarios (old design s17) + **reaction-time tests** (3.13) + threat switch 10 %/30 % + commit point + flinch-never-cancels; `CombatParityTests` 10k tuples |
| M5.4 | **PathService** on the chosen nav (budget, repath rules, unreachable 6 s) | M | `PathServiceTests` (budget, failure, repath) |
| M5.5 | **Ghost + view**: monster as interpolated ghost + `CombatTarget(Kind Hostile)` + `PositionHistory`; pooled view with the existing model/Animator; clip catch-up from `AttackStartTick`; HitCounter flinch; registry for nameplates | M | ghost field tests; `CombatTargetRegistry` entry; PlayMode: two clients see the same swing start within one tick |
| M5.6 | **Death and rewards**: `MonsterKilledEvent` -> bridge -> `HostileKillRewards` / loot; de-rez off ground; real attacker key in `HostileStrike` (reflect works) | M | one event per death under simultaneous hits; XP equal to NGO for the same kill; B54-style PlayMode |
| M5.7 | **Feel checkpoint + soak**: side by side with NGO (owner plays), measured reaction table (3.3) with `[KnBrain]` timestamps off by default; 102-brain and synthetic 2,000-brain soak on the Linux test server (test ports only) | M | p99 brain ms per tick; reaction numbers; 2-client screenshots/GIFs |

M5 total: about 3 weeks of session-days. Done = the Prowler fights two clients on the DOTS test server, reaction table met,
< 0.3 ms per tick at 102.

## 6. Step list: M6 (every Hostile on the Brain)

| Step | What | Size | Tests |
|---|---|---|---|
| M6.1 | `MonsterArchetype` for all 11 species from `HostileDefinition` (ids kept; menu tool), stat table blob from `HostileStatCurve`; raised light windups per the owner's Q3 pick | M | ids equal; blob rows equal the curve at every level/tier |
| M6.2 | **Zones**: spawn (clearance 3 m, Bruiser at the edge), respawn slots, zone sleep -> `BrainActive`; Zone01 subscene authoring | M | spawn-spot rules, respawn timers, sleep ends fights + heals |
| M6.3 | **GM commands + GM window tab** (`gm.hostile.*`, `gm.zone.*` keep their names) | S | command tests |
| M6.4 | **Consumers**: nameplates, minimap, Autopilot/Magnet, target ring, `IHostile` via the view registry (`graphify affected`) | M | each consumer sees the same surface |
| M6.5 | **Smarter switches** per the owner's picks: tokens + surround, assist, flee for chosen species; species tuning pass | M | token rotation (no monster idle > 2 s), max N swinging, assist radius, area skills still hit all |
| M6.6 | **Soak and retire**: 102 + 300 + synthetic 2,000; bot soak 60 min; Emerald parts off on DOTS worlds (removal only at merge-back) | M | p99 tick < 15 ms whole server, brains < 0.3 ms at 102; 2-client test |

M6 total: about 2.5 weeks. Then the performance phase (owner direction 5 Nov) widens The Scar numbers.

## 7. The NPC use (after M6; not built until the owner asks)

| Step | What | Size |
|---|---|---|
| N1 | **Townsfolk mind**: schedule stack per NPC (data rows: time window, place/marker, activity, conditions; first passing row wins, as Bethesda's package stack), smart objects as authoring markers that advertise an activity and slots (bench, stall, forge, patrol post), the Brains scorer picks among reachable markers; server ghosts for **talk-to NPCs** (vendors, guards, quest givers keep NGO's `NpcInteractable` view path) | L |
| N2 | **Ambient crowds**: client-only entities, grown from a seed per city block + time of day + weather; AC Unity-style LOD (full within 12 m, puppet to 40 m, instanced far, cap 40 full); never gameplay-relevant | L |
| N3 | **Guards and fighters** use the Monster mind's combat (same modes/scores) with a faction filter (PvP hostility rule) | M |
| N4 | Off-screen life: NPCs far from players advance their schedule on paper (snap to the row's place) instead of walking | S |

Lore: NPC text and roles follow `Design/Lore-Bible.md` (Ronin stay hidden).

---------------------------------------------------------------------------------------------------

## 8. Owner's questions: ANSWERED 6 Nov 2026, all option A (owner, typed to Nova)

- **Q1 Brain style.** (A, **CONFIRMED by the owner 6 Nov**) modes + scores; (B) plain state machine as the 29 Oct design; (C) behaviour trees.
- **Q2 Packs.** (A, **CONFIRMED by the owner 6 Nov**) attack slots (grid 12 / attack 10 per player) + waiting ring on all species, assist on for
  AttacksOnSight species only; (B) all on everywhere; (C) like Emerald, no group play.
- **Q3 Warning time.** (A, **CONFIRMED by the owner 6 Nov**) every attack shows >= 0.35 s on screen (light windups 0.25 -> 0.40 s) + catch-up + tells;
  (B) keep today's windups, only catch-up; (C) longer for all (0.5 s light).
- **Q4 Flinch.** (A, **CONFIRMED by the owner 6 Nov**) flinch is looks only, poise per species decides interrupts; (B) every hit interrupts (Emerald).
- **Q5 Target switching.** (A, **CONFIRMED by the owner 6 Nov**) threat table with 10 %/30 % switch margins, first hit still owns the kill; (B) always
  the top threat; (C) first attacker until leash.
- **Q6 Navigation.** (A, **CONFIRMED by the owner 6 Nov**) NavWorld after a one-day spike, grid fallback; (B) baked grid now; (C) main-thread
  CalculatePath queue (29 Oct default).
- **Q7 World NPCs.** (A, **CONFIRMED by the owner 6 Nov**) talk-to NPCs as server ghosts, ambient crowds client-only; (B) everything server ghosts;
  (C) decide when the NPC work starts.
- **Q8 Notice delay.** (A, **CONFIRMED by the owner 6 Nov**) 0.1 s default, per species 0-0.2 s; (B) 0 (instant); (C) 0.3 s (the 29 Oct "0.15-0.30 s").

## 8b. NavWorld: monsters and NPCs only, never players (owner's question, 6 Nov 2026)

**Short answer: NavWorld changes nothing for players. It is for monsters and world NPCs only.**

1. **What players use on DOTS today [code, clone]:** a click is planned **once per order** by `NavMeshPathPlanner`
   (`Player/Entities/Runtime/PathPlanners.cs:107-131`): the static `NavMesh.SamplePosition` + `NavMesh.CalculatePath`, main thread,
   on the client when the player clicks and again on the server when it checks the order (`PlayerInputGatherSystem.cs:73-88`,
   `PlayerServerPlanSystem.cs:29`, installed in `PlayerWorldsSetup.cs:88,162`). The corners go into the order; the **predicted walk
   never touches the NavMesh** ("it may use the NavMesh, which must never run inside the predicted loop", `PathPlanners.cs:10-11`).
   Vault (`VaultPath.Plan`, `IVaultPlanner`), Waterstride (`BodyWaterPlanner`, `PlayerWaterFromSkills.cs:22-32`) and Wallstride
   plan through the same `IPathPlanner` or their own raycast rules. No NavMeshAgent moves a DOTS player.
2. **Why the earlier verdict was "no" for players** (`_Docs/Design/15_movement_wasd_controller_navworld.md`, 11 Oct 2026, section 2):
   NavWorld only finds paths, it never moves anything; it would not make movement feel faster (the slowness then was the server
   round trip, which DOTS prediction has since fixed); swapping it in meant rewriting the player mover and the jump/vault/death
   motors; and nothing measured showed pathing costing frame time. The same doc already named the place it **would** earn its
   keep: "our own Hostile mover ... path searches for hundreds of Hostiles spread over frames in jobs is exactly what it is for".
   Both verdicts stand: players are not that case, monsters are.
3. **Monsters and NPCs: yes, NavWorld fits.** They need hundreds of path searches spread over ticks inside Burst jobs on the server
   (M5.2 spike, PathService M5.4). Players need one search per click, and a `CalculatePath` per click is cheap. Same baked NavMesh,
   no bake change, nothing in the player code moves.
4. **Later, only if measured:** in the performance phase (The Scar, 1,000+ players) the server's per-order check could become a
   `NavWorldPathPlanner : IPathPlanner` behind the existing interface (one class, no player behaviour change), and NavWorld's
   batched `MoveLocations` could check many players' moves at once. That is an option with a measurement gate, not part of M5/M6.

## 9. Risks
- NavWorld in a Linux IL2CPP + Burst server build is unproven here (spike M5.2; fallback B).
- Raising light windups changes fight balance (time to kill both ways): balance pass in M6.5 with the TTK GM tool.
- Tokens could make big pulls feel too safe: tunable per species and per difficulty, owner feel checkpoint.
- Hybrid players: the player side is ECS since M4 for hits; any remaining managed player state used by monsters
  (buffs, reflection) goes through the snapshot, filled by the bridge; one-tick staleness accepted.
- The owner is often in the DOTS Editor: brain code is Burst/ISystem (no Fast Script Reload) and must follow the "no edits while
  the owner plays" rule of the DOTS sessions.

## 10. Owner rules kept (checklist)
NGO rules table before code; area skills no target cap; airborne deaths de-rez; move speed cap 28 m/s (monsters far below);
target ring double-click rule untouched; PvP hostility rule for guards; dependency layers; no new machinery; numbers in settings
assets; Encyclopedia after each landed step; test ports only (7797/8097, save `--test` 7802); never the real server or saves;
port first, performance later, never block jobification (all brain state in components).

---------------------------------------------------------------------------------------------------

## 13. Sources

Research digests (full text, URLs on every line) in the notes folder
`side-session-notes/local_fb9f05f3-.../`: `brain-research-architecture.md` (arch), `brain-research-npcs.md` (npc),
`brain-research-mobs.md` (R-mob).

Key links:
- Utility AI / GW2: https://gameai.com/iaus.php ; https://en.wikipedia.org/wiki/Utility_system
- HTN Horizon Zero Dawn: https://guerrilla-games.com/read/htn-planning-in-decima
- Unity Behavior team note: https://discussions.unity.com/t/an-update-on-behavior/1598451
- NavWorld: https://docs.unity3d.com/ScriptReference/Unity.AI.Navigation.LowLevel.NavWorld.html
- V Rising case study: https://unity.com/resources/stunlock-studios-v-rising
- Netcode ghost optimisation (importance, relevancy): https://docs.unity3d.com/Packages/com.unity.netcode@1.8/manual/optimization/optimize-ghosts.html
- Random.CreateFromIndex: https://docs.unity3d.com/Packages/com.unity.mathematics@1.2/api/Unity.Mathematics.Random.CreateFromIndex.html
- AC Unity crowds: https://gdcvault.com/play/1022141/Massive-Crowd-on-Assassin-s ; https://game.watch.impress.co.jp/docs/news/20150307_691715.html
- Kingdom Come 2 NPCs: https://gdcvault.com/play/1035718/Supporting-Thousands-of-NPCs-in
- Bethesda package stack: https://ck.uesp.net/wiki/Package_Stack
- Amalur attack slots: https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter28_Beyond_the_Kung-Fu_Circle_A_Flexible_System_for_Managing_NPC_Attacks.pdf
- SupCom2 flow fields: https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf
- Left 4 Dead AI: https://cdn.fastly.steamstatic.com/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf
- WoW threat 110/130 %: https://www.wowhead.com/classic/cn/guide/threat-overview-classic-wow ; leash/evade: https://warcraft.wiki.gg/wiki/Leash
- Netcode ClientTickRate defaults: https://docs.unity3d.com/Packages/com.unity.netcode@1.6/api/Unity.NetCode.ClientTickRate.html
- Darktide slots: https://forums.fatsharkgames.com/t/darktide-101-enemy-design-and-prototyping-dev-blog/98452
- Stardew schedules: https://stardewvalleywiki.com/Modding:Schedule_data
- Unreal Mass LOD: https://dev.epicgames.com/documentation/unreal-engine/overview-of-mass-gameplay-in-unreal-engine
