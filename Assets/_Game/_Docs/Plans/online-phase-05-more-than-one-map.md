# Online phase 5: more than one map

Part of `online-roadmap.md`. Written 11 Oct 2026. **Not built.** Needs Phase 4 and a second map (a
grey-box one is enough to build against).

## Goal

A player walks through a portal and arrives on another map that is run by another server process, with
their character, Squad, Crew, Contacts and chat intact.

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

## Travelling

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

Ticket lifetime (30 s), the presence report interval, how long a server may be silent before the world
service treats it as gone.

## Done when

- Two map servers and the world service run on the owner's PC. A player walks through the portal and
  arrives on the other map with gear, Creds, pet and Hotbar as they were.
- A Squad of two on different maps sees each other's frame and talks in Squad chat; Faction and Crew chat
  arrive on both.
- A forged or reused ticket is refused and logged.
- Killing the destination server mid-travel returns the player safely.
- A second client does all of the above, not only the owner.

## Risks

- This is where "everything is in one process" stops being true, and four social systems lean on that.
  Each is moved one at a time with its tests.
- The world service becomes the one thing everything depends on. It must restart cleanly, and map servers
  must keep their players playing (without cross-server chat) while it is away.
