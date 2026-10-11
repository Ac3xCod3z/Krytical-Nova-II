# Krytical Nova II online: the security model

Written 11 Oct 2026. Part of `online-roadmap.md`. **Nothing here is built.** This is the list of what can
go wrong when the game is on the internet, what stops each thing, and which phase builds the stop.

This is engineering guidance, not legal advice. Terms of service, a privacy policy and the owner's
liability need a qualified person before the beta opens to strangers and before any money is taken.

## The one rule

**The server decides; the client asks.** A client sends what the player wants to do ("move there", "cast
this at that", "buy two of those"). The server checks the request against its own copy of the world and
either does it or refuses. A client is never believed about where it is, what it owns, what it hit, how
much something costs, or who it is.

The game already follows this for movement, combat, Flux, loot, trades, shops and saves
(`05_Economy_And_Social.md`, "The server decides"). The gaps are below.

## What is open today

| # | Gap (where) | Why it matters | Safeguard | Phase |
| --- | --- | --- | --- | --- |
| 1 | No connection approval (`NetworkManager.prefab`: `ConnectionApproval: 0`) | Anyone with the address gets a player object | Approve every connect: build version, verified sign-in, one session per account, ban list, room on the map, a time limit to finish | 2 |
| 2 | The account id is made on the client and trusted (`CharacterSession`, `PlayerIdentity.ServerAssign`: "taken on trust until real sign-in exists") | Anyone who learns an id can load that account | The account is the id Unity Authentication signed; the server checks the signature itself | 2 |
| 3 | Traffic is not encrypted (`UseEncryption: 0`) | Traffic can be read and altered on the way | Encrypted transport with a server certificate; Relay traffic is already encrypted | 2 |
| 4 | 100 client-to-server calls in 29 files; only chat has a rate limit (`PlayerChat`, 5 messages in 5 s) | A modified client can flood any call or probe it with bad values | The audit below; one shared rate budget for every call | 2 |
| 5 | The client reports melee hits (`RequestMeleeStrikeDamageServerRpc`); the server checks the swing window, the step and the range | The one place a client claims a result | Keep the checks, tighten the leniency, log refusals; decide whether the server should test hits itself | 2 |
| 6 | The jump request carries the obstacle layers and the model offset from the client (`PlayerJumpMotor.RequestJumpServerRpc`) | A client could plan a jump against rules of its own choosing | The server uses its own values and ignores the client's | 2 |
| 7 | A character's first look, its name and its faction are sent by the client (`PlayerIdentity.SubmitIdentityRpc`). Since 11 Oct 2026 the look is saved on first entry and the saved one wins from then on. The roster (which characters exist) still lives in PlayerPrefs | Check that the name and the faction are held the same way as the look; names are not unique across players | The server holds the roster, look, name and faction from creation; names unique game-wide | 4 |
| 8 | GM is "whoever is the host" (`PlayerChat.IsGameMaster`, `GmConsoleGate.MayUse`, `GmTarget`) | A dedicated server has no host, so there is no GM; and GM power must never come from the client | GM by a list of account ids the server is started with; every GM command logged | 1 (interim), 2 |
| 9 | Saves are files on the host's PC; a quit is saved from a menu (`GameMenuController.Leave`) and `Application.quitting` | A server that is stopped or crashes must not lose progress | Save everyone on a stop signal; autosave stays at 60 s; then the database | 1, 4 |
| 10 | A trade is two separate file writes (`PlayerTrade.Commit`); the economy ledger is not kept | A crash between the two writes can duplicate or destroy items and Creds | One database transaction; a kept ledger of every Creds and item movement | 4 |
| 11 | "Already in the world" is checked inside one process (`ServerSave.TryBind`) | With two map servers, one character could be loaded twice | A lock in the database: one map server holds a character at a time | 4 |
| 12 | Version fields are written but nothing reads them (`AccountSave.CurrentVersion`) | An old save meets new code | Save migrations, tested on a copy of real data | 4 |
| 13 | No network attack protection of any kind | A flood knocks the server offline | A host with UDP DDoS protection included; only the game port open; limits on connects per address | 8 |
| 14 | No secrets exist yet, so none are protected | Keys in a repository or a client are found within hours | Keys only in server settings; never in the repository, never in the client build | 4, 8, 10 |
| 15 | No payment system | Card data is the most dangerous thing a small studio can hold | We never hold it: see "Money" below | 10 |

## Threats and what answers them

| Threat | Answer |
| --- | --- |
| **A modified client** sends requests the real one never would | Every call is owner-only, checked against server state, argument-checked and rate-limited (audit below). Refusals are logged with the account |
| **Speed and teleport hacks** | Already stopped: the server walks the path on its own NavMesh. Phase 3 keeps this true when the client starts moving early |
| **Duplicating items or Creds** | Server-owned packs and wallets (built); trades as one transaction, a ledger, and item moves that name the expected item (Phase 4) |
| **Stolen accounts** | Verified sign-in; we never store a password (Unity Authentication, Steam or Unity Player Accounts hold it) |
| **Bots and farming** | Not fully stoppable. Rate limits, the daily trade cap (built), the ledger and bans make it expensive |
| **Reading memory to see hidden things** | The server only sends what a player may know: interest culling (`SpatialInterestManager`), owner-only data for Creds, storage and lists (built) |
| **Reverse-engineering the client** | The client is built with IL2CPP; server-only rules and tables are left out of it where that is practical; nothing in the client is treated as secret |
| **Flooding the server (DDoS)** | Cannot be solved in game code. The host must absorb it (Phase 8). In code: approval time limits, a cap on connects per address |
| **Breaking into the server machine** | Only the game port faces the internet. The database and the world service listen on a private network. Remote login by key only. The machine is patched |
| **An insider or a stolen GM login** | GM by account list on the server, every command logged, GM commands that create wealth kept out of the live server's list |
| **Losing the data** | Backups every day, kept off the machine, and a restore that has been tried (Phase 4, Phase 8) |
| **Abuse in chat** | The block list (built), rate limit (built), a report command and a mute and ban tool (Phase 9) |

## Money

- **A card number never enters our game and never reaches our servers.** The player pays on a page run by
  the payment company (Steam's own purchase window on the Steam build; Stripe's hosted checkout on the
  website build). We only ever see "this order was paid".
- **The store is opened from the game, and lives in the browser** (owner, 11 Oct 2026). The Store Clerk
  hands the player a link that works once, runs out in minutes, and signs them in to the store only. It
  cannot enter the game and shows no personal details. The store's address is fixed in the build, so a
  link in chat is never the store.
- **Goods are granted by our server**, and only after the payment company's signed message arrives. The
  game client asking for its goods is never enough.
- **A grant happens once.** Every order has an id; a second message for the same order does nothing.
- **Refunds and chargebacks take the goods back** or mark the account, by the same signed-message route.
- **Do not use an asset-store payment kit that puts a card form in the game or a payment key in the
  build.** Opening the payment company's own hosted page is the only shape allowed; it leaves card
  handling and fraud checks with them.
- **It is proved with pretend payments first**: GM commands, then a pretend payment page, then the
  payment company's test mode, and only then one real purchase (`online-phase-10-payments.md`).
- On Steam, purchases made inside the Steam build are expected to go through Steam's own system. Confirm
  against the Steamworks rules when Phase 10 starts.

## Personal data

- Keep as little as possible: an account id, a character name, and for the website build an email address
  held by the sign-in provider rather than by us.
- Chat is not kept beyond what a report needs.
- A player can ask for their data to be removed; the save layout (one account document, characters under
  it) makes that one delete.
- Privacy law depends on where players live and how old they are. That is for the legal review in Phase 9.

## Logging

One server log line, with the account id and the time, for each of: a refused connect and why; a refused
call and why; every GM command; every trade, Kiosk sale, shop sale, Vault move of Creds, Temper, Fusion
and Reforge; every kick and ban. Logs are kept off the game machine (Phase 8).

## The audit: every client-to-server call

This is the working list for Phase 2. It was taken from the code on 11 Oct 2026 by searching for
`[ServerRpc` and `[Rpc(SendTo.Server` under `Assets/_Game`: **100 calls in 29 files.** "Style" is the
attribute in use: *old* is `[ServerRpc]`, *new* is `[Rpc(SendTo.Server, InvokePermission = Owner)]`. The
14 old ones are moved to the new form as they are reviewed. "Look at first" is where to start, worked out
from the arguments alone; it does not say the call is unsafe. Nothing has been reviewed yet.

For each call the review answers five questions, and the Status column is filled in when all five hold:

1. **Owner only.** Can only the player who owns this object send it?
2. **State.** Does the server check that the player may do this now (alive, in reach, not trading, not
   mid-swing, at the right NPC)?
3. **Arguments.** Is every argument checked for range, length and meaning, with nothing of value (a
   price, an amount owned, a result) taken from the client?
4. **Rate.** Is there a budget, so a flood is dropped and logged?
5. **Refusal.** Does a refusal change nothing, tell the player why where that helps, and write a log line?

| # | System | File : line | Call | Style | Look at first | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Social | `PlayerSponsor.cs` : 142 | `RequestRpc(NetworkObjectReference targetReference)` | new | target exists, in reach, allowed | Not reviewed |
| 2 | Social | `PlayerSponsor.cs` : 149 | `RequestByNameRpc(string characterName)` | new | text length and content | Not reviewed |
| 3 | Social | `PlayerSponsor.cs` : 172 | `RespondRpc(bool accept)` | new | rate only | Not reviewed |
| 4 | Social | `PlayerSponsor.cs` : 192 | `EndRpc(string characterId)` | new | text length and content | Not reviewed |
| 5 | Social | `PlayerParty.cs` : 140 | `InviteByNameRpc(string playerName)` | new | text length and content | Not reviewed |
| 6 | Social | `PlayerParty.cs` : 168 | `InviteRpc(NetworkObjectReference targetReference)` | new | target exists, in reach, allowed | Not reviewed |
| 7 | Social | `PlayerParty.cs` : 172 | `RespondRpc(bool accept)` | new | rate only | Not reviewed |
| 8 | Social | `PlayerParty.cs` : 210 | `LeaveRpc()` | new | rate only | Not reviewed |
| 9 | Social | `PlayerParty.cs` : 217 | `KickRpc(string playerName)` | new | text length and content | Not reviewed |
| 10 | Social | `PlayerParty.cs` : 224 | `PromoteRpc(string playerName)` | new | text length and content | Not reviewed |
| 11 | Social | `PlayerFollow.cs` : 100 | `FollowRpc(NetworkObjectReference targetReference)` | new | target exists, in reach, allowed | Not reviewed |
| 12 | Social | `PlayerFollow.cs` : 104 | `FollowByNameRpc(string memberName)` | new | text length and content | Not reviewed |
| 13 | Social | `PlayerEmotes.cs` : 146 | `PlayRpc(FixedString64Bytes key)` | new | rate only | Not reviewed |
| 14 | Social | `PlayerCrew.cs` : 191 | `FoundRpc(string crewName, byte symbol)` | new | text length and content | Not reviewed |
| 15 | Social | `PlayerCrew.cs` : 194 | `InviteRpc(NetworkObjectReference targetReference)` | new | target exists, in reach, allowed | Not reviewed |
| 16 | Social | `PlayerCrew.cs` : 198 | `InviteByNameRpc(string playerName)` | new | text length and content | Not reviewed |
| 17 | Social | `PlayerCrew.cs` : 201 | `RespondRpc(bool accept)` | new | rate only | Not reviewed |
| 18 | Social | `PlayerCrew.cs` : 213 | `LeaveRpc()` | new | rate only | Not reviewed |
| 19 | Social | `PlayerCrew.cs` : 216 | `ExpelRpc(string memberId)` | new | text length and content | Not reviewed |
| 20 | Social | `PlayerCrew.cs` : 219 | `OfficerRpc(string memberId, bool officer)` | new | text length and content | Not reviewed |
| 21 | Social | `PlayerCrew.cs` : 222 | `HandOverRpc(string memberId)` | new | text length and content | Not reviewed |
| 22 | Social | `PlayerCrew.cs` : 225 | `TitleRpc(string memberId, string title)` | new | text length and content | Not reviewed |
| 23 | Social | `PlayerCrew.cs` : 228 | `AnnouncementRpc(string text)` | new | text length and content | Not reviewed |
| 24 | Social | `PlayerCrew.cs` : 231 | `SymbolRpc(byte symbol)` | new | rate only | Not reviewed |
| 25 | Social | `PlayerCrew.cs` : 234 | `DisbandRpc()` | new | rate only | Not reviewed |
| 26 | Social | `PlayerContacts.cs` : 165 | `AddByNameRpc(string characterName)` | new | text length and content | Not reviewed |
| 27 | Social | `PlayerContacts.cs` : 168 | `AddRpc(NetworkObjectReference targetReference)` | new | target exists, in reach, allowed | Not reviewed |
| 28 | Social | `PlayerContacts.cs` : 177 | `RespondRpc(string characterId, bool accept)` | new | text length and content | Not reviewed |
| 29 | Social | `PlayerContacts.cs` : 184 | `CancelRpc(string characterId)` | new | text length and content | Not reviewed |
| 30 | Social | `PlayerContacts.cs` : 187 | `RemoveRpc(string characterId)` | new | text length and content | Not reviewed |
| 31 | Social | `PlayerContacts.cs` : 190 | `RemoveByNameRpc(string characterName)` | new | text length and content | Not reviewed |
| 32 | Social | `PlayerContacts.cs` : 193 | `BlockRpc(string characterId)` | new | text length and content | Not reviewed |
| 33 | Social | `PlayerContacts.cs` : 196 | `BlockByNameRpc(string characterName)` | new | text length and content | Not reviewed |
| 34 | Social | `PlayerContacts.cs` : 199 | `UnblockRpc(string characterId)` | new | text length and content | Not reviewed |
| 35 | Social | `PlayerContacts.cs` : 202 | `UnblockByNameRpc(string characterName)` | new | text length and content | Not reviewed |
| 36 | Social | `PlayerContacts.cs` : 205 | `FavouriteRpc(string characterId, bool favourite)` | new | text length and content | Not reviewed |
| 37 | Social | `PlayerContacts.cs` : 208 | `NoteRpc(string characterId, string note)` | new | text length and content | Not reviewed |
| 38 | Social | `PlayerChat.cs` : 122 | `SendRpc(ChatChannel channel, string text, string whisperTarget)` | new | text length and content | Not reviewed |
| 39 | Restyle | `PlayerWeaves.cs` : 122 | `WeaveRpc(byte slot, ushort lineId)` | new | index in range | Not reviewed |
| 40 | Restyle | `PlayerWeaves.cs` : 125 | `ClearWeaveRpc(byte slot)` | new | index in range | Not reviewed |
| 41 | Restyle | `PlayerWeaves.cs` : 128 | `DyeRpc(byte slot, ushort colourway)` | new | index in range | Not reviewed |
| 42 | Restyle | `PlayerRestyle.cs` : 90 | `RestyleRpc(byte service, CharacterNetState proposed)` | new | the look is sent by the client | Not reviewed |
| 43 | Crafting | `PlayerCrafting.cs` : 90 | `CraftRpc(ushort recipeId, ushort times)` | new | count: zero, huge | Not reviewed |
| 44 | Autopilot | `PlayerAutopilot.cs` : 105 | `SetOnServerRpc(bool value)` | new | rate only | Not reviewed |
| 45 | Autopilot | `PlayerAutopilot.cs` : 118 | `SetPreferencesServerRpc(string json)` | new | size and shape of the JSON | Not reviewed |
| 46 | Combat | `PlayerDuel.cs` : 124 | `RequestRpc(NetworkObjectReference targetReference)` | new | target exists, in reach, allowed | Not reviewed |
| 47 | Combat | `PlayerDuel.cs` : 148 | `RespondRpc(bool accept)` | new | rate only | Not reviewed |
| 48 | Combat | `PlayerDuel.cs` : 175 | `ForfeitRpc()` | new | rate only | Not reviewed |
| 49 | Combat | `PlayerSkillQueue.cs` : 151 | `QueueServerRpc(string skillId, NetworkObjectReference targetReference)` | old | text length and content; target exists, in reach, allowed | Not reviewed |
| 50 | Combat | `PlayerSkillQueue.cs` : 160 | `ClearServerRpc()` | old | rate only | Not reviewed |
| 51 | Combat | `PlayerMeleeCombat.cs` : 631 | `RequestMeleeStrikeDamageServerRpc(ulong targetNetworkObjectId, int comboStep)` | old | the client says it hit: swing window, range, once per target | Not reviewed |
| 52 | Items | `PlayerLoot.cs` : 92 | `PickupRpc(NetworkObjectReference dropReference)` | new | target exists, in reach, allowed | Not reviewed |
| 53 | Items | `PlayerLoot.cs` : 100 | `DropRpc(ItemLocation at)` | new | index in range | Not reviewed |
| 54 | Economy | `PlayerKiosk.cs` : 157 | `OpenRpc(string shopName, KioskOffer[] offers, byte tier)` | new | text length and content; array length | Not reviewed |
| 55 | Economy | `PlayerKiosk.cs` : 160 | `CloseRpc()` | new | rate only | Not reviewed |
| 56 | Economy | `PlayerKiosk.cs` : 163 | `CollectRpc()` | new | rate only | Not reviewed |
| 57 | Economy | `PlayerKiosk.cs` : 166 | `BuyRpc(NetworkObjectReference standReference, byte index, KioskListing expected, ushort count)` | new | count: zero, huge; target exists, in reach, allowed; index in range; the expected item must match the server's | Not reviewed |
| 58 | Progression | `PlayerNova.cs` : 214 | `AscendRpc()` | new | rate only | Not reviewed |
| 59 | Progression | `PlayerNova.cs` : 217 | `BuyStandingRpc(StandingTrack track)` | new | rate only | Not reviewed |
| 60 | Progression | `PlayerNova.cs` : 220 | `WearStandingRpc(StandingTrack track, bool wear)` | new | rate only | Not reviewed |
| 61 | Items | `PlayerInventory.cs` : 245 | `MoveRpc(ItemLocation from, ItemLocation to)` | new | index in range | Not reviewed |
| 62 | Items | `PlayerInventory.cs` : 248 | `QuickEquipRpc(byte bagIndex)` | new | index in range | Not reviewed |
| 63 | Items | `PlayerInventory.cs` : 251 | `QuickUnequipRpc(EquipSlot slot)` | new | index in range | Not reviewed |
| 64 | Items | `PlayerInventory.cs` : 254 | `DestroyRpc(ItemLocation at)` | new | index in range | Not reviewed |
| 65 | Items | `PlayerInventory.cs` : 257 | `SortRpc()` | new | rate only | Not reviewed |
| 66 | Items | `PlayerInventory.cs` : 260 | `UseRpc(byte bagIndex)` | new | index in range | Not reviewed |
| 67 | Progression | `PlayerMovementSkills.cs` : 231 | `UpgradeServerRpc(MovementSkillId skill, int count)` | old | count: zero, huge | Not reviewed |
| 68 | Progression | `PlayerMovementSkills.cs` : 273 | `SelectServerRpc(MovementSkillId skill)` | old | rate only | Not reviewed |
| 69 | Economy | `PlayerShopping.cs` : 120 | `BuyRpc(FixedString64Bytes vendor, ushort itemId, ushort count)` | new | count: zero, huge | Not reviewed |
| 70 | Economy | `PlayerShopping.cs` : 123 | `SellRpc(ushort cell, ushort count, ItemStack expected, bool confirmed)` | new | count: zero, huge; index in range; the expected item must match the server's | Not reviewed |
| 71 | Economy | `PlayerStorage.cs` : 162 | `DepositRpc(short packCell, short toCell, ushort count)` | new | count: zero, huge; index in range | Not reviewed |
| 72 | Economy | `PlayerStorage.cs` : 165 | `WithdrawRpc(short cell, short packCell, ushort count)` | new | count: zero, huge; index in range | Not reviewed |
| 73 | Economy | `PlayerStorage.cs` : 168 | `MoveRpc(short from, short to)` | new | index in range | Not reviewed |
| 74 | Economy | `PlayerStorage.cs` : 171 | `SortRpc()` | new | rate only | Not reviewed |
| 75 | Economy | `PlayerStorage.cs` : 174 | `DepositCredsRpc(long amount)` | new | amount: negative, zero, overflow | Not reviewed |
| 76 | Economy | `PlayerStorage.cs` : 177 | `WithdrawCredsRpc(long amount)` | new | amount: negative, zero, overflow | Not reviewed |
| 77 | Economy | `PlayerTrade.cs` : 199 | `RequestRpc(NetworkObjectReference targetReference)` | new | target exists, in reach, allowed | Not reviewed |
| 78 | Economy | `PlayerTrade.cs` : 203 | `RequestByNameRpc(string playerName)` | new | text length and content | Not reviewed |
| 79 | Economy | `PlayerTrade.cs` : 231 | `RespondRpc(bool accept)` | new | rate only | Not reviewed |
| 80 | Economy | `PlayerTrade.cs` : 234 | `OfferRpc(short packCell, ushort count)` | new | count: zero, huge; index in range | Not reviewed |
| 81 | Economy | `PlayerTrade.cs` : 237 | `WithdrawRpc(byte lineIndex)` | new | index in range | Not reviewed |
| 82 | Economy | `PlayerTrade.cs` : 240 | `SetCredsRpc(long amount)` | new | amount: negative, zero, overflow | Not reviewed |
| 83 | Economy | `PlayerTrade.cs` : 243 | `LockRpc(bool on)` | new | rate only | Not reviewed |
| 84 | Economy | `PlayerTrade.cs` : 246 | `ConfirmRpc()` | new | rate only | Not reviewed |
| 85 | Economy | `PlayerTrade.cs` : 249 | `CancelRpc()` | new | rate only | Not reviewed |
| 86 | Progression | `PlayerCombatSkills.cs` : 490 | `UpgradeServerRpc(string skillId, int count)` | old | text length and content; count: zero, huge | Not reviewed |
| 87 | Progression | `PlayerCombatSkills.cs` : 630 | `RequestCastSkillServerRpc(string skillId, NetworkObjectReference targetReference)` | old | text length and content; target exists, in reach, allowed | Not reviewed |
| 88 | Forge | `PlayerGearWork.cs` : 126 | `TemperRpc(short cell, ushort alloyItemId, ushort protectionItemId)` | new | index in range | Not reviewed |
| 89 | Forge | `PlayerGearWork.cs` : 129 | `FuseRpc(short targetCell, short materialCell)` | new | index in range | Not reviewed |
| 90 | Forge | `PlayerGearWork.cs` : 132 | `TuneRpc(short cell)` | new | index in range | Not reviewed |
| 91 | Forge | `PlayerGearWork.cs` : 135 | `ResetTuningRpc(short cell)` | new | index in range | Not reviewed |
| 92 | Forge | `PlayerGearWork.cs` : 138 | `ReforgeRpc(short cell)` | new | index in range | Not reviewed |
| 93 | Progression | `PlayerCharacterProgression.cs` : 204 | `AllocateServerRpc(CharacterAttribute attribute, int count)` | old | count: zero, huge | Not reviewed |
| 94 | Player | `PlayerAnimancerMotor.cs` : 580 | `RequestToggleWeaponDrawServerRpc(bool draw)` | old | rate only | Not reviewed |
| 95 | Player | `PlayerIdentity.cs` : 139 | `SubmitIdentityRpc(CharacterNetState state, FixedString64Bytes account, FixedString64Bytes character)` | new | account and character ids are taken on trust; look and faction come from the client | Not reviewed |
| 96 | Player | `PointAndClickPlayer.cs` : 763 | `RequestStopMoveServerRpc()` | old | rate only | Not reviewed |
| 97 | Player | `PointAndClickPlayer.cs` : 990 | `RequestMoveDestinationServerRpc(Vector3 destinationPoint)` | old | point on the map, distance | Not reviewed |
| 98 | Player | `PointAndClickPlayer.cs` : 1291 | `RequestCancelAttackServerRpc()` | old | rate only | Not reviewed |
| 99 | Player | `PointAndClickPlayer.cs` : 1299 | `RequestAttackEntityServerRpc(ulong targetNetworkObjectId)` | old | target exists, in reach, allowed | Not reviewed |
| 100 | Player | `PlayerJumpMotor.cs` : 123 | `RequestJumpServerRpc(Vector3 clickedPoint, int obstacleLayersValue, float modelForwardOffset)` | old | the obstacle layers and the model offset are sent by the client: use the server's own | Not reviewed |

Two more calls start on a client and reach other clients through the server. They carry no game result
but belong on the list: `PlayerSkillAttacks.ChargeStartedRpc(string skillId)` (line 373) and
`ChargeEndedRpc(string skillId, bool completed)` (line 380), both
`[Rpc(SendTo.NotOwner, InvokePermission = Owner)]`. Check the skill id and the rate.

Calls the plan will add, each to be put on this list before it is written: the direction a client sends
in Keys movement (`Design/15_movement_wasd_controller_navworld.md`); switching Line and "go to their
Line"; joining and leaving a war queue; the answer to a Merc offer (`online-phase-06-lines.md`). A Merc's
side for an event is set by the server alone: a client can only say yes or no to an offer the server made.

The GM commands (275 command entries in 18 files under `Tools/Debug`) run only where the server is, by
the rule in `GmTarget`. They are not client calls today. If a GM is ever to type commands from a client,
each one becomes a new client-to-server call and goes on this list first.
