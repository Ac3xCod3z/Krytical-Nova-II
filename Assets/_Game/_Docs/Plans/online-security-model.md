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

This is the working list for Phase 2. **Taken again from the code on 18 Oct 2026 (milestone 3): 111 calls**, every one
now `[Rpc(SendTo.Server, InvokePermission = Owner)]` (the 16 old `[ServerRpc]` ones moved). Every call starts with
`RpcBudget.Allow` (its speed limit by kind; `callLimits` in `server-settings.json`); every text it takes is checked for
length (`RpcBudget.TextFits`), every position for being a real place (`RpcBudget.PointFits`); what a real game never
sends is flagged (`SecurityFlags`: the control page's Flags section and `flags.log`). The Notes column says where a call
was looked at more closely. The state and argument checks deeper inside each system (amounts owned, reach, the right NPC)
were already the server's in every system checked; the attack tests of milestone 5 try each call with what a modified
game would send.

For each call the review answers five questions, and the Status column is filled in when all five hold:

1. **Owner only.** Can only the player who owns this object send it?
2. **State.** Does the server check that the player may do this now (alive, in reach, not trading, not
   mid-swing, at the right NPC)?
3. **Arguments.** Is every argument checked for range, length and meaning, with nothing of value (a
   price, an amount owned, a result) taken from the client?
4. **Rate.** Is there a budget, so a flood is dropped and logged?
5. **Refusal.** Does a refusal change nothing, tell the player why where that helps, and write a log line?

| # | System | File : line | Call | Checks in place (18 Oct 2026) | Notes |
| --- | --- | --- | --- | --- | --- |
| 1 | Economy | `PlayerKiosk.cs` : 159 | `OpenRpc(string shopName, KioskOffer[] offers, byte tier)` | owner only, speed limit (Money), text length, impossible claims flagged | Listings capped at the Kiosk's slots before reading; cells, counts and prices checked by KioskService. |
| 2 | Economy | `PlayerKiosk.cs` : 173 | `CloseRpc()` | owner only, speed limit (Money) |  |
| 3 | Economy | `PlayerKiosk.cs` : 180 | `CollectRpc()` | owner only, speed limit (Money) |  |
| 4 | Economy | `PlayerKiosk.cs` : 187 | `BuyRpc(NetworkObjectReference standReference, byte index, KioskListing expected, ushort count)` | owner only, speed limit (Money) |  |
| 5 | Economy | `PlayerShopping.cs` : 122 | `BuyRpc(FixedString64Bytes vendor, ushort itemId, ushort count)` | owner only, speed limit (Money) |  |
| 6 | Economy | `PlayerShopping.cs` : 129 | `SellRpc(ushort cell, ushort count, ItemStack expected, bool confirmed)` | owner only, speed limit (Money) |  |
| 7 | Economy | `PlayerStorage.cs` : 164 | `DepositRpc(short packCell, short toCell, ushort count)` | owner only, speed limit (Items) |  |
| 8 | Economy | `PlayerStorage.cs` : 171 | `WithdrawRpc(short cell, short packCell, ushort count)` | owner only, speed limit (Items) |  |
| 9 | Economy | `PlayerStorage.cs` : 178 | `MoveRpc(short from, short to)` | owner only, speed limit (Items) |  |
| 10 | Economy | `PlayerStorage.cs` : 185 | `SortRpc()` | owner only, speed limit (Items) |  |
| 11 | Economy | `PlayerStorage.cs` : 192 | `DepositCredsRpc(long amount)` | owner only, speed limit (Money) | Amount > 0, carried, fits the Vault cap. |
| 12 | Economy | `PlayerStorage.cs` : 199 | `WithdrawCredsRpc(long amount)` | owner only, speed limit (Money) | Amount > 0, stored, fits the wallet. |
| 13 | Economy | `PlayerTrade.cs` : 201 | `RequestRpc(NetworkObjectReference targetReference)` | owner only, speed limit (Money), text length |  |
| 14 | Economy | `PlayerTrade.cs` : 208 | `RequestByNameRpc(string playerName)` | owner only, speed limit (Money), text length |  |
| 15 | Economy | `PlayerTrade.cs` : 238 | `RespondRpc(bool accept)` | owner only, speed limit (Money) |  |
| 16 | Economy | `PlayerTrade.cs` : 245 | `OfferRpc(short packCell, ushort count)` | owner only, speed limit (Money) |  |
| 17 | Economy | `PlayerTrade.cs` : 252 | `WithdrawRpc(byte lineIndex)` | owner only, speed limit (Money) |  |
| 18 | Economy | `PlayerTrade.cs` : 259 | `SetCredsRpc(long amount)` | owner only, speed limit (Money) | Clamped to >= 0; per-trade and per-day caps. |
| 19 | Economy | `PlayerTrade.cs` : 266 | `LockRpc(bool on)` | owner only, speed limit (Money) |  |
| 20 | Economy | `PlayerTrade.cs` : 273 | `ConfirmRpc()` | owner only, speed limit (Money) |  |
| 21 | Economy | `PlayerTrade.cs` : 280 | `CancelRpc()` | owner only, speed limit (Money) |  |
| 22 | Items | `PlayerFlask.cs` : 91 | `RefillRpc()` | owner only, speed limit (Items) |  |
| 23 | Items | `PlayerInventory.cs` : 247 | `MoveRpc(ItemLocation from, ItemLocation to)` | owner only, speed limit (Items) |  |
| 24 | Items | `PlayerInventory.cs` : 254 | `QuickEquipRpc(byte bagIndex)` | owner only, speed limit (Items) |  |
| 25 | Items | `PlayerInventory.cs` : 261 | `QuickUnequipRpc(EquipSlot slot)` | owner only, speed limit (Items) |  |
| 26 | Items | `PlayerInventory.cs` : 268 | `DestroyRpc(ItemLocation at)` | owner only, speed limit (Items) |  |
| 27 | Items | `PlayerInventory.cs` : 275 | `SortRpc()` | owner only, speed limit (Items) |  |
| 28 | Items | `PlayerInventory.cs` : 282 | `UseRpc(byte bagIndex)` | owner only, speed limit (Items) |  |
| 29 | Items | `PlayerLoot.cs` : 94 | `PickupRpc(NetworkObjectReference dropReference)` | owner only, speed limit (Items) |  |
| 30 | Items | `PlayerLoot.cs` : 103 | `DropRpc(ItemLocation at)` | owner only, speed limit (Items) |  |
| 31 | Forge | `PlayerGearWork.cs` : 128 | `TemperRpc(short cell, ushort alloyItemId, ushort protectionItemId)` | owner only, speed limit (Money) |  |
| 32 | Forge | `PlayerGearWork.cs` : 135 | `FuseRpc(short targetCell, short materialCell)` | owner only, speed limit (Money) |  |
| 33 | Forge | `PlayerGearWork.cs` : 142 | `TuneRpc(short cell)` | owner only, speed limit (Money) |  |
| 34 | Forge | `PlayerGearWork.cs` : 149 | `ResetTuningRpc(short cell)` | owner only, speed limit (Money) |  |
| 35 | Forge | `PlayerGearWork.cs` : 156 | `ReforgeRpc(short cell)` | owner only, speed limit (Money) |  |
| 36 | Progression | `PlayerCharacterProgression.cs` : 207 | `AllocateServerRpc(CharacterAttribute attribute, int count)` | owner only, speed limit (Money) |  |
| 37 | Progression | `PlayerCombatSkills.cs` : 530 | `UpgradeServerRpc(string skillId, int count)` | owner only, speed limit (Money), text length |  |
| 38 | Progression | `PlayerCombatSkills.cs` : 674 | `RequestCastSkillServerRpc(string skillId, NetworkObjectReference targetReference)` | owner only, speed limit (Combat), text length |  |
| 39 | Progression | `PlayerMovementSkills.cs` : 241 | `UpgradeServerRpc(MovementSkillId skill, int count)` | owner only, speed limit (Money) |  |
| 40 | Progression | `PlayerMovementSkills.cs` : 284 | `SelectServerRpc(MovementSkillId skill)` | owner only, speed limit (Move) |  |
| 41 | Progression | `PlayerNova.cs` : 233 | `AscendRpc()` | owner only, speed limit (Money) |  |
| 42 | Progression | `PlayerNova.cs` : 240 | `BuyStandingRpc(StandingTrack track)` | owner only, speed limit (Money) |  |
| 43 | Progression | `PlayerNova.cs` : 247 | `WearStandingRpc(StandingTrack track, bool wear)` | owner only, speed limit (Money) |  |
| 44 | Combat | `PlayerCadence.cs` : 186 | `PressRpc(string skillId, double pressedAt)` | owner only, speed limit (Combat), text length |  |
| 45 | Combat | `PlayerDuel.cs` : 126 | `RequestRpc(NetworkObjectReference targetReference)` | owner only, speed limit (Social) |  |
| 46 | Combat | `PlayerDuel.cs` : 151 | `RespondRpc(bool accept)` | owner only, speed limit (Social) |  |
| 47 | Combat | `PlayerDuel.cs` : 179 | `ForfeitRpc()` | owner only, speed limit (Social) |  |
| 48 | Combat | `PlayerMeleeCombat.cs` : 657 | `RequestMeleeStrikeDamageServerRpc(ulong targetNetworkObjectId, int comboStep)` | owner only, speed limit (Combat), impossible claims flagged | Step 1-3, hostile, within 2.7 m, open swing, once per target per swing; late hits counted as lag. |
| 49 | Combat | `PlayerSkillQueue.cs` : 153 | `QueueServerRpc(string skillId, NetworkObjectReference targetReference)` | owner only, speed limit (Combat), text length |  |
| 50 | Combat | `PlayerSkillQueue.cs` : 164 | `ClearServerRpc()` | owner only, speed limit (Combat) |  |
| 51 | Player | `PlayerAnimancerMotor.cs` : 587 | `RequestToggleWeaponDrawServerRpc(bool draw)` | owner only, speed limit (Cosmetic) |  |
| 52 | Player | `PlayerIdentity.cs` : 156 | `SubmitIdentityRpc(CharacterNetState state, FixedString64Bytes account, FixedString64Bytes character)` | owner only, speed limit (Account) |  |
| 53 | Player | `PlayerJumpMotor.cs` : 142 | `RequestJumpServerRpc(Vector3 clickedPoint)` | owner only, speed limit (Move), real position | Layers and model turn now the server's own (no longer taken from the game). |
| 54 | Player | `PlayerWallRunMotor.cs` : 242 | `RequestClimbServerRpc(Vector3 point, Vector3 normal)` | owner only, speed limit (Move), real position | Layers and model turn now the server's own (no longer taken from the game). |
| 55 | Player | `PlayerWallRunMotor.cs` : 362 | `RequestDropServerRpc(Vector3 point)` | owner only, speed limit (Move), real position |  |
| 56 | Player | `PointAndClickPlayer.cs` : 828 | `RequestStopMoveServerRpc()` | owner only, speed limit (Move) |  |
| 57 | Player | `PointAndClickPlayer.cs` : 1060 | `RequestMoveDestinationServerRpc(Vector3 destinationPoint)` | owner only, speed limit (Move), real position |  |
| 58 | Player | `PointAndClickPlayer.cs` : 1502 | `RequestCancelAttackServerRpc()` | owner only, speed limit (Combat) |  |
| 59 | Player | `PointAndClickPlayer.cs` : 1511 | `RequestAttackEntityServerRpc(ulong targetNetworkObjectId)` | owner only, speed limit (Combat) |  |
| 60 | Social | `PlayerChat.cs` : 126 | `SendRpc(ChatChannel channel, string text, string whisperTarget)` | owner only, speed limit (Chat), text length |  |
| 61 | Social | `PlayerContacts.cs` : 167 | `AddByNameRpc(string characterName)` | owner only, speed limit (Social), text length |  |
| 62 | Social | `PlayerContacts.cs` : 175 | `AddRpc(NetworkObjectReference targetReference)` | owner only, speed limit (Social) |  |
| 63 | Social | `PlayerContacts.cs` : 185 | `RespondRpc(string characterId, bool accept)` | owner only, speed limit (Social), text length |  |
| 64 | Social | `PlayerContacts.cs` : 194 | `CancelRpc(string characterId)` | owner only, speed limit (Social), text length |  |
| 65 | Social | `PlayerContacts.cs` : 202 | `RemoveRpc(string characterId)` | owner only, speed limit (Social), text length |  |
| 66 | Social | `PlayerContacts.cs` : 210 | `RemoveByNameRpc(string characterName)` | owner only, speed limit (Social), text length |  |
| 67 | Social | `PlayerContacts.cs` : 218 | `BlockRpc(string characterId)` | owner only, speed limit (Social), text length |  |
| 68 | Social | `PlayerContacts.cs` : 226 | `BlockByNameRpc(string characterName)` | owner only, speed limit (Social), text length |  |
| 69 | Social | `PlayerContacts.cs` : 234 | `UnblockRpc(string characterId)` | owner only, speed limit (Social), text length |  |
| 70 | Social | `PlayerContacts.cs` : 242 | `UnblockByNameRpc(string characterName)` | owner only, speed limit (Social), text length |  |
| 71 | Social | `PlayerContacts.cs` : 250 | `FavouriteRpc(string characterId, bool favourite)` | owner only, speed limit (Social), text length |  |
| 72 | Social | `PlayerContacts.cs` : 258 | `NoteRpc(string characterId, string note)` | owner only, speed limit (Social), text length |  |
| 73 | Social | `PlayerCrew.cs` : 194 | `FoundRpc(string crewName, byte symbol, byte colour)` | owner only, speed limit (Social), text length |  |
| 74 | Social | `PlayerCrew.cs` : 202 | `InviteRpc(NetworkObjectReference targetReference)` | owner only, speed limit (Social), text length |  |
| 75 | Social | `PlayerCrew.cs` : 209 | `InviteByNameRpc(string playerName)` | owner only, speed limit (Social), text length |  |
| 76 | Social | `PlayerCrew.cs` : 217 | `RespondRpc(bool accept)` | owner only, speed limit (Social) |  |
| 77 | Social | `PlayerCrew.cs` : 230 | `LeaveRpc()` | owner only, speed limit (Social), text length |  |
| 78 | Social | `PlayerCrew.cs` : 237 | `ExpelRpc(string memberId)` | owner only, speed limit (Social), text length |  |
| 79 | Social | `PlayerCrew.cs` : 245 | `OfficerRpc(string memberId, bool officer)` | owner only, speed limit (Social), text length |  |
| 80 | Social | `PlayerCrew.cs` : 253 | `HandOverRpc(string memberId)` | owner only, speed limit (Social), text length |  |
| 81 | Social | `PlayerCrew.cs` : 261 | `TitleRpc(string memberId, string title)` | owner only, speed limit (Social), text length |  |
| 82 | Social | `PlayerCrew.cs` : 270 | `AnnouncementRpc(string text)` | owner only, speed limit (Social), text length |  |
| 83 | Social | `PlayerCrew.cs` : 278 | `SymbolRpc(byte symbol, byte colour)` | owner only, speed limit (Social) |  |
| 84 | Social | `PlayerCrew.cs` : 285 | `DisbandRpc()` | owner only, speed limit (Social) |  |
| 85 | Social | `PlayerEmotes.cs` : 148 | `PlayRpc(FixedString64Bytes key)` | owner only, speed limit (Cosmetic) |  |
| 86 | Social | `PlayerFollow.cs` : 109 | `FollowRpc(NetworkObjectReference targetReference)` | owner only, speed limit (Move), text length |  |
| 87 | Social | `PlayerFollow.cs` : 116 | `FollowByNameRpc(string memberName)` | owner only, speed limit (Move), text length |  |
| 88 | Social | `PlayerParty.cs` : 146 | `InviteByNameRpc(string playerName)` | owner only, speed limit (Social), text length |  |
| 89 | Social | `PlayerParty.cs` : 176 | `InviteRpc(NetworkObjectReference targetReference)` | owner only, speed limit (Social) |  |
| 90 | Social | `PlayerParty.cs` : 183 | `RespondRpc(bool accept)` | owner only, speed limit (Social) |  |
| 91 | Social | `PlayerParty.cs` : 222 | `LeaveRpc()` | owner only, speed limit (Social), text length |  |
| 92 | Social | `PlayerParty.cs` : 230 | `KickRpc(string playerName)` | owner only, speed limit (Social), text length |  |
| 93 | Social | `PlayerParty.cs` : 239 | `PromoteRpc(string playerName)` | owner only, speed limit (Social), text length |  |
| 94 | Social | `PlayerSponsor.cs` : 145 | `RequestRpc(NetworkObjectReference targetReference)` | owner only, speed limit (Social), text length |  |
| 95 | Social | `PlayerSponsor.cs` : 153 | `RequestByNameRpc(string characterName)` | owner only, speed limit (Social), text length |  |
| 96 | Social | `PlayerSponsor.cs` : 178 | `RespondRpc(bool accept)` | owner only, speed limit (Social) |  |
| 97 | Social | `PlayerSponsor.cs` : 199 | `EndRpc(string characterId)` | owner only, speed limit (Social), text length |  |
| 98 | Autopilot | `PlayerAutobuff.cs` : 76 | `SetSlotsServerRpc(string json)` | owner only, speed limit (Account), text length | Settings text capped at 4096 characters. |
| 99 | Autopilot | `PlayerAutopilot.cs` : 107 | `SetOnServerRpc(bool value)` | owner only, speed limit (Account) |  |
| 100 | Autopilot | `PlayerAutopilot.cs` : 124 | `SetPreferencesServerRpc(string json)` | owner only, speed limit (Account), text length | Settings text capped at 4096 characters. |
| 101 | Crafting | `PlayerCrafting.cs` : 92 | `CraftRpc(ushort recipeId, ushort times)` | owner only, speed limit (Money) |  |
| 102 | Housing | `PlayerCrate.cs` : 94 | `StoreRpc(ushort packCell, ushort count)` | owner only, speed limit (Money) |  |
| 103 | Housing | `PlayerCrate.cs` : 101 | `WithdrawRpc(ushort itemId, ushort count)` | owner only, speed limit (Money) |  |
| 104 | Networking | `PlayerGm.cs` : 87 | `CommandServerRpc(string line)` | owner only, speed limit (Account), text length |  |
| 105 | Pets | `PlayerPet.cs` : 201 | `ShareTrickRpc(int nameHash)` | owner only, speed limit (Cosmetic) |  |
| 106 | Restyle | `PlayerRestyle.cs` : 92 | `RestyleRpc(byte service, CharacterNetState proposed)` | owner only, speed limit (Money) |  |
| 107 | Restyle | `PlayerWeaves.cs` : 154 | `WeaveRpc(byte slot, ushort lineId)` | owner only, speed limit (Money) |  |
| 108 | Restyle | `PlayerWeaves.cs` : 161 | `ClearWeaveRpc(byte slot)` | owner only, speed limit (Money) |  |
| 109 | Restyle | `PlayerWeaves.cs` : 168 | `DyeRpc(byte slot, ushort colourway)` | owner only, speed limit (Money) |  |
| 110 | Restyle | `PlayerWeaves.cs` : 175 | `MorphRpc(ushort petItemId, byte stage)` | owner only, speed limit (Money) |  |
| 111 | Restyle | `PlayerWeaves.cs` : 182 | `ClearMorphRpc()` | owner only, speed limit (Money) |  |

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
