# DOTS clone: what is tested in the Unity Editor, and how (read-only research, 29 Oct 2026)

Nothing was edited, no Unity opened, no server started. Tags: [R] read in the repo/PC (file:line), [E] my estimate (not measured),
[U] unverified, must be proven at the milestone named.

Where the sources are: the plan and designs are NOT in the clone's checked-out tree (the clone is on `seams/player-key-2`, 773438fd6).
They are on branch `dots/netcode-entities` (a4972a295): `Assets/_Game/_Docs/Plans/netcode-for-entities-migration.md` (276 lines) and
`.../netcode-for-entities-designs/{test-harness-plan,linux-server-and-burst,ghosts-design,monster-ai-design,persistence-and-ui-bridge}.md`.
Read with `git show dots/netcode-entities:<path>`. Line numbers below are file lines of those blobs (`plan:NN`, `harness:NN`, `linux:NN`).

## 0. Short answer

Yes, the plan tests the DOTS clone in the Editor, but only partly, and the Editor is the cheap, early gate, not the final one.

- Layers A (pure rules), B (ECS system tests on a bare World) and C (server world + client worlds in one process, hand-ticked, loopback)
  all run **inside Unity's test runner in EditMode**, no scene, no Play (harness:35-43, 62-70). Same runner as today's ~70 `*RulesTests`.
- Layer D (2-client test with a dedicated test server) uses **one Editor client in Play (PlayMode Tools = Client) + a built Windows
  test server + a built client as client 2** (plan:19-20, 84-85, 237; harness:115-119, 150-174). Editor Play is allowed for D,
  never for F (latency) or E (load) (harness:41-42, 85-92).
- Everything that depends on IL2CPP, Burst AOT, Linux, stripping/baking exclusion, real performance or SIGTERM needs a build (section 4).
- Gap in the plan: it says "EditMode/PlayMode in the Editor" for layer C (harness:39) but the only batch runner we have is EditMode-only
  (`run_editmode_tests.py:62`, `-testPlatform EditMode`). Recommendation: write layer C as EditMode tests (Entities worlds work there), so the
  existing runner works unchanged; a `--platform PlayMode` switch is a small script change if ever needed.
- Also a gap: plan:99-100 says "M1 gate = connect/approve in the test world (no dedicated-server 2-client test yet)" while plan:24-26 says
  every milestone ends with a 2-client test. Master should settle which wins for M1 (I assume M1 is test-world-only, M2 is the first real 2-client).

## 1. How tests are launched (the three entry points)

| Entry | Command | Notes |
|---|---|---|
| Batch EditMode (layers A, B, C, G-unit) | `python "Assets/_Game/Tools/Editor/Architecture/Scripts~/run_editmode_tests.py" [--filter "KryticalNova.Items"] [--checkout <path>] [--read]` | [R run_editmode_tests.py:62-66] runs `Unity.exe -batchmode -projectPath <checkout> -runTests -testPlatform EditMode -testResults Logs/editmode-results.xml -logFile Logs/editmode-tests.log -buildTarget Win64 -EnableCacheServer -cacheServerEndpoint 127.0.0.1:10443 ...`, below-normal priority (line 70). Refuses if the project is open in an Editor (is_open, line 45-58). Compile errors = no results file, script prints log tail (line 76-82). Exit 1 on any failure. Note: `-batchmode` without `-nographics`; add nothing, but for RAM you may add `-nographics` by hand [U that no test needs a GPU]. |
| Editor menu / Test Runner window | Window > General > Test Runner > EditMode; or Krytical Nova menus | Same tests, in the open Editor. Needs the Editor open = RAM. Only when Master allows. |
| Build + external processes (layers D-G) | `ServerBuildBuilder.Build(Os.Windows, "Builds/Server/TestDots/KryticalNovaServer")` (~9 min), `explorer.exe "<folder>\Start Server.bat"`, `SaveService --test`, Editor Play from Zone01 with `Library/KryticalNova.TestSession` (`port=7797`, `join=1`), `kill_test.py` | harness:138-148; memory test-server-play-procedure. |
| Structure gates (no Unity) | `python .../dependency_map.py --check`, `compile_check.py`, `compile_check.py --server` | Cheap, run before every commit (CLAUDE.md). Caveat: they do not run Entities/Netcode source generators or Burst, so for ECS code they prove nothing until the analyzer DLLs are added (plan:96-98, linux:157). A Unity batch compile is the real compile for ECS. |

Existing test asmdefs are all Editor-only (24 `*.Tests.Editor.asmdef`, e.g. `Assets/_Game/Networking/Tests/Editor/`), no PlayMode tests, none starts
a NetworkManager (harness:192). New ECS test asmdefs follow the same naming `KryticalNova.<System>.Tests` (harness:59-60).

## 2. Per milestone: what runs in the Editor, what needs a build

Common gate for all (harness:210-215): layers A+B+C green with no skipped test; `dependency_map --check` 0 uphill/0 two-way; compile checks;
2-client script (harness:156-174); server log 0 exceptions/errors/Burst warnings; G green on the test DB; latency 60 and 150 ms pass, 250 ms/5 % loss survives; ports free after.
Time/RAM columns are [E] unless marked; the only measured numbers are the empty-project spike (1.2 GB peak, 287 s cold / 87 s incremental Linux build, plan:264-265).

Legend for "RAM": batch Editor on this project [E] 6-12 GB steady (the project's own Editor was 10-28 GB in Play, memory editor-memory-watch); built server 1-2 GB, built client 3-7 GB (harness:117-119).

| Milestone | Editor, batch EditMode (A/B/C) | Needs build / 2 clients (D) / bots / latency / econ / Linux | Launch | RAM / time [E] | Pass criteria |
|---|---|---|---|---|---|
| **M0** docs & seams | A: `PlayerKey`/`PlayerKeys`/`ServerAccounts` tests (written, NOT yet run: plan:277); seam tests for NetRole/NetClock/NetSession/IServerLeaving/IWorldTargets; stub-base compile gate; existing ~70 rules tests unchanged | None (still NGO). Existing NGO tests stay the safety net. | `run_editmode_tests.py` (whole suite or `--filter KryticalNova.Core`, `.Networking`, `.Persistence`) + the three python gates | 6-10 GB; first run in the clone 10-25 min import [R script docstring:4-6] + a few min tests | Suite green, 0 uphill/0 two-way, compile_check 59/59 and --server 27/27 (plan:273-274). Seams can go to main as NGO PRs (Q3). |
| **M1** packages, bootstrap, Linux spike, early spikes | **Heavy Editor milestone.** H0 (read real test class names in `Library/PackageCache/com.unity.netcode@*/` and entities, harness:24-29); C-tests S1 replicated buffer GhostFields, S3 client plan survives rollback, S9 RPC size, S11 subscene-in-Addressable-map loads on client+server with matching prespawned-ghost hash (plan:94-96); connect/approve in test world; compile-gate proof on a deliberately broken `IJobEntity` (plan:96-98); entity counts return to baseline | S-Linux batch spike: Windows->Linux x64 Mono and IL2CPP server with Burst, subscene baking, EG excluded, `lib_burst_generated` check (plan:101-103); build-id gate across Editor 6000.6.3f1. Spike already proved empty-project IL2CPP+Burst Linux build and WSL run (plan:262-270). S11 against a **built** Addressables catalog is not provable in the Editor [U]. | Batch EditMode for C; batch `-executeMethod ServerBuildBuilder...` for builds (Master OK needed, plan:103) | C: 8-12 GB; Linux builds: Editor 10-30 GB (memory note 22 Oct: OOM at 52 GB Editor) and 3-9 min each [E: 287 s cold was an empty project] | All C-tests green incl. S1/S3/S9/S11; test world: connect/approve/refuse paths; Burst compiled in server build; no `FloatMode` surprises. 50 thin bots join/leave 10 min, server tick p95 < 5 ms idle (harness:220) belongs to a build. |
| **M2** connection & identity | C: approval via `IApprovalRpcCommand` chunks, refusal paths (wrong build, ban, full, bad token), roster path, `NetworkSetupTests` replacement (one bootstrap/config location; ghost collection check, harness:184), `RpcBudget` throttle test (harness:186), `ServerAccounts` by PlayerKey | D: first true 2-client join/leave on test server 7797. Encryption (DTLS) and Relay-driver spikes need real sockets: loopback IPC in the test world cannot prove them [U]; do them with built server + client. SIGTERM->save-all->exit, CLI flags/ports contract, ServerControlPage need a built (Linux/Windows) server. | C: batch EditMode. D: section 3 script | C seconds-tens of seconds; D needs 1 Editor + server 1-2 GB + client 3-7 GB = ~15 GB free commit | Join/leave x2 clients clean; refusals tested; flags parse; SIGTERM saves < 90 s (plan:170). |
| **M3a** walk/run/stop, teleport, save/identity | B: predicted move system on a bare World (integers, mm); determinism guard (run same input twice, identical components, harness:56-58); A: path planning rules. C: input->server position, rollback replay, TeleportSeq snap, passthrough save byte-identical load->save for every part (plan:221) | D: owner "feel" checkpoint (plan:243-247), prediction error p95 <= 0.05 m at 60 ms, <0.15 m at 150 ms, rollbacks < 5/s at 150 ms (harness:221). S4 Windows vs Linux parity needs two builds. | B/C batch; D manual with screenshots | B/C light; D ~15 GB | See left. Prediction error is measured in a **development build client**, not the Editor. |
| **M3b** jump/death MotionPlan, warp spike S7 | B/A: `MotionPlan` evaluated by tick (pure); C: late-join sees same plan | D for warp visuals (Animancer + KINEMATION, GameObject side, Editor Play OK for a screenshot) | same | same | Same position on both clients at plan end; no teleport at 250 ms/5 %. |
| **M3c** vault/wall-run/waterstride, follow, skill walk-in | A/B for the data functions | D: needs scene geometry (Zone01) and visuals; no owner prediction of vault/wall-run (Q16) | same | same | As M3b. |
| **M4** combat + lag compensation + wallet slice + one Trade | A: `CombatCalculator` Burst-port parity (10k-tuple, plan:151), Hostility pure function; B: `PositionHistory` ring (16 ticks), hit window systems; C: hit result identical server vs both clients, bad-request refusals (harness:222) | G: `TransactionLedgerTests` (EditMode) + `kill_test.py` against `SaveService --test` 7802 / `krytical_nova_test` (harness:94-100); trade with disconnect mid-trade; golden ledger identical to NGO; D 2-client fight with screenshots; F latency profiles | EditMode batch; `python Persistence/Service~/KillTest/kill_test.py` (test DB only) | G needs Postgres service running; light RAM | Hit results identical; golden ledger identical; no double spend at 250 ms; owner feel checkpoint #2. |
| **M5** monster prototype (Prowler) | **P0 pure simulation tests, EditMode, no netcode** (plan:119); B Burst brain in `IJobEntity`; budget < 0.3 ms/tick for 102 monsters is only an estimate (plan:151), measure in a build | P1 ghost + pooled GameObject view: D screenshots; P2 reward bridge dry run (ledger rules) | batch EditMode; D manual | light / 15 GB | P0 green; tick budget measured in a build, not Editor; entity count back to baseline. |
| **M6** hostiles in full | A/B: `HostileStatCurve` level x tier table, archetype baking tests, sleep/respawn rules; nav-grid unit tests; baking a zone subscene in the Editor (RAM heavy, see section 3) | E: bot soak with 102 monsters + 50 bots, tick p95 < 15 ms of 33 ms (harness:223, plan:86); built server only | batch EditMode; E: `Builds/Bots` + measure server 7790/8091 | E needs built server + bots process, no Editor | p95 < 15 ms, snapshot bytes within agreed KB/s, hostile cap, drops visible to both clients. |
| **M7** world objects | A/B: Kiosk, world clock/weather singleton, music-state ghost, ZonePortal rules; C: drop/kiosk static ghosts reach both clients, relevancy | D: Kiosk sale = economy G run; GM tools (commands + GM window + control page) by hand | same | same | Golden ledger for Kiosk sale; GM controls in both places. |
| **M8** remaining ~34 systems, 4 groups | Per group: keep pure-rule tests, add C test per edge (RPC->IRpcCommand, ghost/buffer, paged view RPC); Trade group adds: disconnect mid-trade, crash after Commit, NetworkId reuse, pending roster call at disconnect (plan:126-127) | G for Group 2 (Storage, Shopping, Kiosk, Trade); D per group; F per group | EditMode batch + kill_test; D | per group ~15 GB for D | Each group: 2-client test passes, ledger golden unchanged, 0 save regressions (`SaveMigrationTests`, `SplitSaveTests` stay). |
| **M9** scale & Linux parity | C: relevancy/importance rules; replay harness unit tests (H7) | Everything here is build-only: thin-client bots (many per process), per-type send rates, war-map crowd preset, Linux IL2CPP server on WSL2, replay with state hashes Windows vs Linux (harness:234-246), 100 bots / 60 min soak, Windows vs Linux prediction error within 10 % | built player + bots; WSL2 | server 1-2 GB, bots few GB; Editor closed | Section 8 of plan ticked; hashes identical for server-only systems; p99 tick < budget; balanced ledger. |
| **M10** compare/merge/cutover | Full EditMode suite, compile checks, dependency check on the merge candidate | Full regression of M1-M9 on Linux build; real-server migration is outside the clone's rules (plan:17) and only on the owner's go-ahead | batch + builds | - | Owner go-ahead. |

Notes that apply to the table:
- Bot sign-in bypass must be proven compiled out of release builds (plan:239 item 25). That is a **build** test.
- Thin clients cannot test client relevancy/prediction cost; use real clients (plan:239).
- NetCodeTestWorld may be internal-only (harness:21-29, [U] until H0). Fallback is our own helper using public `ClientServerBootstrap.CreateServerWorld/CreateClientWorld` + loopback, ticking `world.Update()` by hand. Decide at H0 before writing C-tests.
- For B-tests, set Burst "Synchronous Compilation" so a managed-type slip fails in the test, not only in a player (harness:54-55).
- Statics: with domain reload off in the Editor (section 3), every static needs `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset (plan:223 item 9) or Play-in-Editor results are unreliable. Batch EditMode starts a fresh domain per run, so it is immune.

## 3. What the clone needs BEFORE ITS FIRST OPEN

### 3.1 Current state of the clone [R]
- Path `C:\Users\dlaco\Krytical Nova II DOTS`, git worktree, branch `seams/player-key-2`, Unity `6000.6.3f1` (`ProjectSettings/ProjectVersion.txt:1`). Contains only `Assets`, `Packages`, `ProjectSettings`, `Temp`, docs. **No `Library/`** (checked: absent). Assets = 16 GB. Uncommitted modifications exist in `Networking/Runtime` (NetworkConnectionManager, NetworkHealth, RpcBudget, ServerRecords, ServerControlPage) = seam 1b in progress; another writer is active.
- `Packages/manifest.json` has NO Entities/Netcode-for-Entities/Burst/Collections/Mathematics/Physics entries. It does have NGO 2.13.3, dedicated-server 3.0.0, multiplayer.playmode 3.0.0, multiplayer.tools 2.2.12, test-framework 1.8.0, addressables 2.11.2, URP 17.6.0.
- ProjectSettings [R]: `EditorSettings.asset:29-30` `m_EnterPlayModeOptionsEnabled: 1`, `m_EnterPlayModeOptions: 3` (domain reload AND scene reload both OFF); `ProjectSettings.asset` `scriptingBackend Standalone: 0` (Mono), `managedStrippingLevel Standalone: 0`, `stripEngineCode: 1`; `MultiplayerManager.asset` `m_EnableMultiplayerRoles: 1`, no stripping types set; `ProjectSettings/Packages/` has settings for ai.assistant, dedicated-server, multiplayer.tools only (nothing for entities/netcode yet).

### 3.2 Packages to add (all built into Editor 6000.6.3f1) [R `...\Editor\Data\Resources\PackageManager\BuiltInPackages\<pkg>\package.json`]
| Package | Built-in version | Depends on | Need |
|---|---|---|---|
| com.unity.entities | 6.6.0 | burst 1.8.25, collections 6.6.0, test-framework.performance 3.2.0, profiling.core, SBP 1.23.1 | yes |
| com.unity.netcode | 6.6.0 | transport 6.6.0, entities 6.6.0 | yes |
| com.unity.entities.graphics | 6.6.0 | entities, render-pipelines.core | **Presentation side only** (plan:233 item 23 defers it; add only if owner insists); never in a Simulation/server asmdef (linux:158) |
| com.unity.burst | 2.0.0 (the registry lock of the real project already resolves 2.0.0; entities asks >= 1.8.25) | - | transitive |
| com.unity.collections | 6.6.0 (already in real lock) | burst | transitive |
| com.unity.mathematics | 1.4.0 (already in real lock) | - | transitive |
| com.unity.transport | 6.6.0 (already in real lock, depth 1, builtin; NGO asks only 2.6.0 so both coexist) | collections, burst | transitive |
| com.unity.physics | 6.6.0 | entities | **NOT needed**: plan:114 decided no Unity Physics, own `PositionHistory` |
| com.unity.test-framework | 1.8.0 (already) | - | already; entities' own tests need test-framework 1.8.0 (harness:53) |
| com.unity.dedicated-server 3.0.0, com.unity.multiplayer.playmode 3.0.0 | already in manifest | - | keep (roles, Editor client switch) |
Add in the clone via manifest edit by whoever owns M1 (versions `6.6.0` for entities/netcode/entities.graphics). NGO stays until cutover (plan:199). Expected first compile after adding: package import + source generators run; the first Editor open after the manifest change recompiles everything (several minutes [E]).
[U] confirm at M1 that NGO 2.13.3 and N4E coexist in one assembly set without type clashes (`Unity.Netcode` namespace is shared by both packages' names; NGO uses `Unity.Netcode`, N4E uses `Unity.NetCode` (capital C), the compiler treats these as different; still verify).

### 3.3 Assemblies
- Today 184 asmdef/asmref files; 49 asmdefs reference `Unity.Netcode.Runtime` [R grep]. Core and UI must lose it first (plan:91-92, M0). The new ECS assemblies per plan: `KryticalNova.Monsters` (Simulation), `KryticalNova.Monsters.Presentation` (client-only), `X.Net` edge in each system's own asmdef, Bootstrap, test asmdefs `KryticalNova.<System>.Tests` (Editor platform, harness:59). They must pass `dependency_map.py --check` (Entities Graphics only in Presentation; linux:158).
- ECS assemblies need `Unity.Entities`, `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics`, `Unity.NetCode`, `Unity.Transport`, `Unity.Entities.Hybrid` (baking authoring) refs; `Unity.NetCode.Hybrid`/`Unity.Entities.Graphics` only on presentation [U exact names: confirm from the installed asmdefs at H0].
- Server compile: ECS asmdefs must be `UNITY_SERVER` safe (no EG refs); `compile_check.py --server` template must get the Entities/Netcode source-generator and analyzer DLLs (plan:96-98).

### 3.4 Project Settings to set before/at first open [U names where marked, verify in the Editor at M1]
1. **Player > Scripting Backend**: plan:253-260 says IL2CPP for client and Linux server, Mono for fast iteration; the real project is Mono today (`ProjectSettings.asset` Standalone 0). Keep Mono as the Editor default; builds switch via `TestBuildBuilder.MenuIl2Cpp` / `ServerBuildBuilder` IL2CPP switch (to write, plan:258). IL2CPP Windows needs the VS C++ workload (plan:257); Linux IL2CPP uses `com.unity.toolchain.win-x86_64-linux 1.1.0` resolved automatically (plan:264-265).
2. **API compatibility level**: .NET Standard 2.1 vs .NET Framework: not stated in the plan; Entities/Burst work with both; leave as the real project has it [U, not read].
3. **Burst**: Project Settings > Burst AOT Settings (player builds only, linux:24). Keep Burst enabled; in tests use Jobs > Burst > Synchronous Compilation. For predicted code `[BurstCompile(FloatMode.Deterministic, FloatPrecision.High)]` (plan:27-28). Server build must fail if `lib_burst_generated` missing (plan:30-31).
4. **Entities / baking**: Project Settings > Entities (Baking, scene/subscene settings) [U names]. Subscenes bake in the Editor on import: this is the main new RAM cost ("RAM cost of subscene baking in the Editor", plan:237).
5. **Multiplayer settings** (Project Settings > Multiplayer, N4E): Netcode `ClientServerTickRate` 30/30 (plan:101); client build type `ClientOnly`, server `ClientAndServer` set automatically for the DGS (linux:115-116); **Excluded Baking System Assemblies** for the server: `Unity.Entities.Graphics` and presentation-only bakers (linux:118-120, 156; unverified menu label).
6. **Multiplayer PlayMode Tools window** (Window > Multiplayer > PlayMode Tools): Play Mode Type = **Client** for the Editor client against the built test server (plan:237, 84-85), set Auto Connect Address 127.0.0.1 / Port 7797 [U field names]. "Client & Server" only for C-style quick host checks; never together with a long-running client build. Do not enable the simulator in the Editor (no-runtime-evals-in-play: it dropped the Editor to 1 fps three times; harness:88-90).
7. **Enter Play Mode options**: currently domain reload and scene reload OFF (`EditorSettings.asset:29-30`). Consequences for DOTS: static fields (including `SharedStatic`, caches, singletons like `NetSession`) survive between Plays; all statics must reset in `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`; add a lint/test (plan:223 item 9); ECS worlds are created/disposed by Entities on Play enter/exit [U that this is clean with reload off on 6.6, verify]; stale compiled eval code stays loaded (memory no-runtime-evals-in-play). Batch EditMode runs are unaffected. If Play-in-Editor results look odd, re-test once with reload ON before blaming code.
8. **Scripting defines**: `UNITY_SERVER` is automatic in server builds (linux:115). Existing Android/Linux defines list DOTWEEN etc. [R ProjectSettings.asset]; no new define is required by the plan. If a Linux-only or DOTS-only define is wanted (e.g. `KN_DOTS`), decide at M1 [U].
9. **Pin the Editor** 6000.6.3f1 for clone, client, server (plan:98). Read the 6000.6.6f1 Known Issues first if upgrading (linux:131-132).
10. Keep `Fast Script Reload` off the ECS assemblies: FSR does not cover ISystem/Burst (plan:29-30, linux:88-92) so every ECS change needs a normal compile = no hot-iteration loop; plan test edits for batch.

### 3.5 First import: time, RAM, disk
- Real project's Library is **70 GB** [R, `du` of `C:\Users\dlaco\Krytical Nova II\Library`] (task note said ~68 GB). The clone's Assets are 16 GB; a clone-only `Library` will grow to roughly the same size [E 60-70 GB]; C: has 820 GB free [R df]. No cost to git/LFS (Library is not committed).
- Time: docstring says first import in a worktree takes 10-25 min **with the Accelerator's shared results**, less afterwards (`run_editmode_tests.py:4-6`). Without cache hits it can be much longer [E 1-2 h]. The Accelerator is running now: `unity-accelerator` PIDs 8280 and 12748, TCP 10443 LISTENING [R netstat]. The script uses it (`-cacheServerEndpoint 127.0.0.1:10443`, line 33, 63-66) so the first batch run both downloads existing import results and uploads new ones. The Accelerator is shared with the main project; it helps only for assets with identical import hash (same content and importer settings), so the new Entities/Netcode package import and any baked subscenes will still be imported cold [E].
- RAM: the first full import spawns several `AssetImportWorker` processes; on 11 Oct two workers sat at ~10 GB each (memory editor-memory-watch). Peak commit for editor + workers can reach 25-40 GB [E]. Physical RAM is the limit, not commit (see right below).
- RAM right now [R, 29 Oct]: free physical 8.9 GB, free commit 75.2 GB; no Unity.exe or KryticalNova process running. **8.9 GB is below the 11-12 GB gate** (Master's gate plan:248 says >= 12 GB; the task says >= 11 GB). So do not start now.
- Editor open adds a **modal-dialog risk**: a Unity dialog (script compile failure, "Opening file failed", package-manager prompts, Synty package helper installs shadergraph) in a batch-less Editor can leave the process growing 6 GB/h (memory mppm-clone-testing, editor-memory-watch). Prefer batch with `-batchmode` (dialogs auto-dismissed) for the first import.

### 3.6 Safe first-open checklist (follows the RAM rules)
1. **Ask Master first** (plan:19, 248). No Editor in the clone without OK.
2. Confirm the clone's working tree is quiet: `git status` clean or the other writer is done (seam 1b modifications are uncommitted now). Do not import while someone edits Assets.
3. Free RAM >= 11-12 GB: `$o=Get-CimInstance Win32_OperatingSystem; $o.FreePhysicalMemory/1MB; $o.FreeVirtualMemory/1MB` (need > 10 GB commit as well, harness:110-113). Read private bytes of every `Unity.exe`. Close ComfyUI (port 8188) first. **The owner's main Editor must be closed or at least not in Play and under ~35 GB**; never Stop-Process it (memory editor-memory-watch). One Editor at a time: two Editors (main + clone) were the cause of the 8 Oct hang.
4. Ports: not needed for the import, but `netstat -ano` to confirm 7797/8097/7790/8091/7802 free and note PIDs of 7777/7778/8090/7801 (real server) before any later run (harness:131). Check `127.0.0.1:10443` Accelerator is listening.
5. Run **batch, not interactive**, below normal priority, with the Accelerator: `python .../run_editmode_tests.py --checkout "C:\Users\dlaco\Krytical Nova II DOTS" --filter "KryticalNova.Core"` as the first run (small filter, so the import is the only long part). Do not add the DOTS packages in the same step: first prove the baseline imports and the existing tests pass on the seam branch, then add packages as a separate commit and run again (isolates package breakage).
6. Watch in short polls (no silent waits, memory no-silent-waits): free commit every few minutes; if free physical < 3 GB, kill the batch Unity (this is my own process; plan:249) and tell Master. Tail `Logs/editmode-tests.log` for "error" lines only.
7. After the run: `Logs/editmode-results.xml` read with `--read`; make sure no `Temp/UnityLockfile` and no stray `Unity.exe`/`AssetImportWorker` left; report RAM peak and Library size to Master (to calibrate this table).
8. Only after step 5-7 are green: add packages (3.2), run compile gate proof (M1), then bring the Editor up interactively if layer D requires it (single Editor, Master's OK again).
9. Interactive Play in the clone: follow Server-Guide "test server" steps exactly; stop Play myself the moment the test ends; never compile during Play; no polling evals; `FrontEndPlayMode.Enabled=false` + `Zone01_StarterTown` for a straight join (memory test-server-play-procedure, stop-my-play-sessions, no-compiling-during-play). The clone has its own `Library`, so its `KryticalNova.TestSession` file cannot clash with the main project's (harness:146-147).
10. MPPM Player 2: do not start unless the owner asks (memory mppm-clone-testing, 23 Oct); second client = built client.

## 4. What can NOT be tested in the Editor (needs a build, usually Windows server + client, or Linux)

| Item | Why the Editor cannot | Where it is tested |
|---|---|---|
| IL2CPP behaviour (AOT generic gaps, stripping) | Editor is Mono JIT | IL2CPP client and Linux server builds (plan:253-260) |
| Burst AOT really compiled, `lib_burst_generated` present, "Burst skipped" on invalid target | Editor Burst is JIT; AOT problems appear only in player builds (linux:24-26, 63-65) | Build log scan + file check, hard fail (plan:30-31, 230) |
| Linux anything: x64 Burst variants, SIGTERM->save-all, systemd KillMode, flags/ports contract, health/presence, Postgres/SaveService over Linux socket | Not on Linux | WSL2 first, Hetzner test VM later (plan:165-186) |
| Windows vs Linux float parity, prediction-error comparison, state-hash replay | Needs two builds | harness:230-246, plan:183 |
| Dedicated Server role stripping, baking exclusion for the server, server build size | Build-time only | S-Linux spike and every server build (linux:115-123) |
| Subscene content from a **built** Addressables catalog (S11 in a player) and Entities scene loading in a player | Editor loads from AssetDatabase | server+client builds [U] |
| Real performance numbers: tick p95/p99, snapshot bytes, GC, 60 fps | Editor overhead, safety checks, JIT | Development/release builds; "measure only in builds" (plan:230) |
| Latency/loss profiles 60/150/250 ms, simulator | Simulator in the Editor dropped it to 1 fps three times (memory no-runtime-evals-in-play; harness:41-42, 88-90) | built clients with `-simProfile ... -simLoss ...` |
| Thin-client bot load (50-100+ bots), 60 min soak | Needs headless players | built bots process against measure server 7790/8091 (harness:75-83) |
| Encryption (DTLS) and Relay driver | Test world uses loopback IPC, no real sockets | built server + client (plan:236) |
| Release-build checks: bot sign-in bypass compiled out, dev-only code stripped | Editor builds are dev | release build test (plan:239) |
| ServerApp, launcher, Smart App Control, installer paths, Server list | Windows apps outside Unity | existing ServerApp/Launcher test routes (CLAUDE.md) |
| Real server / real DB paths | Forbidden for the clone (plan:17) | never in the clone |

## 5. Open points for Master (items I could not settle from the docs)
1. Layer C in EditMode (works with existing runner) vs PlayMode (needs runner change). Recommend EditMode.
2. M1 gate: test-world-only (plan:99) or also a 2-client (plan:24-26). Recommend test-world-only at M1, first real 2-client at M2.
3. Exact N4E settings window labels, asmdef names and `NetCodeTestWorld` visibility: unknown until the packages are imported (H0).
4. Package versions: all 6.6.0 built-in for entities/netcode/entities.graphics/collections/transport; mathematics 1.4.0; burst 2.0.0. No registry download needed.
5. RAM at this moment is under the gate (8.9 GB free physical): wait or ask the owner to close apps before the first batch import.
6. The clone has uncommitted seam-1b edits and "another writer is active": the first import should start from a committed, quiet tree.
