# Online phase 2: lock the door

Part of `online-roadmap.md`; the rules are in `online-security-model.md`. Written 11 Oct 2026.
**Milestones 1 to 4 built 18 Oct 2026** (see "As built" below); milestone 5 (attack tests, client hardening) is not built. Order (owner, 18 Oct 2026): milestone 1,
then **Phase 3** (client-side prediction), then milestones 2 to 5 of this phase.

## Goal

Nobody gets a player object without proving who they are and that they run the right build, and nothing a
modified client sends can do more than the real client could.

## What exists today

- No connection approval (`Networking/Prefabs/NetworkManager.prefab`: `ConnectionApproval: 0`).
- The client signs in to Unity Authentication (`NetworkConnectionManager.EnsureServicesInitializedAsync`)
  but the save is keyed by an id the client generates (`CharacterSession`, key `kn.account.v1`) and sends
  in `PlayerIdentity.SubmitIdentityRpc`. The code says so: "taken on trust until real sign-in exists".
- Transport encryption is off (`UseEncryption: 0`).
- 100 client-to-server calls in 29 files; 14 use the old `[ServerRpc]` attribute. Only chat has a rate
  limit (`PlayerChat`, 5 in 5 s).
- The only kick is for a second character of one account (`PlayerSaveAgent.cs` : 160).

## As built

**Milestone 1, who you are (18 Oct 2026).** Owner's picks: the game signs in to Unity by itself (no login screen) and
the server checks the proof; the characters played before are carried over once; GMs are bound to accounts from the
control page, with no password in the game; the GM console (F2) opens for GMs only; hits stay client-reported with
tighter checks (that part is milestone 3).
- *Join request* (`JoinRules`, tag `KN2`): build, Editor flag, the Unity sign-in token, and the account id the game made
  up before sign-in. A game from before this (`KN1`) is told to update.
- *The check* (`AccountProof.cs`: `UnityToken.Verify`, `UnityKeys`, `AccountIds`, `ServerAccounts`): the RS256 signature
  against Unity's published keys (fetched at server start, kept, fetched again for an unknown key at most once a
  minute), expiry with a minute's slack, issuer `player-auth.services.api.unity.com`, this project (`project_id` or
  `upid:` in the audience). The account id is 32 hex characters of SHA-256 of the Unity player id. While the keys are
  being fetched the approval waits (`response.Pending`). A dedicated server requires the proof; the Editor hosting a test
  still takes a game that cannot give it (`NetworkConnectionManager.RequireProof`).
- *Saves under the proven account*: `PlayerIdentity` takes the account from `ServerAccounts`, never from the game;
  the game's own copy follows it (`CharacterSession.VerifiedAccountId`; `MadeUpAccountId` stays for playing alone).
- *Carry-over* (`ServerSave.CarryOverOnce`, setting `carryOverOldSaves`): only onto a proven account with no saves of
  its own, only once per old account (`world/carried-accounts`), the old document kept as a backup; Kiosks follow the
  account (`ServerSave.AccountCarried`).
- *GMs* (`GmRoster`, `PlayerGm`, `GmConsoleGate`, control page): a GM entry is a name and an account; **Bind** beside it
  picks an online player; a bound account is GM on every join; Unbind or Remove ends it at once. The in-game password
  login is gone.
- *Tried:* a test server (Measure server, port 7790) seeded with a copy of the Editor's old account: the Editor joined
  through its own Unity sign-in, the server carried the account (1 character) over to the proven one, Tolan stood in
  the world; Bind on the page made him GM at once with the tag. EditMode tests 911 / 911 (`AccountProofTests`: forged,
  changed, expired, foreign-project tokens refused; the keys read; the carry-over once only; `GmRosterTests`).

**Milestone 2, encrypted direct connections (18 Oct 2026).** Owner's pick: the master key lives in Documents with a
backup, never in the project.
- *Certificates* (`Networking/Tools~/certificates.sh`, openssl in Git Bash): a certificate authority made once
  (`ca.key` secret, `ca.crt` public) in `Documents/KryticalNova-Secrets`; a server key and certificate for the name
  `krytical-nova-server`, signed by it and copied into a `certificates` folder beside the server's folder (a rebuild
  never touches it; the settings may name other files: `certificateFile`, `privateKeyFile`).
- *Server* (`ServerEncryption`, `NetworkConnectionManager.StartDedicatedServer`): in Direct mode it encrypts with its
  certificate and key (`UnityTransport.SetServerSecrets`); without them it says so and games can join only through
  Relay. Relay keeps its own encryption.
- *Game*: every join of a dedicated server (the server list, the F1 panel, the Editor's join to a test server) encrypts
  and accepts only a server our authority signed (`SetClientSecrets`, the authority's public certificate in
  `ServerEncryption.AuthorityCertificate`); an Editor hosting its own test session stays unencrypted. Games from before
  this cannot join a server built after it.
- *Tried:* the measure server (port 7790) logged "Direct connections are encrypted"; the Editor joined it directly with
  encryption on and stood in town (109 fps, 37 ms). EditMode tests 911 / 911.

**Milestone 3, speed limits, the audit and tighter hit checks (18 Oct 2026).** Owner's picks: tighter checks with
refusals logged; the flags live on the control page too, with who and where; lag tracked apart from cheating.
- *Speed limits* (`RpcBudget`, `CallLimits` in `server-settings.json`, added to an older file by itself): every one of
  the 111 client-to-server calls starts with `RpcBudget.Allow` (a burst and a refill per second, by kind: move, combat,
  items, money, social, chat, cosmetic, account); over it the call is ignored and flagged; 50 ignored in 10 s disconnects
  the game ("Too many requests"). Texts are capped (`TextFits`, 500 characters; settings text 4096), positions must be
  real places (`PointFits`).
- *Flags* (`SecurityFlags`): character, account, connection, map, position, the call, what it asked, why it was refused
  and what the server did; repeats count up on one line; a server log line, `flags.log` beside the saves, and the
  control page's **Flags** section, live (`/api/flags`).
- *The audit* (`online-security-model.md`): the 16 old `[ServerRpc]` calls moved to owner-only `[Rpc]`; the jump and the
  climb no longer take the obstacle layers and the model's turn from the game; melee hit reports flag a step outside
  1-3, a friend and a hit beyond 2.7 m; a Kiosk with more stacks than it holds is flagged before it is read; the table
  is taken again from the code (111 rows) with what each call checks.
- *Lag* (`NetworkHealth`, owner): late hits and hits on a target already gone are never flagged; they are counted per
  player (the control page's **Ping · late** column, last 5 minutes) and per minute in the performance log
  (`late_hits`, `gone_targets`, `worst_ping_ms`).
- *Also (owner):* the control page and the launcher in the name logo's colours (its cyan and red; no yellow or pink),
  the logo in place of the written name, and the launcher's top bar dark in its own navy.
- *Tried:* on the measure server, 20 emotes at once: 8 through, the rest flagged as one line counted x10 with the
  character, account, map and position, in `flags.log` too; 70 more: "Flooding the server", the game disconnected and
  told why. EditMode tests 911 / 911.

**Milestone 4, bans and the server's records (18 Oct 2026).** Work items 7 and 8.
- *Bans* (`ServerBans`, the `world/bans` save document): on the proven account, for `30m`, `12h`, `3d`, `2w` (a number
  alone is days) or for good; by connection or character name when the player is on, by character name (every saved
  account is searched, `ServerSave.AccountsWithCharacter`) or account id when not. A player who is on is sent away; a
  banned account asking to join is turned away by the approval (`NetworkConnectionManager.Banned`, also on an Editor host
  test without sign-in, by the game's own id) and reads the reason and the time left. GMs and the host cannot be banned.
  `gm.ban`, `gm.unban`, `gm.bans`; on the control page **Ban** beside a player (reason and length asked), the **Bans**
  section with **Unban** and a form for someone not on; Ban Player, Unban Player and List Bans in the GM Tools window.
- *The records* (`ServerRecords`, item 8): a server log line and a line in a file beside the saves, each with the time,
  character, whole account id and connection. `gm.log`: every GM command, from a joined GM (with their GM name), the
  server window, the control page's command box (with its address), the host's F2 and the GM Tools window, every
  control page button press, every kick, ban and unban (`GmCommandRelay.RunningFor` says who a command ran for).
  `economy.log`: trades item by item with the Creds, shop purchases and sales, Kiosk sales and tills collected, Vault
  Creds in and out, Temper, Fusion, Tuning and Reforge with the piece before and after and the cost. `access.log`: joins,
  leaves and every game turned away with the reason. Only where the game is decided, while a session runs.
- *Also:* a game build (not a server) failed to compile since milestone 2 (`ServerEncryption.TryLoadServerSecrets` named
  the server's settings, which exist only in a server build or the Editor); fixed, and the bot client built again.
- *Tried:* on the measure server, from its control page: a GM's account refused, an unknown name and a wrong length
  refused, two absent accounts banned (12 hours, for good), listed in `gm.bans` and the page's status, unbanned. A bot
  in the game banned with **Ban** (3 days) was sent away and read "This account is banned from this server for 3 days
  more (until 21 Oct 2026, 00:54 server time): Testing bans"; started again it was turned away with the same words, and
  `access.log` said why; `gm.unban Botkaka` and it joined again. `gm.log` has one line per command or button with its
  answer and the account. EditMode tests 941 / 941 (`ServerBansTests`).

## Work items

1. **Connection approval.** Turn it on and add a `ConnectionGate` (in `Networking/Runtime`). The client
   sends, in the approval data: the build version, a protocol number we raise when the two sides stop
   being compatible, and its Unity Authentication token. The gate refuses, with a reason the client can
   show, when: the version or protocol is wrong; the token is missing, expired or not signed by Unity; the
   account is banned; the account is already in the world; the map is full; approval took too long.
2. **Verified accounts.** The server checks the token's signature against Unity's published keys (fetched
   and cached; they rotate) and its expiry and project. The account id becomes a 32-character id derived
   from the provider and the verified player id, so every existing save key rule (`SaveKeys.IsId`) holds.
   `SubmitIdentityRpc` stops carrying an account id.
   **Prove first:** verifying the token inside a Unity server build (one forum report says the token and
   the published keys used different signature types). If it cannot be done locally, the server asks
   Unity's player API to confirm the token instead.
3. **Carry the test saves across.** Once, after signing in, a client may present its old `kn.account.v1`
   id and the server moves that account document to the verified id if nobody has claimed it. This exists
   for the owner's and his brothers' test characters and is switched off before any beta.
4. **Encryption.** Relay connections are already encrypted. For direct connections, the server is given a
   certificate and key at launch (`UnityTransport.SetServerSecrets`) and the client checks the server's
   name against our own certificate authority (`SetClientSecrets`). The key is a secret (environment
   setting).
5. **A rate budget for every call.** A small `RpcBudget` helper in Core: each client has an allowance per
   kind of call; a call over the allowance is dropped and logged; a client that keeps flooding is
   disconnected. The numbers live in a new `NetworkSafetySettings` asset, not in code. Chat's own limit
   moves onto it.
6. **The audit.** Work through the 100-row table in `online-security-model.md`, in this order: Economy
   (25 calls), Items, Forge, Progression, Combat and Player, Social, the rest. Each call is checked
   against the five questions, fixed, moved to `[Rpc(SendTo.Server, InvokePermission = Owner)]` if it is
   one of the 14 old ones, and its row marked. Three are known before starting:
   - `PlayerJumpMotor.RequestJumpServerRpc` takes the obstacle layers and the model offset from the
     client. The server uses its own.
   - `PlayerMeleeCombat.RequestMeleeStrikeDamageServerRpc` is the client reporting a hit. Keep the swing
     window, step and range checks; review `hitLatencyGrace`, `serverMaxValidationRange` and the skills'
     `impactRangeLeniency = 1.5`; log refusals. Whether the server should test hits itself is an owner
     decision (it costs animation-accurate hit shapes on the server).
   - `PlayerAutopilot.SetPreferencesServerRpc(string json)` and `PlayerKiosk.OpenRpc(..., KioskOffer[]
     offers, ...)` take free-size data: cap the size before reading it.
7. **GM by account.** The `-gm` list from Phase 1 now names verified accounts. A `bans` world document,
   with `gm.kick`, `gm.ban` and `gm.unban`. Every GM command writes a log line.
8. **The log lines** listed under "Logging" in the security model.
9. **Harden the client build.** IL2CPP for the client. Use Multiplayer Roles to leave server-only data
   (drop tables, Hostile brains, the GM tools) out of the client build where it is practical.
10. **Attack tests.** A test assembly that connects a client and sends what a modified one would: no
    token, a stale build, another player's object, negative amounts, out-of-range cells, a thousand calls
    a second. Each must be refused, change nothing and leave a log line. These stay in the test suite.

## Tunables

`NetworkSafetySettings`: allowance per call kind, the flood threshold that disconnects, the approval time
limit, the leniency values for melee and skill reach.

## Done when

- A client with no token, a wrong build or a banned account is refused with a readable reason.
- The old client-made account id is no longer accepted anywhere.
- Traffic between a client and the server is encrypted on both the Relay and the direct path.
- All 100 rows of the audit are marked reviewed; no `[ServerRpc]` attribute is left.
- Every attack test passes.
- Two real clients still play normally (second client, not only the owner).

## Risks

- Anonymous sign-in belongs to one machine and profile: reinstalling loses the account. Acceptable for
  testers; fixed by real accounts in Phase 4.
- Rate limits set too tight punish fast legitimate play (Autopilot, quick sorting). Set from measurements
  of real play, with a margin.
