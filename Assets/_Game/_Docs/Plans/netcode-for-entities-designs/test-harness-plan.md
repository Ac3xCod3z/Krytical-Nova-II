# DOTS migration: test harness plan

Written 29 Oct 2026 for the dots-migration mini-boss. Docs only; no Unity opened, no code changed.
Work happens only in `C:\Users\dlaco\Krytical Nova II DOTS` (branch `dots/netcode-entities`).

## 0. What is verified and what is not (read first)

Verified in official docs (Netcode for Entities 1.6 manual, docs.unity3d.com/Packages/com.unity.netcode@1.6/manual/):
- Thin clients (`thin-clients.html`): stripped-down clients, only systems flagged `WorldSystemFilterFlags.ThinClientSimulation`
  run; no rendering; no ghosts, so `AutoCommandTarget` does not work and you set `CommandTarget` and create the input
  entity/buffer by hand; `World.IsThinClient()` tells them from full clients (`IsClient` is true for both). The page itself
  recommends thin clients in built players to stress and soak a server: a config option creates n thin-client worlds beside
  the main client, joining the same IP:port.
- Network simulator (`network-connection.html`): Unity Transport's `SimulatorUtility` is configurable in Netcode for
  Entities, reached through Multiplayer > PlayMode Tools; the page says to test "frequently" with it on. It does NOT list
  delay/loss/jitter settings there; those live on the PlayMode Tools page (not fetched, 404/linked only).
- Prediction (`prediction.html`): `GhostPredictionSmoothingSystem` reconciles and reduces prediction errors over time. No
  numbers or test API given.
- Burst float determinism: https://docs.unity3d.com/Manual/burst/float-precision-determinism.html (from shared context).

NOT found in public docs (my fetches of the API page and the testing page returned 404, and no netcode ECS package is in
either project's PackageCache yet): `NetCodeTestWorld` (namespace `Unity.NetCode.Tests`), the Entities test fixture
(`ECSTestsFixture`), Bootstrap/`CreateWorlds`/`Connect`/`Tick`. I know these from the package's own test sources, but I
cannot cite them. **Task H0 (first thing after the package is added, no Editor needed to read it):** open
`Library/PackageCache/com.unity.netcode@*/Tests/` (or `Tests~`) and `com.unity.entities@*/Unity.Entities.Tests`, record the
exact class names and whether they are public/internal-visible, and fix section 1 layer C accordingly. If `NetCodeTestWorld`
is internal and not reachable, fallback: write our own small helper that makes a server world plus N client worlds with
`ClientServerBootstrap.CreateServerWorld/CreateClientWorld` (public API) and loopback IPC, ticking `world.Update()` by hand.
Do not trust this document for those names until H0 is done.

## 1. Test pyramid

Cost and speed go down the page; run the cheap layers on every change, the dear ones per milestone.

| Layer | What | Where it runs | Cost | When |
|---|---|---|---|---|
| A. Pure C# rules | Everything that is already plain C# (GearLadder, Trade, Wallet, Cadence, Respawn, Hostility...) | EditMode, no world, later also `dotnet test` outside Unity | seconds | every commit |
| B. ECS system tests | One system (or a few) on a bare `World`: set up entities, `Update()`, assert components | EditMode (Entities test framework) | seconds | every commit touching a system |
| C. Netcode test world | Server world + 1..N client worlds in one process, loopback, hand-ticked: ghost spawn, command input, prediction, RPC, connect/approve | EditMode/PlayMode in the Editor, no scene | tens of seconds | every networking change, per milestone |
| D. Integration | Dedicated TEST server build (Windows, later Linux) + 2 real clients + test save service | built player + one Editor | minutes, manual with screenshots | per milestone |
| E. Load | Thin-client bots against the test/measure server | batchmode, no graphics | minutes | per milestone from the first ghost milestone |
| F. Latency/loss | Same as D/E with simulator on | built clients (never the Editor, see no-runtime-evals-in-play) | minutes | per milestone |
| G. Economy/save | TransactionLedgerTests + kill_test.py on the test DB | EditMode + python vs 7802 | minutes | any save/economy change |

### A. Pure rules
Rule: new simulation logic is written as static/pure functions or `struct` data first (the repo already does this: ~70 `*RulesTests`).
For DOTS, a rule lives in a plain, Burst-friendly static class (blittable inputs, no managed types) and is used by BOTH the
ISystem and the test. Test it once as plain C#, once through the system (layer B). Keep an optional `dotnet test` project
for the managed-only rules so they run without Unity (saves RAM; the SaveService already builds with dotnet).

### B. ECS system tests
- Unity.Entities test framework (`com.unity.entities` tests need `com.unity.test-framework` 1.8.0, already present). Create a
  `World`, add the system via `world.GetOrCreateSystem<T>()`, build entities with `EntityManager`, call `world.Update()`,
  assert. Use `EntityCommandBuffer` playback explicitly; use `BeginSimulation`/`EndSimulation` ECB systems as in game.
- Burst check: run a subset with Burst compile synchronous (`Burst: Synchronous Compilation` on) so a managed-type slip
  fails in the test, not only in a player build (shared-context warning: Burst errors show only in player builds).
- Determinism guard test per predicted system: run the same input sequence twice and assert identical component values
  (catches accidental `UnityEngine.Random`, time, ordering). Float cross-platform (Windows vs Linux) is tested in section 6.
- Each test asmdef: `KryticalNova.<System>.Tests`, Editor-only platforms, references the system asmdef; follows repo layer
  rules (a lower system's tests never reference a higher one). Namespace `KryticalNova.<System>.Tests`.

### C. Netcode test world (see H0 caveat)
Goal: prove netcode behaviour without a scene, a built server, or 2 Editors (RAM!). Cases to write as milestones land:
connect + approval (build id, ban, full server -> refusal, roster request path), ghost spawn/despawn on client, ownership
(owner-predicted player vs interpolated others), command stream (input -> server position), RPC budget (token bucket, 171
call sites map to RPCs/commands), relevancy/importance (replaces `SpatialInterestManager`), snapshot size, server-only
state not leaking to clients, disconnect cleanup (ServerRecords/Accounts keyed by NetworkId instead of ulong client id).
Tick the worlds by hand for N ticks; assert on server and client worlds side by side; set a simulated latency in the test
world's driver if the helper exposes it (decide at H0).
These run in the Editor, so they obey the Play rules below (never during the owner's Play; no compile while Play).

### D. Integration (dedicated test server + 2 real clients)
Template in section 3. Real server and real DB never involved.

### E. Load: thin-client bots
- Build a thin-client path in the client player: a launch option `-bots n` (replaces/extends `MeasureBot`) that creates n
  thin-client worlds next to (or instead of, in `-batchmode -nographics`) the main client; each follows the leader address.
  Each bot sends scripted input (walk a loop, jump, use a skill, open/close pack) via commands/RPCs; there are no ghosts to
  predict (docs) so bots only measure server cost + bandwidth.
- Auth: bots need a test-only way past `RequireProof` on the TEST server (the existing measure server solves this today;
  carry that over, test server only, never on the real one).
- Pass numbers in section 5 (server tick time, bandwidth per client, p95 snapshot size, GC, bots up for 10 minutes without disconnect).
- Reuses existing ports/layout: `Builds/Bots/` folder, `bots.txt`, measure server 7790 / 8091, `MeasureTools`.

### F. Latency / loss simulation
- Three profiles, always the same: **60 ms** (good), **150 ms** (typical), **250 ms** (bad), each with 0 % and 2 % loss,
  jitter 10 % of delay. 250 ms + 5 % loss is the "survives" profile (no crashes, correct end state, reconnect works).
- Use Unity Transport's simulator (`SimulatorUtility`, Multiplayer > PlayMode Tools per docs). Configure it in a **development
  build** client via launch options / `Simulator` settings on the driver, NOT in the Editor: the owner's note
  (no-runtime-evals-in-play) says the Multiplayer Tools NetworkSimulator preset dropped the Editor to 1 fps three times.
  Exact simulator parameter names: confirm at H0 from the PlayMode Tools page (docs gave none).
- A launch flag `-simProfile 60|150|250 -simLoss 0|2|5` is the planned entry point; it sits in the same debug-only code as the bots (compiled out of release).

### G. Economy/save
- `TransactionLedgerTests` keep running unchanged (they test pure ledger + `SaveTogether`; no netcode).
- `Persistence/Service~/KillTest/kill_test.py` against `SaveService --test` (7802, DB `krytical_nova_test`) after any milestone that touches
  save, trade, kiosk, wallet or the player/roster handoff. Never the real DB (7801 / `krytical_nova`).
- New DOTS-specific economy test: run a trade, a Kiosk sale and a Crew founding through the DOTS server (layer D or C with a fake
  `IBatchSaveStore`) and assert the ledger lines + Creds tallies are identical to the NGO build for the same inputs (golden file).
- Server loop test: kill the DOTS test server mid-trade; on restart the all-or-nothing rule holds (same assertion as kill_test).

## 2. Safe running: RAM, ports, start and stop

### RAM budget (31 GB PC, often ~5 GB free)
Rules from memory notes: editor-memory-watch (check free commit before EVERY Play; main Editor above ~35 GB = ask owner to
restart; never Stop-Process the owner's Editor; never match processes by a word that my own command contains), mppm-clone-testing
(owner 23 Oct: do not start Player 2 unless asked; clone can leak 6 GB/h and take port 7801), no-runtime-evals-in-play.
Preflight (copy-paste, PowerShell):
```
(Get-CimInstance Win32_OperatingSystem).FreeVirtualMemory/1MB   # need > 10 GB commit
Get-Process Unity,KryticalNova* -EA SilentlyContinue | % { "{0} {1} {2:N1} GB" -f $_.Id,$_.ProcessName,($_.PrivateMemorySize64/1GB) }
```
Stop and tell the owner if free commit < 10 GB, or a clone is stuck behind a dialog.

Cheapest test set-up, in order of preference:
1. **Layers A-C only** inside one Editor (no Play, no second process): default for every commit.
2. **One Editor (client, Play from zone) + built TEST server** (+ optionally one built client): the normal D set-up. Server build
   runs headless, about 1-2 GB, not another Editor.
3. **Built server + two built clients** (no Editor at all): each ~3-7 GB. Needs 15 GB free commit. Preferred over MPPM.
4. MPPM Player 2: only if the owner asks (his 23 Oct rule). Never together with a long-running client build.
Never two Editors plus a build plus ComfyUI. Close ComfyUI first (close-comfyui-when-done).

### Ports
| Use | Game/UDP | Page/TCP |
|---|---|---|
| REAL server (never touch) | 7777, 7778 (status) | 8090; real save service 7801; real DB `krytical_nova` |
| DOTS test server | 7797 | 8097 (status port 7798 if used; check free) |
| Measure / bot server | 7790 | 8091 |
| Old NGO test server | 7796 | 8096 |
| Test save service (`--test`) | 7802 | DB `krytical_nova_test` |
Before each run: `netstat -ano -p UDP` and `-p TCP`, confirm 7797/8097/7790/8091/7802 free and note the PIDs that own 7777/7778/8090.
Server settings for the DOTS test server: `"serverList": []`, `saveService` pointing at 7802 only, own `pagePassword`. The test
server's settings file must be refused on 7777/7778/8090/7801 by a check in the start script (grep and abort).
GM for tests: use `TestGM` on the test page 8097, never "Ascension"/"Craven" (isolated-play-testing).
Session file: `Library/KryticalNova.TestSession` in the DOTS clone (`port=7797`, `join=1`); NOTE the DOTS clone has its own
Library, so it cannot clash with the main project's session file.

### Start / stop (follow Server-Guide "test server" steps exactly; test-server-play-procedure)
1. Ports free (above). 2. Build: `ServerBuildBuilder.Build(Os.Windows, "Builds/Server/TestDots/KryticalNovaServer")` in the DOTS clone
   (about 9 min, poll a marker file; check Editor private memory first, below ~35 GB).
3. Start the test save service: `SaveService --test` (7802) from the clone's `Builds/Bots/SaveService` (or the Server app's test mode).
4. Start the server ONLY with `explorer.exe "<folder>\Start Server.bat"` (TrustFailure from any terminal).
5. Client: built client, or Editor: open `Zone01_StarterTown`, `FrontEndPlayMode.Enabled = false`, write session file, Play; check
   `Time.frameCount > 0` before driving anything.
6. Clean-up, always, same step as the test ends: stop Play (before any code edit/compile, no-compiling-during-play), stop ONLY the PID
   holding 7797 plus its Start Server.bat cmd.exe, stop only the `SaveService --test` PID I started, delete the session file, reopen
   Title, `FrontEndPlayMode.Enabled = true`, re-run netstat to prove 7797/7802 are free. Report ports used.
No eval polling in loops; max one eval per step; say what I am doing at least every minute while the owner watches.

## 3. Per-milestone 2-client test script (template)

Copy this block into each milestone's section; fill the brackets. Screenshots are Game-view PNGs via
`ScreenCapture.CaptureScreenshot` in a coroutine (no Recorder; testing-feedback-screenshots), one per step, one-line caption each;
motion = side-view film strip with ffmpeg. Keep the Editor window focused or Play drops to ~1 fps.

```
MILESTONE [Mx: name]   build [commit sha]   date [..]   tester [session]
Pre: layers A,B,C green (counts: __ / __).  RAM check done (free commit __ GB).  Ports 7797/8097/7802 free (netstat).
Setup: test save service --test (PID __), test server build (PID __), TestGM bound.
Client 1 = [Editor Play from Zone01 | built client], character Tolan (Hegemony).
Client 2 = [built client | thin bot | MPPM only if owner asked], character Arinis (Covenant).   Both join "TestDots 7797".
Steps (each: action -> expected -> screenshot name -> PASS/FAIL):
 1. C1 joins; C2 joins.              Both see own HUD; server log: 2 players, no errors.   S01_join_c1, S01_join_c2
 2. C1 and C2 see each other.        Other player present, correct faction look, name tag. S02_see_other_c1/c2
 3. C1 moves; C2 watches.            Smooth on C2, no teleports; C1 no rubber-banding.     S03_move_c2
 4. [milestone feature, solo]        ...
 5. [milestone feature, cross-client] e.g. C1 hits hostile, C2 sees HP bar fall and drop appear on BOTH.
 6. Disconnect C2 hard (kill process); C1 sees despawn within __ s; rejoin C2 restores same state.
 7. Same-faction-only systems (party/trade): C1/C2 flip `TradeZone.crossFaction` via GM command on the test server, not eval.
 8. Latency run: relaunch C2 with -simProfile 150 -simLoss 2; repeat steps 3 and 5. Then 250/5% survival check.
 9. Telemetry dump (section 5) saved: perf_*.txt + tick/prediction counters.
Post: save check (restart test server, state persists; ledger lines match), server log scan: 0 errors/exceptions, 0 "Burst" warnings.
Clean-up: per section 2. Report: PASS/FAIL per step, screenshots, ports used, anything left running (must be none).
```
Rules: host-mode-only is never a pass (drops once never reached clients); two clients are mandatory for any networked claim.

## 4. Mapping existing tests to DOTS

| Existing | What it covers | Under DOTS |
|---|---|---|
| ~70 `*RulesTests` (Gear, Trade, Wallet, Kiosk, Cadence, Respawn, HostileLoot, StatCurve...) | pure rules | Keep unchanged; they are the contract. If a rule class moves to a Burst-friendly struct API, keep the old test and add the same table-driven cases through the system (B). |
| `SaveMigrationTests`, `SaveStoreTests`, `ServiceSaveStoreTests`, `SplitSaveTests`, `ServerRosterTests` | persistence, no NGO | Keep. Only the NGO-key tie changes: `ServerAccounts` keyed by N4E NetworkId (new test: remember/forget/ban by NetworkId; reconnect with new id). |
| `TransactionLedgerTests` | all-or-nothing + ledger | Keep unchanged (guard of the save rules in CLAUDE.md). Add DOTS golden-trade test (G). |
| `NetworkSetupTests` (YAML-parses NetworkManager prefab/44 scenes; guards the "setup hash must match" incident) | NGO config identical in every scene | NGO-only; retire with NetworkManager. Replacement: (1) a test that the single N4E bootstrap + `ClientServerBootstrap` config (tick rate, `ClientServerTickRate`, snapshot/ghost relevancy settings, prefab/ghost collection) lives in ONE place (subscene / settings asset) and no map scene overrides it; (2) a build-time check that every ghost prefab used is in a baked ghost collection (replaces `GameplayPrefabsSetup.SpawnedPrefabs` guard). |
| `CharacterRosterTests` (FastBufferWriter/Reader wire) | roster wire format | Keep the pure format tests; replace the NGO writer with the new wire (RPC struct / `FixedString` or byte buffer); add a round-trip test of the new serialisation, plus max size under the approval/RPC limits. |
| `RpcBudgetTests`, `ServerHooksTests` | token bucket per client | Keep logic tests; re-key by NetworkId; add a layer C test that a client spamming a request RPC is throttled and flagged. |
| `JoinRulesTests`, `AccountProofTests`, `ServerBansTests`, `GmRosterTests`, `ControlPageTests`, `ServerSettingsTests` | join/ID/bans/settings | Keep; approval path itself gets a layer C test (N4E connection approval). |
| `MeasureBot` (`-bot n`, via `JoinFlow`, dev builds) | load | Becomes the thin-client bot launcher (E): same flags/folders, joins as thin clients instead of full NGO clients. |
| `world_test.py` (WorldTest) | world directory (presence/ticket/redeem) | Unchanged, runs against 7802 SaveService `--test`; add ticket-handoff test when travel moves to DOTS. |
| `KillTest/kill_test.py` | crash safety of saves | Unchanged, test DB only. |
| `NetworkSetup`-style scene-wide grep checks, `dependency_map.py --check`, `compile_check.py` (and `--server`) | structure | Keep as gates; add `compile_check.py --linux` equivalent when Linux target exists (parity, section 6). Dependency map must stay 0 uphill / 0 two-way; the new ECS assemblies must fit the layers. |
| "no PlayMode tests, none starts a NetworkManager" (current state) | gap | DOTS adds layer C (first real netcode tests) and D scripts. |

## 5. Telemetry (tick, prediction error, bandwidth) and definitions of pass

Collected into the existing `PerformanceLogs/perf_*.txt` writer (`PerformanceLogRecorder`; it currently touches `Unity.Netcode`, so
it needs a DOTS-side feed through an interface in Core, per layer rules, not a new bus):
- Server: tick time avg/p95/p99 (ms), ticks over budget, entity counts (players, hostiles <=102, drops <=200), GC allocs per tick
  (target 0 in hot systems), snapshot bytes per client per second, ghost count per client, RPC rate per client.
- Client: predicted tick time, rollback ticks per second, **prediction error**: per predicted ghost (player position, health) log
  the delta between predicted state and the server snapshot at the ack tick; report mean, p95, max, count of corrections above a
  threshold (start value: 0.05 m; tune from the first run). Also interpolation delay, RTT, frame time (60 fps target, performance-target).
- Burst: log of any Burst compile warnings from the player build.
Netcode for Entities ships a netcode profiler/debug overlay (Multiplayer > PlayMode Tools window, Network Debugger/Netcode
Stats) but I could not verify exact names in docs; confirm at H0 and prefer our own counters if the built-in tools only work in the Editor.

### Pass/fail per milestone (milestone names are placeholders; map to the migration plan's list)
Every milestone must satisfy the COMMON gate, plus its own:

COMMON gate (all must hold):
1. Layers A+B+C green; no new test skipped or ignored. 2. `dependency_map.py --check` 0 uphill / 0 two-way; `compile_check.py`
and `--server` compile every assembly. 3. Section 3 script: every step PASS with screenshots, 2 clients. 4. Server log: 0 exceptions,
0 errors, 0 Burst warnings, no entity-leak (entity count returns to baseline after all players leave). 5. G green on the test DB.
6. Latency profiles: 60 and 150 ms steps pass; 250/5 % survives (no crash, converges to server state). 7. Cleanup proven (ports
free, no stray processes). 8. Encyclopedia + build list updated (CLAUDE.md rule).

Milestone-specific minimum (thresholds are proposals for the owner to confirm; start strict, loosen only with data):
| Milestone (proposed) | Own pass criteria |
|---|---|
| M1 Bootstrap + connect/approve, empty world | Connect/refuse paths all tested (wrong build, ban, full, bad token); 50 thin bots join/leave 10 min; server tick p95 < 5 ms idle |
| M2 Player ghost: movement, prediction | Position error p95 < 0.05 m at 60 ms, < 0.15 m at 150 ms; rollbacks < 5/s at 150 ms; no teleport at 250 ms/5 % loss; Windows build == Linux build result on the replay (see 6) |
| M3 Combat/skills/health (server-authoritative) | Hit results identical on both clients and server for the scripted fight; damage exploit tests (client sends bad request) all refused; rule tests unchanged |
| M4 Hostiles ECS (102/map) + drops (200/zone) | 102 hostiles + 50 bots: server tick p95 < 15 ms of 33 ms budget (30 tick); snapshot < agreed KB/s per client; hostile count cap; drops visible to both clients |
| M5 Economy: inventory, trade, Kiosk, vault, Crew | Golden ledger identical to NGO; kill test green; no double-spend under 250 ms; all-or-nothing preserved |
| M6 Persistence + roster + world/travel | Save/restart restores everything; ticket handoff across two servers on test ports; old-save carry-over test |
| M7 UI/presentation bridges (GameObject side) | Visuals follow ghosts; 60 fps on mid hardware with bots in view; no GameObject owns simulation state |
| M8 Load and soak | 100 thin bots (or agreed map cap) for 60 min on test server, no leak, p99 tick < budget, bandwidth within target |
| M9 Linux server parity + release candidate | Section 6 all pass; full regression of M1-M8 on Linux build |

## 6. Windows-server vs Linux-server parity check

Why: Burst float results may differ between platforms (shared-context warning). Client is Windows; the real server may later be
Linux (see `online-linux-cloud-servers-plan.md`, untracked in the real repo).
1. **Same inputs, two servers**: record a replay (input commands per tick + initial state) from a 2-client session. Replay it
   headless into (a) the Windows server build and (b) the Linux server build (both on test ports, test DB), same Unity, same Burst version.
   Dump server state hash per tick: positions/health/RNG state quantised to the ghost precision. Pass: identical hashes for the
   whole replay for all server-only systems; for predicted systems, the maximum position divergence is below the quantisation step.
2. **Client prediction vs Linux server**: Windows client against Linux server at 60/150 ms; prediction-error telemetry from section 5 must
   be within 10 % of the Windows-server run. A step in the error graph = platform float drift.
3. **Mitigations tested, not assumed**: with `FloatMode.Deterministic`/`FloatPrecision.Standard` on predicted jobs vs default; keep whichever
   passes with smallest cost (docs link in section 0).
4. **Build parity**: `compile_check.py --server` for both targets; server build log has no Burst warnings; both builds pass layer D script
   once. Linux runs: where to run it is an owner decision (no RAM on this PC for WSL + Editor; use a spare machine or WSL2 with the
   server only, test ports, the Linux service reading the Windows test save service at 7802 over loopback or a copy of the test DB).
5. **Save/DB parity**: same account/ledger result in `krytical_nova_test` from both servers for the golden trade.
6. Fast Script Reload does not apply to ISystem code (shared context), so parity runs always use full builds.

## 7. Task list (to schedule)
- H0 (read-only, no Editor): list real test classes in netcode/entities packages after install; update layer C and the simulator section.
- H1: test asmdef skeletons per new ECS system assembly; pure-rule project for `dotnet test` (optional).
- H2: netcode test helper (own or `NetCodeTestWorld`) + connect/approve/ghost tests.
- H3: thin-client bot + sim launch flags (debug builds only) replacing `MeasureBot`.
- H4: telemetry counters + `perf_*.txt` feed (via Core interface).
- H5: start/stop scripts for the DOTS test server with port/real-server guards (python or ps1, in Tools).
- H6: golden economy trade fixture; kill_test run recipe for DOTS test server.
- H7: replay recorder + state-hash dump for parity.

## 8. Open owner questions (also in the short report)
1. Second client for D: built client / MPPM / thin bot. Recommend built client (RAM, owner 23 Oct no Player 2).
2. Where Linux parity runs: spare machine / WSL2 / cloud VM. Recommend WSL2 server-only for tests, cloud later.
3. Prediction-error and tick thresholds in section 5: accept starting values or set your own? Recommend accept, tune after M2.
4. Bots may bypass sign-in on the TEST server only (as the measure server does): yes/no. Recommend yes, compiled out of release builds.
