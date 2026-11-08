# Review rules

What counts as a real failure in Krytical Nova II, how bad each kind is, and how a review reports it. Every review of a
change starts from this page: the Master's `/code-review` on each PR before it goes in, and any session that checks its
own work with a reviewer subagent. The plan behind it is `Plans/code-evals-and-checks-plan.md` (items E and G).

## Who reviews, and how hard

- **The Master reviews every PR before it goes in**, with `/code-review` at the PR's risk level (below), against this
  page. The owner gets one short line: "reviewed, safe to merge", or the problems found.
- **Risk level** comes from what breaking the change does to a player:
  - **High:** a player loses items, Creds or a save, is locked out, or sees another player's data. Saves, the save
    service, economy, wallet, trade, Crews, accounts, GM roles, server authority. Review at `high`, and test with a
    second client.
  - **Medium:** a feature stops working, but nothing is lost. Other gameplay systems, world, networking, rendering,
    builds. Review at `medium`.
  - **Low:** something looks or sounds wrong. UI look, GameFeel, audio, icons, docs, art. Review at `low`; no new step
    for the session that made it.
- `kn_check.py --quick` has run (or the PR says why not). Its output is part of the review, not a substitute for it.

## Real failures, by severity

Each line is one kind of failure. "Seen" names the time it really happened here, so the rule is not a guess.

### High: stop the merge

- **A client decides what the server should decide.** Progression, items, Creds, damage, GM commands: the owning
  client sends a request and waits for the server's answer; it never changes its own copy. GM commands are host,
  server or offline only, and a joined client never gets GM powers of its own.
- **An economy change saved in pieces.** Anything that changes more than one player or world record (a trade, a Kiosk
  sale, a Crew founding, whatever the economy becomes) saves through `ServerSave.SaveTogether` in one change.
- **Creds moved outside `PlayerWallet`.** A new source or sink is a new `CredsSource` / `CredsSink`, never a side door.
- **An item movement with no ledger line.** Every item movement of an economy action writes its line through
  `EconomyLedger` (kinds in `LedgerKinds`; add a kind rather than overload one).
- **A save that changes meaning without a migration.** A rename, a split or another unit needs `IMigratingSave` with a
  tested step per version. A load must never reset a save it cannot read.
- **The real database, the real server or their ports in a test or tool.** Tests use `krytical_nova_test` and the
  save service on 7802; nothing a session runs touches `krytical_nova`, 7777 / 7778 / 7801 / 8090 or
  `SaveService --restore-real`.
- **A private port reachable from the internet.** The page and the save service bind locally unless the settings say
  otherwise. Seen: Linux plan item 10 (`51a722938`).
- **A secret in the repo, a log line or a chat message** (anything from `Documents\KryticalNova-Secrets`).
- **A tool that stops processes by name.** Killing `Unity`, `dotnet` or `AssetImportWorker*` by name can close the
  owner's or another session's Editor and lose their work. Start child processes in a job object the tool owns and stop
  only that. Seen: 30 Oct 2026, an EditMode wrapper's low-memory kill.

### Medium: fix before the merge, or the PR says why it waits

- **A server-spawned prefab not registered.** It goes in `GameplayPrefabsSetup.SpawnedPrefabs`, or clients never see
  it. The host shows nothing wrong. Seen: 5 Oct 2026, item drops invisible on the first real client.
- **A player system not in `PlayerSystemsSetup`.**
- **A NetworkManager field that differs between the prefab and a scene.** Netcode compares a hash of the config and
  drops the client before approval with only "disconnected by server". Seen: 19 Oct 2026 (`NetworkSetupTests` now
  guards it).
- **Code that behaves differently in batch mode, at build time or on the server, untested there.** `[ExecuteAlways]`
  code, `Application.isBatchMode`, `UNITY_SERVER` and `#if` branches are checked in the mode they change. Seen: build 12,
  25 Oct 2026, a black frozen sky: a presenter switched itself off in the batch publish and was packed into the map
  bundles switched off (PR 164). The Editor never showed it.
- **Editor code that breaks the server compile.** UI or GameFeel types reached from `KryticalNova.Editor`, or a
  stray line in an Editor builder, stop the server build or the whole Editor assembly. Seen: 16 Oct 2026 (server
  chunk 1) and `13097d41c` (a stray line in the Tripo gear builder).
- **A dependency that goes uphill or both ways** (`dependency_map.py --check` catches it).
- **A tuning number in code** instead of the settings asset that exists (`EconomySettings`, `CrewSettings`, ...).
- **An asset link that can break:** a script moved without its `.meta`, a renamed serialized field without
  `[FormerlySerializedAs]`, a `[SerializeReference]` class moved without `[MovedFrom]`, a path literal left pointing at
  an old place.
- **A retry with no limit.** A failed fetch, connection or job retries on a timer or a player's action, never on every
  event. Seen: 19 Oct 2026, a failed roster fetch retried on every `ServerDirectory.Changed`, hundreds a minute.
- **A test or check that passes without doing its work.** Seen: 30 Oct 2026, the first single-run `compile_check`
  only restored and "passed" with nothing compiled; caught by planting an error. A new check is proved by making it
  fail once.

### Low: note it; the session that made it decides

- **Per-frame allocations or lookups in hot paths** (`Update`, per-vertex loops, `GetComponent` on every call). The
  target is a locked 60 fps on mid-range hardware; the performance log is checked before blaming fps.
- **Player-facing text that is not the Krytical Nova term** (glossary in `Reference/12Sky/01_Systems_Build_List.md`),
  or a gamepad glyph in a prompt (keyboard glyphs only).
- **A new skill or modelless item without its own icon** from the icon library (Check Icons must stay clean).
- **A doc or the Encyclopedia not updated** after a system lands.

## What a review ignores

Vendor folders (`_ThirdParty`, `Synty`, the pinned roots), generated files (`out/`, `Temp/`, `Library/`,
`graphify-out/`), formatting and line endings, and style that the surrounding code already uses.

## How a review reports

One line per finding, most severe first:

`<High|Medium|Low> | <kind, as named above> | <file:line> | <what goes wrong, for whom>`

Name the kind so the fix covers every place it occurs, not just the one found. If nothing is found, say "reviewed at
<level>, nothing found" and what was not covered (for example "not tested with a second client").

## Round limits for build, check and review loops

For a session that builds, checks and reviews in rounds (itself or with subagents):

1. **At most three rounds.** If it still fails after three, stop patching: rethink the approach, or ask the Master.
2. **After round one, review only what the last fix changed**, and do not report a finding again that was already
   reported.
3. **Every finding names its kind** (above), and the fix covers that kind everywhere in the change.
4. **The reviewer is not the builder.** A subagent that reviews gets a fresh context and the diff, not the builder's
   reasoning.

## Learned rules

A one-line rule is added here when a review missed a bug that came back later, or kept flagging something that was
not a real problem. Only with the owner's yes. Each line: the date, the rule, and the PR or commit that taught it.

_(none yet)_
