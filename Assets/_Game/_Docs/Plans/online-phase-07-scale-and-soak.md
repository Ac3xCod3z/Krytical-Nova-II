# Online phase 7: scale and soak

Part of `online-roadmap.md`. Written 11 Oct 2026. **Not built.** Needs Phase 5; it runs before Phase 6, whose caps come from here.

## Goal

Know, from measurement, how many players a Line and a war map can hold, and set the caps from that.
Until this phase every population number is a guess.

## Targets already written down

| Target | Source |
| --- | --- |
| The server holds 30 ticks a second | `NetworkManager.prefab` (`TickRate: 30`), `DedicatedServerOptimizer` (30 frames) |
| 300 active Hostiles on a map; 25 kbit/s or less per client with 60 Hostiles in view | `hostiles-plan.md`, section 11.4 |
| Under 15–20 KB/s per client while moving and casting | `mmorpg-architecture-plan.md`, verification 4 |
| Clients: a locked 60 frames a second on mid to upper-mid hardware, none under 50 | Owner's performance target, 10 Oct 2026 |

## What exists today

- `Networking/Runtime/SpatialInterestManager.cs`: hides objects beyond 75 m from each client, re-checked
  every 0.5 s, by comparing every client with every entity. That cost grows with players × entities and
  is the first thing to give on a war map.
- `Player.prefab`'s transform sends full-precision position, rotation and scale reliably.
- Hostiles send an animation state every 0.1 s (`HostileNet`).
- No bot clients, no load test, no record of server frame time.

## Work items

1. **Bot clients.** A client build started with `-bot <script>`: it signs in with a test account, joins,
   and plays a script (walk a route, fight Hostiles, cast, chat, trade with another bot, switch Line).
   No window, many per machine. Bots use the same calls as a real client, so they also exercise the rate
   limits of Phase 2.
2. **Server measurements.** Each map server logs, every 10 s: frame time (average and worst), players,
   active Hostiles, bytes sent and received per client, memory. The `PerformanceReadout` shows the same
   to a GM.
3. **Interest management on a grid.** Replace the every-client-with-every-entity loop with cells (the
   architecture plan proposed 48 m), so the cost follows what is nearby.
4. **Send less.** Three distance tiers for update rates (the architecture plan: full rate to 35 m, a
   lower rate to 80 m, nothing beyond); half-precision positions; unreliable deltas for movement; scale
   not sent at all for players.
5. **War-map settings.** A war map is a different load: few Hostiles, hundreds of players in one place,
   and it can never be split into Lines. It gets a crowd setting of its own: its own interest radius and
   update tiers, pets and other decoration not sent, distant players drawn simply on the client (the
   "number of players drawn" setting of build list row 21). The number this phase finds for a war map is
   what the cap per faction and the queue are sized from (`online-roadmap.md`, "Lines, fronts and
   Mercs").
6. **Find the knee.** Raise the bot count in steps on a Home City Line, a hunting map and a war map until
   the server misses 30 ticks or a client passes its bandwidth target. Write the numbers down.
7. **Set the caps** in `WorldSettings` from those numbers, with a margin.
8. **Soak.** Twenty-four hours at the cap with bots joining, leaving, trading and travelling. Memory
   must be flat, saves correct, the ledger balanced.

## Done when

- A table in this document gives, for each map kind: players at 30 ticks, bandwidth per client, server
  memory, on a named machine.
- The caps in `WorldSettings` come from that table.
- The 24-hour soak ends with flat memory, no stuck locks and a balanced ledger.
- Real clients among the bots hold the client frame-rate target.

## Risks

- The owner's PC is not the rented machine. The numbers are taken again in Phase 8 on the real one.
- Emerald AI's cost per active Hostile sets the hunting-map limit more than players do; the Hostile zone
  sleep rules already exist to manage it.
