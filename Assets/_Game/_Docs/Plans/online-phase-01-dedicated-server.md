# Online phase 1: a dedicated server on your PC, then on the old PC

Part of `online-roadmap.md`. Written 11 Oct 2026; rewritten 15 Oct 2026 as the seven chunks the owner approved on
12 Oct. **Chunks 1 to 4 built 16 Oct 2026** (see "as built" under each); chunks 5 to 7 are not built. The owner's
manual of the server as it is today is `Guides/Server-Guide.md`. The owner moved
the short rendering pass (`Design/Rendering-Bible.md` section 8) to after chunk 1.

## Goal

A server program with no window and no player of its own runs first on the owner's gaming PC, then day and night on
the old PC (Dell OptiPlex 9020: i7-4790, 16 GB, SSD, Windows; the GT 1030 does not matter, a headless server draws
nothing). Clients join it; nobody "hosts". The owner controls it from a web page on the home network, and pressing
Enter on character select joins it. Cost: nothing.

## The owner's choices (12 Oct 2026)

- Brothers reach it through **Relay and port forwarding both**: a fallback, and an easy move to a rented server later.
- The server is controlled from a **web page on the home network**, well designed and in the game's comic / Synty
  look; a mock-up sheet comes first.
- **Play joins your server**: character select remembers the last server.
- **One branch and pull request per chunk.**
- Setup steps must be easy to follow; router changes and the installer on the old PC are run by the owner.

## What the code check found (12 Oct 2026)

- **The server build tools are not installed.** Unity 6000.6.3f1 lacks the Windows and Linux Dedicated Server modules
  (Unity Hub ▸ Installs ▸ Add modules), and the `com.unity.dedicated-server` package (2.0.2 fits) is absent. Relay and
  Lobby are already inside `com.unity.services.multiplayer` 2.3.3.
- **Unity's session code cannot run a headless server through Relay.** Its network handler turns a Relay "server"
  into a host (its own comment calls this a bug), and its server path wants a service-account sign-in. The server
  therefore calls Relay directly and starts with `StartServer()`.
- **A server build does not compile today.** With the server target active, `UNITY_SERVER` removes `KryticalNova.UI`
  and `KryticalNova.GameFeel`, and about 24 Editor files that use them stop compiling (`GameFeel/Editor`,
  `UI/Editor/…`, `KryticalNova.UI.Tests.Editor`). The Server target also has no scripting defines
  (`EMERALD_AI_2024_PRESENT`, `DOTWEEN`, …).
- **`StartDedicatedServerAsync`** (`NetworkConnectionManager.cs` 327–367) has never been compiled; it publishes
  127.0.0.1 and would most likely fall back to a plain `StartServer()`.
- **A built client enters the world offline** and connects by hand from the F1 panel. No server list, no remembered
  server.
- **Missing:** settings from outside, a save folder that can be set, backups, a handler for closing the server
  window, kick and shutdown commands, any admin channel. `gm.say` fails on a server with no local player.
- **Reusable:** `DedicatedServerBoot`, `DedicatedServerOptimizer`, `StartDirectServerFallback`,
  `NetworkConnectionManager.LocalProfile()` (argument parsing), `ServerSave` (`Store` settable, quit hook),
  `QuantumConsoleProcessor.InvokeCommand`, `PlayerChat.ServerAnnounce`, `LoadingGate.Require`, `TestBuildBuilder`,
  the `MenuSettings` way of showing a UI Toolkit window on character select, `FactionSkin`. Player and Hostile
  animators already always animate, so the simulation should run with no screen.
- A headless server uses the **"Mobile" quality level** by default (`QualitySettings`); it draws nothing, so this is
  harmless.

## The seven chunks

Each is its own branch and pull request, ends in something the owner can try, and comes with a short "what you do"
list. Work stops after each one.

**0. The owner, first (10 minutes).** Unity Hub ▸ Installs ▸ 6000.6.3f1 ▸ Add modules ▸ Windows Dedicated Server
Build Support and Linux Dedicated Server Build Support, with the Editor closed.

**1. It builds and you can join it** (`online/01-server-build`).
Add `com.unity.dedicated-server`; Server scripting defines; move the UI / GameFeel-dependent Editor code into an
Editor assembly with `!UNITY_SERVER` (as `KryticalNova.Editor.Menus` already is); `ServerBuildBuilder` beside
`TestBuildBuilder` (**Krytical Nova ▸ Build ▸ Dedicated Server (Windows)** and **(Linux)**, into `Builds/Server/…`,
with the size report); replace the never-compiled session block with a plain listen. *Try it:* run the server on the
gaming PC (a console window with the log) and join from the game on a test port, never 7777.

*Chunk 1 as built (16 Oct 2026):*
- `com.unity.dedicated-server` 3.0.0; the Server scripting defines are the Standalone ones; Unity's Dedicated Server
  Optimizations are on, so no shaders, textures or sounds ship in the server.
- The Editor compiles with `UNITY_SERVER`: the HUD, menu, game feel and pet builders (`UI/Editor`, `GameFeel/Editor`,
  `Gameplay/Pets/Editor`) form `KryticalNova.Editor.Presentation`, left out under the server target.
  `KryticalNova.Editor` reaches them only by name, through `PresentationTools`.
- **Krytical Nova ▸ Build ▸ Dedicated Server (Windows)** / **(Linux)** (`ServerBuildBuilder`) build a clean server
  into `Builds/Server/<Windows|Linux>/KryticalNovaServer`, write `Builds/Server/BuildReport-<OS>.txt`, and put the
  Editor back on its own target. The Windows server is 552 MB, 256 MB of it the Sidekick characters' `Resources`.
- The server listens on `-port <number>` (else 7777) on every network card and logs "[Server] Player joined" and
  "[Server] Player left" with the count online. `-profile <name>` gives it a save of its own.
- Parts that only draw (Volumetric Lights, Volumetric Fog) are switched off in the server's copy of every scene
  (`ServerSceneStripper`) and again as each scene loads (`DedicatedServerOptimizer`): without shaders the 72 lamp
  cones threw an error every frame.
- For Editor tests, `Library/KryticalNova.TestSession` takes a third line, `join=1`: the main Editor then joins a
  running server on the test port instead of hosting.
- *Tried:* the server ran in its own console window on test port 7790; the Editor joined, its player spawned in
  Zone01, and the server logged the join and the leave (clip `Recordings/Server/server-join-chunk1.mp4`, not in git).
- *Found:* **Norton suspends a new, unsigned server exe** about a minute after it starts (every thread stops, no crash).
  A Norton exclusion for the `Builds\Server` folder fixed it; the old PC will need the same (chunk 6's guide). The
  vendor GI pack's compute shaders log 116 build errors for the server (harmless: a server draws nothing). A Multiplayer
  Play Mode clone stays on the Title screen while Play From Title is on, so it does not join by itself.

**2. Settings, safe stop, backups** (`online/02-settings-and-safe-stop`).
`ServerLaunchOptions`: a `server-settings.json` beside the server plus command-line overrides (name, port, max
players, save folder, map, connection mode, page port and password, backup hours). The save folder sits outside the
build folder. Closing the window, Ctrl+C or a Windows shutdown saves everyone first. `ServerBackups` zips the save
folder on start, every few hours and before an update, keeping the last N. No-host sweep: `gm.say` without a local
player, new `gm.kick` and `gm.shutdown <seconds>`, the "host left" wording in `GameMenuController`, the three
`ServerClientId` checks. *Try it:* change the name and port in the file, kill the window mid-play, restart.

*Chunk 2 as built (16 Oct 2026):*
- **`server-settings.json`** beside `KryticalNovaServer.exe` (`ServerLaunchOptions`, only in a server build): `serverName`,
  `port` (7777), `maxPlayers` (100), `saveFolder`, `map` (Zone01_StarterTown), `connectionMode` (`direct`; `relay`
  waits for chunk 5), `pagePort` and `pagePassword` (for chunk 3), `backupHours` (6), `backupsKept` (10). The first
  start writes it with every default; a file that cannot be read is left alone and the defaults are used. The command
  line wins: `-name -port -maxPlayers -saveFolder -map -mode -pagePort -backupHours -backupsKept` (no password on the
  command line: it would show in the list of running programs). **Krytical Nova ▸ Build ▸ Dedicated Server** keeps
  the file across a rebuild.
- **The saves sit beside the server's folder**, in `KryticalNovaServer-Save` (unless `saveFolder` says otherwise), so a
  new build never touches them; backups go to `KryticalNovaServer-Save-Backups` as `save_<date>_<time>_<why>.zip`,
  written under a temporary name and renamed when whole, on start, every `backupHours`, on stop and on `gm.backup`,
  the oldest beyond `backupsKept` removed (`ServerBackups`).
- **Safe stop** (`ServerLifetime`): the window's close button, Ctrl+C, logging off and a Windows shutdown all save every
  player and the world, back up, send the players away with "The server was closed." and only then let Windows end
  the program (it allows about five seconds). `gm.shutdown <seconds> "reason"` counts down with the players told at
  5 min, 2 min, 1 min, 30, 10, 5 to 1 seconds; `gm.shutdown cancel` calls it off.
- **The window takes commands:** a line typed in the server's window runs like the GM console (`ServerConsoleInput`):
  `gm.say`, `gm.players`, `gm.kick <id or name> "reason"`, `gm.shutdown`, `gm.backup`, and the rest. `gm.say` and the
  new commands need no player of the GM's own (`GmTarget.HasAuthority`); they also have buttons in the GM Tools
  window (Social, Server).
- **A full server** lets the player in and sends them straight back with "The server is full". (Netcode's connection
  approval would refuse them before they join, but it must then be switched on in every client too, or the server
  refuses every client as a stranger: found 16 Oct 2026.)
- **The player is told why** a session ended: the server's own words (a kick, a shutdown, a full server), else "The
  server closed, or the connection was lost. Your progress is kept on the server." No more "the host left".
- **The three `ServerClientId` checks** needed no change: a duplicate login is refused, the buff resend skips the
  server, and only a host's own player is GM in chat (a dedicated server has none: its GM types in the window).
- *Found:* with Unity's Dedicated Server package installed, Netcode's transport takes the package's port (7777 when
  none is given) over the one the game sets; `ConfigureTransport` now forces its port, and `-port` still wins through
  the settings. Chunk 1's `-port` hid this.
- *Tried:* settings file with a new name, port 7790 and a short backup interval; the Editor joined, a timed backup ran,
  the window was closed mid-play (account saved, stop backup, the player told), the server started again (start
  backup, oldest pruned) and the Editor joined again. Commands typed into the window (`gm.players`, `gm.backup`,
  `gm.shutdown 5`) ran and the countdown stopped the server. Clip `Recordings/Server/server-chunk2-safe-stop.mp4`
  (not in git). The Linux server builds too.

**3. The control page** (`online/03-server-dashboard`).
First a lettered mock-up sheet in the comic / Synty look. Then a small web server inside the server process (no admin
rights, nothing fetched from outside, home network only, password, lock-out after wrong tries). The page: status,
players with Kick, Save now, Backup now, Announce, Restart / Stop with a countdown, how to connect with a copy button,
a GM command box, the live log; works on a phone. GM commands run only from here until Phase 2 (account ids can still
be faked). *Try it:* do everything from the page.

*Chunk 3 as built (16 Oct 2026):*
- **The look:** the owner picked **C, the comic page by night**, from a lettered sheet of three (comic by day, the
  Synty sci-fi HUD, comic by night). Bangers and Roboto Bold, served by the server itself: nothing comes from the
  internet. Desktop and phone layouts.
- **The page** (`ServerControlPage`, server build only): status (ONLINE, or STOP IN / RESTART IN during a countdown;
  players, uptime, map, next backup), How to connect (this PC's home-network addresses and the game port, with Copy),
  players with Kick (a reason they read), Save now, Backup now, Announce, Restart / Stop after a number of seconds with
  Cancel, a GM command box (`ServerConsoleInput.Run`, the same as the window), the live log (start-up noise hidden,
  "every line" shows it). Kick, Restart and Stop ask first in a dialog of the page's own (a browser's confirm and
  prompt are unreliable on phones).
- **Served from a plain socket** (`TcpListener` on `pagePort`, 8090): Windows' `HttpListener` needs administrator
  rights to answer other machines. The page's files live in `Networking/ControlPage~` and the build copies them, with
  the fonts, to `ControlPage` beside the server; they are revalidated on every load (`no-cache`).
- **Security** (`ControlPageRules`, tested in `ControlPageTests`): only loopback and private addresses are answered;
  the Host must be an address, localhost or a bare machine name (no web site's name: DNS rebinding); a password from
  `pagePassword` (made and written into the settings file at the first start, compared in constant time); five wrong
  tries lock that address for five minutes; a session cookie (`HttpOnly`, `SameSite=Strict`, 12 hours, gone with a
  restart); every button press carries the session's check token in a header and must come from the page's own
  origin; requests over 16 KB are refused; only plain file names of known types under the page's folder are served;
  `Content-Security-Policy`, `X-Frame-Options: DENY`, `nosniff`, `no-referrer`. Plain `http` on the home network.
- **Restart:** `ServerLifetime` quits with exit code 3 after the usual save and backup, and **`Start Server.bat`**,
  which the build now writes beside the server (and `start-server.sh` for Linux), starts it again. The script calls the
  exe by its full path (a bare name is not searched for where `NoDefaultCurrentDirectoryInExePath` is set). New
  `gm.restart <seconds> "reason"`; `gm.players` shows names.
- *Tried:* the page from the browser on the server PC: wrong Host, foreign Origin, missing check, path tricks and six
  wrong passwords all refused as they should; Kick (the Editor client read the reason), Announce, Backup now, the GM
  box, Stop with Cancel, Stop, and Restart through `Start Server.bat` (a new process, the page back within seconds).
- *Found:* Norton flags each new build as **IDP.Generic** and blocks the start script or the exe (it reported "cannot
  find the file" and "not recognized"); the cure on a dev PC is a folder exclusion for `Builds` in Norton's scan,
  Auto-Protect/SONAR and Script Control lists; for players, code signing (phase 9). Adding Kronnect's lamp-cone depth
  renderer to `PC_RPAsset` in the rendering pass had switched off the GPU Resident Drawer (it was Forward, not
  Forward+); it is Forward+ now, like the fog's.

**4. Play joins your server** (`online/04-join-flow`).
`ServerDirectory` (servers found online, saved addresses, "This PC (testing)", the last one remembered); a Servers
window in each faction's skin, opened from character select and replacing the F1 panel's contents; Enter loads the
world already connected (`LoadingGate.Require`); clear messages for off, full and different build; a build number in
client and server with a version-only connection check. *Try it:* Enter lands you on the server; a second client does
the same.

*Chunk 4 as built (16 Oct 2026):*
- *Owner's picks:* Enter joins the last server and a SERVERS button changes it; the same build only; layout A of the
  mock-up sheet in all three skins, **without** add or remove for players: the list is kept on the control page and
  **the server uploads it** (PUBLISH LIST, to its own CCD bucket `krytical-nova-servers`, badge `latest`); "This PC
  (testing)" only in the Editor and test builds.
- *How it works:* `ServerDirectory` reads the published list (kept on the PC too), asks each server on its **status
  port** (game port + 1; `ServerStatusResponder` answers a 200-byte request with a smaller reply, four a second per
  address) and remembers the last choice. `MenuServers` is the window, a template in the menus' Settings document so it
  wears the skin. Enter calls `JoinFlow.Begin`; the world scene's `NetworkConnectionManager.JoinChosenServer` connects
  under the loading screen (`LoadingGate.Require`); a failure returns to character select with the reason
  (`JoinFlowScreens`). **Connection approval** is on in client and server: the payload `KN1|version|editor` is checked
  by `JoinRules` (same build; the Editor any; full turned away before it is in). The build number "0.1.0 build N" is
  stamped into the game by Publish to Test and into the server by the server build (`ReleasePublisher.WithVersion`),
  and Publish to Test rebuilds the matching server.
- *START MY SERVER (owner, after the chunk):* on the owner's PC only (`LocalServer`: the project's `Builds` in the
  Editor, `my-server.txt` beside an installed game), the Servers window offers to start the server when it is off;
  `Start Server.bat --close` opens minimized, the game waits and joins, and for now the server stops with the game
  through its control page (`StopWithGame`, to be false after chunk 6).
- *Tried:* the window in the Null and Hegemony skins; status ONLINE 0/100; Enter World joins an isolated copy on a test
  port (the player on its page); add and publish on the control page and a game reading it from CCD; a server stopping
  mid-session; Enter with the server off; START MY SERVER twice (joined after 49 s, window minimized and never in
  front, server and window gone a few seconds after leaving Play). The different-build and full refusals are covered by
  `JoinRulesTests` (all 845 EditMode tests pass).
- *Found:* builds older than the first one with this code (launcher build 4) cannot join a step-4 server (approval);
  the next Publish to Test fixes that for everyone. Bat files written outside `Builds` are deleted by Norton.

**5. Over the internet, both ways** (`online/05-internet`).
Prove first (half a day): a headless server signs in, opens a Relay allocation, starts as a server and lists itself on
a notice board (Unity Lobby: name, build, players, how to connect); and whether Relay and a direct port can be open at
once (if not, one mode at a time, switched from the page). Relay: no router work, address hidden. Direct: one
forwarded port and the public address in the settings. Fallback: the code or address from the page, typed once and
remembered. *Try it:* a brother at his house presses Enter and arrives; then through the forwarded port.

**6. Move it to the old PC** (`online/06-server-pc`).
The build menu makes one zip: the server, `Start Server.bat` (restarts it if it dies, applies updates),
`Install (run once).bat` and an illustrated step-by-step guide. The zip goes to the old PC by external SSD, USB stick
or the home network. The owner, once: unzip to `C:\KryticalNovaServer`, run
the installer as administrator (server name, page password, game port opened, page port opened for the home network
only, start with Windows, sleep off), copy the characters across
(`…\AppData\LocalLow\DefaultCompany\Krytical Nova II\ServerSave\default`). Updates: **Build and Send** to a shared
folder and "Update" on the page, or a USB stick and `Update Server.bat`. *Try it:* the old PC runs it day and night.

**7. Measure** (`online/07-measure`).
The server writes its performance log; a session with all three players, then scripted extra clients until it misses
30 ticks. The number becomes the first real cap.

## Rules that hold throughout

- **The server is a headless Unity build of this same project** (owner's question, 15 Oct 2026: "can server code be
  in Unity at all?"). Yes: the game's rules (combat, NavMesh movement, Hostile AI, saving) are Unity code and run on
  the server; a separate non-Unity server would mean writing them all twice. What matters instead:
  - **Players never get the server's code or secrets.** Server-only code sits behind `UNITY_SERVER` (its own
    assembly, or Multiplayer Roles stripping), so it is not in the client build; passwords and keys live only in the
    server's settings file, never in any build or the repository.
  - **The built server is a standalone folder** (an exe plus its data). It needs no Unity, Unity Hub or project on the
    machine that runs it. It can be carried on an external SSD or USB drive, copied to the old PC, and run there;
    the save folder is copied the same way.
- Settings come from outside the build; nothing about either PC is in code or scenes (roadmap rule 2).
- The control page is never forwarded to the internet.
- Editor tests use the test session port, never 7777. No Editor is stopped by Claude.
- New client-to-server calls go on the audit list of `online-security-model.md`.
- **The Server Guide** (`Guides/Server-Guide.md`, owner, 16 Oct 2026) is the owner's plain-words manual of the server
  as it is today: every command, setting, folder and message. It is updated with every chunk that lands.

## Done when

- The old PC runs the server day and night from a fresh unzip, following only the guide.
- The owner and a brother at his own house both join by pressing Enter, through Relay and through the forwarded port.
- Everything on the control page works, from a phone too; it refuses a wrong password, a request from outside the
  home network, and a forged button press from another site.
- Killing the server during a trade and during an autosave loses nothing.
- The Windows client build and the Linux server build compile at every chunk; EditMode tests pass.

## Risks

- How much Editor code the server compile breaks is only known at the first attempt.
- The Relay path for a headless server (chunk 5) is unproven; the direct port is the fallback.
- Everything tested so far had the owner as host with no delay. Play on a dedicated server feels slower to respond
  until Phase 3.
