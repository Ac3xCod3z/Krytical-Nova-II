# Online phase 4: accounts and shared save

Part of `online-roadmap.md`. Written 11 Oct 2026. **Not built.** Needs Phase 2.

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

## Decisions to make at the start of this phase

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
