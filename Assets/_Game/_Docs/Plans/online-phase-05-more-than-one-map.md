# Online phase 5: more than one map

Part of `online-roadmap.md`. Written 11 Oct 2026; **owner's decisions of 20 Oct 2026 below.** Not built.

## The owner's decisions (20 Oct 2026)

- **A server per map, now, behind one name.** After weighing one server for every map against a server per map, the
  owner chose map servers now, with one condition: "I don't want players seeing different server names on the select
  screen confusing them, as Nova is the one they see now." The server list and character select show **one entry,
  Nova**; the map servers behind it are never listed, and the game passes players from one to another by itself when
  they travel. (An interim pick that morning, one server running every map, was reversed the same day.)
- **The world service grows out of the save service** (one program, already started by the Server app).
- **The Server app runs every map server**, a tab each.
- **The second map is Transit Row** (Meridian Sprawl's first field zone), reached by Concord's portal.
- **Players outside the house** reach the second map server through two more forwarded ports, UDP 7779 and 7780
  (walked through with the owner, as 7777/7778 were).
- **Zone01 is Concord** (owner): Twelve Sky 2's Saigo Temple, the neutral town of all factions; its square stays the
  Central Plaza.

## Goal

A player walks through a portal and arrives on another map that is run by another server process, with their
character, Squad, Crew, Contacts and chat intact, and never sees more than the one server name, Nova.

## Milestones (each stops for the owner's check)

- [x] 1. **A second map server behind one name.** *(built 20 Oct 2026, PR 117, waiting for the owner's check)* Transit Row runs as a server of its own (`-map`, its own port) beside
   Concord's, both started from the Server app (a SERVER tab each). Only Concord's server publishes the server list, so
   players see one entry, Nova; a map server never publishes, refuses to run without the save service, makes no backups
   of its own (the save service's nightly copy covers the database) and keeps its records in a folder of its own. Its
   control page and records say which map it is.
- [ ] 2. **The world service** (in the save service): presence (which map server holds which character) and signed, use-once
   travel tickets.
- [ ] 3. **Travelling**: the client side first (the network and the always-on parts out of Zone01 into a small Bootstrap
   scene loaded once after the menus, maps swapped under the loading page with memory freed between them), then the
   portal (`WorldTransferHandler`), save and release, the ticket, the loading page, the new server checks the ticket and
   places the player; a failure returns the player where they were; the log lines. The rewritten `ZoneSessionManager`
   never starts a local host.
   - [x] Every map an Addressable, loaded through `MapScenes`; scene syncing off; `SceneObjectKeeper` (20 Oct 2026, owner pick B)
   - [x] A built game loads its map from the bundle and joins a server (bot test, 20 Oct 2026: Concord from its bundle, no lost scene objects)
   - [x] What the maps share in one bundle, `World_Common` (map bundles 15 GB to 1.2 GB)
   - [ ] The portal, the ticket, the arrival and the way back on failure
- [ ] 4. **Chat across map servers**: Faction, Crew, Squad and Whisper through the world service; General stays local; `gm.say`
   reaches every server.
- [ ] 5. **Squad, Crew, Contacts and Sponsor across map servers**; the world map knows a friend is elsewhere; GM `gm.tp` and
   `gm.target` find a player on any map.
- [ ] 6. **Two players do all of it** (the done-when list), on the test servers, then the real ones.

The order was changed on 20 Oct 2026: the second server first, because a Bootstrap scene is only needed once a player
moves from one server to another; it is built as the start of travelling.

**Milestone 1 as built (20 Oct 2026):** `mapServers` in `server-settings.json` (Transit Row, port 7779, page 8092 by
default; an older file gains it by itself) and `-role map` (`ServerLaunchOptions.IsMapServer`, `RecordFolderPath`,
`MapServerProblem`). A map server skips `ServerReach` (no list), is forced Direct, makes no backups and trims no ledger
(`ServerLifetime`), writes its records in `MapServers/<map>` inside the save folder and its performance log with the
map in the name, stops at once without the save service or with a map the build lacks (`DedicatedServerBoot`), and its
control page says map server and refuses the list and mode calls. The Server app (`ServerApp~/MainWindow.cs`) has a tab
per game server and stops them all on close. Checked beside the measure server (Concord 7790, Transit Row 7792/8093,
test save service): both online, commands on each tab, the safe stop of both. Not checked: a player on Transit Row's
server (that is travelling, milestone 3). Until milestone 5, world documents (Crews, Kiosks, Contacts, Sponsors) are
only changed on Concord's server; a map server holds what it read at start.

## What exists today

| Piece | Where | State |
| --- | --- | --- |
| Portal | `World/Runtime/Portals/ZonePortal.cs` | Server-side trigger; `destinationZoneId` empty, `TODO(zone2)` |
| Transfer hook | `World/Runtime/Portals/IZoneTransferHandler.cs` | Only `SealedPortalHandler`, which always refuses |
| Zone manager | `Networking/Runtime/ZoneSessionManager.cs` | Switched off (`allowZoneTransitions`). Would disconnect, look for a Unity session tagged with the zone, and otherwise **start a local host**, which must never happen on a real server |
| Social systems | `PlayerParty` (a static `Group` list), `CrewService.OnlinePlayers`, `ContactsService.OnlinePlayers`, `PlayerChat.Gather()` over `ConnectedClientsList`, `SponsorService` | All find players inside one process |
| Kiosks | `KioskService` | Records already carry the scene name |
| Maps | One: `Zone01_StarterTown` | The portal on the North Island leads nowhere |

## The world service

A small always-on program (the save service of Phase 4, grown). It has two faces:

- **Public** (clients, over normal encrypted web traffic, every request carrying the sign-in token):
  sign in, the character roster, "where do I go". Being ordinary web traffic, it can sit behind a standard
  web protection service.
- **Private** (map servers only, on the private network): saves, who is online and where, travel tickets,
  chat between servers, Squads, Crews, Contacts, Kiosks.

Every map server keeps one open connection to it and reports: I am map X, this many players, these
characters are here.

## Travelling between servers

1. The player touches a portal. The map server checks the portal's own rules (level, faction, not in a
   fight) through a new `WorldTransferHandler` in place of `SealedPortalHandler`.
2. The map server saves the character and releases its lock.
3. It asks the world service for a **ticket**: this account, this character, to that map, arriving at
   that point, good for 30 seconds, usable once, signed so a map server can check it and nobody can
   forge it.
4. The player's client is sent the address and the ticket, shows the Loading screen, disconnects, and
   connects to the new server with the ticket in its approval data (the gate from Phase 2).
5. The new server checks the ticket, takes the lock, loads the character and places it at the arrival
   point.
6. If anything fails, the player is returned to where they were, or to their Home City if that server is
   gone. A character is never in two places and never in none.

`ZoneSessionManager` is rewritten around this. Its fallback of starting a local host is removed.

## The client side of travelling (added 15 Oct 2026; milestone 3)

What the game itself needs before a second map can be loaded at all:

- **A Bootstrap scene first.** Today the NetworkManager, `NetworkConnectionManager` and `ZoneSessionManager` live
  inside `Zone01_StarterTown` (`DontDestroyOnLoad` keeps them alive, but a second map scene would bring its own copy).
  They move into a small persistent Bootstrap scene loaded once after the menus, together with the other persistent
  systems (settings applier, performance log, warm-up). `ProjectOrganization.md` already plans a `Bootstrap` scene
  folder and Addressables group.
- **One scene per map**, swapped under the Loading page: the old map scene is unloaded, then
  `Resources.UnloadUnusedAssets` and one GC run while the page covers the screen (today only one `GC.Collect` runs,
  in `LoadingScreenController`), then the new map loads. The Loading page already waits on `LoadingGate`; arriving on
  the new server is one more gate.
- **Each map has its own APV bake set** with Day and Night scenarios (as `APV_Zone01_StarterTown` has), so its lighting
  takes over the moment it loads; the day-cycle blend keeps working (`EnvironmentPresenter.BlendLightingScenarios`).
- **No hand-made packet queue.** The client disconnects from one map server and connects to the next; NGO's scene
  sync on the new server delivers the world only once the scene is ready.
- **Every map is an Addressable (owner, 20 Oct 2026: "make sure with the newer maps they use addressables").** The
  owner picked **B** of three: the map scenes themselves are Addressables (a group per map, `World_<scene>`, packed into
  the game, no download server), the game loads a map through `MapScenes` under the loading page and the map before it
  is let go, and the game joins a map's server only once its map is loaded. Netcode's scene syncing is **off**
  (`EnableSceneManagement` 0): it knows only the build list, so it cannot load an Addressable scene, and one map per
  server makes it unneeded. Without it Netcode destroys every networked object a scene placed as a game joins and
  rebuilds each from its prefab, which would lose the placed ones that are no prefab (each map's World Environment,
  Concord's Combat Dummy and Zone Portal) and their scene settings: `SceneObjectKeeper` (Networking) gives each of them
  a prefab-instance handler under its own id before the game joins, so Netcode keeps it and brings that very object to
  life when the server spawns it. The game's build lists only the front end; a dedicated server lists the maps after it and loads its
  own from there (`BuildSceneOrder.ShippedForServer`), with no bundles. APV is stored with the scenes (Probe Volume
  Disable Streaming Assets), as Addressable scenes need. In a session in the Editor, `gm.travel` now closes the host's
  session, loads the map and opens it again there (joined games are asked to join again) until travelling between map
  servers lands. The Sidekick parts in `Resources` (469 MB) move to Addressables later, as a step of their own (owner).
  Built 20 Oct 2026 as the first part of milestone 3 (`MapScenes`, `MapAddressables`).
- Phones join the same map servers; nothing on the server side differs.

## What crosses servers and what does not

| System | Across maps |
| --- | --- |
| Faction, Crew, Squad, Whisper chat | Yes, carried by the world service. General stays a local shout |
| Squad | Membership and the frames (name, level, map, alive) yes. Shared XP stays "same map", as it is designed |
| Crew, Contacts | Online status, last seen, invitations, announcements: yes |
| Whisper by name | Yes; names are unique since Phase 4 |
| Trade, Callout, Follow, Sponsor's nearby bonus | No: both players must be on the same map |
| Kiosks | A Kiosk stands on one map; its record is in the world service so a sale is one transaction |
| Hostiles, world drops, weather | Per map server |
| GM | `gm.say` reaches every server; `gm.tp` and `gm.target` find a player on any map |

## Work items

1. The world service's private face: presence, tickets, the chat relay.
2. `WorldTransferHandler`, the client's travel flow, the rewritten `ZoneSessionManager`.
3. `PlayerChat`: channels other than General go through the relay; delivery stays on each recipient's own
   player object.
4. `PlayerParty`, `PlayerCrew`, `PlayerContacts`, `PlayerSponsor`: membership and status come from the
   world service; the in-process lists become a local cache of it.
5. Each map's portals get their destination and arrival point; the world map (`UI/Runtime/Map/WorldMapController.cs`) learns
   that a player may be elsewhere.
6. A second map scene registered in the build, with its spawn points and its entry in
   `ServerLaunchOptions` (`-map`).
7. The log lines for travel: who, from, to, ticket accepted or refused.

## Tunables

Ticket lifetime (30 s), the presence report interval, how long a server may be silent before the world service treats
it as gone.

## Done when

- Two map servers (Concord and Transit Row) and the world service run on the owner's PC, started by the Server app; the
  server list shows one entry, Nova. A player walks through the portal and arrives on Transit Row with gear, Creds, pet
  and Hotbar as they were.
- A Squad of two on different maps sees each other's frame and talks in Squad chat; Faction and Crew chat arrive on both.
- A forged or reused ticket is refused and logged.
- Killing the destination server mid-travel returns the player safely.
- A second client does all of the above, not only the owner.

## Risks

- This is where "everything is in one process" stops being true, and four social systems lean on that. Each is moved one
  at a time with its tests.
- The world service becomes the one thing everything depends on. It must restart cleanly, and map servers must keep their
  players playing (without cross-server chat) while it is away.
- Memory: each map server is a full Unity program (about 1 to 1.5 GB). Only maps someone can reach run; the owner's PC
  has 31 GB shared with the Editor.
