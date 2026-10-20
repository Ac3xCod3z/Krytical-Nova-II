# Online phase 5: more than one map

Part of `online-roadmap.md`. Written 11 Oct 2026; **changed 20 Oct 2026 (owner): one server runs every map.** Not built.

## The owner's decision (20 Oct 2026): one server, every map

Asked whether separate servers per map are worth it for now, the owner picked **one server process that runs every
map**. Each player's game loads only the map that player is on; the server keeps all of them. Travel moves a character
from one map to another inside the same server, under the loading screen. Chat, Squads, Crews, Contacts and Sponsors keep
working as they do, because everyone is still in one program.

Why: the owner and two brothers play on one home PC. One server carries a few dozen players easily, while a server per
map would cost about 1 to 1.5 GB of memory for each map that runs and would mean rebuilding four social systems to work
across servers. The ceiling of one server (one main thread doing every map's work, very roughly 100 to 200 players in
all) and "one crash takes everyone down" are accepted for now. A server per map comes back in **Phase 8** (rented
machines, many players): travel is kept as one clean step so a busy map can move onto a server of its own then, and the
sections below marked *(Phase 8)* keep that design.

## Goal

A player walks through a portal (or uses Transit) and arrives on another map, with their character, Squad, Crew,
Contacts and chat intact, while players on other maps carry on undisturbed and never receive what happens elsewhere.

## Milestones (each stops for the owner's check)

1. **The Bootstrap scene.** The NetworkManager, the session manager and the other always-on parts (settings applier,
   performance log, warm-up) move out of Zone01 into a small Bootstrap scene loaded once after the menus. Maps are
   scenes swapped under the loading page: the old map unloaded, unused assets and one garbage collection while the page
   covers the screen, then the new map. Checked with the Editor hosting and with the test server.
2. **Two maps in one server.** The server loads its maps side by side, each placed far from the others so their ground,
   physics and pathfinding never touch (a fixed offset per map, the same on server and players' games). A player's game
   loads only its own map (Netcode's scene check refuses the others) and is shown only that map's players, Hostiles,
   drops and pets (Netcode object visibility by map). The per-map parts become per map: weather and the day cycle, the
   fight music's conductor, spawn points, Hostile Zones, trade zones. The second map: the owner's pick (a field zone, for
   instance Transit Row). The world map knows which map a player is on.
3. **Travelling.** The portal checks its rules (level, faction, not in a fight), saves the character, shows the loading
   page with the destination's travel screen, moves the player's object to the other map at its arrival point and makes
   it visible there only; Transit at the Wayfinder does the same. If the arrival fails, the player stays where they were.
   The log line: who, from, to.
4. **Everything that assumed one map.** Chat's General is a local shout (same map), the rest is unchanged; Trade,
   Callout, Follow and the Sponsor's nearby bonus need both players on the same map; Squad frames show the map; GM
   `gm.tp`, `gm.target` and `gm.travel` work across maps; the control page lists each player's map; the save keeps the
   map (already saved since phase 4) and a player rejoins on the map they left.
5. **Two players do all of it**, on the test server, then the real one: one travels, the other stays, both see only
   their own map and still chat, Squad and trade where the rules allow.

## Done when

- One server runs two maps. A player walks through the portal and arrives on the other map with gear, Creds, pet and
  Hotbar as they were; a player who stays sees them leave and nothing of the other map.
- A Squad of two on different maps sees each other's frame with the map name and talks in Squad chat; Faction and Crew
  chat arrive on both.
- A player who quits on the second map rejoins on it.
- A second client does all of the above, not only the owner.

## Risks

- Unity's NavMesh, physics and lighting are one world: maps must never overlap (the offset), and anything that searches
  "all objects" or "all players" (the dependency map lists them) must be made per map. Each is moved with its tests.
- Netcode syncs every scene the server has to every player by default: the scene check and object visibility must hold
  for every new spawned object, or a player would receive another map's objects.
- Floating-point precision far from the origin: offsets stay within a few kilometres.

## What exists today

| Piece | Where | State |
| --- | --- | --- |
| Portal | `World/Runtime/Portals/ZonePortal.cs` | Server-side trigger; `destinationZoneId` empty, `TODO(zone2)` |
| Transfer hook | `World/Runtime/Portals/IZoneTransferHandler.cs` | Only `SealedPortalHandler`, which always refuses |
| Zone manager | `Networking/Runtime/ZoneSessionManager.cs` | Switched off (`allowZoneTransitions`). Would disconnect, look for a Unity session tagged with the zone, and otherwise **start a local host**, which must never happen on a real server |
| Social systems | `PlayerParty` (a static `Group` list), `CrewService.OnlinePlayers`, `ContactsService.OnlinePlayers`, `PlayerChat.Gather()` over `ConnectedClientsList`, `SponsorService` | All find players inside one process |
| Kiosks | `KioskService` | Records already carry the scene name |
| Maps | One: `Zone01_StarterTown` | The portal on the North Island leads nowhere |

## *(Phase 8)* The world service

A small always-on program (the save service of Phase 4, grown). It has two faces:

- **Public** (clients, over normal encrypted web traffic, every request carrying the sign-in token):
  sign in, the character roster, "where do I go". Being ordinary web traffic, it can sit behind a standard
  web protection service.
- **Private** (map servers only, on the private network): saves, who is online and where, travel tickets,
  chat between servers, Squads, Crews, Contacts, Kiosks.

Every map server keeps one open connection to it and reports: I am map X, this many players, these
characters are here.

## *(Phase 8)* Travelling between servers

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

## The client side of travelling (added 15 Oct 2026; milestone 1)

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
- **Addressables are optional here**: one map is one scene in the build. They matter for build size and the phone
  port (`ProjectOrganization.md` section 10, `mobile-port-readiness.md`).
- Phones join the same map servers; nothing on the server side differs.

## *(Phase 8)* What crosses servers and what does not

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

## *(Phase 8)* Work items for a server per map

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

## *(Phase 8)* Tunables, done when and risks of a server per map

Ticket lifetime (30 s), the presence report interval, how long a server may be silent before the world service treats
it as gone. Done when two map servers and the world service run, a forged or reused ticket is refused and logged, and
killing the destination server mid-travel returns the player safely. Risk: four social systems lean on "everything is in
one process"; the world service becomes the one thing everything depends on.
