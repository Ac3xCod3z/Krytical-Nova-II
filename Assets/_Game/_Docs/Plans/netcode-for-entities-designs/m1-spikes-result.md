# DOTS M1 spikes: results (29 Oct 2026, Unity 6000.6.3f1, Entities/Netcode/Transport/Collections 6.6.0)

Project: `C:\Users\dlaco\KN-worktrees\dots-spike` (throwaway, not git). Everything ran as EditMode tests in batch mode
(`-batchmode -nographics -runTests -testPlatform EditMode`), one Unity at a time via the watchdog
(`run_spike_tests.ps1`, copied beside this file). A full run is about 21 s wall time, min free RAM seen 14.9 GB (start 18.5 GB).
**Final run: 32 tests, 32 passed, 0 failed.** Raw measured lines: `dots-m1-spikes-raw-results.txt`.
Files in the spike project: `Assets/Spike/Tests/{KnTestWorld,KnSpikeAssets,SpikeTests}.cs` (asmdef `KnSpike.Tests`, Editor only),
`Assets/Spike/Runtime/{KnSpikeComponents,KnGhostAuthoring,KnPrefabHolderAuthoring}.cs` (asmdef `KnSpike.Runtime`, all platforms).
Manifest change: added `com.unity.test-framework` 1.8.0 (it was already in PackageCache).

## H0: test-world helpers (public vs internal)
- `NetCodeTestWorld`, `NetcodeWorld`, `EditModeTestWorldStrategy`, `SubSceneHelper`, `NetCodeScenarioUtils` are ALL `internal`
  (package folder `Tests/Runtime/Utils`, asmdef `Unity.NetCode.TestsUtils.Runtime.Tests`, which is not compiled in a user project).
  They are reachable only through `InternalsVisibleTo` for fixed assembly names. We must not hijack those names.
- Built our own: **`Assets/Spike/Tests/KnTestWorld.cs`** (copy to the game's test asmdef). Public API only:
  `ClientServerBootstrap.CreateServerWorld/CreateClientWorld`, `NetworkStreamReceiveSystem.DriverConstructor` (public static) with
  `DefaultDriverBuilder.CreateServer/ClientNetworkDriver(new IPCNetworkInterface(), settings)`, `NetworkStreamDriver.Listen/Connect`,
  `NetworkStreamInGame`, `SceneSystem.LoadSceneAsync`. Server plus N clients in one process over **IPC loopback (no sockets)**, ticked by hand
  (`world.SetTime(new TimeData(..))`, then Initialization, Simulation, Presentation groups: server first, then clients). 30 Hz default.
  API: `CreateWorlds(n)`, `LoadSubScene(guid)`, `Connect(goInGame)`, `Tick(n)`, `TickUntil(cond,max)`, `Logs`/`Interesting`;
  settings `SimulationTickRate, MaxMessageSize, RequireConnectionApproval, SimulatedDelayMs/JitterMs/DropInterval`
  (UTP latency simulator on the client driver; deterministic because the driver time is fixed per tick).
- Gotchas the helper handles (each cost one iteration): remove the worlds from the Editor player loop
  (`ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop`); disable `UpdateWorldTimeSystem` and drive time ourselves;
  `Application.runInBackground = true` (else netcode logs an Error); `LogAssert.ignoreFailingMessages = true` and capture logs with
  `Application.logMessageReceivedThreaded` (job exceptions come from worker threads, the non-threaded event misses them);
  `NetworkTimeSystem.s_FixedTimestampMS` is internal (set by reflection, optional);
  `RequireConnectionApproval` can only be set after the first world update (the `NetworkStreamDriver` singleton does not exist before) and before Listen/Connect;
  call `EntityManager.CompleteAllTrackedJobs()` before reading `NetworkStreamConnection` between ticks.
- Ghost prefabs without the internal `BakingUtility`: the public route is the real one: prefab asset (`GhostAuthoringComponent` + our authoring
  MonoBehaviour) referenced from a holder authoring inside a **SubScene**, loaded with `SceneSystem.LoadSceneAsync`. `KnSpikeAssets.Ensure()` generates
  prefabs and subscenes at test start (needs a SAVED active scene before additive scene creation; `prefabId` is stamped by the asset save).
  Authoring MonoBehaviours must live in a NON-Editor asmdef (`AddComponent` on an Editor-only script fails and returns null).
- So pyramid layer C of `test-harness-plan.md` is feasible exactly as planned, with KnTestWorld in place of NetCodeTestWorld.

## S1: ghost component + buffers (PASS)
Tests: `S1_GhostComponentAndBuffers_Replicate_OwnerBufferOnlyToOwner`, `S1_FixedList4096_ReplicatesAtMost64Elements(64|100)`,
`S1_FixedString4096_GhostField_LargeStrings(300|1000|1500|4000)`.
- Two clients; ghost is OwnerPredicted, `GhostOwner.NetworkId` = client A. `IComponentData` with `[GhostField]` `int`, `FixedString64Bytes`,
  `FixedString512Bytes`, `FixedList64Bytes<int>` (12 ints), `FixedList4096Bytes<int>` (64 ints) arrived intact on owner AND observer; every change (delta) arrived.
- `[GhostComponent(OwnerSendType = SendToOwnerType.SendToOwner)]` buffer (`int` + `FixedString64Bytes`): 5 entries intact on the owner, delta
  (change one, append one) arrives; on the observer the buffer exists on the entity but stays EMPTY (length 0) = no leak. A normal ghost buffer replicates
  to both, including shrinking.
- **Surprise (compile error):** `FixedList4096Bytes<T>` (capacity above 64) as a GhostField does not compile: "has a capacity greater than 64 elements...
  use the GhostFixedListCapacity attribute". Needs `[GhostField, GhostFixedListCapacity(Capacity = 64)]`. Sending 100 elements: client gets 64 and netcode
  logs an Error ("only the first 64 elements are going to be replicated"). So the docs' "4096 B / 64 elements" is right, but the attribute is mandatory.
- `FixedString4096Bytes` GhostField with 4000 chars arrived intact in 6 ticks (a snapshot above the MTU is fragmented, no error). Bandwidth is the only reason to avoid it.
- Plan impact: `CharacterNetState` CAN be a ghost component (capability confirmed); keeping LookVersion for bandwidth still stands. Owner-only private state
  (PartyView, inventory mirrors) works as designed.

## S3: client-planned path + prediction rollback (PASS)
Tests: `S3_ClientOnlyPlan_SurvivesRollback_AndIntegerProgressEqualsServer_300Ticks(0|100|200 ms)`,
`S3_ServerCorrection_ClientRollsBackAndConverges_PlanSurvives(0|100|200 ms)`.
- Setup: predicted ghost (`DefaultGhostMode=Predicted`), `S3Move{[GhostField] int ProgressMm, SpeedMm=150}`; plan buffer `S3PathCorner` (NOT a ghost field)
  written ONCE by a client-only system in `GhostInputSystemGroup` (the server has its own plan system); `S3PredictedMoveSystem` (ISystem, Burst,
  `FloatMode.Deterministic`, in `PredictedSimulationSystemGroup`, identical code on both worlds) advances progress in mm (clamped to 40000) and derives a
  float3 position along 5 corners. A 512-slot history ring (tick -> progress + position float bits) is written on every run.
- Rollback is real: with 100 ms one-way simulated delay the client ran the predicted system **3994 times for 329 distinct ticks (up to 13 re-simulations of one tick)**;
  200 ms: 5322 runs, max 17. With 0 ms delay there is no re-simulation (client is only about 1 tick ahead), so rollback tests MUST simulate latency (KnTestWorld does).
- Result at all three delays: plan buffer written exactly once (`PlanWrites==1`) and still 5 corners after 330 ticks, so **a client-only buffer survives
  snapshot rollback** (snapshot restore touches only ghost fields). Progress ends at 40000 on both. **0 mismatches over 330-337 compared ticks** of integer progress
  AND of the position float bits (bitwise equal) between the server world and the client world.
- Server-only correction (+7000 mm at one tick, not predictable by the client): the client converges; 0 mismatches from correction tick +12 on; plan intact
  (only the history entry of the correction tick itself differs).
- **Limit of this evidence:** both worlds run in ONE process on ONE CPU/OS, so equal float bits prove the code path is deterministic, NOT that a Windows client
  equals a Linux IL2CPP server. That needs the Linux server build (SpikeServer already builds) fed the same inputs and compared with the Windows run.
  Not done (no WSL allowed, no player run requested). The design rule "state is integers, positions are derived" stays mandatory.
  Burst was active in the Editor (Burst frames in the log), so the Deterministic-mode code ran compiled.

## S9: RPC size + chunked approval (PASS, numbers measured)
Tests: `S9_RpcCommand_PayloadSizeSweep_DefaultDriver(100..4000)`, `S9_RpcCommand_PayloadSize_MaxMessageSize1472(1400|1440|1500)`,
`S9_ChunkedApproval_ThreeChunks_Completes`.
- IRpcCommand `{int Seq; FixedString4096Bytes Text}` (ASCII, 1 B per char): delivered intact up to **1350 chars**; **1400 chars fails** with an exception from the
  RpcSystem job: `RPC ... was too big to be sent! It was 1415 bytes [netcode header: 5B, rpc message header: 4B, payload ...], but UTP only offered a packet
  buffer of 1378B`. The RPC is lost (no retry, no fragmentation); the log line is the only symptom.
  => **Hard limit per RPC on the default driver: 1378 B on the wire = about 1369 B payload after the 9 B headers.** The whole RPC must fit ONE packet; the 16 KB
  fragmentation stage does not apply to RPCs.
- `NetworkConfigParameter.maxMessageSize` is capped at **1472** (UTP: "must be > 0 and <= 1472"; world creation fails above that). At 1472 the buffer is 1450 B:
  1400 chars OK, 1440 chars (1455 B) fails. So the ceiling is about 1.45 KB per RPC however it is tuned.
- Chunked approval: `NetworkStreamDriver.RequireConnectionApproval = true` on both sides, `IApprovalRpcCommand { int Index, Total; FixedString512Bytes Data }` x3
  (500+500+450 chars = 1450 chars) sent as soon as the client connection state is Handshake; server reassembles, compares, adds `ConnectionApproved`; the client
  then received its NetworkId. Client state sequence: Connecting(t2) Handshake(t3) Approval(t5) Connected(t7), chunks sent at t3. **Completes.**
  Chunks arrived in order here; reassembly must still tolerate any order (use Index/Total and a transfer id).
- Plan impact (sections 5/12): any RPC that can carry more than about 1.3 KB (Unity-token proof, roster pages, menu pages, chat history) MUST be chunked by us, each chunk
  at most about 1000 B payload for margin. Add a unit test that fails if any IRpcCommand's maximum serialized size exceeds about 1300 B. FixedString512 chunks are a good unit.

## S11: SubScene with prespawned ghosts (Editor PASS, player/Addressables UNVERIFIED)
Test: `S11_SubScene_PrespawnedGhosts_LoadOnServerAndClient_Match`.
- Generated a SubScene asset with 3 instances of a ghost prefab (plus a parent scene with a `SubScene` component, `AutoLoadScene=false`). Loaded with
  `SceneSystem.LoadSceneAsync(world, guid, BlockOnImport|BlockOnStreamIn)` in BOTH the server and the client world (no Addressables), then connected and set
  `NetworkStreamInGame`: the server has 3 prespawned ghosts and the client gets the same 3, **same ghostIds (-2147483647 to -2147483645) and same positions**
  (equal within 0.01; an exact float compare failed once, positions go through the LocalTransform ghost variant).
- Surprise: `SceneSystem.IsSceneLoaded` returned true while the ghost entities were still 0 in BOTH worlds; they appeared 1 tick after Connect.
  Never count content right after the load call; wait a tick or for a marker singleton.
- Addressables (Entities docs shipped with the Editor): `content-management-intro.md` says Addressables/AssetBundles/Resources "either don't work with Entities or are
  suboptimal"; Entities builds its own content archives from the subscenes included in the build. `streaming-loading-scenes.md`: the build only detects authoring
  scenes referenced by `EntitySceneReference` and `SubScene` MonoBehaviours; scenes referenced by GUID are missing from builds. In the Editor all scenes always work
  (baking is triggered on demand), which is exactly what these tests used.
  => In a player, an Addressable GameObject scene holding a `SubScene` component is only safe if a build proves that the subscene's entity scene file is included.
  The safe design is `EntitySceneReference` baked into a component (or weak scene references via `RuntimeContentManager`), loaded from a system.
  **Needs a player build to confirm.** Exactly what is needed: the `com.unity.addressables` package in the spike manifest, a map scene marked Addressable and NOT in
  Build Settings, one Windows (or Linux server) player build, run once. NOT built (instruction).

## Compile gate: Entities/Netcode source generators in a plain `dotnet build` (PASS)
- Where they live (same files in two places): project `Library/PackageCache/com.unity.entities@<hash>/Unity.Entities/SourceGenerators/*.dll` and
  `Library/PackageCache/com.unity.netcode@<hash>/Runtime/SourceGenerators/{NetCodeAnalyzers,NetCodeSourceGenerator}.dll`; the Editor install holds a copy at
  `C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Data\Resources\PackageManager\BuiltInPackages\com.unity.{entities,netcode}\...` (same relative paths).
  Unity's own exact list is in `Library/Bee/artifacts/<dag>/<Assembly>.rsp` (`-analyzer:`, `/additionalfile:`, `-r:`, `-define:`, `-langversion:9.0`, `/unsafe+`).
- Analyzer lines needed (entities, under `...\Unity.Entities\SourceGenerators\`): `Common.dll, JobEntityGenerator.dll, SystemGenerator.Common.dll,
  SystemGenerator.EntityQueryBulkOperations.dll, SystemGenerator.LambdaJobs.dll, SystemGenerator.SystemAPI.Query.dll, SystemGenerator.SystemAPI.QueryBuilder.dll,
  SystemGenerator.SystemAPI.dll, SystemGenerator.dll, Unity.Entities.Analyzer.CodeFixes.dll, Unity.Entities.Analyzer.dll`; netcode (under `...\Runtime\SourceGenerators\`):
  `NetCodeAnalyzers.dll, NetCodeSourceGenerator.dll`. In the csproj, one line per dll:
  `<Analyzer Include="$(ProjectRoot)\Library\PackageCache\com.unity.entities@de96d69a35c5\Unity.Entities\SourceGenerators\JobEntityGenerator.dll" />`
  (the hash changes per package version). Netcode ALSO needs its 42 templates as
  `<AdditionalFiles Include="...\com.unity.netcode@<hash>\Runtime\SourceGenerators\Templates\**\*.NetCodeSourceGenerator.additionalfile" />`,
  otherwise it errors "Missing internal Netcode package template".
- Proof: `rsp_to_csproj.py` (copied beside this file) turns a Unity `.rsp` into an SDK-style csproj (`net10.0`, `NoStdLib`, 283 `<Reference>`, 20 `<Analyzer>`,
  42 `<AdditionalFiles>`). `dotnet build` on it with a file holding `partial struct NoExecute : IJobEntity {}` and a two-`Execute` job FAILS with
  **`error SGJE0008`** (2 errors, 0.5 s). The same sources with the analyzers removed compile with exit 0 = a silent false green. So the gate MUST include the
  analyzers. Simplest alternative without any csproj: `dotnet "<Editor>\Data\DotNetSdk\sdk\8.0.318\Roslyn\bincore\csc.dll" @Library\Bee\artifacts\<dag>\<Asm>.rsp` (2 s, exit code = result).
- Caveats: needs the compiled reference DLLs and the rsp from at least one Unity compile (a populated Library). The netcode generator prints thousands of
  `info NetCode:` lines; filter them. A bare ISystem with a non-component in `SystemAPI.Query` shows the generator ran (errors inside `*.g.cs`) vs plain CS0453 without it.

## What this means for the plan
1. Test pyramid layer C works with our own helper in EditMode in about 20 s for 32 tests: no scene, no sockets. Keep one KnTestWorld; add loss/jitter profiles there.
2. RPC budget: 1369 B payload hard cap per RPC (about 1450 B at maxMessageSize 1472, the UTP maximum). Chunk everything bigger; add a size check test.
3. Fixed lists as ghost fields need `GhostFixedListCapacity` (at most 64 elements); strings up to 4096 B work but cost bandwidth.
4. Client-only planned path buffers are safe across rollback; keep "plan once outside the predicted group, integers inside".
5. OPEN: cross-OS float determinism (Windows client vs Linux server): needs the Linux build run, not possible in-process.
6. OPEN: subscene and Addressables in a player build: needs one player build plus the Addressables package; design with `EntitySceneReference` meanwhile.
7. Authoring MonoBehaviours, bakers and components must be in a non-Editor asmdef; tests in an Editor asmdef that references it.
