# M8 group 4, Crew on DOTS: NGO rules and the DOTS design (DOTS 12, 3 Nov 2026)

Read with `m7m8-rules-tables/M8-g4.md` section 3. Read from the clone's `Social/Runtime/PlayerCrew.cs` (380), `CrewService.cs` (433),
`CrewRules.cs` (192), `Data/CrewSettings.cs` and `Data/Assets/CrewSettings.asset`. Rules stay NGO's; only the edges move. Risk **HIGH**
(Creds, a ledger line, SaveTogether): memory store in PlayMode, the kill test only on `SaveService --test` (7802 / krytical_nova_test).

## 1. NGO facts the port keeps (file:line in the clone)

| Rule | NGO | DOTS consequence |
| --- | --- | --- |
| 12 owner RPCs at `CallKind.Social` + TextFits: Found(name, symbol, colour, tagColour, tagBackground), Invite(ref), InviteByName, Respond, Leave, Expel, Officer, HandOver, Title, Announcement, Symbol, Disband | `PlayerCrew.cs:195-292` | one `CrewOpRpc` at Social; a picked player goes by name (remote views carry no PlayerCrew's server half) |
| Arrival on load / leaving on despawn -> `CrewService.ServerArrived/ServerLeft` (OnlinePlayers by character id) | `PlayerCrew.cs:96-124` | `Start()` on the server body / `IServerLeaving` (as Contacts, Sponsor) |
| Founding: Vaultkeeper (`NpcOffer.CrewRegistry`, 2.5 m padding), level 30, 10,000,000 Creds, not in a Crew, name 3-16 unique (any case) | `CrewService.cs:121-137`; asset `founderLevel 30`, `foundingCost 10000000` | runs unchanged on the server body (NPC objects are in the server's scene, as the shops) |
| Creds: `wallet.ServerSpend(cost, CredsSink.CrewFounding)` | `CrewService.cs:138` | PlayerWallet is real and role-aware on DOTS |
| Ledger: `LedgerNote.Keep(player, LedgerKinds.CrewFound, "founded " + name)` | `CrewService.cs:150` | unchanged (the save agent's tally) |
| **SaveTogether(founder, "crews")** in one change | `CrewService.cs:151` -> `PlayerCrew.cs:318` | unchanged: `ServerSave.SaveTogether(saveAgent, world)` on the server body (as trade 8.2.3) |
| Every other change saves `world/crews` alone (`SaveWorld`) | `CrewService.cs:52` | unchanged |
| Invite: same faction, rank may invite, member cap by Crew level, not in a Crew, SocialGate; 30 s answer; one pending | `CrewService.cs:160-173`, `PlayerCrew.cs:361-371` | `ServerOfferInvite` guard `IsSpawned && IsServer` -> server body too; the prompt by link |
| Invite by name: `CrewService.OnlinePlayers` (not ConnectedClientsList) | `CrewService.cs:175-189` | works once the server body arrives |
| `ServerShow(crew, roster)`: the tag everyone reads (netTag, read Everyone), `chat.ServerSetGuild` (the Crew channel), the owner's view (header + roster array) | `PlayerCrew.cs:324-350` | guard -> server body; tag = a server field + the `PlayerCrewTag` ghost field for every view; view = text pages to the owner |
| `Tag => IsSpawned ? netTag.Value : default` | `PlayerCrew.cs:66` | server field on the server body, the ghost's on a view |
| Tell = chat System line; Notify = message line (NotifyRpc) | `PlayerCrew.cs:296-310` | as Contacts (Tell -> chat, Notify -> OwnerMessages) |

## 2. DOTS design (the port pattern)

- Core `CrewOpKind` + `IBodyRequests.CrewOp(op, a, b, b0, b1, b2, b3, flag)`; `CrewOpRpc {Op, Flag, B0..B3, FixedString64 A, FixedString512 B}`.
- Items `IBodyCrew` (RunCrewOp, `BodyCrewTag` read/mirror, MirrorView(text), MirrorPrompt, MirrorClosePrompt) and `ICrewOwnerLink`.
- Social `CrewViewText` (header line + one line per member, tab fields, escaped).
- Persistence.Entities `CrewSystems.cs`: requests at Social -> `RunCrewOp`; the owner's view in pages (`CrewPageRpc`, one Seq); the prompt
  (`CrewPromptRpc`); every player ghost's `PlayerCrewTag` written from its body, mirrored on every view (remote views keep PlayerCrew: the
  namecards read its Tag).
- Commits: (a) requests, the owner's view and prompt, founding with SaveTogether + ledger, B34; (b) the tag for everyone (ghost field,
  RemoteKeepsRules) + a test; then the kill test run on `--test` 7802 and the HIGH review summary.

## 3. Tests (planned before the run)

- B34 (real prefabs, MemorySaveStore): far from a Vaultkeeper refused; A (level 30, 10,000,000 + Creds given by the GM source) founds at a
  CrewRegistry NPC: Creds down by exactly 10,000,000, one `crew.found` ledger line on A's account, the `crews` world document and A's
  character written in ONE batch (MemorySaveStore.Batches +1), A's tag (name, Leader); the Guild channel reaches the Crew; invite B by name
  -> prompt -> accept -> both rosters; officer, title, announcement; B leaves; disband; the view asks (exact requests) and mirrors.
- B35 (with commit b): the tag reaches another player's view (namecard) through the ghost field's mirror.
- Kill test: `kill_test.py` with the clone's SaveService on `--test` 7802, then the service stopped (SaveTogether atomicity, unchanged).
