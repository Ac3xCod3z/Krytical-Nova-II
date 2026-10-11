# Online phase 1: a dedicated server on your PC

Part of `online-roadmap.md`. Written 11 Oct 2026. **Not built.** First phase to build, when the owner asks.

## Goal

A server program with no window and no player of its own runs on the owner's PC. Clients join it. Nobody
"hosts": the owner plays as an ordinary client. Cost: nothing.

## What exists today

| Piece | Where | State |
| --- | --- | --- |
| Server boot | `Networking/Runtime/DedicatedServerBoot.cs` | Under `UNITY_SERVER`, skips the menus and loads `Zone01_StarterTown` (the map name is a constant) |
| Server start | `Networking/Runtime/NetworkConnectionManager.cs` `StartDedicatedServerAsync` (line 328) | Opens a direct session that listens on and **publishes 127.0.0.1**, so only the same machine could join. Falls back to a plain `StartServer()` |
| Server tuning | `Networking/Runtime/DedicatedServerOptimizer.cs` | 30 frames a second, audio paused |
| No local player | `Networking/Runtime/OfflinePlayerHandoff.cs` | Hides the offline avatar on a headless server |
| Client-only code kept out | `KryticalNova.UI.asmdef`, `KryticalNova.GameFeel.asmdef` (`!UNITY_SERVER`); `Core/Runtime/ClientPresentationRegistry.cs`; `UNITY_SERVER` / batch-mode guards in 20 files | In place, never compiled as a server |
| In-Editor stand-in | `NetworkConnectionManager.StartLocalServer` (line 171) | Works |
| A server build | — | **None.** `Tools/Editor/Build/TestBuildBuilder.cs` builds the Windows client only. The Dedicated Server package is not installed; Multiplayer Roles are off (`ProjectSettings/MultiplayerManager.asset`) |
| Server settings | — | Port 7777, 100 players and the address are fields on the prefab |

## Work items

1. **Install the pieces.** The Linux Dedicated Server and Windows Dedicated Server build modules in the
   Unity Hub; the `com.unity.dedicated-server` package (check the version that matches Unity 6000.6).
   Turn Multiplayer Roles on.
2. **A server build script.** `ServerBuildBuilder` beside `TestBuildBuilder`, menu **Krytical Nova ▸ Build
   ▸ Dedicated Server (Windows)** and **(Linux)**, into `Builds/Server/Windows` and `Builds/Server/Linux`.
   Scenes: the world scenes only plus whatever `DedicatedServerBoot` needs. Same size report as the client
   build. The first compile as a server will show client-only code that was never excluded; each case is
   fixed the way `ClientPresentationRegistry` already does it (an interface in Core, the UI behind it).
3. **Settings from outside.** A new `ServerLaunchOptions` (in `Networking/Runtime`) reads, from the
   command line and then from environment settings: `-port`, `-listen`, `-publishIp`, `-map`, `-line`,
   `-maxPlayers`, `-saveRoot`, `-gm <account ids>`, `-relay on|off`, and the existing `-profile`.
   `StartDedicatedServerAsync` and `DedicatedServerBoot.WorldScene` take their values from it. Nothing
   about a machine stays on the prefab. This is rule 2 of the roadmap.
4. **Joining over the internet.** Preferred: the server opens a Relay session and writes its join code to
   the log and to a `join-code.txt` beside the build; the client's session panel
   (`UI/Runtime/NetworkSessionPanel.cs`, F1) already joins by code. Fallback: direct address with
   `-publishIp` and a forwarded port.
   **Prove first:** which Unity sessions call lets a server build open a Relay session, and how the
   server signs in to Unity's services (a server build normally uses a service-account key, which is a
   secret and so comes from an environment setting, never the repository).
5. **Stop without losing anything.** `ServerSave` saves on `Application.quitting`. Confirm that closing
   the server the way a machine would (Ctrl+C, a stop signal, the console window's close button) reaches
   that line and every online player is saved; add a handler if it does not. Add a `-shutdownAfter`
   countdown announcement ("the server restarts in 60 seconds").
6. **Sweep out "the host is a player".** The 16 places that test for it:

   | Where | What it assumes | What to do |
   | --- | --- | --- |
   | `Gameplay/Social/Runtime/PlayerChat.cs` : 62 (`IsGameMaster`) and `ChatRouting.cs` : 77 | The GM is the host's player | GM = an account on the server's `-gm` list |
   | `Tools/Debug/GmConsoleGate.cs` (`MayUse`), `Tools/Debug/GmTarget.cs` | The console opens where the server is | Unchanged rule, but a headless server has no screen: GM commands are typed into the server's own console window (lines read from standard input and run through the same command processor). No GM command comes from a client in this phase |
   | `UI/Runtime/GameMenuController.cs` : 280, 307, 349, 365 | Leaving may mean "I was hosting" | Review: a client must never try to save |
   | `Gameplay/Persistence/Runtime/PlayerSaveAgent.cs` : 160 | Kicks any client but the server's own | Correct for both; review only |
   | `Gameplay/Progression/Runtime/PlayerCombatSkills.cs` : 762, `Networking/Runtime/SpatialInterestManager.cs` : 124, 166 | Skips the server's own client id | Check each still does what it means when that id has no player |
   | `Networking/Runtime/NetworkDebugHUD.cs` : 63, `UI/Runtime/NetworkSessionPanel.cs` : 151, `UI/Runtime/Diagnostics/PerformanceReadout.cs` : 217 | Labels | Nothing |

7. **Check the simulation runs with no screen.** Jump, vault and death are simulated on the server from
   animation data, and Hostile brains run there. Confirm nothing stops because nothing is drawn (animators
   set to always animate on the server, no camera needed, no audio listener needed).
8. **Editor testing.** A Play Mode Scenario with one local server and two clients. Editor tests keep using
   the test session file (`Library/KryticalNova.TestSession`: its own port and scratch save), never 7777.
9. **How to run it.** A short page in this folder: start the server, read the code, join, stop it, where
   the save folder is.

## Tunables

None new for players. Server settings are launch arguments, listed in item 3.

## Done when

- The Windows server build runs on the owner's PC with no window and uses a save folder given at launch.
- The owner joins as a client on the same PC; a brother joins over the internet with the code.
- Both play: move, fight Hostiles, trade, chat. A GM command typed in the server console works.
- The server is stopped and started again; both characters are as they were.
- The Linux server build compiles (it is run in Phase 8).
- EditMode tests still pass; the Windows client build still works.

## Risks

- Client-only code that the server build will not compile: size unknown until the first attempt.
- The Relay path for a server build (item 4): if it cannot be proven quickly, use the direct address.
- Everything that was only ever tested with the owner as host has had 0 ms of delay. Play on the
  dedicated server will feel slower to respond. That is expected and is Phase 3's job.
