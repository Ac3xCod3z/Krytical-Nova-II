# Krytical Nova II online: the roadmap from your PC to a first beta

Written 11 Oct 2026. **Nothing in this document is built.** It is the plan for taking the game from "one
player hosts on his PC" to "dedicated servers a beta can run on", in ten phases. Each phase has its own
document beside this one; the security rules are in `online-security-model.md`; the move to rented
machines is in `online-going-live-runbook.md`. Game content (maps, wars, classes) stays in
`Reference/12Sky/01_Systems_Build_List.md`; this covers the online side only.

This document replaces Phase 4 of `mmorpg-architecture-plan.md` as the plan for scaling.

## Decisions (owner, 11 Oct 2026)

- **The Unity route.** Our own Unity dedicated servers on Netcode for GameObjects. The server is the only
  source of truth; the client is never trusted.
- **Planetary Processing is dropped.** Its server is Lua scripts on their machines with no Unity on it, so
  combat, pathfinding, Hostile AI, the economy and saving would all be rewritten. Its free plan stops at
  10 players and 10 dimensions.
- **One open world.** One copy of every map, one server per map and per war. A copy of a map is a
  **Line** (the owner's word for what other games call a channel). A further Line exists only by rule:
  a map that overflows, a war with more queued than it holds, The Scar when enough wait for a fair
  fight. Faction chat reaches every Line. This replaced the first idea of channels everywhere.
- **Wars have a cap per faction and a queue**; a queue larger than one war is split into even wars
  instead of turning players away.
- **Mercs.** When the sides are uneven, players on the crowded side are asked to fight for a short side
  for that one event, as a carbon copy of themselves in the other faction's look. Not in Beacon Wars.
- **Beacon Wars are open to absolutely everyone, whatever map or zone they are on** (for now). The
  Beacon stands in the Home City, which can be attacked; its shops and Kiosks stay open.
- **Fronts.** A front is one Line of each map on a war's road; one Beacon is shared by every front.
  Details of all five: "Lines, fronts and Mercs" below.
- **The Scar** is our name for Twelve Sky 2's Yanggok Valley. The **Ronin** are the fourth faction.
- **The game goes on Steam, and is also downloaded from our own website through our own launcher.** The
  website is made later. Two ways in, one game world, one account behind both (Phase 4 and Phase 9).
- **Documents first.** Phase 1 starts when the owner says so.

## What Unity gives us free, and what it does not

| Need | Unity's answer | Cost while developing |
| --- | --- | --- |
| The server program | Dedicated Server build target (Linux or Windows, no window, no graphics) | Free |
| Running it | **Unity rents no game servers.** Its hosting, Multiplay, ended on 31 Mar 2026 and went to Rocket Science Group | Free on your own PC |
| Testing it in the Editor | Dedicated Server package + Multiplayer Play Mode "Play Mode Scenarios" (a local server instance beside the Editor) | Free |
| Friends joining without port forwarding | Unity Relay (the server opens a session and gives a join code; your home address is not shown to players) | Free allowance (3 GiB per player, 150 GiB a month at the time of writing: check the pricing page) |
| Knowing who a player is | Unity Authentication | Free |
| Saves | Unity Cloud Save has a free tier (5 GiB, 1,000,000 writes a month at the time of writing). Whether we use it or our own database is decided in Phase 4 | Free either way while developing |
| Payments | Not needed from Unity: the Nova Store is a browser page opened from the Store Clerk, with the payment company's hosted checkout (Unity In-App Purchasing can link Stripe, but its in-game checkout is documented for phones) | Nothing until we sell; pretend payments and test modes are free |

A rented machine is needed only from Phase 8 on. Prices and free allowances change; every figure here is
checked again before it is relied on.

## The pieces

| Piece | What it is | Today |
| --- | --- | --- |
| **Client** | The game players install. Holds no secrets and none of the rules that only the server needs | Windows test build (`TestBuildBuilder`) |
| **Map server** | One headless process runs one map on one Line. It decides everything that happens on that map | Stubs: `DedicatedServerBoot`, `DedicatedServerOptimizer`, `NetworkConnectionManager.StartDedicatedServerAsync` (which publishes 127.0.0.1, so nobody outside could join) |
| **Sign-in** | Unity Authentication proves who a player is; the map server checks the proof before letting them in | The client signs in, but the save uses an id the client makes up (`CharacterSession`, `kn.account.v1`) |
| **World service** | A small always-on program beside the map servers. It keeps the list of map servers and how full they are, knows who is online and where, hands out travel tickets, carries chat between servers, and is the only writer of Crews, Contacts and Kiosks | Nothing. Unity has no such piece; it is ours |
| **Database** | Every save, behind `ISaveStore` | Files on the host (`FileSaveStore`) |
| **Payments** | The Nova Store: a stall in the game (the Store Clerk) opens it in the browser; Stripe's hosted checkout on the website build, Steam's own purchase window on the Steam build; goods go to the Pack, or the Vault when it is full | Nothing |
| **Launcher** | A small signed Windows program for the website build: updates itself and the game, signs the player in, starts the game | Nothing |

```
 players ──► map server (Home City, Line 1) ─┐
 players ──► map server (Home City, Line 2) ─┼──► world service ──► database
 players ──► map server (The Scar) ───────────┘        ▲
 players ─────────── sign-in, server list ─────────────┘
```

Clients only ever talk to a map server and, at sign-in, to the world service. They never reach the
database.

## Lines, fronts and Mercs

A **Line** is one running copy of a map. Twelve Sky 2 and most MMOs say "channel"; ours is Line (owner's
word, 11 Oct 2026) because "channel" already means chat. The HUD shows it beside the map name: "Home
City · 2".

### One open world (decided 11 Oct 2026)

There is one Line of every map, and one server per map and per war. Everyone on a map is together. A
further Line exists only where one of the rules below makes one.

| Map | Lines | Rule |
| --- | --- | --- |
| Hunting maps, hubs | One | A second only if measurement (Phase 7) shows one server cannot hold the players who stand on it |
| Home City | One; more only if it overflows | It holds the faction's Beacon and can be attacked. During a Beacon War its Lines are the war's fronts. Its shops and Kiosks stay open during the war (owner) |
| Siege Night, Turf War | One server per war | A cap per faction and a queue; a queue larger than one war is split into even wars |
| The road of a Beacon War: The Scar, each faction map, the Home City | Open to absolutely everyone, whatever map or zone they are on (owner, "for now") | One Beacon, however many fronts |
| The Scar | One, with a cap per faction | A second opens only from an even queue |

Why not literally one server program for the whole game: a Unity server does its work on one processor
core. One program would have to run every map's players and Hostiles on that one core, one crash would
drop everybody, and there would be no way to add room. One program per map keeps each map's load apart
and lets a quiet map cost almost nothing. "One open world" means one Line of each map, not one process.

### Cost

What is paid for is processor cores in use (players and awake Hostiles), memory for each map that is
running (a map holds its whole scene in memory whether one player is on it or a hundred), and bandwidth
per player.

- The same players cost about the same in one Line or in three. Each extra Line adds its own memory and
  its own awake Hostiles, so **Lines cost more, not less.** They buy room, not savings.
- The bandwidth saving is real only in a dense crowd: three hundred players in one plaza send each other
  far more than three plazas of a hundred. Out in the hunting maps the 75 m visibility limit already
  keeps traffic down.
- The real savings: Hostile zones that sleep with nobody near (built); a map's server that starts when
  the first player travels to it and stops after standing empty (Phase 8); many quiet maps sharing one
  rented machine.
- For a first beta, one Line of every map on one machine is both the cheapest and the simplest layout.

### The limit, and what answers it

One map server holds only so many players in one place. Ours is not measured (the only figure in code is
100); Phase 7 finds it, and every cap below is sized from that number, never guessed. The maps that draw
the biggest crowds are the ones that must stay whole, so extra Lines alone cannot answer it. What does:

**Wars are dealt into even wars (owner, 11 Oct 2026).** A war has a cap per faction and a queue. When
more players queue than one war holds, nobody is turned away for that: the server takes everyone who
queued and splits them into as many wars as are needed, with the sides matched in each. The owner's
example: a cap of 99 (33 a faction) and 150 queued becomes two wars. One server per map and per war.

**Mercs even the sides (owner, 11 Oct 2026; the name is the owner's pick).** When the factions queue
unevenly, the server asks players on the crowded side whether they will fight for a short side for this
one event, so that they need not sit out.

- A Merc is the same character: stats, skills, level and gear stats are a carbon copy. They are dressed
  in the other faction's equivalent equipment and fight as that side until the event ends. Then they are
  themselves again.
- If too few accept, the war opens uneven up to a limit (a setting, not yet chosen), and the players
  beyond it wait in the queue.
- A Merc earns what the side they fought for earns, on their own character. One exception is decided:
  the war in The Scar is fought for a faction-wide buff lasting an hour, and a Merc does not benefit
  from it. The other rewards are decided later.
- Mercs are offered in Siege Nights, Turf Wars and The Scar. **There are no Mercs in a Beacon War.**

**Beacon Wars are open to absolutely everyone, whatever map or zone they are on (owner, "for now").**
No Mercs: real factions fight for a real Beacon. Read here as: nobody is made to wait for a Beacon War,
and when more players come than one server holds, another front opens.

**A front is a numbered lane (owner's yes, 11 Oct 2026).** A front is one Line of every map on a war's
road: front 1 is The Scar 1, then faction map 1, then Home City 1; front 2 is the second Line of each.

- Siege Night, Turf War: each front is its own war with its own result.
- Beacon War: there is one Beacon however many fronts there are. Every front shows the same Beacon with
  the same health, kept by the world service; damage on any front counts against it, and it falls
  everywhere at once. A Home City's ordinary Lines become the fronts, so nobody is moved and nobody is
  locked out of their own city.
- **The Scar** uses the same rule all the time, because it never closes. The Scar 1 is always there with
  a cap per faction. A player who arrives when their side is full is offered a place as a Merc on a
  short side; those who decline wait at the portal, and once enough are waiting for a fair fight the
  server opens The Scar 2 and deals them in evenly. A Line that empties stops taking arrivals and
  closes. A player can join a Squad mate's Line when their side has room there.

**Also:** a Beacon War is spread along its road of maps, each its own server; Siege Nights are split by
level bracket in the reference (10–19, 20–29 and so on), each bracket its own war; war maps get a
lighter crowd setting (Phase 7).

Not planned: several servers sharing one map with players handed across invisible borders. Netcode for
GameObjects has nothing for it and it would be a large system of our own.

### Where a map has more than one Line

- On entering it a player is placed in the Line their Squad is in, otherwise the fullest Line that still
  has room.
- The Line list shows each Line and how full it is. A player may switch from it, or jump to the Line of a
  Contact or Squad mate. Not while fighting, and not into a full Line.
- Faction, Crew, Squad and Whisper chat reach their members on every Line and every map. General stays a
  local shout.
- Line sizes are not set. They go in a settings asset, from Phase 7's numbers.

### Proposed details of a Merc (Claude's, not reviewed)

- The look is put on the way Holoweave does it: it changes what is drawn and nothing else
  (`EquipmentLooks`). Where a body cannot wear the other faction's set (a Null frame in Covenant armour),
  the crest and colours change instead.
- The namecard shows the side's crest and a MERC tag. For the event the Merc hears that side's Faction
  chat; their Crew and Squad chat are untouched.
- A Squad is asked as a whole, through its leader, and goes together or not at all.
- The offer is answered once and checked by the server; the answer is a client-to-server call and goes
  on the audit list of `online-security-model.md`.
- Hostility for the event follows the side fought for (`CombatHostility`), and nothing about the Merc's
  saved character changes.

## The ten phases

| # | Phase | What it gives | Needs | Document |
| --- | --- | --- | --- | --- |
| 1 | Dedicated server on your PC | A windowless server; clients join it; nobody "hosts" | — | `online-phase-01-dedicated-server.md` |
| 2 | Lock the door | Verified sign-in at connect, encryption, every client call audited and rate-limited | 1 | `online-phase-02-lock-the-door.md` |
| 3 | Responsive input | Your character moves and casts at once; the server corrects | 1 | `online-phase-03-responsive-input.md` |
| 4 | Accounts and shared save | A database, real accounts, safe trades, backups | 2 | `online-phase-04-accounts-and-shared-save.md` |
| 5 | More than one map | Walking through a portal onto another server | 4, and a second map | `online-phase-05-more-than-one-map.md` |
| 6 | Lines, fronts and Mercs | Extra copies of a map where the rules make them; even wars; Mercs | 5; sized by 7 | `online-phase-06-lines.md` |
| 7 | Scale and soak | Proof of how many players a map holds | 5 | `online-phase-07-scale-and-soak.md` |
| 8 | Real servers | The same build running on rented machines | 7 | `online-phase-08-real-servers.md` |
| 9 | Beta readiness | Steam, our launcher, terms, support tools, patching, invites | 8 | `online-phase-09-beta-readiness.md` |
| 10 | Payments | The Nova Store taking money safely on both builds | 9 | `online-phase-10-payments.md` |

Phases 2 and 3 do not depend on each other and can be done in either order. With one open world,
Phase 7 (measure) runs before Phase 6: every cap in Phase 6 comes from Phase 7's numbers. A first beta
of hunting and towns can open without Phase 6; wars need it. Everything else is in order.

## Why it moves to real servers unchanged

Six rules hold from Phase 1. If they are kept, moving is: copy the build, set the settings, import the save.

1. **The server is a Linux headless build from the first day** (a Windows one is built beside it for your
   PC). No step later "ports" it.
2. **Everything a server needs arrives from outside**: command-line arguments and environment settings.
   Nothing about your PC, your address or your folders is in code, scenes or prefabs.
3. **A map server keeps nothing of its own.** Every save goes through `ISaveStore`, so files become a
   database by swapping one class, and a map server can be stopped and started anywhere.
4. **Clients learn addresses from one place** (a Relay code now, the world service later), never from a
   number typed into the build.
5. **Secrets never enter the repository or the client**: database passwords, service keys, payment keys.
6. **The same build is used everywhere.** A build that passed its tests on your PC is the build that goes
   to the rented machine; only the settings differ.

## What is still open, and the phase that needs the answer

| Question | Needed by |
| --- | --- |
| How uneven a war may open when too few take the Merc offer (a setting); the smallest war worth opening | Phase 6 |
| The Merc details listed as proposed (look on bodies that cannot wear the set, chat, Squads) | Phase 6 |
| The war in The Scar (its hour-long faction buff) and every other war reward | With the war design, build list row 28 |
| "Open to absolutely everyone" for Beacon Wars is "for now": revisit once a war map has been measured | Phase 7 |
| GM on a dedicated server before accounts exist: the plan uses an account list given to the server at start | Phase 1 |
| Should the server test melee hits itself instead of checking the client's report? | Phase 2 |
| Sign-in for the website build: Unity Player Accounts or our own email accounts (the Steam build uses Steam sign-in) | Phase 4 |
| Database: our own Postgres (my recommendation, for trades as one transaction) or Unity Cloud Save | Phase 4 |
| Line sizes, and the size of a war map | Phase 6 / 7 |
| The host | Phase 8 |
| Kiosks and Bruisers on a map with more than one Line: in every Line or in one | Phase 6 |
| Whether beta characters are kept at launch | Phase 9 |
| Steam's current rules for in-game purchases and for a game also sold elsewhere | Phase 9 / 10 |

## Sources checked on 10 and 11 Oct 2026

Unity Multiplay shutdown notice (status.unity.com); Unity Gaming Services pricing page; Netcode for
GameObjects 2.x manual (client anticipation; it has no full prediction and rollback); Unity IAP 5.4
direct-to-consumer notes; Planetary Processing documentation repository and pricing page; OVHcloud,
Edgegap and AWS GameLift DDoS notes; Stripe fulfilment guide. Vendor pages are marketing; figures are
re-checked before any money is committed.
