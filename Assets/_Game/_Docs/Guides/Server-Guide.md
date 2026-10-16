# Server Guide: how the Krytical Nova server works

A running guide to the game's own server, in plain words. It says what works **today**, and it is updated after
every server step (online phase 1, `Plans/online-phase-01-dedicated-server.md`).

**Last updated:** 16 Oct 2026, after step 4 (the server list, Enter joins your server, the same build only).

## In one minute

- The **server** is the game running on its own, with no window of the game, no picture and no player of its own. It
  holds the world, decides everything (fights, drops, trades, saves) and players join it.
- You **build** it from the Unity Editor with one menu. You get a folder with `KryticalNovaServer.exe` and
  `Start Server.bat` in it.
- You **start** it by double-clicking `Start Server.bat`. A black window shows what it is doing, and you can **type
  commands** into it.
- You **run** it from its **control page**, a web page you open on any PC or phone in the house: who is on, the log,
  Save, Backup, Announce, Kick, Restart, Stop, and a box for GM commands. It has a password.
- It reads its **settings** from `server-settings.json`, a small text file beside it that you edit with Notepad.
- It keeps the **saves** in a folder beside its own, and **backs them up** by itself.
- **Closing the window is safe**: it saves everyone first and tells the players.
- Players **join** by pressing **Enter World** on character select: it joins the server they played last. **S** opens
  the **Servers** window, which lists the servers you publish from the control page.
- Up to **three Game Masters** are set on the control page, each with a **GM password**. In the game they press
  **F2**, type it, and their GM commands run on the server. Nobody else has GM commands or the GM chat.

## What works today

| Step | What it gives you | State |
| --- | --- | --- |
| 1. It builds and you can join it | A server program for Windows and Linux; a game joins it | Built 16 Oct 2026 |
| 2. Settings, safe stop, backups | The settings file, saves beside the server, backups, a safe stop, commands in its window | Built 16 Oct 2026 |
| 3. The control page | A web page on your home network to see and run the server, from a phone too | Built 16 Oct 2026 |
| Game Masters | Three GMs by password, set on the control page; GM commands, GM chat and a [GM] tag for them only | Built 16 Oct 2026 |
| 4. A server list in the game | Enter joins your server; a Servers window; only the same build may join | Built 16 Oct 2026 |
| 5. Joining from outside your house | Unity Relay, and port forwarding as a fallback | Not yet |
| 6. The move to the old PC | A zip, an install step and a step-by-step guide | Not yet |
| 7. Measuring | How many players and Hostiles one PC carries | Not yet |

## Words used here

| Word | Means |
| --- | --- |
| Server | The game running on its own, deciding everything. It has no player of its own. |
| Host | A player whose game is also the server (the Editor's normal Play). Not needed once the server runs. |
| Client | A player's game that joins a server. |
| Port | The number the server listens on, like a door number. The game's is **7777**; tests use 7790 and up. |
| Settings file | `server-settings.json`, beside the server. Your server's name, port, player limit, page password and more. |
| Control page | The server's own web page, on your home network only, with a password. |
| Save folder | Where the server keeps every account, character, Crew, Kiosk and Contact. |
| Backup | A zip of the whole save folder, made by the server by itself. |
| GM (Game Master) | A player allowed GM commands and the GM chat channel: the host, or one of the server's three GMs after typing their GM password. |
| GM password | One per GM, set on the control page. Typed once per visit in the GM console (F2). Not the page password. |

## 1. Build the server

1. In the Unity Editor: **Krytical Nova ▸ Build ▸ Dedicated Server (Windows)**. (**(Linux)** builds one for a rented
   Linux machine later.)
2. It takes about 5 minutes. The server lands in `Builds/Server/Windows/KryticalNovaServer` (about 550 MB), with the
   control page's files (`ControlPage`) and `Start Server.bat` beside the exe.
3. A size report is written to `Builds/Server/BuildReport-Windows.txt`. Its "116 errors" are shader messages from
   the GI pack: a server draws nothing, so they do no harm.

A rebuild replaces the server's folder but **keeps your `server-settings.json`**, and never touches the saves (they
live beside the folder, not in it). **Stop the server before you build:** while it runs from that folder the build
says so and changes nothing.

## 2. Start the server

Double-click **`Start Server.bat`** in the server's folder. A window titled "Krytical Nova Server" opens and fills
with Unity's start-up lines. The ones that matter start with **`[Server]`** and **`[Backup]`**:

```
[Server] Krytical Nova Server: port 7777, up to 100 players, map Zone01_StarterTown, ...
[Backup] save_2026-10-16_14-05-09_start.zip (2 KB).
[Server] Control page: http://192.168.1.73:8090 (home network only).
[Server] Krytical Nova Server is listening on port 7777 (UDP, every network card of this machine). Type a command (gm.help) and press Enter.
[Server] Player joined (connection 1). Online: 1.
```

Lines about missing scripts, serialization layouts or shaders are normal for a server: it leaves out everything that
only draws or plays sound.

When the server stops by itself (Stop, `gm.shutdown`), the window says "The server has stopped. Press a key to close
this window." A **Restart** starts it again in the same window by itself.

### Norton and Windows Firewall

**Norton** flags new programs nobody else has run yet as **IDP.Generic**, and every new build is one. It then blocks or
freezes the server, or the start script reports "cannot find the file" or "not recognized". It is not a virus: it is
Norton not knowing the file. Exclude the whole **`Builds`** folder once (`C:\Users\dlaco\Krytical Nova II\Builds`), in
Norton 360: Settings ▸ Antivirus ▸ Scans and Risks, then

- **Items to Exclude from Scans** ▸ add the folder, and
- **Items to Exclude from Auto-Protect, SONAR and Download Intelligence Detection** ▸ add the same folder, and
- **Script Control**, if it is there, the same folder.

Restoring one file from Norton's history only allows that exact file: the next build is caught again, so the folder
exclusion is the one that lasts. The old PC needs the same. (For players, the cure is a code-signing certificate before
the beta; see the Steam and launcher plan.)

**Windows Firewall** asks whether the server may use the network the first time it starts: allow it on **private**
networks. Without that, other PCs and your phone cannot reach the game or the control page.

## 3. The control page

The server's own web page, in the comic look, for any PC or phone in the house.

**Open it:** the server's window prints the address when it starts, for example
`[Server] Control page: http://192.168.1.73:8090`. Type that into a browser on any PC or phone on the same home
network. On the server PC itself, `http://localhost:8090` works too.

**The password:** the first time, the server makes one and writes it into `server-settings.json` as `pagePassword`
(open the file with Notepad to read it). Change it there to one you like, then restart the server. A browser that
logged in stays in for 12 hours, or until the server restarts.

What is on the page:

| Part | What it does |
| --- | --- |
| Top | The server's name, ONLINE (or STOP IN … / RESTART IN … during a countdown), players on, how long it has run, the map, the next backup. **Log out**. |
| How to connect | The address and port players type in **F1**, with a **Copy** button. |
| Players on | Everyone on: name (a yellow **GM** before a GM who is signed in), level, faction, map. **Kick** asks for a reason they will read, then sends them away (they can join again). |
| Run the server | **Save now** (everyone saved at once), **Backup now**, **Announce** (a line every player reads), and **Restart** / **Stop** after the number of seconds you set, with the players told as it counts down. **Cancel** calls a countdown off. Each asks first. |
| Game Masters | Who may be GM (three at most), whether each is in the game now (**in the game**), **Remove**, and **Add GM** with a name and their own GM password. Saved into `server-settings.json` at once. |
| GM command | Any GM command, as in the server's window (`gm.help` lists them all). The answer shows below. |
| Live log | What the server says, newest at the bottom. Unity's start-up noise is hidden; tick **every line** to see it all. |

**Safety:** the page answers the home network only, never the internet (and must never be forwarded on the router).
Five wrong passwords lock that device out for five minutes. A button only works when pressed on the page itself, so
another web site cannot press it for you. The page is plain `http` on your home network: do not use it on a shared or
public Wi-Fi.

**No page:** set `pagePort` to `0` in the settings and restart.

## 4. The settings file

`server-settings.json` sits beside `KryticalNovaServer.exe`. The first start writes it with every setting at its
default (and makes the page password). Close the server, edit it with Notepad, save, start again.

```json
{
    "serverName": "Krytical Nova Server",
    "port": 7777,
    "maxPlayers": 100,
    "saveFolder": "",
    "map": "Zone01_StarterTown",
    "connectionMode": "direct",
    "pagePort": 8090,
    "pagePassword": "abc-def-ghj-42",
    "backupHours": 6.0,
    "backupsKept": 10,
    "gameMasters": [
        { "name": "Dom", "password": "their-own-password" }
    ]
}
```

| Setting | Default | What it does |
| --- | --- | --- |
| `serverName` | Krytical Nova Server | The server's name, on the control page (and in the server list from step 4). |
| `port` | 7777 | The port players connect to. Your router forwards this one (step 5). |
| `maxPlayers` | 100 | Players at once. One more is sent back with "The server is full". |
| `saveFolder` | empty | Where saves go. Empty means `KryticalNovaServer-Save` beside the server's folder. A path without a drive letter is counted from the server's folder. |
| `map` | Zone01_StarterTown | The map the server opens with. A name the build does not have falls back to Zone01. |
| `connectionMode` | direct | `direct`: players connect to this PC's address. `relay` comes in step 5. |
| `pagePort` | 8090 | The control page's port. 0 switches the page off. |
| `pagePassword` | made for you | The control page's password. Only ever in this file. Empty makes a new one at the next start. |
| `backupHours` | 6 | Hours between backups. 0 backs up only at start and stop. |
| `backupsKept` | 10 | How many backups are kept; the oldest go first. |
| `gameMasters` | none | Up to three GMs, each a name and their own GM password. Set them on the control page (easier), or here. |
| `statusPort` | 0 (= game port + 1, so 7778) | Where the server tells the Servers window it is on, its players and its build. |
| `serverList` | none | The servers players see. Set them on the control page and press PUBLISH LIST. |
| `listBucket`, `cloudProjectId`, `cloudEnvironment` | krytical-nova-servers, this project, production | Where PUBLISH LIST uploads the list. Leave them. |
| `ugsPath` | empty (`%LOCALAPPDATA%\KryticalNova\tools\ugs.exe`) | Unity's command-line tool on this PC, for PUBLISH LIST. |

A mistake is forgiven: a port that is not a port becomes 7777, a player limit of 0 becomes 1, and a file Notepad
broke is left as it is while the server uses the defaults (the window says so).

**Overrides on the command line** win over the file, for one run (put them after `-logFile -` in `Start Server.bat`):

| Add | Example |
| --- | --- |
| `-name "<text>"` | `-name "Brothers Night"` |
| `-port <number>` | `-port 7790` |
| `-maxPlayers <number>` | `-maxPlayers 10` |
| `-saveFolder "<path>"` | `-saveFolder "D:\KN Saves"` |
| `-map <scene>` | `-map EldersReach_Aldhavn` |
| `-pagePort <number>` | `-pagePort 8091` |
| `-backupHours <hours>` | `-backupHours 1` |
| `-backupsKept <number>` | `-backupsKept 20` |

The page password is never taken from the command line: it would show in the list of running programs.

## 5. Where everything is kept

```
Builds/Server/Windows/
├── KryticalNovaServer/              the server (replaced by every build)
│   ├── KryticalNovaServer.exe
│   ├── Start Server.bat             double-click to start
│   ├── server-settings.json         your settings (kept across builds)
│   └── ControlPage/                 the control page's files and fonts
├── KryticalNovaServer-Save/         the saves
│   ├── accounts/<id>.json           one per account, with its characters
│   └── world/                       crews, kiosks, contacts, sponsors
└── KryticalNovaServer-Save-Backups/
    └── save_2026-10-16_14-05-09_start.zip   ... the newest 10
```

To move the server to another PC, copy all three folders together.

## 6. Join the server

**From the game (step 4):** on character select, **Enter World** joins the server played last, while the loading
screen is up. **S** (or the **Server: ...** prompt beside Settings) opens the **Servers** window:

| Column | Means |
| --- | --- |
| ONLINE / OFF / UPDATE / FULL | ONLINE: answering and the same build. OFF: not answering (off, or not reachable from there). UPDATE: another build ("open the launcher"). FULL: no room now. |
| Name and address | As you published them. **LAST PLAYED** marks the one Enter joins. |
| 3 / 100, 12 ms | Players on, the limit, and how long the server took to answer. |

Up / Down choose, **Enter** or **JOIN** joins (and is remembered), **Esc** closes, **REFRESH** asks again. Players
cannot add or remove servers: the list is yours (next section). The Editor and test builds also show **This PC
(testing)** and **Play here (no server)** (the Editor hosts, a test build plays offline, as before).

If a join fails (another build, full, no answer) the game goes back to character select and the Servers window says
why.

**START MY SERVER (your PC only).** When Enter World finds *your* server off, the Servers window offers **START MY
SERVER**. It opens `Start Server.bat` **minimized** (the server's window waits in the taskbar and never covers the game
or the Editor), waits until the server answers (about half a minute), and joins by itself. It only shows when the game
knows where your server is and the server is on this PC:

| Where | How it finds your server |
| --- | --- |
| The Editor | The project's `Builds/Server/Windows/KryticalNovaServer` |
| Your installed game | A file `my-server.txt` beside the game (in the launcher's `Game` folder), one line: the server's folder |

A brother's game has no `my-server.txt` and never offers it. **For now the server stops with the game that started
it** (quitting, or leaving Play in the Editor, runs its safe stop: everyone saved, a backup, then its window closes).
A server you start yourself with `Start Server.bat` is never stopped by the game. When the server moves to the old PC
(step 6) this changes: the server keeps running on its own.

**Only the same build joins.** A release game and its server carry their build number ("0.1.0 build 5"); a game of
another build is refused before it is in: *"This server runs build 5 and your game is build 4. Open the Krytical Nova
launcher to update."* **Publish to Test** rebuilds the server with the new number by itself; if the server was running
it says so, and you rebuild it after stopping it (**Krytical Nova ▸ Build ▸ Dedicated Server**, which always takes the
latest release's number). The Editor may join or host any build: it is yours.

### The server list players see

On the control page, **Servers players see**: add a server with the name players read, the address they reach it by
and its port (**Add**), remove one (**Remove**), then **PUBLISH LIST**. Your server uploads the list to its own small
Cloud Content Delivery bucket (`krytical-nova-servers`, apart from game builds) and every game's Servers window shows it
within a few minutes. No new build is needed.

PUBLISH LIST needs Unity's command-line tool on the PC that runs the server, logged in once, as for releases
(Release Guide, section 6): on this PC it already is. On the old PC (step 6) the install step will do it.

Your brothers reach your server from outside the house in step 5, through the forwarded **game port (7777) and status
port (7778)**. Until then the list works inside the house: use the PC's home address (the control page shows it).

**From the F1 panel (testing):** press **F1** for the network panel, type the server's address in **IP** and its port
in **Port**, and press **JOIN**. The control page shows both under "How to connect".

| The server is on | IP to type |
| --- | --- |
| this same PC | `127.0.0.1` |
| another PC in the house | that PC's address, like `192.168.1.20` (the control page shows it; or on that PC open Command Prompt, type `ipconfig`, read "IPv4 Address") |
| outside your house | step 5 (Relay or port forwarding), not yet |

**From the Editor, for tests:** put a file `Library/KryticalNova.TestSession` in the project with these three lines,
and Play joins the server instead of hosting:

```
port=7790
save=C:/some/scratch/folder
join=1
```

Delete the file afterwards, or the Editor's Play keeps joining instead of hosting.

## 7. Commands

Type a command in the server's window and press **Enter**, or in the control page's **GM command** box. The answer
appears below it. When you host instead, the GM console (**F2**) takes the same commands (`gm.shutdown`, `gm.restart`
and `gm.backup` belong to the server only).

| Command | What it does | Example |
| --- | --- | --- |
| `gm.help` | Lists every GM command. | `gm.help` |
| `gm.players` | Who is on: connection number, name, level, where they stand. | `gm.players` |
| `gm.say "text"` | Every player of every faction reads it on the GM channel. | `gm.say "Server restart at 9 pm"` |
| `gm.kick <who> "reason"` | Sends a player away. `<who>` is the connection number or the character's name. They can join again (no bans yet). | `gm.kick Tolan "Take a break"` |
| `gm.restart <seconds> "reason"` | Counts down, telling the players, then saves, backs up, stops and starts again. 60 seconds if you give no number. | `gm.restart 120 "Update"` |
| `gm.shutdown <seconds> "reason"` | Counts down, telling the players, then saves, backs up and stops. 60 seconds if you give no number. | `gm.shutdown 300 "Done for tonight"` |
| `gm.shutdown cancel` | Calls off a countdown (shutdown or restart). | `gm.shutdown cancel` |
| `gm.backup` | Saves everyone and backs up now. Do it before an update. | `gm.backup` |
| `gm.target <connection>` | Points the other GM commands (give items, levels, teleport) at that player. | `gm.target 1` |

In the Editor's **GM Tools** window the Social tab has the same under **Server**: Kick Player, Shutdown Server, Restart
Server, Cancel Shutdown, Back Up Saves Now.

## 7b. Game Masters (GMs)

A **GM** can use the GM commands and speak on the GM chat channel; everyone sees **[GM]** before their name. Everyone
else has neither. Until players sign in with a Unity account (a later phase), a GM proves who they are with a **GM
password** of their own.

**Who is GM**

| Who | GM? |
| --- | --- |
| The host (the Editor's normal Play, or a test build that hosts) | Always, with no password. |
| One of the server's three GMs | After typing their GM password in **F2**, until they leave the server. |
| Everyone else | Never. F2 only asks for a GM password. |

**Make someone a GM (on the control page):** under **Game Masters**, type their name and a password of at least 8
characters that only they know (not one they use anywhere else), press **Add GM**, and tell them the password yourself.
Three at most; each password must be different. **Remove** takes it away; if they are in the game, their GM rights
end at once and they are told so. The list is saved into `server-settings.json` (`gameMasters`) straight away and is
kept across restarts and rebuilds.

**Become GM in the game:**

1. Join the server, press **F2**. The console asks for **GM password...** (the letters show as dots).
2. Type it, press **Enter**. "You are GM (Dom)." appears, and **[GM]** shows before your name to everyone.
3. Now every line typed in F2 runs **on the server**, aimed at **your own player**: `gm.help`, `gm.players`,
   `gm.say "text"`, `gm.kick`, give items or levels, teleport... `gm.target 3` aims the commands at another player (for
   you only; `gm.target self` comes back; `gm.players` lists the numbers). `clear` empties your console. The answer
   prints below each line. The console works as always: hints while you type, **Tab** to complete, **Up** for the last
   lines.
4. The GM chat channel (**/gm text**) now works for you.
5. **In the Editor**, the **GM Tools** window works too: **Target player** at the top lists the players your game can
   see (pick one, or yourself), and every button runs on the server on that player. The answers show in the window's
   log. Players far away are reached with `gm.players` and `gm.target` in F2.

Leaving the server ends it; type the password again next time. **Five wrong passwords** send that player away from the
server (they can join and try again). Every GM line is written to the server's log as `[GM] Dom: gm.say ...`, so you
can see on the control page who did what.

The password travels to the server like the rest of the game's traffic, which is not encrypted yet: use one that is
for this game only.

## 8. Stop or restart safely

Any of these is safe: the window's **close button**, **Ctrl+C** in the window, logging off, shutting Windows down,
**Stop** on the control page, or `gm.shutdown`. The server then:

1. saves every player and the world,
2. makes a backup,
3. tells every player why ("The server was closed.", or your reason),
4. stops.

**Restart** (on the page, or `gm.restart`) does the same, and `Start Server.bat` starts it again by itself.

Countdowns tell the players at 5 minutes, 2 minutes, 1 minute, 30 and 10 seconds, then each of the last 5.

Only pulling the plug, or ending the program in Task Manager, skips the save. Even then little is lost: every player
is also saved once a minute while they play, and as they leave.

## 9. Backups

- **When:** at start, every `backupHours` (6 by default), at every stop and restart, on **Backup now** and on
  `gm.backup`.
- **Where:** `KryticalNovaServer-Save-Backups`, beside the save folder. Each is a zip named by its date and time and
  why it was made: `save_2026-10-16_14-05-09_timed.zip` (`start`, `timed`, `stop`, `page`, `gm`).
- **How many:** the newest `backupsKept` (10). A backup is written under a temporary name and renamed when whole, so a
  stop in the middle never leaves a broken one.

**To go back to a backup:** stop the server, rename `KryticalNovaServer-Save` (to keep it, for instance
`KryticalNovaServer-Save-old`), make a new empty `KryticalNovaServer-Save`, unzip the backup into it, start the server.

## 10. What players see

| When | The player reads |
| --- | --- |
| The server's window is closed | "The server was closed. Your progress is kept on the server." |
| A countdown runs | GM lines: "The server shuts down in 1 minute." (or "restarts in") ... then the reason |
| Kicked | "A GM removed you from the server: <reason>. Your progress is kept on the server." |
| The server is full | "The server is full. Try again in a little while." (before they are in) |
| Another build | "This server runs build 5 and your game is build 4. Open the Krytical Nova launcher to update." |
| The server does not answer | "Krytical Nova Server did not answer. It may be off, or not reachable from here." |
| The connection drops | "The server closed, or the connection was lost. Your progress is kept on the server." |

Each comes with the same two ways out as the game menu (character select or exit), or staying where they stand.

## 11. When something goes wrong

| What you see | Why | What to do |
| --- | --- | --- |
| Norton says **IDP.Generic**, or the window says "cannot find the file" / "not recognized", or the server freezes after a minute | Norton does not know the new build | The folder exclusion (section 2). |
| The game says it cannot connect | Wrong address or port, or the server is not running | Check the window or the page says it is listening, and type that address and port in F1. |
| Your phone or another PC cannot open the control page | Windows Firewall, or a different network | Allow the server on private networks (Windows Security ▸ Firewall ▸ Allow an app); the phone must be on the home Wi-Fi, not mobile data. |
| "Open the page by this PC's address" | The page was opened by a name it does not know | Use the address the server's window prints (numbers like 192.168.1.73). |
| "Too many wrong passwords" | Five wrong tries from that device | Wait five minutes. The password is `pagePassword` in `server-settings.json`. |
| Forgot the page password | | Read it in `server-settings.json`, or empty it (`""`) and restart for a new one. |
| Not sure which port it really uses | | In PowerShell: `Get-NetUDPEndpoint -OwningProcess (Get-Process KryticalNovaServer).Id` |
| "Could not listen on port …" or "The control page could not open port …" | Something else uses that port | Close the other program, or change `port` / `pagePort`. |
| The window says "could not read server-settings.json" | A typo broke the file (a missing comma or quote) | Fix it, or delete it and start once to get a fresh one (a new page password too). |
| "Player turned away … the server is full" | `maxPlayers` reached | Raise `maxPlayers`. |
| The build says "The server … is still running, or a file in it is open" | The server (or a window showing a file in its folder) is open | Stop the server, close that window, build again. |
| Pressing Play in the Editor says "Port 7777 is in use on this PC … Play does not host" | Your server is running on this PC, so the Editor does not try to host beside it | Press **F1**, IP `127.0.0.1`, Port `7777`, **JOIN**. |
| A GM's password is not taken | A typo, or they were removed | Check the name under Game Masters on the control page; remove and add them again with a new password if needed. Five wrong tries send them away; they can join and try again. |

## 12. What comes next

- **Step 5:** joining from outside the house, through Unity Relay, with port forwarding as the fallback.
- **Step 6:** the zip, an install step (firewall rules, the Norton note) and moving it to the old PC by external SSD.
- **Step 7:** measuring how much one PC carries.

## For developers

| Piece | Where | What it does |
| --- | --- | --- |
| `ServerBuildBuilder` | `Tools/Editor/Build` | The build menus; adds the control page's files and fonts and the start script; keeps the settings file across a rebuild. |
| `ServerSceneStripper` | `Tools/Editor/Build` | Switches off parts that only draw in the server's copy of each scene. |
| `KryticalNova.Editor.Presentation` | `UI/Editor` (asmdef) | The HUD, menu, game feel and pet builders, left out under the server target; the rest of the Editor reaches them by name (`PresentationTools`). |
| `ServerLaunchOptions` | `Networking/Runtime/Server` | Reads the settings file and the command line; makes the page password. Server build only. |
| `ServerLifetime` | `Networking/Runtime/Server` | Backups on a timer, countdowns, the safe stop (the console close handler), restart (exit code 3). Server build only. |
| `ServerControlPage` | `Networking/Runtime/Server` | The control page: a small web server on a plain socket (no administrator rights), sessions, the calls. Server build only. |
| `ControlPageRules` | `Networking/Runtime/Server` | Home network only, request reading, the lockout, same-origin and Host checks (tested in `ControlPageTests`). |
| `ControlPage~` | `Networking` | The page's own files: `index.html`, `style.css`, `app.js` (copied beside the server by the build). |
| `ServerBackups` | `Gameplay/Persistence/Runtime` | Zips the save folder and prunes old backups. |
| `ServerConsoleInput` | `Tools/Debug` | Runs lines typed into the server's window, and the page's GM command box. Server build only. |
| `GmRoster` | `Networking/Runtime` | Who is GM: the list (three, name + password), password checks, five tries, removal ending GM at once (tested in `GmRosterTests`). |
| `PlayerGm` | `Networking/Runtime` | On every player: the GM name everyone sees, the password and command lines sent to the server, the answers back to that player only. |
| `GmCommandRelay` | `Tools/Debug` | Runs a joined GM's line on the server, aimed at that GM's player; keeps each GM's `gm.target` apart from the server's. |
| `JoinRules`, `GameBuild` | `Networking/Runtime` | Who may join: the same build (the Editor any), a free place; the build number in the version (tested in `JoinRulesTests`). |
| `ServerDirectory`, `ServerListFile`, `ServerStatusProtocol` | `Networking/Runtime` | The published list (from CCD, kept on the PC), "This PC" and "Play here" in development, each server's status, the last choice. |
| `JoinFlow` | `Networking/Runtime` | Enter's server; the world joins it under the loading screen (`NetworkConnectionManager.JoinChosenServer`) and says how it went. |
| `ServerStatusResponder` | `Networking/Runtime/Server` | Answers on the status port (never more bytes than asked, four a second per address). Server build only. |
| `LocalServer` | `Networking/Runtime` | START MY SERVER: finds your server's folder (the project's Builds, or `my-server.txt`), starts it minimized, and for now stops it with the game through its control page (`StopWithGame`). |
| `MenuServers`, `JoinFlowScreens` | `UI/Runtime/Menus` | The Servers window (in the menus' Settings document, so it wears the skin), the loading hold and the way back on failure. |
| `GmConsoleGate` | `Tools/Debug` | F2: the host runs commands itself; a joined player gets the password prompt, then their lines go to the server. |
| `ServerCommands` | `Tools/Debug` | `gm.kick`, `gm.restart`, `gm.shutdown`, `gm.backup`. |
| `NetworkConnectionManager.StartDedicatedServer` | `Networking/Runtime` | Listens on the settings' port and turns away players past the limit. |
| `DedicatedServerBoot`, `DedicatedServerOptimizer` | `Networking/Runtime` | Skip the menus and open the map; 30 ticks a second; parts that only draw switched off. |
| `EditorTestSession` | `Networking/Runtime` | The Editor's test port, scratch save and `join=1`. |

Things learned the hard way:

- With Unity's Dedicated Server package installed, Netcode's transport takes the package's port (7777) unless the port
  is set with `SetConnectionData(true, ...)`.
- Netcode's connection approval must never be switched on only on the server: every client would be refused.
- The Editor assemblies must keep compiling with `UNITY_SERVER`: never reference the UI or GameFeel assemblies from
  `KryticalNova.Editor`.
- Windows' own web listener (`HttpListener`) needs administrator rights to answer other machines, so the page answers
  on a plain socket. The start script calls the exe by its full path: a bare name is not looked for in the current
  folder where `NoDefaultCurrentDirectoryInExePath` is set.

## Change log

- 16 Oct 2026: written, after steps 1 and 2.
- 16 Oct 2026: step 3, the control page; `Start Server.bat`; Restart and `gm.restart`; the Norton IDP.Generic steps.
- 16 Oct 2026: Game Masters: three GMs by password on the control page; F2 GM commands run on the server; GM chat and
  the [GM] tag for GMs only.
- 16 Oct 2026: step 4: the Servers window (layout A in every skin), Enter joins the last server, the list kept on the
  control page and published by the server, the status port, the same build only (connection approval).
- 16 Oct 2026: START MY SERVER: the game starts your own server (minimized) when it finds it off and joins it; for now
  the server stops with the game; `my-server.txt` beside an installed game; `Start Server.bat --close`.
