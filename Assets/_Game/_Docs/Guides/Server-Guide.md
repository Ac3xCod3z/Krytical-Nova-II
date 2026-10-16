# Server Guide: how the Krytical Nova server works

A running guide to the game's own server, in plain words. It says what works **today**, and it is updated after
every server step (online phase 1, `Plans/online-phase-01-dedicated-server.md`).

**Last updated:** 16 Oct 2026, after steps 1 and 2 (the server builds and you can join it; settings, safe stop and
backups).

## In one minute

- The **server** is the game running on its own, with no window of the game, no picture and no player of its own. It
  holds the world, decides everything (fights, drops, trades, saves) and players join it.
- You **build** it from the Unity Editor with one menu. You get a folder with `KryticalNovaServer.exe` in it.
- You **start** it, and a black window shows what it is doing. You can **type commands** into that window.
- It reads its **settings** from `server-settings.json`, a small text file beside it that you edit with Notepad.
- It keeps the **saves** in a folder beside its own, and **backs them up** by itself.
- **Closing the window is safe**: it saves everyone first and tells the players.
- Players **join** from the game's network panel (**F1**) with the server's address and port.

## What works today

| Step | What it gives you | State |
| --- | --- | --- |
| 1. It builds and you can join it | A server program for Windows and Linux; a game joins it | Built 16 Oct 2026 |
| 2. Settings, safe stop, backups | The settings file, saves beside the server, backups, a safe stop, commands in its window | Built 16 Oct 2026 |
| 3. The control page | A web page on your home network to see and run the server, from a phone too | Next |
| 4. A server list in the game | Play joins your server without typing an address | Not yet |
| 5. Joining from outside your house | Unity Relay, and port forwarding as a fallback | Not yet |
| 6. The move to the old PC | A zip, a `Start Server.bat`, an install step and a step-by-step guide | Not yet |
| 7. Measuring | How many players and Hostiles one PC carries | Not yet |

## Words used here

| Word | Means |
| --- | --- |
| Server | The game running on its own, deciding everything. It has no player of its own. |
| Host | A player whose game is also the server (the Editor's normal Play). Not needed once the server runs. |
| Client | A player's game that joins a server. |
| Port | The number the server listens on, like a door number. The game's is **7777**; tests use 7790 and up. |
| Settings file | `server-settings.json`, beside the server. Your server's name, port, player limit and more. |
| Save folder | Where the server keeps every account, character, Crew, Kiosk and Contact. |
| Backup | A zip of the whole save folder, made by the server by itself. |

## 1. Build the server

1. In the Unity Editor: **Krytical Nova ▸ Build ▸ Dedicated Server (Windows)**. (**(Linux)** builds one for a rented
   Linux machine later.)
2. It takes about 5 minutes. The server lands in `Builds/Server/Windows/KryticalNovaServer` (about 550 MB).
3. A size report is written to `Builds/Server/BuildReport-Windows.txt`. Its "116 errors" are shader messages from
   the GI pack: a server draws nothing, so they do no harm.

A rebuild replaces the server's folder but **keeps your `server-settings.json`**, and never touches the saves (they
live beside the folder, not in it).

## 2. Start the server

Open a Command Prompt in the server's folder and run:

```
KryticalNovaServer.exe -logFile -
```

`-logFile -` makes the server write what it does into its window. (Without it the window stays almost empty and the
log goes to `C:\Users\<you>\AppData\LocalLow\DefaultCompany\Krytical Nova II\Player.log`.) Step 6 adds a
`Start Server.bat` you double-click instead.

The window fills with Unity's start-up lines. The ones that matter start with **`[Server]`** and **`[Backup]`**:

```
[Server] Krytical Nova Server: port 7777, up to 100 players, map Zone01_StarterTown, ...
[Backup] save_2026-10-16_14-05-09_start.zip (2 KB).
[Server] Krytical Nova Server is listening on port 7777 (UDP, every network card of this machine). Type a command (gm.help) and press Enter.
[Server] Player joined (connection 1). Online: 1.
```

Lines about missing scripts, serialization layouts or shaders are normal for a server: it leaves out everything
that only draws or plays sound.

**Norton:** the first time a new server runs, Norton can freeze it about a minute after it starts. Add an exclusion
for the `Builds\Server` folder (Norton 360: Settings ▸ Antivirus ▸ Scans and Risks ▸ Items to Exclude from
Auto-Protect, SONAR and Download Intelligence Detection). The old PC will need the same.

**Windows Firewall** may ask whether the server may use the network: allow it on **private** networks.

## 3. The settings file

`server-settings.json` sits beside `KryticalNovaServer.exe`. The first start writes it with every setting at its
default. Close the server, edit it with Notepad, save, start again.

```json
{
    "serverName": "Krytical Nova Server",
    "port": 7777,
    "maxPlayers": 100,
    "saveFolder": "",
    "map": "Zone01_StarterTown",
    "connectionMode": "direct",
    "pagePort": 8090,
    "pagePassword": "",
    "backupHours": 6.0,
    "backupsKept": 10
}
```

| Setting | Default | What it does |
| --- | --- | --- |
| `serverName` | Krytical Nova Server | The server's name (the server list shows it from step 4). |
| `port` | 7777 | The port players connect to. Your router forwards this one (step 5). |
| `maxPlayers` | 100 | Players at once. One more is sent back with "The server is full". |
| `saveFolder` | empty | Where saves go. Empty means `KryticalNovaServer-Save` beside the server's folder. A path without a drive letter is counted from the server's folder. |
| `map` | Zone01_StarterTown | The map the server opens with. A name the build does not have falls back to Zone01. |
| `connectionMode` | direct | `direct`: players connect to this PC's address. `relay` comes in step 5. |
| `pagePort` | 8090 | The control page's port (step 3). |
| `pagePassword` | empty | The control page's password (step 3). Only ever in this file. |
| `backupHours` | 6 | Hours between backups. 0 backs up only at start and stop. |
| `backupsKept` | 10 | How many backups are kept; the oldest go first. |

A mistake is forgiven: a port that is not a port becomes 7777, a player limit of 0 becomes 1, and a file Notepad
broke is left as it is while the server uses the defaults (the window says so).

**Overrides on the command line** win over the file, for one run:

| Add | Example |
| --- | --- |
| `-name "<text>"` | `-name "Brothers Night"` |
| `-port <number>` | `-port 7790` |
| `-maxPlayers <number>` | `-maxPlayers 10` |
| `-saveFolder "<path>"` | `-saveFolder "D:\KN Saves"` |
| `-map <scene>` | `-map EldersReach_Aldhavn` |
| `-backupHours <hours>` | `-backupHours 1` |
| `-backupsKept <number>` | `-backupsKept 20` |

The page password is never taken from the command line: it would show in the list of running programs.

## 4. Where everything is kept

```
Builds/Server/Windows/
├── KryticalNovaServer/              the server (replaced by every build)
│   ├── KryticalNovaServer.exe
│   └── server-settings.json         your settings (kept across builds)
├── KryticalNovaServer-Save/         the saves
│   ├── accounts/<id>.json           one per account, with its characters
│   └── world/                       crews, kiosks, contacts, sponsors
└── KryticalNovaServer-Save-Backups/
    └── save_2026-10-16_14-05-09_start.zip   ... the newest 10
```

To move the server to another PC, copy all three folders together.

## 5. Join the server

**From the game (a test build or the Editor):** press **F1** for the network panel, type the server's address in
**IP** and its port in **Port**, and press **JOIN**.

| The server is on | IP to type |
| --- | --- |
| this same PC | `127.0.0.1` |
| another PC in the house | that PC's address, like `192.168.1.20` (on that PC: open Command Prompt, type `ipconfig`, read "IPv4 Address") |
| outside your house | step 5 (Relay or port forwarding), not yet |

**From the Editor, for tests:** put a file `Library/KryticalNova.TestSession` in the project with these three lines,
and Play joins the server instead of hosting:

```
port=7790
save=C:/some/scratch/folder
join=1
```

Delete the file afterwards, or the Editor's Play keeps joining instead of hosting.

## 6. Commands you can type in the server's window

Type a command and press **Enter**. The answer appears below it. When you host instead, the GM console (**F2**) takes
the same commands (`gm.shutdown` and `gm.backup` belong to the server only).

| Command | What it does | Example |
| --- | --- | --- |
| `gm.help` | Lists every GM command. | `gm.help` |
| `gm.players` | Who is on: connection number, level, where they stand. | `gm.players` |
| `gm.say "text"` | Every player of every faction reads it on the GM channel. | `gm.say "Server restart at 9 pm"` |
| `gm.kick <who> "reason"` | Sends a player away. `<who>` is the connection number or the character's name. They can join again (no bans yet). | `gm.kick Tolan "Take a break"` |
| `gm.shutdown <seconds> "reason"` | Counts down, telling the players, then saves, backs up and stops. 60 seconds if you give no number. | `gm.shutdown 300 "Update tonight"` |
| `gm.shutdown cancel` | Calls off a countdown. | `gm.shutdown cancel` |
| `gm.backup` | Saves everyone and backs up now. Do it before an update. | `gm.backup` |
| `gm.target <connection>` | Points the other GM commands (give items, levels, teleport) at that player. | `gm.target 1` |

In the Editor's **GM Tools** window the Social tab has the same under **Server**: Kick Player, Shutdown Server, Cancel
Shutdown, Back Up Saves Now.

## 7. Stop the server safely

Any of these is safe: the window's **close button**, **Ctrl+C** in the window, logging off, or shutting Windows down.
The server then:

1. saves every player and the world,
2. makes a backup,
3. tells every player "The server was closed.",
4. stops.

For a planned stop, use `gm.shutdown <seconds>`: the players are told at 5 minutes, 2 minutes, 1 minute, 30 and 10
seconds, then each of the last 5.

Only pulling the plug, or ending the program in Task Manager, skips the save. Even then little is lost: every player
is also saved once a minute while they play, and as they leave.

## 8. Backups

- **When:** at start, every `backupHours` (6 by default), at every stop, and on `gm.backup`.
- **Where:** `KryticalNovaServer-Save-Backups`, beside the save folder. Each is a zip named by its date and time and
  why it was made: `save_2026-10-16_14-05-09_timed.zip`.
- **How many:** the newest `backupsKept` (10). A backup is written under a temporary name and renamed when whole, so a
  stop in the middle never leaves a broken one.

**To go back to a backup:** stop the server, rename `KryticalNovaServer-Save` (to keep it, for instance
`KryticalNovaServer-Save-old`), make a new empty `KryticalNovaServer-Save`, unzip the backup into it, start the server.

## 9. What players see

| When | The player reads |
| --- | --- |
| The server's window is closed | "The server was closed. Your progress is kept on the server." |
| `gm.shutdown` counts down | GM lines: "The server shuts down in 1 minute." ... then the reason |
| `gm.kick` | "A GM removed you from the server: <reason>. Your progress is kept on the server." |
| The server is full | "The server is full. Try again in a little while." |
| The connection drops | "The server closed, or the connection was lost. Your progress is kept on the server." |

Each comes with the same two ways out as the game menu (character select or exit), or staying where they stand.

## 10. When something goes wrong

| What you see | Why | What to do |
| --- | --- | --- |
| The server's window stops moving after about a minute | Norton froze the new program | Add the Norton exclusion (section 2). |
| The game says it cannot connect | Wrong address or port, or the server is not running | Check the window says "is listening on port …" and type that port in F1. |
| Still cannot connect from another PC | Windows Firewall | Allow the server on private networks (Windows asks the first time; else Windows Security ▸ Firewall ▸ Allow an app). |
| Not sure which port it really uses | | In PowerShell: `Get-NetUDPEndpoint -OwningProcess (Get-Process KryticalNovaServer).Id` |
| "Could not listen on port …" | Another server already uses that port | Close the other one, or change `port`. |
| The window says "could not read server-settings.json" | A typo broke the file (a missing comma or quote) | Fix it, or delete it and start once to get a fresh one. |
| "Player turned away … the server is full" | `maxPlayers` reached | Raise `maxPlayers`. |

## 11. What comes next

- **Step 3, the control page:** a page in the comic look, on your home network only, with a password: who is on, the
  log, the commands as buttons, backups, stop. A mock-up sheet comes first for you to pick from.
- **Step 4:** a server list in the game; Play joins your server.
- **Step 5:** joining from outside the house, through Unity Relay, with port forwarding as the fallback.
- **Step 6:** the zip, `Start Server.bat`, an install step, and moving it to the old PC by external SSD.
- **Step 7:** measuring how much one PC carries.

## For developers

| Piece | Where | What it does |
| --- | --- | --- |
| `ServerBuildBuilder` | `Tools/Editor/Build` | The build menus; keeps the settings file across a rebuild. |
| `ServerSceneStripper` | `Tools/Editor/Build` | Switches off parts that only draw in the server's copy of each scene. |
| `KryticalNova.Editor.Presentation` | `UI/Editor` (asmdef) | The HUD, menu, game feel and pet builders, left out under the server target; the rest of the Editor reaches them by name (`PresentationTools`). |
| `ServerLaunchOptions` | `Networking/Runtime/Server` | Reads the settings file and the command line. Server build only. |
| `ServerLifetime` | `Networking/Runtime/Server` | Backups on a timer, the countdown, the safe stop (the console close handler). Server build only. |
| `ServerBackups` | `Gameplay/Persistence/Runtime` | Zips the save folder and prunes old backups. |
| `ServerConsoleInput` | `Tools/Debug` | Runs the lines typed into the server's window. Server build only. |
| `ServerCommands` | `Tools/Debug` | `gm.kick`, `gm.shutdown`, `gm.backup`. |
| `NetworkConnectionManager.StartDedicatedServer` | `Networking/Runtime` | Listens on the settings' port and turns away players past the limit. |
| `DedicatedServerBoot`, `DedicatedServerOptimizer` | `Networking/Runtime` | Skip the menus and open the map; 30 ticks a second; parts that only draw switched off. |
| `EditorTestSession` | `Networking/Runtime` | The Editor's test port, scratch save and `join=1`. |

Things learned the hard way:

- With Unity's Dedicated Server package installed, Netcode's transport takes the package's port (7777) unless the port
  is set with `SetConnectionData(true, ...)`.
- Netcode's connection approval must never be switched on only on the server: every client would be refused.
- The Editor assemblies must keep compiling with `UNITY_SERVER`: never reference the UI or GameFeel assemblies from
  `KryticalNova.Editor`.

## Change log

- 16 Oct 2026: written, after steps 1 and 2.
