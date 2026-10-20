# Online phase 4: accounts and shared save

Part of `online-roadmap.md`. Written 11 Oct 2026. **Started 18 Oct 2026** with the owner's picks below; nothing
built yet. Needs Phase 2 (milestones 1 to 4 built; 5 parked).

## Goal

Saves live in a database that more than one map server can use safely. A player has a real account that
survives a reinstall and works from Steam and from the website build. A crash can neither duplicate nor
lose items or Creds.

## How saving works today (kept)

From `05_Economy_And_Social.md` and the code:

- The server saves; a client never reads or writes a save (`ServerSave.HasAuthority`).
- Each system saves its own part (`ISaveParticipant`: a key, a scope, capture, restore). Parts on
  11 Oct 2026: inventory, wallet, vault, progression, skills, movement, nova, serums, autopilot, look,
  weave, sponsor (per character); deepvault, trade, weaves (per account). The list grows as systems land;
  the current one is the "Saving" table of `05_Economy_And_Social.md`.
- `PlayerSaveAgent`: load on entry, autosave every 60 s, save on leave, save at once after a trade, a
  sale or a founding; only written when something changed.
- Storage is behind `ISaveStore` (`TryRead`, `Write`, `Delete`, `Keys`). `FileSaveStore` writes one JSON
  file per key under `persistentDataPath/ServerSave/<profile>/`.

All of that stays. What changes is underneath.

## What breaks with more than one server

| Today | Why it breaks |
| --- | --- |
| One document per account holds every character (`AccountSave`) | Two characters of one account on two servers overwrite each other |
| "Already in the world" is an in-process check (`ServerSave.TryBind`) | Two servers do not see each other |
| `world/crews`, `world/contacts`, `world/kiosks`, `world/sponsors` are whole-file rewrites (`CrewService`, `ContactsService`, `KioskService`, `SponsorService`) | The last server to save wins |
| A trade writes two account files one after the other (`PlayerTrade.Commit`, 464–498) | A crash between them duplicates or destroys |
| `ISaveStore` is synchronous | A database call must not stall the server's frame |
| The roster is in PlayerPrefs (`PlayerPrefsCharacterStore`, `kn.characters.v1`); a character's first look and its name come from the client (the look is saved on first entry since 11 Oct 2026 and wins from then on); names are unique only inside one roster | Which characters exist is the client's word; nothing survives a reinstall; two players can hold one name |
| Map and position are not saved | Needed once there is more than one map |
| `version = 1` is written, never read | Old saves meet new code |

## The owner's picks (18 Oct 2026)

1. **Postgres**, our own, after the pros and cons: more setup now, much less trouble as the game grows (a trade, a
   Kiosk sale or a Crew founding changes together or not at all; Crews, Kiosks and unique names fit; a change of save
   format is one tested migration run on the server; free; moves to a managed host later without touching the game).
   Unity Cloud Save was turned down: one blob per player, no all-or-nothing changes, a capped free tier.
2. **A small save service** in C# beside the servers, the only holder of the database password; game servers ask it
   over the private network through a new `ISaveStore`. It is the first piece of Phase 5's world service.
3. **Unity Player Accounts** for our own download (the launcher build): a Unity sign-in page in the browser the first
   time (email, Google or Apple), automatic after that as today; Steam sign-in on the Steam build, linked to the same
   account through Unity Authentication.
4. **The same account opens the Nova Store** (owner): from the game, the Store Clerk's one-time link ("this account,
   this character", once, a few minutes; Phase 10) signs the player in to the store; opening the store's page directly
   in a browser offers "Sign in" with the same Unity Player Account, which must come out as the same Krytical Nova
   account, so a purchase always lands on the right account. **To prove in milestone 7 before it is built on:** that
   Unity's web sign-in gives the same player as the game's.

### Milestones (each stops for the owner's check)

1. **Postgres and the service's first slice.** Postgres on the owner's PC (local only, its password in
   `Documents\KryticalNova-Secrets`, never in the repo); the save service with load, save and the per-character lock;
   `ServiceSaveStore` behind `ISaveStore` with an asynchronous path; the measure server pointed at it; the file store
   still used for the Editor and tests. **Small saves (owner, 18 Oct 2026: the 60-second saves must be byte-sized and
   usable on their own):** each part a system saves (`ISaveParticipant`: Pack, wallet, Vault, skills, pets, ...) is its
   own row (character, part, version, its JSON); an autosave sends only the parts whose text changed since the last
   one, in one all-or-nothing call, so a usual 60-second save is a few hundred bytes, not the whole account, and any
   part can be read, fixed or carried on its own.
2. **Records.** One record per character, the account record holding only what is shared; map and position saved;
   versions read and stepped up by tested migrations; the tool that carries a file-store folder into the database and
   back (test characters survive).
3. **All-or-nothing changes and the ledger.** Trade, Kiosk sale, Deep Vault move and Crew founding as one call each; the
   kept ledger; the kill tests (a server stopped mid-change, many times: nothing duplicated or lost).
4. **World records.** Crews, Contacts, Kiosks and Sponsors changed one entry at a time through the service.
5. **The roster on the server.** Create, list and delete characters through the service, names unique across the game,
   the look checked there. *Built 19 Oct 2026 (see As built).*
6. **Backups.** Nightly, kept 14 days, off the machine; a restore drill; the runbook and the Server Guide.
7. **Real accounts.** Unity Player Accounts on the launcher build, the end of anonymous accounts outside local tests,
   Steam linking ready for the Steam build, and the proof that the Nova Store's web sign-in is the same account.

### Keeping the disk in check (owner, 18 Oct 2026: "kick the oldest so old data does not fill my disk")

- A save **replaces** the one before it (the version goes up; no history of saves is kept), so the database grows with
  the number of characters, not with how often they save. Postgres's own autovacuum (on by default) takes back the
  space each replaced row leaves.
- **Backups** (milestone 6): nightly, the newest 14 kept, older ones deleted by themselves.
- **The ledger** (milestone 3): kept for a set number of days (`ledgerDays`, 90 to start) in the server's settings;
  older lines leave the database after that night's backup holds them.
- **The record files** beside the server (`gm.log`, `economy.log`, `access.log`, `flags.log`): a new file each month,
  the last 6 kept (done with milestone 3).
- The control page's Saves chip shows the database's size.

### As built

**Milestone 1 (18 Oct 2026): Postgres, the save service, the game's side.**
- Postgres 18.6 on the owner's PC, a Windows service that starts with Windows, listening on this PC only. Two
  databases: `krytical_nova` (the real saves) and `krytical_nova_test` (tests and the measure server only); one login,
  `krytical_save`, made by `Persistence/Service~/Setup Database.bat` (secrets in `Documents\KryticalNova-Secrets`).
- The save service (`Persistence/Service~/SaveService`, .NET 10, published as one exe beside the server by a server
  build): save documents with a version each (a write naming an older version is refused), a lock per account that one
  server holds at a time and that runs out three minutes after the server stops renewing it, a key on every request,
  `/health`. Real on port 7801; `--test` on 7802.
- The game's side, `ServiceSaveStore` behind `ISaveStore`: writes on a thread of its own (no frame waits), the version
  sent with each write, the account locked when a player's character loads and let go only after their last save
  reached the service, a health check every 10 seconds (the control page's **Saves** chip). `ServerSave` uses it when
  `server-settings.json` names `saveService`; a server set to use it does not open without it. `Start Server.bat`
  starts the save service the settings name (7801 real, 7802 test). New measure servers use the test saves.
- Not yet: the account is still one document (milestone 2 splits it into characters and parts, for byte-sized
  autosaves); the real server still saves to files (it switches once milestone 2 carries its saves across).
- *Tried:* the service's own check (14 of 14: no key or a wrong key refused, a stale write refused, the lock taken,
  refused to another server, renewed and let go); `ServiceSaveStoreTests` 6 of 6 against the test service; EditMode
  1000 of 1000. End to end on the measure server with the test saves: the Editor joined as Tolan, the account was in
  `krytical_nova_test` (17 parts, 3 KB, saved on joining and by the autosave), its lock held by the server; on leaving
  it was saved once more and the lock let go at once.

**Milestone 2 (18 Oct 2026): accounts kept in parts, and the copy tool.**
- An account is a small header (`accounts/<id>`: version 2, the account-wide part names, each character's name,
  faction, level, map, place and part names; no game state) and one document per part
  (`accounts/<id>/parts/<part>`, `accounts/<id>/<character>/<part>`: a character's parts live inside its account, since a character id comes from the player's game; a part name is kept in lower case). A save
  writes only the parts whose text changed and the header if it changed, as one all-or-nothing batch on the save
  service (`IBatchSaveStore`; files are written one by one); a part a system no longer saves is deleted. A character
  loads in one trip (`/read`).
- An account in the old one-piece layout is read as it is, kept once as `accounts/<id>-v1` and saved in parts from its
  next save. The header's version says which layout an account is in; `ServerSave.ReadAccountFrom` reads either.
- Map and position are saved in each character's header (a move of more than a metre counts as a change); they are used
  once there is more than one map.
- The save service: `/batch` (one stale write turns the whole save away), `/read`, the database size in `/health` and
  on the Saves chip.
- **Copy tool** (Krytical Nova ▸ Online ▸ Saves): a save folder into the real or the test database, in parts, and a
  database back into an empty folder. The source is only read; an account the target has is left alone and named.
- *Tried:* `SplitSaveTests` (6), `SaveCopyToolTests` (test database), EditMode 1007 of 1007. End to end on the test
  server: Tolan, saved in milestone 1 as one 3 KB document, joined; the server kept that document as `-v1` and wrote a
  471-byte header and 17 parts (11 to 492 bytes). After a few steps the autosave wrote 617 bytes (the header, for the
  new place, and the XP boost timers) and nothing else; on leaving it saved once more and let the lock go.

**Milestone 3 (19 Oct 2026): all-or-nothing changes and the kept ledger.**
- **One change, whole or not at all.** A save is a `SaveBatch` (documents written and deleted, and ledger lines). The save
  service applies a batch in one database transaction; the save folder through a journal (`FileSaveStore` writes the
  change whole to `_journal/` first, puts it in place, deletes the journal; a journal a stop left is finished when the
  store opens again, its ledger lines never twice). `ServerSave.SaveTogether` (players and world documents) and
  `WriteTogether` (accounts) put several into one batch: a **trade** (both players), a **Kiosk** opening, purchase and
  collection (the player and `world/kiosks`), a **Crew founding** (the founder and `world/crews`). A Deep Vault move is
  inside one account, so one batch already.
- **A batch sent twice is saved once.** Each batch has an id; the service remembers applied ids for a day and answers a
  batch sent again (its first answer was lost) with the first answer. Before this, a lost answer made the resend look
  stale, and every later save of that player was refused (found while building the kill test).
- **The kept ledger** (`LedgerEntry`; database layout 2: tables `ledger` and `applied_batches`, stepped up by itself).
  A line waits for the next save of its account and goes in the same batch as the change it describes, so a crash
  loses both or neither. **Creds** are tallied at the one door they all pass, `PlayerWallet` (`creds.<source or sink>`,
  one line per kind per save): an account's lines add up to its wallet, whatever system moves Creds, now or after a
  rework. **Items** through `EconomyLedger` at each action: trade (with the partner), Kiosk open / buy / collect (with
  the owner), shop buy / sell, Deep Vault in / out, Crew founding, forge (a broken piece counts as lost), loot and drops
  (tallied per save, not per pickup: about a line a minute per player while hunting), and GM commands (the Pack compared
  before and after every command, so any GM command is covered; `GmCommandRelay`). `gm.ledger` and the GM Tools window
  read it. The database keeps lines as text with only account, kind and time (owner, 18 Oct 2026: the economy will be
  reworked; the database must not know its rules).
- **Disk:** ledger lines older than `ledgerDays` (90) dropped at start and once a day (`ServerLifetime`); each backup holds
  the lines since the backup before it (`ledger.jsonl`); the record files start a new file each month and keep
  `recordMonths` (6) (`ServerRecords.CurrentFile`).
- *Tried:* `TransactionLedgerTests` (6: two players, a world document and their lines as one batch; tallies; the file
  journal finished after a stop and not counted twice; order in the background; ledger by day and trimmed),
  `ServiceSaveStoreTests` (a batch's lines kept with it; a batch sent twice applied once), EditMode 842 of 842. **Kill
  test** (`Service~/KillTest/kill_test.py`, test database): 4,000 trades between 6 accounts while the save service was
  killed 103 times (85 batches sent again after no answer, 15 dropped by a simulated game-server crash): no Creds or
  items made or lost, every wallet and Pack equal to its start plus its ledger lines, no line twice. **With the game
  itself:** the measure server rebuilt with this milestone, bots (rebuilt as build 8) joined, each was given 500 Creds and
  two items by GM command and hunted, and the server was killed (taskkill /F) at a random moment, four rounds (20 bot
  characters, 74 ledger lines, Creds from GM commands and from Hostiles): every saved wallet equalled its start plus its
  ledger lines; a change the kill caught before its save was lost together with its lines, never one without the other.
  Not tried with two players: a trade and a Kiosk purchase between two clients (their saving is `SaveTogether`, tested
  above; the trade and Kiosk rules themselves did not change).

**Milestone 4 (19 Oct 2026): world records, one entry at a time.**
- Crews, Contacts, Kiosks and Sponsor links are each a list of entries (`IWorldEntries` in Core: each world class lists
  its entries with a key and puts them back). `ServerSave` keeps such a document as a small header (`world/<name>`:
  version 2 and the entries' keys, in order; no game state) and one document per entry (`world/crews/<crew id>`,
  `world/contacts/<character id>`, `world/kiosks/<owner character id>`, `world/sponsors/<recruit>-<sponsor>`). A save
  writes only the entries whose text changed, deletes the ones that went and rewrites the header only when the list
  changed, as one all-or-nothing batch (with players too, through `SaveTogether`). So a change to one Crew writes that
  Crew alone, and two servers changing two different Crews never write over each other; a server holding an older copy
  of an entry another server changed is refused by the entry's version.
- A document saved whole before this is read as it is, kept once as `world/<name>-v1` and saved in entries from its
  next save: the real server's records change over by themselves. Bans and the carried-accounts list stay whole (one
  short document each). The services did not change: they still call `Save()` after each change.
- *Tried:* `WorldEntriesTests` (5: one entry written alone; restart reads them back in order; a whole document kept once
  and split; keys made safe and never shared; a player and an entry in one change), EditMode 997 of 997, the dependency
  check (0 uphill, 0 two-way). On the test server (built from this branch, test database): the Crews, Contacts, Kiosks
  and Sponsors copied from the test saves (plus sample Crews, a Kiosk and a Sponsor link) were split on the first stop
  with Tolan online, every `-v1` kept, nothing lost (Crews gained only the game's default fields; Tolan's Contact book
  only its last-seen time); on the next round exactly one document was written, Tolan's Contact book (193 bytes).
- *Found:* a game built before the decoupling (19 Oct) cannot play on a server built after it, even with the same build
  number (the assemblies were renamed, so the network message ids changed; a build-8 bot from 19 Oct 01:33 failed its
  messages on a server from today). The next real server rebuild goes with a new release (Publish to Test).

**Milestone 5 (19 Oct 2026): the roster on the server.** Owner's picks: connect first (Title, then character select
shows the chosen server's own list); names stay letters only, 3 to 16 (`CharacterNameRules`), and are unique across
the whole game with capitals ignored; where two characters already shared a name, a GM's account keeps it ("the GM
Tolan, the one we always test with, stays"), then the higher level, then the one played least recently.
- **How a question reaches the server.** The menus ask by opening a short connection of their own (`RosterLink`, from the
  worlds' NetworkManager prefab without the session manager): the request rides in the join message as a sixth part
  (`JoinRules`, packed by `RosterWire`: gzip and base64), the server proves the Unity sign-in token as for a join, answers
  through the roster (`ServerHooks.Roster`, filled by Persistence's `ServerRoster`) and turns the connection away with the
  answer as its reason. No new port, page or service; it works directly (encrypted) and through Relay, and a full server
  still answers. A ban turns the question away as it turns a join away.
- **What the server keeps.** Each `CharacterSave` has its `slot` (one of the select screen's three), `created`, and
  `rename` (another character took its name first). A new character is made by the server: the name and the look are
  checked (`CharacterNameRules`, `CharacterRecipeValidator`), it gets its id, slot and saved look, and the names in use
  (`world/names`, one entry a name, `NameIndex`) take its name in the same all-or-nothing change. Delete removes the
  character with everything it carried, frees its name and writes a `character.deleted` ledger line in the same change.
  Rename is only for a character marked `rename`. An account in the world (here or, with the save service, on another
  server) is never changed from the menus.
- **The names in use are gathered once,** the first time a question comes, from every saved account; the characters that
  lose a shared name are marked and must choose a new one before they play again (the select screen asks, `NEW NAME
  NEEDED`).
- **Joining.** On a dedicated server a character plays only if the server has it, under the name it has, and not waiting
  for a new name (`PlayerSaveAgent.NotOnRoster`); anything else is sent back with the reason. The Editor hosting a test
  and "Play here" keep this PC's own characters as before.
- **The game's side.** `MenuRoster` keeps one copy of each server's roster on this PC (what each character last wore is
  only kept here, for the doll) and switches `CharacterSession`'s store to it; character select asks for the list when it
  opens or when another server is chosen, Delete and the rename box ask the server, and the creator's Create Character
  asks the server to make the character and says why not (a name taken). A look this PC kept of a character made before
  the server kept looks is sent with the list and taken for a character the server has none for, checked as a new one.
- *Tried:* `ServerRosterTests` (8: made, listed with its look and slot; a name once in the game whatever its capitals;
  name rules and three slots; delete frees the name with a ledger line; shared names: the GM's keeps it, then the higher
  level, the rename; the names gathered once and kept one an entry; a kept look taken only where none; an answer of three
  characters fits its message), `JoinRulesTests` (the question in the join message), EditMode 1037 of 1047 (10 skipped).

- *Found while copying the real saves (18 Oct 2026):* the account carried over to Unity sign-in and the old account
  kept as its backup hold the same character id, so with character parts filed by character id alone the two shared
  them; worse, a character id comes from the player's game, so a forged one could have reached another account's
  character. Character parts were moved inside their account (`accounts/<id>/<character>/<part>`) before any server
  used the layout (`SplitSaveTests.TwoAccountsWithTheSameCharacterIdKeepTheirOwnParts`). The real saves were copied
  again: 6 accounts, 110 parts and 6 world documents, every one identical to the files.

## Decisions to make at the start of this phase (as written 11 Oct 2026)

1. **Database.** Recommended: our own **Postgres**, because a trade, a Kiosk sale and a unique name each
   need several records to change together or not at all. Unity Cloud Save has a free tier and suits one
   document per player, but it has no natural home for Crews, Kiosks, unique names or two-player
   transactions.
2. **Who talks to it.** Recommended: a small **save service** (the first slice of the world service of
   Phase 5), written in C# so it can share our pure rules classes (`TradeRules`, `KioskRules`). Map
   servers call it over the private network through a new `ISaveStore` implementation; the database
   password exists only on the service.
3. **Sign-in.** Decided 11 Oct 2026 that the game ships on Steam and from our own website with a
   launcher. So: Steam sign-in on the Steam build, an email account (Unity Player Accounts, or our own
   through Unity Authentication) on the website build, both through Unity Authentication, and a way to
   link the two to one account.

## Work items

1. **Async save path.** `ISaveStore` gains asynchronous calls; `ServerSave` and `PlayerSaveAgent` await
   them. A player whose character is still loading stands in a "loading" state and cannot act.
2. **One record per character**, with the account record holding only what is shared (Deep Vault, the
   day's trade total, the list of characters).
3. **A lock per character.** A map server takes it when it loads a character and renews it with each
   autosave. Nobody else can load or write while it is held. If a server dies, the lock runs out and the
   character can be loaded again from its last save.
4. **Writes that cannot go backwards.** Every record carries a counter; a write is accepted only if the
   counter matches. A server that lost its lock cannot overwrite newer data.
5. **Transactions.** One call that changes several records together: a trade (two characters and the
   sender's daily total), a Kiosk sale (buyer, the Kiosk's takings), a Deep Vault move, a Crew founding
   (the Creds and the Crew). `PlayerTrade.Commit` becomes one such call.
6. **A kept ledger.** Every Creds and item movement between players, and every creation or destruction
   of value (drops, shops, Tempering breaks, GM grants), one line each. `CredsLedger` exists but keeps
   nothing.
7. **Crews, Contacts, Kiosks and Sponsors as records**, changed one entry at a time through the service.
8. **The roster on the server.** Create, list and delete a character are requests to the service, checked
   with the sign-in token. The look is validated there (`CharacterRecipeValidator`, `CharacterNameRules`)
   and stored; `SubmitIdentityRpc` sends only which character. Names are unique across the game. Faction
   is fixed at creation (changing it is the Ronin rule, later).
9. **Map and position** saved with the character.
10. **Migrations.** Each record's version is read on load and stepped up to the current one by a tested
    function. A copy of real saves is run through every migration before it ships.
11. **Backups.** A full copy every night, kept 14 days, stored off the machine. A restore is practised
    once before the phase is called done, and the steps written into the runbook.
12. **Carrying saves across.** A tool that reads a `FileSaveStore` folder and loads it into the database,
    and the reverse, so test characters survive and a copy of live data can be studied offline.
13. **Real accounts.** Steam and email sign-in through Unity Authentication, linking, and the end of
    anonymous accounts for anything but local testing.

## Tunables

Autosave stays 60 s (on `Player.prefab`). New, in a settings asset: the lock's length and renewal, the
number of characters per account (today `CharacterRoster.SlotCount = 3`).

## Done when

- Two map servers on one database: the second cannot load a character the first holds.
- A server is killed in the middle of a trade, a Kiosk sale and a Deep Vault move, many times: no item or
  Cred is duplicated or lost, and the ledger agrees.
- A character created on one machine appears on another after signing in; a taken name is refused.
- A save from before this phase loads through its migration.
- A night's backup is restored onto an empty database and the game runs from it.
- Every EditMode save test passes against both the file store and the service.

## Risks

- This is the phase where a mistake costs players their characters. Nothing here goes to a live server
  without the kill tests and the restore drill.
- A database driver inside a Unity server build is avoided on purpose (decision 2); if the save service
  is dropped, that risk comes back.
