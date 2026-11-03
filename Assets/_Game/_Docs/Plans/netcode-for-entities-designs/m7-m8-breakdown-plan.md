# M7 (world objects) and M8 (the menu systems): breakdown to COMPLETE

Owner order via Master (2 Nov 2026): M7 and M8 next, M4 combat still waits; this plan before any code. Written by DOTS 9.
The NGO rules tables (every NetworkBehaviour, RPC, NetworkVariable, save key, ledger kind and SaveTogether, with file:line) are in
`m7m8-rules-tables/` (`M7.md`, `M8-g12.md`, `M8-g3.md`, `M8-g4.md`), read-only research of both checkouts on 2 Nov; each system
below names its section there. This file is the plan: per system the DOTS edge, dependencies, tests, risk and size; then the order,
the checkpoints, the owner's Thursday hand-test list and the owner questions.

## 0. Rules for every system (from the owner's and the Master's standing rules)

- NGO rules table first (done in `m7m8-rules-tables/`; a system's port re-reads its section before code). Rules, saves and UI stay
  NGO's (edge swap only, plan section 4 M8 line); an owner change is marked as one.
- **A save key goes real in the same change that ports what writes it** (`SavePassthrough.DefaultReal`), never before (the system's
  stub would write nothing) and never after (the real Pack would lose what went into a passed-through part). Same format, so no
  `IMigratingSave`; the PlayMode test proves an old save loads unchanged and saves back byte for byte (as B3).
- **Item uses:** the server uses only effects whose result it saves (`PlayerRequestServerSystem.EffectPorted`; added 2 Nov after the
  research found mounts, Mount Feed, XP boosts and Dossiers spent into passed-through parts). A system's port adds its effect there.
- HIGH risk (saves, Creds, items between players, ledger, SaveTogether): test database / memory store only (never `krytical_nova`,
  ports 7777/7778/7801/8090), `TransactionLedgerTests` + the kill test where SaveTogether is used, Creds only through
  `PlayerWallet` (`CredsSource`/`CredsSink`), item moves of an economy action through `EconomyLedger`, and a review summary to the
  Master before the system is called done.
- Tests per system: EditMode rules + netcode-world (`PlayerRig`) tests; PlayMode on the real prefab where saves change; a batch
  `KnPlayHere` proof; a GUI GIF run where there is something to see. Max 2 runs per step; runs that never reach the test do not count.
  Slots from the Master before every Unity or dotnet job. `dependency_map.py --check` and `compile_check.py` (+ `--server`) before
  every multi-system commit.
- **Every system that looks at player bodies gets one real-prefab test** (Master, 2 Nov 2026), not only fake bodies: 7.3 and 7.4 passed
  with fake `IPlayerBody`s while every real DOTS server body answered `Owner = None` (NGO's `OwnerClientId`), so both would have
  skipped every player. `RealPrefabSmokeTests` (PlayMode, MemorySaveStore) is where these go.
- The shared edge (built once, reused by all): requests from the owner's view body through `BodyRoleTag.Requests` (never the stub's
  local `ServerX`), answered by a reply RPC with the result or the refusal text; owner-only ghost fields for what the HUD shows; paged
  text RPCs (`GoInGamePages`/`KnRosterPages`) for big state; observers get small ghost fields. Guards: `IsSpawned && IsServer` code
  never runs on a DOTS server body and `!IsSpawned || IsServer` lets the client stub act; each port switches its guards to `NetRole`.
  `OnNetworkSpawn/Despawn` hooks become `IServerLeaving` (before `SaveAndRelease`). NGO client ids become `PlayerKey`.

## 1. Shared plumbing (system 0, before the rest)

| Piece | DOTS edge | Tests | Risk | Size |
|---|---|---|---|---|
| Owner message line | `ServerTellRpc` (server -> owner text, paged when long) + the view's `ClientPresentationRegistry.Messages`; every "the message comes with M8's chat port" refusal (movement, water, wall, items) uses it | EditMode world test (server says, owner shows); RpcSizeLint | LOW | S |
| Owner Creds / Renown / level / points | owner-only ghost fields beside `OwnerFlux` (owner Q1), `PlayerWallet`/progression mirror on the view | world test: server changes, owner HUD follows; mirror never writes | MEDIUM | S |
| Generic body request | `BodyRequestRpc { Kind, args }` + one server dispatcher table (or one RPC per group, same shape as `ItemUseRpc`) | per-kind tests ride with each system | LOW | S |
| Item-use guard | `EffectPorted` (done 2 Nov, `PlayerRequestRulesTests`) | EditMode | HIGH (item loss) | S |

## 2. M7: world objects (`m7m8-rules-tables/M7.md`)

| # | System | DOTS edge | Needs | Tests | Risk | Size |
|---|---|---|---|---|---|---|
| 7.1 | World clock / weather (section 3) | server world singleton ghost (time of day, weather, calendar day), a Netcode-for-Entities clock provider replacing `NetcodeNetClock`; GM `time.set`/`weather` write it | nothing | world test (2 clients see one sky/day); batch + GUI GIF (GM time jump on both clients) | MEDIUM (calendar day feeds daily sale limits) | S-M |
| 7.2 | Music state (section 4) | small ghost singleton (~14 bytes) on the same clock; move the data the server needs out of Audio (layer fix) | 7.1 | world test (beat grid equal on server and owner); GUI by ear: Thursday | LOW-MED | S |
| 7.3 | ZonePortal (section 5) | `PortalRpc` request, cooldown by `PlayerKey`; only the "sealed" message today, real travel later via `KnWorldTickets` | message line | world test; GUI message | LOW | S |
| 7.4 | GM tools (section 6) | `GmCommandRpc` (paged text) -> the NGO command table on the server body, `GmReplyRpc`; `GmRoster`/`GmTarget` keyed by `PlayerKey`; GM window through it; `KnServerPage` gains `/api/command` + the GM roster (owner Q3 below decides the rest of the control page) | message line, Creds mirror | EditMode per command group (give, take, time, travel, kick), ledger `gm` line test on the test DB; batch + GUI GIF (window commands) | HIGH (Creds, items, ledger, who is GM) | L (221 commands, 2110-line window: ported by group) |
| 7.5 | Drops on the ground (section 1) + Loot (`M8-g12.md` 1.4) | drop ghost (pooled GameObject views first; Entities Graphics later), pickup request (5 m reach, 20 s holder hold by `PlayerKey`, 180 s life, 200 per zone), ledger `loot`/`drop` | 7.4 (`gm.drop`), Pack/wallet real (done); hostile loot after M5/M6 reward bridge (stub: player drops + `gm.drop` only) | world test (hold, reach, expiry, cap); ledger test; batch + GUI GIF (drop and pick up) | HIGH | M |
| 7.6 | Kiosk stands (section 2; `M8-g12.md` 2.3) | stand ghost (spike S1: replicated buffer of up to 32 listings; settings allow 48: cap at 32 or page) + open/buy/collect requests; `kiosks` world document; SaveTogether x3 | 8.2.1 Storage (permits from the Vault), Creds mirror | `TransactionLedgerTests`, kill test (test DB), world tests; GUI GIF | HIGH | L |

## 3. M8 group 1 (`M8-g12.md` 1.1-1.5)

| # | System | DOTS edge | Needs | Tests | Risk | Size |
|---|---|---|---|---|---|---|
| 8.1.1 | Wallet | owner Creds mirror (system 0); stub writes blocked (`NetRole`) | - | world test | HIGH | S |
| 8.1.2 | Inventory (rest) | move / equip / unequip / sort / destroy / split by request (cells by index is safe once the Pack mirror is exact; or by item + cell compare-and-swap), `EquipmentLooks` to observers, equipped data | Pack mirror (done) | world tests per op; PlayMode save round trip; GUI GIF (equip changes the look on a 2nd client: Thursday) | MEDIUM | L |
| 8.1.3 | Loadouts | requests + `loadouts` real | 8.1.2 | world test; PlayMode save | LOW | S |
| 8.1.4 | Flask refill | `RefillRpc` with the Apothecary range check (owner Q2), `CredsSink.Vendor`, save at once | Creds mirror, NPC range | ledger/wallet test; batch | MEDIUM | S |

## 4. M8 group 2 (`M8-g12.md` 2.1-2.4; all HIGH: test DB, review summary)

| # | System | DOTS edge | Needs | Tests | Size |
|---|---|---|---|---|---|
| 8.2.1 | Storage (Vault, Deep Vault) | requests + paged view; `vault` (character) and `deepvault` (account) real together | NPC range (Q2), Creds mirror | ledger `deepvault.in/out`, `creds.vault*`; PlayMode save; batch | M |
| 8.2.2 | Shopping (NPC shops) | buy/sell requests; `vendorWeek` real | NPC range (Q2), calendar day (7.1) | ledger `shop.buy/sell`; batch | M |
| 8.2.3 | Trade | 13 RPCs as requests/replies, partner by `PlayerKey`, "left the game" cancel as `IServerLeaving` before `SaveAndRelease`; `trade` real | message line, Creds mirror, neutral-ground zone data | `TransactionLedgerTests`, kill test (test DB), new: disconnect mid-trade, crash after Commit, NetworkId reuse, pending roster call at disconnect, PlayerKey leak; 2-client GUI GIF | L |
| 8.2.4 | Kiosk (player side) | with 7.6 | 7.6 | with 7.6 | (in 7.6) |

## 5. M8 group 3 (`M8-g3.md`; the NGO code already runs on the server body, the edge is missing)

| # | System | DOTS edge | Needs | Tests | Risk | Size |
|---|---|---|---|---|---|---|
| 8.3.1 | Progression | owner mirror (level, XP, points, Renown); allocate request | system 0 | world test | MEDIUM | S-M |
| 8.3.2 | Skills | upgrade request, owner rank list + unlock mask; `skills` + `movement` real together; Dossier (`LearnSkill`) added to `EffectPorted` | 8.3.1 | PlayMode save; world test; contract tests (SkillChipTests) | MEDIUM | M |
| 8.3.3 | XP boosts | `xpboosts` real, `ExperienceBoost` added to `EffectPorted`; minutes shown to the owner | 8.3.1 (XP only from kills: M5/M6, so the boost is proven by its clock) | ExperienceBoostTests, PlayMode save | MEDIUM | S |
| 8.3.4 | Serums | owner mirror + message line (use already ported) | system 0 | world test | LOW | S |
| 8.3.5 | Nova | 4 requests, NovaState to observers (halo/title), `nova` real; Ascension 100 Renown + 500,000 Creds (`CredsSink.Herald`, Renown handed back on a Creds failure) | 8.3.1, Creds mirror | NovaRulesTests, wallet test, PlayMode | HIGH | M |
| 8.3.6 | GearWork (Forge) | 5 requests, Armorer range 2.5 m, `CredsSink.GearWork`, Renown, ledger `forge`; slotting not built in NGO | NPC range (Q2), Creds mirror | GearProgression/Ladder/StatTable tests, ledger test (test DB), batch + GUI GIF | HIGH | M |
| 8.3.7 | Crafting | `CraftRpc` (<= 99), `CredsSink.Crafting` | as 8.3.6 | CraftRulesTests, wallet test | HIGH | S |
| 8.3.8 | Restyle | paged look request -> `PlayerLookServerSystem.SetLook`, passes 9301/9302 | look system (done) | RestyleRulesTests; GUI GIF (2nd client sees it: Thursday) | MEDIUM | S-M |
| 8.3.9 | Weaves / dye | 7 requests, fee 5000 (`CredsSink.Appearance`), `EquipmentLooks` to observers, `weave` real | 8.1.2 | WeaveRulesTests, MorphRulesTests, PlayMode | HIGH | M |
| 8.3.10 | Weave Library | owner-only paged lists (account `weaves` real) | 8.3.9 | PlayMode, world test | MEDIUM | M |
| 8.3.11 | Autobuff | move from Automation to Rules on the server body, cells request, licence <= 90 days, `autobuff` real, `AutobuffTime` added to `EffectPorted` | 8.3.2 | AutobuffRulesTests, PlayMode | MEDIUM | S-M |

## 6. M8 group 4 (`M8-g4.md`)

| # | System | DOTS edge | Needs | Tests | Risk | Size |
|---|---|---|---|---|---|---|
| 8.4.1 | Chat | say / tell / zone / system lines (2 in, 4 out), `ServerAnnouncer`; the message line grows into it | system 0 | world tests (who hears whom); 2-client GUI: Thursday | LOW-MED | M |
| 8.4.2 | Contacts | 12 requests, paged lists, `world/contacts` | 8.4.1 | world tests | MEDIUM | M |
| 8.4.3 | Party / Squad (+ Follow) | 6 requests, `PartyId` to observers, Follow as `ServerOrder` | 8.4.1; XP share after M5/M6 | world tests, batch | MEDIUM | M |
| 8.4.4 | Sponsor (mentor/recruit, not a subscription) | 4 requests, `world/sponsors`, `sponsor` real | 8.4.1; XP after M5/M6 | PlayMode, world tests | MEDIUM | M |
| 8.4.5 | Crew | 12 requests, `CrewTag` to observers, founding: Creds (`CrewFounding`), ledger `crew.found`, SaveTogether founder + Crew | 8.4.1, Creds mirror | `TransactionLedgerTests`, kill test (test DB), world tests | HIGH | L |
| 8.4.6 | Emotes | 2 requests, `EmoteState` to observers | - | world test; GUI GIF | LOW | S |
| 8.4.7 | Pets / tricks | 1 in / 4 out, `PetPublic` to observers; growth after M5/M6 | - | world test; GUI GIF | LOW | M |
| 8.4.8 | Mounts | 2 requests, `MountPublic`, `mounts` real, `RegisterMount`/`FeedMount` added to `EffectPorted` (refused until then: no item loss) | movement (ride speed already in `MoveStats`) | PlayMode save, world test, GUI GIF (riding) | MEDIUM | M |
| 8.4.9 | Furnishing Crate | 2 requests, `crate` real in the same change | Pack (done) | PlayMode save | HIGH (item loss) | S |
| 8.4.10 | Recall | channel aura + message (the move already works through the item use) | message line | batch + GUI GIF | LOW | S |
| 8.4.11 | Housing (Crew Hall) | offline-only on DOTS for now (no RPCs; no host mode, decision D5) | owner later | check it saves nothing on a DOTS client | LOW | S (check) |
| 8.4.12 | Duel prompts | WAITS for M4 (hostility reads the duel on every machine) | M4 | - | - | M |
| 8.4.13 | Cadence | WAITS for M4 + 7.2 | M4, 7.2 | - | - | M |

## 7. Recommended order and checkpoints

Each line is one checkpoint: the system's tests pass, its proof runs (batch, then GUI GIF where visible), the plan's evidence line is
written, a one-line report goes to the Master (HIGH: the review summary first), then the next one starts.

1. **System 0**: item-use guard (done, test run pending), owner message line, owner Creds/Renown/level mirror (Q1), generic request.
2. **7.1 clock/weather -> 7.2 music -> 7.3 portal** (small, no saves; the calendar day is needed by shops).
3. **7.4 GM tools, by command group** (give/take/time/weather/travel/kick first): every later system is tested with them.
4. **M8 group 1**: 8.1.1 wallet, 8.1.2 inventory rest, 8.1.3 loadouts, 8.1.4 Flask refill.
5. **M8 group 3 core**: 8.3.1 progression, 8.3.2 skills, 8.3.3 XP boosts, 8.3.4 Serums mirror (other systems read level and points).
6. **7.5 drops + loot** (player drops and `gm.drop`; hostile loot later).
7. **M8 group 2**: 8.2.1 storage, 8.2.2 shopping, 8.2.3 trade, then **7.6 + 8.2.4 Kiosk** (stand ghost).
8. **M8 group 3 money/looks**: 8.3.6 Forge, 8.3.7 crafting, 8.3.5 Nova, 8.3.8 restyle, 8.3.9 weaves, 8.3.10 library, 8.3.11 Autobuff.
9. **M8 group 4**: 8.4.1 chat, 8.4.2 contacts, 8.4.3 party, 8.4.4 sponsor, 8.4.5 Crew, 8.4.6 emotes, 8.4.7 pets, 8.4.8 mounts,
   8.4.9 crate, 8.4.10 Recall, 8.4.11 housing check.
10. **Waiting for M4/M5/M6** (listed, not started): 8.4.12 duels, 8.4.13 Cadence, hostile loot, party/sponsor XP, pet growth.

Size in session work (rough): S ~ half a session step (code + tests + one batch proof), M ~ one, L ~ two to three. Whole plan
about 45 to 55 steps; several DOTS sessions (DOTS 9 hands over at 85-90 % of context).

## 8. The owner's hand-test list for Thursday (nothing waits on it before then)

From movement (already done): item 2 jump from the water to a clicked spot (real camera); item 3 re-click while waterstriding;
item 8 b Jump selected + click a roof (real camera); item 5 stim + Flux Flask on the DOTS test server, relog, counts stay; the two
backflips' speed (80 % of the air). Added as M7/M8 systems land (each: what to click, what to see):
- 7.1/7.2: two clients see the same sky, time and weather; a GM time jump changes both; the music sounds the same on both.
- 7.3: walk into the Zone01 portal (north-east outskirts) on a DOTS test server: only you see "The way beyond is sealed... for now.",
  once per walk in, and not again within 3 s.
- 7.4: as a bound GM on a DOTS test server, F2 opens the GM console and gm.players / gm.time / gm.give answer in it (a player who is
  not GM: F2 stays shut, and a line is refused); the GM window's commands (give, take, time, travel, kick); on the server page the
  command box and the GM list (add, bind to a player in the game, unbind, remove).
- 8.1.2/8.3.8/8.3.9: equip, restyle and a weave seen from a second client.
- 7.5: drop an item, a second player cannot take it for 20 s, then can; it is gone after 3 minutes.
- 8.2.x/7.6: a Vault deposit and withdraw, a shop buy and sell, a trade with a second player, a Kiosk sale, each after a relog.
- 8.4.x: chat say/tell, a contact, a party with Follow, founding a Crew, an emote and a mount seen from the second client.
The list is kept current in this section as systems land.

## 9. Owner questions (real choices; recommendation first)

1. **How the owner's client knows Creds, Renown and level** (needed by the wallet, Forge, Nova, Kiosk, Storage):
   (a) Recommended: owner-only ghost fields beside the Flux, always current (a few bytes per snapshot, like the Flux and HP now);
   (b) sent as pages only when a window opens.
2. **The NPC-range systems** (Flask refill, shops, Storage, Forge, crafting, restyle): (a) Recommended: port them now with server-side
   range checks against the map's NPC objects and refusals as message lines; (b) wait for `NpcAnchor` (M4b) and the full chat port so
   each is ported once.
3. **The GM control page**: (a) Recommended: `/api/command` + the GM roster now (every command works from the page's box), the
   other ~17 NGO page controls later; (b) the whole NGO control page in M7.
Decided here without a question (DOTS choices, can be changed): Kiosk with its stand ghost in M7 (7.6) and its player side in M8 g2;
drops as pooled GameObject views first (Entities Graphics later, as decided for bodies); social systems for one server now (the
world-service relay across map servers is not built in NGO either); mount, XP-boost, Dossier, Autopilot and Autobuff item uses
refused until their port (no item loss); Housing stays offline-only on DOTS.

**Owner answers (2 Nov 2026, via Master):** Q1 "just like the current game": Creds in the Pack window, Renown in the Character window,
level where NGO shows it, always correct when shown, by NGO's own update path (NGO keeps them in network variables, so owner-only
ghost fields), no new HUD display. Q2 (a): port the NPC-range systems now with server range checks against the map's NPC objects and
refusals as message lines. Q3 (a): `/api/command` + the GM roster now, the other control-page controls later.
