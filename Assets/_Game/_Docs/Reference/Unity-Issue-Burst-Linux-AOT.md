# Unity issue note: Burst AOT stopped compiling for Linux player builds

Written 30 Oct 2026 for a report to Unity. Status: **open, built-player test (B3) parked** (owner's pick). Plan: `Plans/netcode-for-entities-migration.md` sections 46-48.

## Versions

- Unity 6000.6.3f1 (Windows 11 host), URP, Entities, Netcode for Entities.
- Burst 2.0.0 (`Library/PackageCache/com.unity.burst@626e66ab421b`).
- Build: `BuildPipeline.BuildPlayer`, target StandaloneLinux64, Development, Player subtarget, IL2CPP or Mono, one scene plus a scene that only holds a SubScene. Called from `KryticalNova.EditorTools.KnLinuxTestBuild` (`Tools/Editor/Build/KnLinuxTestBuild.cs`).

## Symptom

Every Linux player build up to 30 Oct 07:21 ran Burst AOT: the log has
`...\Tools\Burst\Client\bcl\bcl.dll exited after 92975 ms.` and `Completed library ...\Library\BurstCache\Linux-Intel\<hash>.so`, and the
player carries `<Name>_Data/Plugins/lib_burst_generated.so` (185 MB). Since 09:00 no Linux player build does: the step is registered
(the build plan input names "Running Burst Compiler" with `BurstAotSettings_StandaloneLinux64.json`, `Library/BurstCache/AotSettings_StandaloneLinux64.hash`
and `bcl.dll` as inputs), Burst rewrites the settings hash, but bcl never runs, no library is made, and nothing is logged: no error, no
"skipping bcl: no assembly uses Burst", no "Burst is disabled entirely from the command line". The build reports success.

Without Burst the player's Netcode client crashes (SIGSEGV) about 2 s after its client world is made:
`__libc_free` <- `Memory.Unmanaged.Free` <- `BufferHeader.SetCapacity` <- `DynamicBuffer<SnapshotDynamicDataBuffer>.ResizeUninitialized` <-
`GhostSpawnSystem.OnUpdate`, called through the non-Burst fallback (`BurstDirectCall.Invoke`). That is a second issue worth reporting: the
non-Burst path of Netcode for Entities' ghost spawning crashes in a player.

## What happened between the last good build and the first bad one

1. Linux Dedicated Server builds (Server subtarget) of the same project. None of them ever had Burst. Some were stopped by a RAM watchdog
   mid-build, so the Editor was left with active target Linux and subtarget Server.
2. Two builds with Burst AOT switched off through `ProjectSettings/BurstAotSettings_StandaloneLinux64.json` (`EnableBurstCompilation: false`), the
   file removed after each build.
3. A second scene with a SubScene component added to the build list.

## Ruled out (each with a fresh build, Burst on)

| Tried | Burst after |
|---|---|
| Build outside a Windows job object (the wrapper normally limits cores and kills the tree on exit) | no |
| All 16 cores, no `-job-worker-count` | no |
| Today's `Library/Bee/Player*` build plans moved aside | no |
| Linux player-script cache moved aside (`Library/Bee/artifacts/2400b0aPDev.dag`, `StandaloneLinux64_CodeGen`, `PlayerScriptAssemblies`); 202 Burst IL post-process passes ran | no |
| `BurstAotSettings_StandaloneLinux64.json` written with `EnableBurstCompilation: true` | no |
| `UNITY_BURST_DEBUG=1` (shows only the Editor JIT side; nothing from the AOT step) | no |
| The SubScene-only scene left out of the build | no |
| `EditorUserBuildSettings.standaloneBuildSubtarget = Player` set before `BuildPlayer` | no |
| Unity started with `-buildTarget Linux64 -standaloneBuildSubtarget Player` | no |
| The whole `Library/BurstCache` moved aside | no |
| Built from the Editor window (not batch mode) | no |

Also checked: no Burst values in the Unity registry keys (EditorPrefs), no `-burst` flag on the command line, Jobs ▸ Burst ▸ Enable Compilation is on,
the Linux Burst AOT settings read `EnableBurstCompilation = true`. `ProjectSettings/ProjectSettings.asset` has two uncommitted changes from that day:
a `scriptingBackend` entry `Server: 0` and an empty `{fileID: 0}` in `preloadedAssets` (when the second appeared is not known).

## Logs and backups (in the DOTS clone, git-ignored)

- With Burst: `Logs/linux-client-il2cpp-build.log` (29-30 Oct, bcl ran 93 s).
- Without Burst: `Logs/linux-client-il2cpp-build-12.log` (30 Oct 13:15, fresh BurstCache).
- The crash: `Logs/b3-run-20261030-091112/bots/player-b2late.log`.
- The old `Library/BurstCache` and the moved Bee caches are kept in the session scratchpad (`burstcache-backup`, `bee-player-cache-backup`).

## Next ideas (not tried)

- A clean `Library` (a fresh checkout of the clone), then one Linux client build: tells "project state" from "Library state".
- The empty `preloadedAssets` entry removed, then one build.
- Report both issues to Unity with the logs above.
