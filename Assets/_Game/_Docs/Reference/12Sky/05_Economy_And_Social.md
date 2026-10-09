# Economy and Social Systems: as built

As of 5 Oct 2026 · branch `economy-social-pets` · companion to `01_Systems_Build_List.md` (rows 1, 12, 13, 14, 15, 20, 29, 33) · pets are in `../../Design/12_pet_evolution_system.md`

What exists in code for saving, Creds, item pickup, the Vault and Deep Vault, Trade, Kiosks, Contacts and Crews: the rules, the numbers and where each lives. Every number below is a field of a settings asset and marked *(proposed)* until the owner confirms it; none is a constant in code.

## How every system here is built

- **Rules** are plain static classes with no networking (`WalletRules`, `StorageRules`, `TradeRules`, `KioskRules`, `LootRules`, `ContactRules`, `CrewRules`), each with EditMode tests.
- **The server decides.** Each system has a `NetworkBehaviour` on the player (`PlayerWallet`, `PlayerVault`, `PlayerDeepVault`, `PlayerTrade`, `PlayerKiosk`, `PlayerLoot`, `PlayerContacts`, `PlayerCrew`). The owner sends requests; the server checks them against the rules and replicates the result. What only the owner should know (Creds, storage, their own lists) is owner-readable.
- **The HUD only shows and asks.** One UI Toolkit controller per window, in `UI/Runtime/Economy/` and `UI/Runtime/Social/`.
- **Items move whole.** An `ItemStack` carries `data` (a pet's growth and hunger today; Temper, Fusion and Tuning later), so an item keeps its own state in the Pack, a slot, the Vault, a trade, a Kiosk and on the ground.

## Saving (row 1)

| | |
| --- | --- |
| Where | Files on the server host: `persistentDataPath/ServerSave/<profile>/` behind `ISaveStore` (`FileSaveStore`; `MemorySaveStore` in tests). A database or Cloud Save is another `ISaveStore`. |
| Account document | `accounts/<accountId>`: the Deep Vault, the day's trade total, and the account's characters. |
| Character | Progression, combat and movement skills, the Pack, worn items with their state, Creds, the Vault. |
| World documents | `world/crews`, `world/kiosks`, `world/contacts`: they go on while their players are offline. |
| How a system saves | It implements `ISaveParticipant` (a key, a scope, capture, restore) on its player component. `PlayerSaveAgent` does the rest: load on entry, autosave every 60 s, save on leave, save at once after a trade, a sale or a founding. |
| Identity | The client sends an account id (generated locally, `kn.account.v1`) and its character id. Trusted until real sign-in exists. One character of an account in the world at a time. |
| GM | `gm.save`, `gm.save.where`, `gm.save.wipe confirm` |
| GM in a build | The host (or an offline player) opens the GM console with **F2** (`Windows/GmConsole`, rebindable; `GmConsoleGate`, prefab `Tools/Debug/Prefabs/GmConsole`). A joined client has no console and no GM commands; the host aims commands at a client with `gm.players` and `gm.target <clientId>`. In the Editor the GM Tools window does the same job. |

## Creds and the economy model

`Assets/_Game/Gameplay/Economy/Data/Assets/EconomySettings.asset` holds every number. Its inspector shows, per level, the income target and the hours of play each cap and cost takes to earn, and warns when a cost is out of reach.

| Tunable *(proposed)* | Value |
| --- | --- |
| Wallet cap (the Vault and Deep Vault each hold as much again) | 999,999,999,999 |
| Creds per trade, each way | 5,000,000 |
| Creds an account sends in trades per day (UTC) | 20,000,000 |
| Kiosk price cap per unit: Common / Uncommon / Rare / Epic | 50,000 / 500,000 / 5,000,000 / 20,000,000 |
| Kiosk price cap per unit: stackable non-gear | 10,000 |
| Kiosk price cap: a pet, rising with its growth from the Common cap to | 20,000,000 at 100 % |
| Kiosk sales tax (a Creds sink) | 5 % |
| The forge (a Creds sink; numbers in `GearProgressionSettings`, see the gear spec) | Tempering: item level × 500 × rarity multiplier, doubled from 120 % · Fusion: 100,000 + 50,000 a stage · Tuning: 50,000 / 150,000 / 400,000 by stage bracket · Reforge: 1,000,000 at level 45, +200,000 per ten levels |
| The Herald (a Creds sink; numbers in `NovaSettings`, see the gear spec section 9) | Ascension: 500,000 an attempt, with 100 Renown. On average +0 to +24 takes about 51 attempts (25.7M Creds, 5,100 Renown) and +0 to +96 about 275 (138M Creds, 27,500 Renown). Nova Standings cost Renown only: 65,800 a track |
| Income target, Creds per hour at level 1 / 20 / 40 / 60 / 80 / 100 / 112 | 3k / 25k / 90k / 250k / 600k / 1.2M / 2M |

- **Nothing pays Creds yet.** They enter only through `gm.creds`. The income target is the design number Hostile drops, Contracts and vendors must be tuned against when they are built.
- **The ledger** (`CredsLedger`, `gm.creds.report`) counts Creds by source and sink since the server started, so real earn rates can be set beside the target.
- **The forge is the first Creds sink a player chooses to pay** (`CredsSink.GearWork`, counted by the ledger). Tuning also costs Renown (10 / 25 / 50 an attempt), which nothing pays yet.
- **The Herald is the second** (`CredsSink.Herald`): every Ascension attempt takes its Creds whether it rises, holds or slips. It is the long sink of the end game, and the one that makes Renown worth earning; both of its ladders wait on a Renown source.
- **Why the caps:** to make selling Creds for money slow. A full day's allowance is one Epic at its price cap.
- **Known tension to tune:** at the level-30 income target the 10,000,000 Crew fee is about 200 hours of play.

## Item pickup (row 13)

- `WorldDrop` is the item on the ground: networked, carries the whole stack, cleared after 180 s, at most 200 per zone (oldest goes). Several drops on one spot spread out in a spiral.
- Click a drop to walk over and take it; **~** takes the nearest within 3.5 m. The server checks reach, who it is held for, and Pack space. What does not fit stays.
- The character crouches (the Synty Base Locomotion Stand-to-Crouch and Crouch-to-Stand clips: no pack has a pick-up clip) and turns to the item; the parcel goes to the hand. Everyone sees it.
- **Dropping from the Pack:** the item is held for its dropper for **20 seconds**, then it is free for all, whatever their faction (`PlayerLoot.dropsOpenToEveryone`; off limits it to the dropper's faction). The dropper sees a small countdown notice above the hotbar; labels over held drops show the seconds left.
- Hostile drops come with Hostiles: `PlayerLoot.ServerPlace` is the call to make.

## Vault and Deep Vault (row 14)

- **Vault**: one per character, at a **Vaultkeeper**. **Deep Vault**: one per account, shared by all its characters, at a **Wayfinder**. 96 cells (two pages) and a Creds balance each.
- Right-click moves an item between Pack and storage; Shift+right-click asks how many; drag onto a cell to place, top up or swap. The server refuses any request made away from an NPC offering the service.
- Both NPCs stand at their plaza stalls (Crew Hall and Wayfinder). An NPC offers a service through `NpcDefinition.offers`; its dialogue then shows a button per service.

## Trade (row 12, the Trade half)

1. Ask: TRADE on the target strip, the player menu, or `/trade [name]`. Within 6 m.
2. The other accepts (prompt, or `/trade accept`).
3. Each lays out up to 12 stacks and Creds. Right-click a Pack item to offer it; offered cells are held.
4. Both **Lock**. Any change after that unlocks both.
5. Both **Confirm**. The server swaps everything at once, or nothing: both Packs must still hold what was offered and have room for what comes back; the Creds must be within the per-trade cap, the account's remaining daily allowance and the receiver's wallet cap.

Walking more than 8 m apart, dying or leaving cancels. The trade window shows the caps and what the account may still send today.

## Kiosks (row 15)

- **Where:** inside a trade zone that allows Kiosks (`TradeZone.allowKiosks`): the Central Plaza of the starter town today. Not within 2.5 m of another Kiosk or a vendor.
- **Setting up (T):** a shop name, up to 24 stacks from the Pack with a price for one of each, and how long it runs.

| Duration | Cost |
| --- | --- |
| 3 days | free |
| 14 days | one **Kiosk Permit (14 Days)**, item 9020, Nova Store $1.99 |
| 30 days | one **Kiosk Permit (30 Days)**, item 9021, Nova Store $3.99 |

  The Nova Store does not sell yet; `item.give Permit_Kiosk14` hands one out. A duration without its permit shows locked with its store price.
- **The stand-in** is a doll of the owner as they looked when they opened it, with the shop's name over it. The items leave the Pack and the owner is free to go: it sells while they play or are offline, and survives a server restart.
- **Buying:** click the stand, right-click an item, choose how many, confirm. The buyer pays the price; 5 % is tax; the rest goes to the till.
- **The owner** is told of each sale in chat, or gets a summary on return. The till is collected from the Kiosk window anywhere. Closing, selling out or running out of time removes the stand and holds unsold items and the till until collected.
- **GM:** `gm.kiosk` (list), `gm.kiosk.expire`.

## Contacts (row 29)

- **Mutual, per character.** One asks, the other accepts; then each is in the other's list. Removing takes both out. A request waits while either is offline.
- **The window (F):** Contacts (favourites first, then online), Requests, Recent (Squad mates, trade partners, Callout opponents and whisperers of this session), Blocked. Each contact shows level, weapon class and when they were last seen, and has a private note.
- **Block** is one-sided and never announced. It ends the contact and stops whispers, Squad invitations, Callouts, trade, contact requests and Crew invitations in both directions: every one of those asks `SocialGate` first.
- `/contact add|remove <name>`, `/block <name>`, `/unblock <name>`; Add Contact and Block are in the player menu (Shift+click a player).

## Crews (row 33)

- **Founding:** at a Vaultkeeper, level 30, 10,000,000 Creds *(reference page 28)*. A name of 3 to 16 characters, unique, and one of 12 symbols.
- **Ranks:** one **Leader**, up to 3 **Officers**, **Members**. 20 members at Crew level 1 (the table has later levels; Crews do not level yet).

| | Member | Officer | Leader |
| --- | --- | --- | --- |
| Crew chat (`/g`), leave | yes | yes | yes |
| Invite, expel Members, write the announcement | | yes | yes |
| Appoint and remove Officers, Crew Titles, the symbol, expel Officers, hand over, disband | | | yes |

- A Leader with other members hands the Crew over before leaving; the last member out closes it.
- **The window (G):** roster with rank, title, level, class and last seen; the announcement, which reaches every online member when it changes and is shown at login.
- `/cinvite [name]`, `/caccept`, `/cdecline`, `/cleave`, `/ckick <name>`. **GM:** `gm.guild [name|none]`, `gm.guild.list`.

## Namecards (row 44)

Over every player's head: the Standing they wear, their faction's crest, their name and level in the faction's colour, and their Crew (`<Crew Name>`). A Callout opponent's name is red while the duel lasts. The card sits just above the head and rises to clear a Nova.

- **Who sees whose** is one rule, `NamecardRules.Visible`: your own if the setting "My own namecard" is on (Settings ▸ Interface; on by default), your faction's always, another faction's only while that player stands on neutral ground. The setting "Names over NPCs and players" switches all of them, with the NPC nameplates.
- Everything on the card was already replicated (identity, level, Crew tag, the Nova state), so namecards send nothing. `PlayerNamecardController` draws them; hidden past 38 m, like NPC nameplates.

## Emote wheel (row 45)

Hold **B** (rebindable): a ring of eight categories; a click turns it into that category's emotes, eight a page (the mouse wheel turns the page, the hub or a right-click goes back); a click on an emote performs it and closes the wheel; letting go of the key closes it at any time.

| Tunable (`EmoteWheelSettings.asset`) | Value |
| --- | --- |
| Categories, clockwise from the top | Greet, Cheer, Moves, Taunt, Feels, Gestures, Props, Stances: each a list of the expression library's groups |
| Emotes on a page | 8 |
| Seconds before the same player may start another | 1 |

- The motions are the creator's expression library (`CharacterExpressionLibrary`): 127 emotes and 34 stances. A character only performs the ones made for its body type, and props (a cup, a phone, food) are held in the right hand.
- `PlayerEmotes` asks the server, which checks that the character is alive, standing still and not striking, casting or jumping, then tells everyone. The weapon is put away first. An emote plays once; a **stance** (the names shown in colour on the wheel) is held until the character moves, strikes, casts, jumps, draws, is hit or dies, and a player who arrives meanwhile sees it held.
- A motion is named on the wire by its kind, group and name (`EmoteCatalog.Key`, for example `E/Greet/Bow`), never by its place in the library: a re-bake reorders the library.

## Who may deal with whom

| | Same faction | Another faction |
| --- | --- | --- |
| Seeing a namecard | anywhere | only while that player is on neutral ground |
| Trade, buying from a Kiosk | anywhere | on neutral ground only |
| Contacts | anywhere, online or not | face to face on neutral ground only |
| Whisper, Callout | anywhere | anywhere (a Callout cannot be asked of someone who is already an enemy) |
| Attacking | only in a Callout | off neutral ground while `gm.pvp` is on; **on neutral ground only in a Callout** |
| Squad | yes | **never** |
| Crew | yes | **never** |
| Picking up a dropped item (after its 20 s) | yes | yes |

**Declined for the player.** In **Settings ▸ Gameplay** a player can have trade requests, contact requests, Crew invitations, Squad invitations and Callouts declined without being shown them (`HudPreferences.RefuseTrades`, `RefuseContacts`, `RefuseCrewInvites`, `RefuseSquadInvites`, `RefuseCallouts`; all off as shipped). The request still reaches the player's game, which answers no on arrival exactly as the Decline button would (`RequestPromptController.Refused`, `PartyHudController`, `DuelHudController`): the server is asked nothing new and the asker sees an ordinary refusal. It is kept per machine, like the other options, not per character.

**Neutral ground** is a `TradeZone` with **Neutral Ground** ticked (the field is `crossFaction`). The whole starter town is marked so (scene object "Neutral Ground - Starter Town (for testing)"). Since 9 Oct 2026 it is also the non-combat zone: between two players, hostility is decided where they stand (`CombatHostility.IsHostile` → `FactionRules.AreHostile(a, b, neutralGround)`), and when either of the two is on neutral ground the factions are at peace, so a player of another faction is selected, targeted and offered the same things as a faction mate (the target strip and the player menu, without the Squad and Crew invitations) and can be hurt only in a Callout. The server refuses the strike; the zone is a scene object, the same on every machine, so every peer draws the same conclusion. `gm.pvp` therefore changes nothing inside the town: to test faction fights there, untick Neutral Ground on that object, or fight outside the walls.

## Leaving: Log Out and Exit Game (row 46)

**Esc** opens the game menu when nothing else has a use for the key: an open window, a dialogue, a context menu, a target and a journey on the map all take the Esc first, as before. The MENU button in the top bar opens it too. It offers Resume, Settings, **Log Out** (back to character select, with the character just played highlighted) and **Exit Game** (closes the application).

- Both count down (`GameMenuController.leaveSeconds`, 5 s) and can be cancelled until the count ends. A host is told how many other players leaving disconnects.
- When the count ends: everything is saved where this machine decides the game (`ServerSave.FlushAll`: a host, or a game with no session); the session is closed and waited for; a host saves every player as they leave the world (`PlayerSaveAgent.OnNetworkDespawn`), so a client's character is written by the host. Then the game closes, or the session objects are destroyed and character select loads. The NetworkManager and what rides on it outlive scene changes and hold on to that world's player; they are destroyed so the next world brings its own, which is what lets the world be entered again in the same run.
- A player whose session ends under them (the host left, the connection dropped) is shown **Session Ended**: Character Select, Exit Game, or Stay (they stand where they were, offline, and can join or host again from the network panel). Leaving through the menu or the network panel's Disconnect is the player's own choice and shows nothing.
- When the host leaves, the session ends for everyone: nobody takes it over.
- The title screen shows "Esc  Exit Game" and asks before closing (Enter exits, Esc stays). Character select already has Back to the title.

## Keys and commands

| Key | Window | | Key | Window |
| --- | --- | --- | --- | --- |
| **F** | Contacts | | **T** | Kiosk |
| **G** | Crew | | **~** | Pick up the nearest item |
| **B** (held) | Emote wheel | | **Esc** | Game menu (when nothing else is open) |

## Not built

The Exchange board; selling Kiosk Permits (Nova Store); any Creds faucet (Hostile drops, Contracts, vendors); Crew levels; the Crew Title and symbol on the namecard (the Crew's name is there); any Renown source; emotes for two players; handing a session on when its host leaves; trade zones in faction Home Cities; the dedicated non-combat zone; real sign-in.

## Verified

EditMode tests for every rules class (242 pass on 5 Oct 2026). Host plus a Multiplayer Play Mode client, two factions: pickup and its gesture, drop hold, Vault, Trade, Kiosk (open, buy, tax, away summary, expiry, collect), Contacts (request, offline request, favourite, note, block), Crew (found, ranks, title, announcement, chat, hand over, close), and the neutral-ground rules. Saves survive a restart.
