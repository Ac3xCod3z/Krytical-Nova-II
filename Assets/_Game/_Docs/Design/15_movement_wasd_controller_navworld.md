# 15. Movement: WASD, a controller, and NavWorld

Status: **design only** (11 Oct 2026). Nothing here is built. It answers three questions the owner asked: could the
game take WASD and a controller next to click-to-move, what would that look like, and should navigation move to
Unity 6.6's NavWorld.

Short answers:

1. **NavWorld: do not switch.** It finds paths in jobs; it does not move anything. It would not make movement
   feel faster, and our Hostiles cannot use it.
2. **What makes movement feel slow today is the network model, not pathfinding.** Your own character only moves
   when the server's copy of it arrives. Fixing that helps click-to-move at once, and WASD cannot work without it.
3. **WASD is a medium job once that is fixed. A controller is a large one**, and most of its size is the windows,
   not the movement.

## 1. How movement works today

- `PointAndClickPlayer` reads a click, and on a client sends the point to the server
  (`RequestMoveDestinationServerRpc`). The server checks it (on the navmesh, a whole path, the Run cost) and moves
  that player's `NavMeshAgent`. No client moves its own agent.
- The position comes back through a server-authority `NetworkTransform` at 30 ticks a second, smoothed over 0.1 s.
  So between a click and the first step there is the trip to the server, a tick, the trip back and the smoothing:
  roughly 100 to 150 ms on one machine, more over the internet.
- There is no prediction and no reconciliation. The only thing shown at once is the move marker.
- Everything is built on **orders with a destination**: arrival, the stall watchdog, the vault being armed for an
  order, the Run cost being paid per order, the skill queue walking in. Nothing can say "move in this direction".
- Seven player classes hold the agent directly (the controller, the jump, vault and death motors, the animation
  motor, melee and the skill queue). The agent's `baseOffset` is read as "feet to root" in about ten files.

## 2. NavWorld

**What it is.** `Unity.AI.Navigation.LowLevel.NavWorld` (in our Editor, 6000.6.3f1) is the replacement for the old
experimental `NavMeshQuery`. One handle (`NavWorld.GetDefaultWorld()`, to be disposed) gives navmesh **queries that
may run inside jobs**:

| Call | What it does |
| --- | --- |
| `MapLocation` | Nearest point and node on the navmesh (the job-safe `SamplePosition`) |
| `BeginFindPath` / `ContinueFindPath` / `EndFindPath` / `GetResultFromFindPath` | A path search that can be spread over frames, on a `NavQueryBuffer` |
| `GetPortalPoints` | The shared edge between two neighbouring nodes of a path |
| `MoveLocation` / `MoveLocations` | Slides one location, or a batch, along the surface toward a target |
| `Raycast` | A line across the navmesh |
| `AddDependency` | Holds navmesh changes until a job has finished |

**What it is not.**

- It does not move, steer or avoid. `NavMeshAgent` still does that, or we write a mover of our own.
- A path comes back as a **corridor of nodes**, not corner points. Turning it into the straight path
  `NavMesh.CalculatePath` gives is ours to write (the sample project behind the video uses portal midpoints, which
  is cruder than what we have).
- It uses the same baked navmesh. Nothing about the bake changes.

**Why it does not earn its place here now.**

- **Hostiles**, the only real agent load (up to 300), are driven by Emerald AI, which is hard-wired to
  `NavMeshAgent`. NavWorld cannot be put under it. It would need our own Hostile mover.
- **The agent's own work is already off the main thread**: path requests from `SetDestination` are asynchronous
  and the crowd update is native. What we do on the main thread are a few synchronous calls
  (`CalculatePath`, `SamplePosition`, `NavMesh.Raycast`) per order, per player.
- **Nothing we have measured shows navigation costing frame time.** The performance work of 10 Oct found grass,
  the portrait and UI; the Hostile plan's budget (2.5 ms of AI and pathing per server frame at 300 Hostiles) has
  not been tested yet.
- **It would not change how movement feels.** The delay is the round trip in section 1.
- The cost is a rewrite of the player mover and the jump, vault and death motors (the vault needs raycast, sample
  and path length, which NavWorld has, and the straight path, which it has not).

**Where it would earn its place later.**

- **Our own Hostile mover**, if the 300-Hostile soak shows Emerald's movement over budget: path searches for
  hundreds of Hostiles spread over frames in jobs is exactly what it is for.
- **Many predicted movers on the server.** `MoveLocations` is "slide these positions along the navmesh" in one
  batch, which is what checking a few hundred players' WASD input would be.
- **Reach checks in bulk** (Autopilot target picking, spawn placement).

**Recommendation.** Leave navigation as it is. Before anything else here is built, measure: one Profiler marker
round the server's movement and path calls, read in the buffed dummy fight and in the first Hostile soak. If
pathing shows up, move those calls to NavWorld one at a time behind a small query class; nothing else changes.

## 3. The thing to fix first: your own character should move at once

Keep the server in charge of where everyone is. Let the player's own machine **move its own character ahead of
the server** and be corrected when the server disagrees.

- Netcode for GameObjects 2.x has this built in: `AnticipatedNetworkTransform` (the owner anticipates, the server
  stays the authority, `OnReanticipate` smooths a correction).
- The owner runs the same checks and the same agent locally for its own character, so its first step happens the
  frame the order is given. The server runs them too, as now, and its result wins.
- Speed is still the server's, so nothing new can be cheated: a client that runs ahead is pulled back.
- Things that must agree on both sides: the Run cost (refused orders snap back), the vault (already planned on the
  server and played by RPC), jump (already has a reject path).

Size: medium. It touches `PointAndClickPlayer`, the three motors' "who owns the agent" checks, and the animation
motor (which already measures speed on remote proxies). It needs a careful two-machine test with added latency.
**This is worth doing whether or not WASD ever is.**

## 4. WASD next to click-to-move

A choice in Settings ▸ Controls: **Click** (as now) or **Keys**. Clicking still moves in both; Keys adds:

| | Proposal | Open choice |
| --- | --- | --- |
| Direction | W A S D relative to the camera (the camera is a free orbit that never follows the character, so this is the natural mapping) | |
| How it moves | The owner moves its agent a step each frame along the navmesh (it slides along edges); the direction is sent to the server about 30 times a second and replayed there | |
| Keys | W, Q, E and R are Hotbar 11 to 14 today. In Keys they need new homes | **Which keys**: for example the four mouse-side keys F, G, V, C are taken by windows; Shift + 1 to 4 is free |
| Stopping an order | Any direction key cancels a click order, the skill queue's walk-in, Follow and Autopilot (the hook exists: `MoveOrdered` / `ManualCommand`) | |
| Run cost | Paid per order today | **Per second while running**, or per distance |
| Jump | A click on a landing spot today | **Space jumps forward** by the rank's distance; the click stays |
| Vault | Armed when an order crosses an obstacle | Auto-vault when pushed into a low obstacle for a moment, or on Space |
| Targeting | Click only today | **A "nearest enemy" key (Tab) and "next"**; needed once the hand is off the mouse |
| Facing | The character turns toward travel | Strafe while a target is selected? (new animations) |

Size: medium, after section 3. Without section 3 it would feel worse than clicking.

## 5. A controller

| | Proposal |
| --- | --- |
| Move / camera | Left stick moves (section 4's mover), right stick orbits, stick click recentres |
| Targeting | A button for nearest enemy, the D-pad for next / previous, a button for nearest friend or NPC |
| Hotbar | 14 slots do not fit 4 face buttons: hold a trigger for slots 1 to 4, the other for 5 to 8, both for 9 to 12; bumpers for two more |
| Interact / pick up | One button: talk, open, pick up the nearest |
| Windows | The expensive part, below |

What makes it large:

- **The windows are pointer-only.** HUD buttons are made unfocusable, nothing answers UI Toolkit's navigation
  events, about twenty window controllers read `Keyboard.current.escapeKey` directly, Y / N for requests is a
  hard-wired key, and the Pack, the Vault, Trade and the HUD layout editor are drag-and-drop. Each needs a focus
  order and a "pick up / put down" way of moving items.
- **`PlayerInput` switches control scheme by itself**, and every gameplay binding is in the Keyboard & Mouse
  scheme: a pad press would switch them off. The schemes need sorting first.
- **Key rebinding refuses pad buttons on purpose** (`KeyBindings`, with a test that says so).
- **Prompts show keyboard glyphs only.** That is a standing rule of this project; a controller needs it changed
  to "the glyphs of whatever was used last".

A sensible middle: **movement, camera, targeting and Hotbar on a pad, with the windows still on the mouse**. That
is perhaps a third of the work, and is how many PC MMOs ship pad support first.

## 6. Order, if it is built

1. **Measure** navigation and movement cost on the server (hours).
2. **Owner-side prediction** (section 3). Helps today's game by itself.
3. **Keys** (section 4), with Tab targeting.
4. **Pad for movement, camera, targeting and Hotbar**; windows on the mouse.
5. **Pad in the windows**, one window at a time, starting with the Pack.
6. NavWorld only where step 1, or a later soak, shows pathing on the frame.

## Sources

- Unity scripting reference: `Unity.AI.Navigation.LowLevel.NavWorld`, `NavWorld.MoveLocations`, `NavWorld.BeginFindPath`
  (docs.unity3d.com, 6000.5 and 6000.6).
- "Why you should switch to NavWorld in Unity 6.6" (video, youtu.be/rFyHcozTF3Y) and its sample project
  (github.com/letquit/Unity66-NavWorld-Job-Pathfinding): a job that maps points, searches in slices and reads the
  corridor back; it benchmarks 200 path queries against `NavMesh.CalculatePath`, and does not move agents.
- This repo: `Gameplay/Player/Runtime/PointAndClickPlayer.cs`, `PlayerJumpMotor.cs`, `PlayerVaultMotor.cs`,
  `Gameplay/Combat/Runtime/PlayerMeleeCombat.cs`, `PlayerSkillQueue.cs`, `Gameplay/Player/Runtime/KeyBindings.cs`,
  `_Docs/Plans/hostiles-plan.md` (the navigation budget).
