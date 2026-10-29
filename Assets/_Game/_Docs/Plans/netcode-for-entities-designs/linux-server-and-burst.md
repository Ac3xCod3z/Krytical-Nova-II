# DOTS: Burst rules and the Linux dedicated server (research, 29 Oct 2026)

Read-only research. Nothing installed, no Unity opened. Tags: [V] = read on the cited official page; [U] = unverified or inferred
(say who must test it); [L] = looked at on this PC.
Unity docs moved to docs.unity.com; docs.unity3d.com/Manual/burst/... redirects there. Cited below by the page that was read.

## Part A. Burst

### A1. What the docs say (verified)

| Topic | Fact | Source |
| --- | --- | --- |
| Float determinism | `FloatMode.Deterministic` aims at the same results "across all supported platforms", **64-bit only**; uses deterministic math functions, turns off platform-varying optimisations, **flushes denormals to zero on all platforms**. NaN *bit patterns* are not guaranteed equal. Intrinsics / platform-specific instructions can still break it. | [V] https://docs.unity.com/en-us/engine/current/manual/programming-environment/script-compilation/burst/compilation/float-precision-determinism |
| Scope of that guarantee | Only inside Burst-compiled code. Float inputs computed elsewhere (managed C#, non-Burst), and function pointers from non-deterministic code, break it. Costs speed. | same page [V] |
| FloatMode | Default = Strict. `Fast` allows re-ordering and fused multiply-add (a*b+c) on hardware that has it, so results can differ by CPU. `Deterministic` = same everywhere. | same page [V] |
| FloatPrecision | Standard/Medium 3.5 ulp, High 1.0 ulp, Low up to 350 ulp for sin/cos/exp/log/fmod/pow. Default is Standard. | same page [V] |
| N4E is not deterministic | Netcode for Entities says it is "not a deterministic package"; determinism is not guaranteed with unquantised values; there is a "Quantization and determinism" section (1.5 page, body not read). | [V, via search snippet] https://docs.unity3d.com/Packages/com.unity.netcode@1.3/manual/prediction-details.html and .../netcode@1.5/manual/prediction-details.html |
| Managed types | Burst = subset of .NET without classes/managed objects. No managed arrays (use NativeArray), no `string`, `char`, `decimal`. | [V] https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-type-support.html |
| Static fields | Only `static readonly` supported; read-only managed arrays from static readonly fields only, not passed around, not modified, not multi-dimensional; unsupported static constructor = error BC1361. Mutable statics: use `SharedStatic<T>`. | [V] same page (statics), https://docs.unity3d.com/Packages/com.unity.burst@1.7/manual/docs/CSharpLanguageSupport_Lang.html (SharedStatic, via search; body not read) |
| Struct layout | Sequential, Explicit, Pack, Size ok; `LayoutKind.Auto` not. | [V] type-support page |
| Other type limits | `Enum.HasFlag` and enum methods no. `Span<T>` only inside jobs/function pointers, not across the Burst boundary. `ValueTuple` only inside Burst, not across the boundary (use a plain struct). | [V] type-support page |
| Exceptions | No `try/catch`. `throw` of simple patterns allowed; BC1370 warns when a throw only works in the Editor and should sit under `ENABLE_UNITY_COLLECTIONS_CHECKS`. | [V via search] https://docs.unity3d.com/Packages/com.unity.burst@1.7/manual/docs/CSharpLanguageSupport_Lang.html; [U] exact release-build behaviour of a throw: test it |
| Generics / calls | Calling generic methods (or methods on a generic declaring type) is not supported for direct calls; function pointers do not support generic delegates. | [V] https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-calling-burst-code.html |
| Player builds | Burst writes one native library per platform (Windows: `Data/Plugins/lib_burst_generated.dll`) loaded on first Burst call. Settings: Edit > Project Settings > **Burst AOT Settings** (player builds only). | [V] https://docs.unity.com/en-us/engine/current/manual/programming-environment/script-compilation/burst/building-projects ; https://docs.unity.com/en-us/engine/6000.6/manual/programming-environment/script-compilation/burst/building-aot-settings |
| Linux target | Host Windows/Linux/macOS can cross-compile desktop targets by default. Linux player = **x64 (SSE2, SSE4, AVX, AVX2), no external toolchain**. Linux arm64 is **not** in the Burst table. | [V] building-projects page above |
| Invalid target | If a target is unsupported or tools are missing, "Unity skips Burst and still builds": the game then runs the slow managed path with no error. | [V] building-projects page |
| CPU dispatch | At runtime Burst picks the best compiled variant for the CPU, but only SSE2/SSE4 are used on x86 desktop (a CPU with only SSE3 falls back to SSE2). AVX2 needs AVX2+FMA+F16C+BMI1+BMI2. | [V] building-projects page |
| Dedicated Server package | `com.unity.dedicated-server` 3.0 (matches Editor version). Needs the Dedicated Server Build Support module. Multiplayer roles strip **GameObjects/components** per role. For N4E "roles don't affect how entities and components are baked in subscenes". Known limit: runtime `InstantiateAsync` prefabs may keep stripped children. | [V] https://docs.unity3d.com/Packages/com.unity.dedicated-server@3.0/manual/index.html |

Not found in official text (do not state as fact): why FloatMode.Deterministic is or is not needed for *different compilers*
(Windows MSVC-free Burst/LLVM vs Linux Burst/LLVM are the same compiler, so the real difference is FMA/AVX2 versus SSE4
dispatch per CPU and `Fast` mode, not Windows versus Linux itself) [U, my reasoning]; search for a Burst-ECS-netcode
Windows/Linux drift report returned nothing Unity-specific. Treat as risk, measure it (see A3 test).

### A2. Where the owner's warning is right and where it is narrower

- Right: client prediction (Windows/Android/iOS, arm or x64, AVX2 or SSE4 picked per CPU) re-simulates what the server (Linux x64)
  simulated. Any float difference makes small mispredictions that the server corrects with a snap. N4E does not promise
  determinism [V]. Mobile ARM clients differ from x64 by design.
- Narrower: the difference comes from fused multiply-add, instruction choice by CPU, `FloatMode.Fast`, denormals, libm-style
  functions (sin/cos/pow), and unquantised data, not from "Linux". Mispredictions are small and corrected; the design must expect
  them, not prevent them.
- Practical design (all [U] until measured): keep predicted code to add/sub/mul/compare and `math.` functions; mark predicted
  systems `[BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]`; quantise ghost fields
  (`[GhostField(Quantization=...)]`) so a 1-ulp difference rounds to the same value; compare states as "close enough" never `==`;
  authority-only effects (damage, loot, Creds, progression) never run on predicted code. This matches the repo rule that the server
  decides progression.

### A3. DO / DON'T checklist for every system

DO
1. Put every predicted system under one assembly attribute policy: `[BurstCompile(FloatMode.Deterministic, FloatPrecision.High)]`
   on predicted jobs/systems; Fast only on presentation/non-predicted code.
2. Keep Burst code on blittable structs, `NativeArray/NativeList/DynamicBuffer`, `FixedString*`, `BlobAssetReference`; no classes,
   `string`, `List<T>`, `Dictionary`, delegates, LINQ, `foreach` over managed collections.
3. Use `SharedStatic<T>` for any mutable global; `static readonly` for constants.
4. Make a hot system an `ISystem` struct with `[BurstCompile]` on the struct and on `OnUpdate` (and `OnCreate` if it only touches
   unmanaged state). Put managed work (HTTP, logging backends, Addressables, GameObject calls) in a separate `SystemBase` or a
   plain MonoBehaviour, behind a small unmanaged request/result component.
5. Guard checks: `[Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]` / `#if` for throws; assume safety checks exist only in the
   Editor and development builds [U for N4E release builds: confirm per package].
6. Log from Burst with `Debug.Log` of a `FixedString`/literal only (supported in Burst); keep logs out of per-entity loops.
7. Build an actual **Burst-enabled Linux server** every week and run the headless smoke test; Burst problems in the Editor show
   in the Console as BCxxxx, but "Burst skipped" or platform-only differences show only in the player.
8. Pin the same Burst, Entities, Collections, Mathematics and Netcode versions on client and server (one Unity version, one
   `packages-lock.json`). Netcode's handshake also checks protocol/ghost hashes; the 1.12.0 changelog warns schema changes can slip
   past a hash check in some cases [V changelog], so keep a build-number gate (the repo already has "a game joins only a server of
   its own build").
9. Quantise every ghost field that clients predict from; test prediction with the net simulator (packet loss / jitter) before
   calling a feature done.
10. Run `compile_check.py --server` plus a Unity batch build for every change that touches ECS code (Part C).

DON'T
1. Don't use `FloatMode.Fast` (or fused multiply-add reliance) in predicted code, and never compare predicted floats with `==`.
2. Don't call `try/catch`, `string.Format`, `ToString()`, `Enum.HasFlag`, `Span` across a Burst boundary, `LayoutKind.Auto` structs,
   generic method calls, `ValueTuple` parameters in Burst entry points.
3. Don't use `System.Random` or `UnityEngine.Random` in predicted code: use `Unity.Mathematics.Random` seeded from the tick/
   entity so client and server draw the same number [U on the exact seeding pattern: N4E samples show it].
4. Don't read GameObject/Transform/Time.deltaTime/ScriptableObject data in Burst code; bake it into components/blobs
   (ScriptableObjects stay design data: bake to `BlobAsset`).
5. Don't use hardware intrinsics (`Unity.Burst.Intrinsics`) in predicted code [V: breaks cross-platform determinism].
6. Don't pass float results from non-deterministic code or function pointers into deterministic code [V].
7. Don't rely on `Burst` running: check the player log for "Burst skipped" or missing `lib_burst_generated` in the build output
   [V that Burst silently skips invalid targets].
8. Don't use `[BurstCompile]` on code that touches Animancer, KINEMATION, Sidekick, Cinemachine, Feel, UI: those stay managed
   presentation.
9. Don't leave `Debug.Log` or managed allocations in per-tick systems; GC pauses on the server cost ticks.
10. Don't expect Fast Script Reload on Burst/ISystem code. [U-partly] The Asset Store listing says generic methods/classes are
    not hot-reloaded and a forum thread says hot-reloaded jobs fall back to managed code with a warning because Burst never
    supports pre-compiled assemblies (https://discussions.unity.com/t/burst-compiler-failing-to-use-compile-methods-and-jobs-in-pre-built-managed-dll/874157 ;
    https://assetstore.unity.com/packages/tools/utilities/fast-script-reload-239351). No official statement about ISystem found.
    Rule: ECS code changes use a normal compile.

### A4. Mono vs IL2CPP and Burst on Linux

- Burst compiles Linux x64 with no toolchain [V]. Burst is independent of the scripting backend for jobs/ISystem. Burst
  works with IL2CPP and Mono players [U: I found no official line saying Mono is unsupported for standalone; confirm with the
  smoke test]. Entities/Netcode docs and many Linux DGS reports assume IL2CPP; the repo currently builds Mono (report string
  "Scripting backend: Mono, Dedicated Server target") [L ServerBuildBuilder.cs].
- Installed variants on this PC [L, listing of `...\6000.6.3f1\Editor\Data\PlaybackEngines\LinuxStandaloneSupport\Variations`]:
  `linux64_server_{development,nondevelopment}_{mono,il2cpp}`, `linuxarm64_server_*_il2cpp`, `linux64_player_*_il2cpp`.
  So the Linux Dedicated Server module IS installed, both backends, plus arm64 (IL2CPP only). Burst has no Linux arm64 row [V] so an
  arm64 server would run Burst-less (slow): do not choose ARM cloud boxes for the DOTS server until verified.
- Netcode changelog: no entries on Linux or IL2CPP; AOT function-pointer fixes were for iOS/WebGL only; netcode 1.4/1.5 let
  thin clients run on DGS builds [V] https://docs.unity3d.com/Packages/com.unity.netcode@6.6/changelog/CHANGELOG.html (first 100k of 175k
  chars read; older entries not read; the page has no Known Issues section).

## Part B. Linux dedicated server with Entities / Netcode / Entities Graphics

### B1. What the platform needs
- Editor module "Linux Dedicated Server Build Support" [V name in repo message; module files present [L]]; `com.unity.dedicated-server`
  3.0 package (version = Editor) optional but gives multiplayer roles for GameObject stripping [V].
- `StandaloneBuildSubtarget.Server` with `BuildTarget.StandaloneLinux64` (already what ServerBuildBuilder does) [L].
- `UNITY_SERVER` is defined automatically in server builds (and also set in the Editor when the Server subtarget is active) [V]
  https://docs.unity3d.com/Packages/com.unity.netcode@1.7/manual/project-settings.html . Netcode project setting: client
  `ClientOnly`, server `ClientAndServer` (applied automatically for the DGS) [V same page].
- Baking and subscenes: **roles do not change baking** [V dedicated-server page]. Use Project Settings > Multiplayer >
  **Excluded Baking System Assemblies** (server list) to leave out `Unity.Entities.Graphics` and any presentation-only baker
  assemblies; a Unity tracker entry reports 500 MB -> 170 MB server build after excluding Entities Graphics from server baking
  [V via search snippet] https://issuetracker.unity.com/issues/22822/graphics-assets-are-not-stripped-from-the-server-build-when-a-scene-is-saved-as-a-reference .
- Automatic dedicated-server shader/asset stripping does **not** apply to builds that go through the Scriptable Build Pipeline,
  which Addressables and Entities subscenes use [V via search snippet] https://discussions.unity.com/t/why-unity-compiles-shadres-for-dedicated-server-no-graphics-build-maybe-it-should-be-disabled/912884 .
  Consequence for us: keep presentation data out of server subscenes by baker/assembly separation, not by trusting Unity.
- Server-only world: `ClientServerBootstrap` creates worlds from the build type; custom bootstrap overrides `Initialize`, and
  `CreateServerWorld()` throws if the build is `ClientOnly` [V] https://docs.unity3d.com/Packages/com.unity.netcode@1.7/manual/client-server-worlds.html .
- Entities Graphics is not needed on the server: it only draws; remove from server asmdefs/bakers [V indirectly above].
- Known tracker items: a Dedicated Server build failing with the Entities package and an `EditorMultiplayerManager` compile error
  when DGS + Netcode packages are both installed were reported (fixed in Netcode 1.3.0-exp.1 per changelog [V]); an old Linux crash
  with no graphics device fixed in 2021.3.22/2020.3.44 [V via search] https://issuetracker.unity3d.com/issues/dots-netcode-0-dot-51-linux-server-build-runtime-not-working-properly ;
  server behaving differently in a build than in the Editor, fixed in Netcode 1.0.8 [V via search snippet]. None found specific to
  netcode 6.6 [V negative: the 6.6 changelog page has no Known Issues]. The 6000.6.6f1 release-notes page lists Known Issues: not read
  (https://unity.com/en/releases/editor/whats-new/6000.6.6f1) -> **to do**.
- Packages: the repo's `Packages/manifest.json` has no Entities/Netcode-for-Entities yet; Burst is 1.8.25 in the lock file [L]. The
  DOTS clone must add `com.unity.entities`, `.netcode`, `.entities.graphics`, `.collections`, `.mathematics` pinned to the 6000.6
  core versions (the 6.x numbering shown in the docs URLs, e.g. netcode@6.6 / 6.7).

### B2. IL2CPP or Mono for the Linux server
| Option | For | Against |
| --- | --- | --- |
| Mono | Fast build; today's tool uses it; easier stack traces | [U] Entities/N4E are tested mostly on IL2CPP; Mono is slower for managed code; some generic-sharing gaps with ECS generics |
| IL2CPP | Matches what Unity tests for Entities; faster managed code (HTTP page, logging); AOT catches generic problems early | Slower builds (needs C++ toolchain on the build machine: [U] cross-building Linux IL2CPP from Windows needs the Linux IL2CPP module; module files are present [L variations dir `il2cpp`]) |
Recommendation: build both flavours once as a spike, pick IL2CPP for production if it starts (ECS is designed for AOT), keep Mono for fast dev.

### B3. Can this PC compile-check for Linux?
- Yes: `LinuxStandaloneSupport\Variations` contains server variations [L], so a Unity **batch-mode** `BuildPipeline.BuildPlayer`
  for `StandaloneLinux64` + Server subtarget is possible. It needs a Unity process, so under the repo rule "do not open Unity" this
  waits for the mini-boss's go. No install is needed.
- `compile_check.py` (dotnet/Roslyn on generated csprojs) is C# only: it will not run Burst, baking, source generators of
  Entities unless their analyzers are referenced, nor IL2CPP. It can run with `--server` defines.

## Part C. What must change in the repo tooling

| Piece | Today [L] | Change for DOTS / Linux |
| --- | --- | --- |
| `ServerBuildBuilder.cs` | Builds Windows or Linux with `StandaloneBuildSubtarget.Server`, Mono, addressables disabled, `ServerSceneStripper` strips only vendor render components, `CleanBuildCache`, saves service/app Windows-only, `start-server.sh` | (1) add scripting-backend switch (IL2CPP for Linux). (2) subscene content is built by the SBP, not by `IProcessSceneWithReport` stripping: add the server baking exclusion list (Entities Graphics etc.) to Project Settings and assert it in `ModuleMissing`-like precheck. (3) precheck that Burst AOT settings has Linux x64 enabled and fail the build if `lib_burst_generated` is missing from the output. (4) set the Netcode build type for DGS automatically (already done by Unity [V]). (5) keep Addressables off for the server; ECS subscenes are scene-GUID loaded: verify they land in the server build [U]. (6) the report string must print the real backend. (7) `BuildSaveService` publishes `win-x64`: add `linux-x64` for the cloud box. |
| `ServerSceneStripper` | Switches off VolumetricLights/Fog components in scene copies | Scenes become thin; the ECS stripping happens in baking; keep for remaining GameObject scenes. |
| `compile_check.py --server` | Compiles our assemblies with `UNITY_SERVER` and without Editor assemblies; skips `!UNITY_SERVER` asmdefs | Add Entities/Burst source-generator analyzers to the csproj template (generated by Unity once with the packages), and a rule list that every `KryticalNova.*` DOTS assembly has `UNITY_SERVER`-safe references (no Entities.Graphics in server asmdefs). It still cannot find Burst errors: pair with a Unity batch build + `-burst` log scan. |
| `dependency_map.py --check` | Layer rules | New assemblies (e.g. `KryticalNova.Sim.*` ECS, `KryticalNova.Presentation.Entities`) must keep the layer stack; Entities Graphics belongs in Presentation, never in Simulation. |
| ServerApp~ (WinForms wrapper) | Starts SaveService + server hidden in one window | Windows-only, unchanged for the owner's PC. A DOTS server keeps the same command line, so the app needs only the new exe and ports. No Linux app: the cloud uses systemd. |
| Server-Guide.md | Windows steps, "Mono" in report text | Add a DOTS section; keep it plain words per the guide's rule. |

## Part D. The host-layer contract versus a Netcode for Entities server world
Contract text: `Assets/_Game/_Docs/Plans/online-linux-cloud-servers-plan.md`, "Design rule" section [L].

| Contract line | Holds for N4E? | Conflict / change |
| --- | --- | --- |
| Started as `KryticalNovaServer -batchmode -nographics -role map -map <name> -port <udp> ...`, settings from `server-settings.json` or flags | **Yes**, with a custom `ClientServerBootstrap` that reads the existing `ServerLaunchOptions` before any world is created [V custom bootstrap is the supported way: client-server-worlds page] | The bootstrap must create **only** the server world (`CreateServerWorld`, plus thin-client worlds in test builds [V thin clients on DGS since 1.4]). No client world, no default scene loading by Netcode. `-nographics` fine. |
| One UDP game port per map server | **Yes**: listen via `NetworkStreamRequestListen` entity or `AutoConnectPort` on the bootstrap [V network-connection page] https://docs.unity3d.com/Packages/com.unity.netcode@1.7/manual/network-connection.html | Port comes from CLI/`ports.json`, so use `NetworkStreamRequestListen` after parsing, not a hard-coded `AutoConnectPort`. |
| One TCP page port per map server (control page) | **Yes, unchanged**: it is managed code outside ECS | Run it from a MonoBehaviour/thread or a managed `SystemBase`; it must read ECS state (player count, tick, saving) through a thread-safe snapshot component copied each second, never by touching the `EntityManager` from the HTTP thread. |
| `ports.json` read by firewall, units, world service | Yes | No change. |
| On SIGTERM: save everyone, release locks, exit in time | **Needs a check** [U] | Unity's player quits on SIGTERM and raises `Application.quitting` / `wantsToQuit` (I found no official page; test on Linux). N4E worlds are disposed by Unity on quit, which can come before a save finishes. Do the save in `Application.wantsToQuit` (return false until done, with a timeout) or have a managed system that sees a `ShutdownRequested` flag set by a signal handler and drives: stop accepting, `NetworkStreamRequestDisconnect` all, save through `ServerSave.SaveTogether`, then `Application.Quit()`. Save code is managed and talks HTTP to SaveService: keep it out of Burst. |
| Health: control page answers; world service sees the 10 s presence report | Yes | Presence report is a managed timer (HttpClient async); never block the simulation thread. |
| Travel ticket carried in "connect with data" (a connection-request RPC) | **Yes, by design**: set `RequireConnectionApproval` on **both** sides; the client may send only `IApprovalRpcCommand` during Handshake/Approval, the server approves by adding `ConnectionApproved`, rejects with `NetworkStreamRequestDisconnect` [V network-connection page] | (a) Handshake+Approval time limit is `HandshakeApprovalTimeoutMS` default 5000 ms [V]: the world service call must finish in that window or be raised in the config. (b) HTTP validation of the ticket must be async (a managed system polling a task result), with the connection waiting in the Approval state. (c) The ticket is a managed string: send as `FixedString512Bytes`/bytes in the RPC; check max size [U]. (d) `NetworkId` appears only after approval [V], so the world service must key the ticket on the player account, not the NetworkId. (e) `NetworkStreamInGame` is added by us after the roster/save load; until then no snapshots [V]. |
| Linux Dedicated Server build | Yes, module installed [L] | Netcode needs the server world from a build with `UNITY_SERVER` and a custom/default bootstrap [V]; Part B. |
| (Not in the contract, add) | | **Relay mode** (owner's current "Switch to Relay") uses Netcode for GameObjects with Unity Transport relay; for N4E the relay allocation must be created in managed code and given to the driver [U: check the N4E "Unity Relay" sample]. **Build gate**: one build id both sides. **Thin-client bot test** for Phase 7 is supported on DGS [V]. |

## Open owner questions
1. Prediction strictness (Windows/Android/iOS clients vs Linux server): a) Deterministic Burst + quantised ghost fields on predicted systems
   only (recommended); b) Deterministic everywhere (slower); c) server-authoritative with interpolation only for most things, prediction
   for the local player's movement only.
2. Linux server scripting backend: a) IL2CPP for cloud, Mono for fast dev (recommended); b) Mono only; c) IL2CPP everywhere.
3. Cloud CPU: a) x64 only for DOTS (recommended, because Burst has no Linux arm64 target); b) allow arm64 after a verified spike.
4. Go-ahead for a Unity batch spike: a) one 30-minute Linux IL2CPP + Burst build of an empty N4E server in the DOTS clone, run in the
   Linux plan's test box (recommended); b) wait for the first real ECS system.

## Sources not fully read (to finish before relying on them)
Burst "Calling Burst code / exceptions" pages for 6.6; the Netcode 1.5 "Quantization and determinism" section; 6000.6.6f1 Known Issues;
Netcode changelog entries below char 100000; the Dedicated Server 3.0 package manual beyond the summary.
