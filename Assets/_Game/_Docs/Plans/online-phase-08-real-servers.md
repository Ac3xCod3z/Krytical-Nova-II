# Online phase 8: real servers

Part of `online-roadmap.md`; the checklists are in `online-going-live-runbook.md`. Written 11 Oct 2026.
**Not built.** Needs Phase 7. This is the first phase that costs money every month.

## Goal

The build that passed its tests on the owner's PC runs on rented machines, reachable by anyone with the
game, protected against the attacks a public game server gets, with nothing changed in the code.

## Why nothing should need changing

The six rules in `online-roadmap.md` ("Why it moves to real servers unchanged") have held since Phase 1:
a Linux server build, every setting from outside, no data on the map server, addresses from one place,
no secrets in the repository or client, one build everywhere. This phase is the test of those rules. If
something has to be edited in code to run on the rented machine, that is a bug in an earlier phase.

## Choosing the host

Unity does not rent game servers (Multiplay ended 31 Mar 2026). What matters, in order:

1. **Protection against UDP floods is included**, in writing, for the product actually bought. Game
   servers are attacked more than almost anything else, and this cannot be fixed in game code.
2. **Linux machines near the players** (start with one region).
3. **A private network** between machines, so the database and the world service are never on the
   internet.
4. **Price that is known in advance.**
5. **How servers are started**: by hand and by script at first; automatically by population later.

What was found on 11 Oct 2026 (vendor pages; confirm before buying):

| Kind | Example | Notes |
| --- | --- | --- |
| Rented machine with game DDoS protection | OVHcloud Game servers | Game-specific UDP protection included on the Game range; check the exact product |
| Cloud game hosting | AWS GameLift Servers | DDoS protection included since 2026; starts servers by demand; more to learn |
| Managed game hosting | Edgegap | Starts and stops servers for us, pay by the minute, protection varies by location |
| Plain rented machine | Hetzner and similar | Cheapest; flood protection described as basic. Not without a test |
| The Multiplay software | Rocket Science Group | What Unity's hosting became; pricing not published |

Recommended for a first beta: one rented machine with game DDoS protection for the map servers, and the
world service with the database on a second small machine on the private network. Managed hosting is
looked at again when Lines need to start and stop by themselves.

## Work items

1. Choose the host against the list above. Owner's decision.
2. A deploy script: copy a numbered server build, write its settings, start the world service, then each
   map server and Line; and the reverse to roll back to the previous numbered build.
3. Each process runs under a supervisor that restarts it if it dies and sends it a stop signal on
   shutdown (which saves everyone: Phase 1, item 5).
4. Firewall, secrets, backups, monitoring and the public face of the world service, as listed in the
   runbook.
5. A **staging** copy: the same layout at the smallest size, with its own database. Every new build goes
   there first.
6. Import the test saves with the tool from Phase 4.
7. Repeat the Phase 7 measurements on the real machine and correct the caps.
8. An attack rehearsal: the attack tests of Phase 2 against the public address; a port scan showing only
   the game ports and the web port answer; a restore of last night's backup onto staging.
9. Automatic Lines: the world service asks for a new Line when the last one passes its soft cap and stops
   sending arrivals to an emptying one. (By script on one machine first.)

## Done when

- A tester who has never touched the owner's PC installs the client, signs in and plays on the rented
  server.
- No code was changed to make that happen: only settings.
- Only the game ports and the world service's web port answer from the internet.
- A build is rolled out and rolled back on staging by the script.
- Last night's backup restores onto staging.
- The owner has, in writing, what the host does during a flood and how to reach them.

## Cost

Not yet known: it depends on the host and the caps from Phase 7. The machine, the database's disk, backup
storage, the bandwidth, and a code-signing certificate for the client (Phase 9) are the lines to price.
