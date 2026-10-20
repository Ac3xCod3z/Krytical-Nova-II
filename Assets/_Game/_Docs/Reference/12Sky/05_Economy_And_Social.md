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
| Where | Files on the server host: `persistentDataPath/ServerSave/<profile>/` behind `ISaveStore` (`FileSaveStore`; `MemorySaveStore` in tests). A dedicated server whose settings name a save service saves to the save database instead (online phase 4, `ServiceSaveStore`; the owner's real server since 18 Oct 2026). |
| Account document | `accounts/<accountId>`: the Deep Vault, the day's trade total, the Weave Library (`weaves`: gear looks and, since 12 Oct 2026, Morphs), and the account's characters. |
| Character | Progression, combat and movement skills, the Pack, worn items with their state, Creds, the Vault; since 11 Oct 2026 also the character's look (`look`), the Weave of each slot and the Morph of the pet slot (`weave`; a dye is part of the worn piece's state) and its Sponsor points (`sponsor`). |
| World documents | `world/crews`, `world/kiosks`, `world/contacts`, `world/sponsors`: they go on while their players are offline. |
| The look | Saved with the character since 11 Oct 2026 (`PlayerIdentity`). The first time a character enters, the look its player's game sends is taken and saved; from then on the saved look is the one shown, whatever is sent, and only the server changes it (the Stylist, the Reshaper, the Coach). The player's own game copies it back into its roster so character select matches (`CharacterSession.RememberRecipe`). |
| How a system saves | It implements `ISaveParticipant` (a key, a scope, capture, restore) on its player component. `PlayerSaveAgent` does the rest: load on entry, autosave every 60 s, save on leave, save at once after a trade, a sale or a founding. |
| All or nothing (online phase 4, milestone 3; 19 Oct 2026) | A save is one change: on the save service one database transaction, in files one journaled change (`FileSaveStore` writes it whole to `_journal/` first and finishes it on the next start if a stop cut it short). Anything that changes more than one player or world document saves through `ServerSave.SaveTogether` as one change: a **trade** (both players), a **Kiosk** opening, purchase and collection (the player and `world/kiosks`), a **Crew founding** (the founder and `world/crews`). A Deep Vault move is inside one account, so it is one change already. A crash keeps the change whole or loses all of it: an item is never on both sides of a trade, nor on neither. **Every economy action, now and after a rework (owner, 18 Oct 2026), keeps this.** |
| The kept ledger (milestone 3) | One line per account per event, saved in the same change as the event, so the ledger always agrees with the saves (`LedgerEntry`, `ServerSave.Ledger`). **Creds:** every Cred a wallet earns or pays is tallied by `PlayerWallet` itself, by where it came from or went to (`creds.trade`, `creds.vendor`, `creds.hostilekill`, `creds.gm`, ...), one line per kind per save, so an account's lines add up to its wallet whatever moved the Creds. **Items** (`EconomyLedger`): `trade` (given and received, with the partner), `kiosk.open` / `kiosk.buy` / `kiosk.collect` (with the owner), `shop.buy` / `shop.sell`, `deepvault.in` / `deepvault.out`, `crew.found`, `forge` (what was worked, a broken piece as lost), `loot` and `drop` (tallied per save), `gm` (a GM command that changed a Pack, with the command; found by comparing the Pack before and after, so every GM command is covered). Kept `ledgerDays` (90) in the server's settings, then dropped once a day; each backup holds the lines since the one before it (`ledger.jsonl`). The save database keeps the lines as text with only account, kind and time: the economy's rules are never in it. |
| The roster on a server (online phase 4, milestone 5; 19 Oct 2026) | Which characters an account has is the server's: the game lists, creates, deletes and renames them by asking it (`RosterLink`, `ServerRoster`), each character keeps its slot, and names are unique across the game, capitals ignored (`world/names`). Where two characters shared a name before, a GM's keeps it, then the higher level, then the one played least recently; the others choose a new name before they play again. A character the server does not have cannot join it. Deleting writes a `character.deleted` ledger line. Three slots (`CharacterRoster.SlotCount`). |
| When a save changes shape (save migrations, 20 Oct 2026) | Adding or removing a field needs nothing. A system whose saved text changes meaning (a rename, a split, another unit) implements `IMigratingSave`: a version and one tested step per version; older saves are stepped up when they load, and one that cannot be is refused, never reset. Run Check Saves Against This Build on a copy of the real saves before the release. The economy rework uses this. |
| What the menus know | The roster (PlayerPrefs, per machine; on a server, this PC's copy of the server's roster, `MenuRoster`) is not the save: it holds who each character is, its look, its level and a record of the gear it was last played in, so character select can draw it. The player's own game keeps the level and the gear record current (`CharacterSession.RememberLevel`, `RememberWorn`). Nothing is ever read back from it into the game. |
| Identity | The client sends an account id (generated locally, `kn.account.v1`) and its character id. Trusted until real sign-in exists. One character of an account in the world at a time. |
| Decided, partly built | The game is moving to dedicated servers with several map servers. The rules above stay (the server saves, each system saves its part, 60 s, on leave, at once after a trade). Built (online phases 2 and 4): verified sign-in, the save database behind `ISaveStore`, one server holding an account at a time, accounts saved in parts, last map and position saved, trades, Kiosk sales and Crew foundings as single changes, the kept ledger. Crews, Contacts, Kiosks and Sponsors changed one entry at a time (milestone 4), the roster and names held by the server (milestone 5). Not yet: nightly database backups. `../../Plans/online-phase-04-accounts-and-shared-save.md`. |
| GM | `gm.save`, `gm.save.where`, `gm.save.wipe confirm`, `gm.ledger <who> [days] [lines]` (who: a character name, an account id, or all; the ledger, newest first; also in the GM Tools window, Economy tab) |
| GM in a build | The host (or an offline player) opens the GM console with **F2** (`Windows/GmConsole`, rebindable; `GmConsoleGate`, prefab `Tools/Debug/Prefabs/GmConsole`). On a server, up to **three GMs** (`GmRoster`, `PlayerGm`; owner, 16 Oct 2026, until Unity sign-in) are set on the control page, each with a GM password of their own (`gameMasters` in `server-settings.json`, 8 characters or more, each different): a joined player presses F2, types it (shown as dots, never logged), and from then on each line runs on the server aimed at their own player (`GmCommandRelay`; `gm.target` for them alone), with the answer back to them only. Five wrong passwords send the player away; removing a GM ends their rights at once. Everyone else gets only the password prompt and no GM commands. The host aims commands at a client with `gm.players` and `gm.target <clientId>`. In the Editor the GM Tools window does the same job. |

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

- **Hostile kills pay Creds** (since 9 Oct 2026): as a stack of cash on the ground where the Hostile died (`HostileLoot`; item 9040, `LootRules.CredsItemId`, up to 60,000 in one drop), held for the player who holds the kill like any drop. Picking it up pays the wallet (`PlayerLoot` → `ICurrencyReceiver`, `CredsSource.HostileKill`) and never touches the Pack; what a full wallet cannot hold stays on the ground, 70 % of kills, the amount from `HostileStatCurve` (3 + 1.1 per level times the expected gear rarity, 20 % either way), scaled by the level gap. At level 1 that is about half the income target at 600 kills an hour: to tune. Otherwise Creds enter only through `gm.creds`; Contracts and vendors are not built.
- **The ledger** (`CredsLedger`, `gm.creds.report`) counts Creds by source and sink since the server started, so real earn rates can be set beside the target.
- **The forge is the first Creds sink a player chooses to pay** (`CredsSink.GearWork`, counted by the ledger). Tuning also costs Renown (10 / 25 / 50 an attempt), which nothing pays yet.
- **The Herald is the second** (`CredsSink.Herald`): every Ascension attempt takes its Creds whether it rises, holds or slips. It is the long sink of the end game, and the one that makes Renown worth earning; both of its ladders wait on a Renown source.
- **Why the caps:** to make selling Creds for money slow. A full day's allowance is one Epic at its price cap.
- **Known tension to tune:** at the level-30 income target the 10,000,000 Crew fee is about 200 hours of play.

## Item pickup (row 13)

- `WorldDrop` is the item on the ground: networked, carries the whole stack, cleared after 180 s, at most 200 per zone (oldest goes). Several drops on one spot spread out in a spiral.
- Click a drop to walk over and take it; **~** takes the nearest within 3.5 m. The server checks reach, who it is held for, and Pack space. What does not fit stays.
- The character crouches (the Synty Base Locomotion Stand-to-Crouch and Crouch-to-Stand clips: no pack has a pick-up clip) and turns to the item; the parcel goes to the hand. Everyone sees it.
- **Magnet needs Autopilot time (owner, 15 Oct 2026):** it still runs with Autopilot off, but only while the character has Autopilot time left (`MagnetRules.MayRun`; a GM's free mode counts). Before that it was free, which is why an item put down with `gm.drop` vanished at once. `gm.magnet.test` says so when it is waiting.
- **Dropping from the Pack:** the item is held for its dropper for **20 seconds**, then it is free for all, whatever their faction (`PlayerLoot.dropsOpenToEveryone`; off limits it to the dropper's faction). The dropper sees a small countdown notice above the hotbar; labels over held drops show the seconds left.
- Hostile drops (since 9 Oct 2026): a Stim (6 %) or a piece of ladder gear of the Hostile's level (2.5 %; a Bruiser 25 %, of the best rarity its level has) falls where it died, through `PlayerLoot.ServerPlaceAt`, held for the player who holds the kill for the usual 20 s. Chances are `HostileSettings`. Magnet (`MagnetController`) picks up what a player has ticked: Common, Uncommon, Rare and Epic gear each have a box, as do Stims and materials and other items; Creds are always taken. Drops are pooled (`WorldDropPool`): one that is picked up or runs out goes back to be used again, on the server and on every client.

## Vault and Deep Vault (row 14)

- **Vault**: one per character, at a **Vaultkeeper**. **Deep Vault**: one per account, shared by all its characters, at a **Wayfinder**. 96 cells (two pages) and a Creds balance each.
- Right-click moves an item between Pack and storage; Shift+right-click asks how many; drag onto a cell to place, top up or swap. The server refuses any request made away from an NPC offering the service.
- Both NPCs stand at their plaza stalls (Crew Hall and Wayfinder). An NPC offers a service through `NpcDefinition.offers`; its dialogue then shows a button per service.
- **What is stored pays costs (11 Oct 2026).** Anything that uses up items takes them from the Pack first, then the Vault, then the Deep Vault, wherever the player stands: the forge's Alloys, Failsafes, catalyst and Chips, a Kiosk Permit, a Chroma Cell or a Prism Cell for a dye, a Restyle or Training Pass, and every craft at the Fabricator. One place does it (`PlayerItemSources.ServerSpend`, rules in `ItemCostRules`): all of a cost is there or nothing is taken. Only plain items count (no state of their own), so a cost never eats a worked piece of gear. The piece being worked on, and the piece a Fusion uses up, must still be in the Pack. Using a consumable, selling and trading stay Pack-only. The forge and the Fabricator show where a material is ("Pack 3 · Vault 12"). Putting things in and taking them out by hand still needs the NPC.

## Trade (row 12, the Trade half)

1. Ask: TRADE on the target strip, the player menu, or `/trade [name]`. Within 6 m.
2. The other accepts (prompt, or `/trade accept`).
3. Each lays out up to 12 stacks and Creds. Right-click a Pack item to offer it; offered cells are held.
4. Both **Lock**. Any change after that unlocks both.
5. Both **Confirm**. The server swaps everything at once, or nothing: both Packs must still hold what was offered and have room for what comes back; the Creds must be within the per-trade cap, the account's remaining daily allowance and the receiver's wallet cap.

Walking more than 8 m apart, dying or leaving cancels. The trade window shows the caps and what the account may still send today.

## Kiosks (row 15)

- **Where:** inside a trade zone that allows Kiosks (`TradeZone.allowKiosks`): the Central Plaza of the starter town today. Not within 2.5 m of another Kiosk or a vendor.
- **Setting up (J; T until 17 Oct 2026, when T became a Hotbar slot):** a shop name, up to 24 stacks from the Pack with a price for one of each, and how long it runs.

| Duration | Cost |
| --- | --- |
| 3 days | free |
| 14 days | one **Kiosk Permit (14 Days)**, item 9020, Nova Store $1.99 |
| 30 days | one **Kiosk Permit (30 Days)**, item 9021, Nova Store $3.99 |

The Autobuff's Passes (3 / 14 / 30 days, items 9031 to 9033) and the Autopilot Passes (items 9030, 9034, 9035) run on the same clock: see "Autobuff" and "Autopilot Passes" below.

  The Nova Store does not sell yet; `item.give Permit_Kiosk14` hands one out. A duration without its permit shows locked with its store price.
- **The stand-in** is a doll of the owner as they looked when they opened it, with the shop's name over it. The items leave the Pack and the owner is free to go: it sells while they play or are offline, and survives a server restart.
- **Buying:** click the stand, right-click an item, choose how many, confirm. The buyer pays the price; 5 % is tax; the rest goes to the till.
- **The owner** is told of each sale in chat, or gets a summary on return. The till is collected from the Kiosk window anywhere. Closing, selling out or running out of time removes the stand and holds unsold items and the till until collected.
- **GM:** `gm.kiosk` (list), `gm.kiosk.expire`.

## NPC shops (rows 3, 22, 31; built 10 Oct 2026)

A vendor sells from a shelf and buys what the player carries. Two are open, the ones whose goods already existed: the **Apothecary** and the **Armorer**. The Store Clerk, Fixer, Broker, Marshal and Instructors have no shop yet (their goods or their currency do not exist).

**Every price here is a placeholder** (owner, 10 Oct 2026: "arbitrary amounts for now, will tune later"). They are data: a listed item's price is on the vendor's stock asset, the rest are in `EconomySettings`.

| Vendor | Sells | Placeholder price, Creds for one |
| --- | --- | --- |
| Apothecary (`VS_Apothecary`) | HP Stim (S), Flux Stim (S) | 30 |
| | Dual Stim (S) | 80 |
| | HP Stim (M), Flux Stim (M) | 120 |
| | Dual Stim (M) | 320 |
| | HP Stim (L), Flux Stim (L) | 400 |
| | Dual Stim (L) | 1,000 |
| | Recall Tag | 150 |
| | Flux Flask (and its refill, 60 a drink, from the Apothecary's dialogue) | 600 |
| | Pet Ration | 50 |
| Armorer (`VS_Armorer`) | Alumite (+3 %) | 500 |
| | Iridion (+6 %) | 1,500 |
| | Fusion Catalyst, Tuning Chip | 10,000 |
| | (each of these four: **1 per character per in-game week**, see "The weekly limit" below) | |
| | Common ladder gear, levels 1 to 28, for the buyer's own faction and body | item level × 150 (150 at level 1, 4,200 at level 28) |

| Tunable in `EconomySettings` *(placeholder)* | Value |
| --- | --- |
| Gear price: Creds per item level (`vendorGearCredsPerLevel`) | 150 |
| Gear price: weight of Common / Uncommon / Rare / Epic (`vendorGearRarityMultiplier`) | 1 / 4 / 20 / 100 |
| What an item is valued at when no shelf lists it and it is not ladder gear, by rarity (`vendorFallbackValueByRarity`) | 20 / 100 / 2,000 / 20,000 |
| What a vendor pays: share of what the shops value the item at (`vendorSellBackPercent`) | 70 % (owner, 15 Oct 2026; was 20 %) |
| A sale of this rarity or better must be confirmed (`vendorConfirmFromRarity`) | Rare |

- **Opening:** the vendor's dialogue has a BROWSE WARES button (`NpcOffer.Shop`); the shop opens beside the Pack and closes when the player walks away. It is a movable window (SHOP in the layout editor).
- **Buying:** right-click an item. A stackable item buys one, or Shift asks how many; a piece of gear says its price once more first. A price the player's Creds do not reach is red. The Armorer's shelf shows a buyer only the gear they can wear (`GearDictionary.CanUse`: faction and body); a level above the buyer's own is sold, to grow into.
- **Selling (owner, 10 Oct 2026: yes, at a low rate):** with a shop open, right-click an item in the Pack or drop it on the window. Any vendor buys anything that has a value; nothing can be bought back. A stack always asks how many.
- **Rare and better is never sold without a notice and a confirm** (owner, 10 Oct 2026): the notice names the item, its rarity and the Creds it would fetch, and it is sold only on a yes. The same holds for a pet (its growth goes with it) and for any gear that has been Tempered, Fused or Tuned, whatever its rarity: that part is Claude's addition, not the owner's word. The server refuses such a sale that did not come with the confirmation, so the rule does not rest on the window.
- **Who decides:** `PlayerShopping` (Gameplay/Economy) sends requests; the server checks that the player stands by the vendor, the price (`VendorRules`, the same rules the window reads), the wallet, and the room in the Pack before any Creds move. Buying is the sink `CredsSink.Vendor`, selling the source `CredsSource.VendorSale`: both show in `gm.creds.report`. Online only, like Kiosks.
- **A new vendor or a new item on a shelf** is a row in `VendorStockBuilder` (**Krytical Nova ▸ Items ▸ 10. Build Vendor Stock**; a price tuned on an asset is kept) and, for a new vendor, `NpcOffer.Shop` on its row in `Zone01Setup`.
- **The weekly limit** (owner, 17 Oct 2026): a listing may carry a weekly limit (`VendorListing.weeklyLimit` on the vendor's stock asset; 0 = none). The Armorer's Alumite, Iridion, Fusion Catalyst and Tuning Chip have **1**: one of each per character per in-game week; the rest of what the forge uses comes from Hostiles and the Fabricator. A week is `EconomySettings.vendorWeekDays` = **7** in-game days, so seven real hours while a day is one real hour (`DC_Default`); weeks are counted from in-game day 0, so every character's turns over together. The server keeps what each character bought this week (`PlayerShopping`, saved with the character under `vendorWeek`) and tells it only to that character: a purchase is cut to what is left, and one with nothing left is refused with the time until the next week. The shop shows "1 LEFT" on the item, greys it out with "SOLD OUT" once bought, and its tooltip says when more come. GM: `gm.shop.week` (what the targeted player bought, and when the week turns), `gm.shop.week reset`, and two buttons in the GM window's Economy tab. `VendorStockBuilder` writes the limit where an asset has none; one tuned on the asset is kept.
- **Selling is a second way Creds come in**, beside Hostile kills: at 70 % a level-28 Common weapon pays 2,940. To be weighed against the income target when the prices are tuned.

## Appearance services (rows 19 and 52; built 11 Oct 2026)

Five NPCs in the plaza. Code: `Gameplay/Restyle/` (`PlayerWeaveLibrary`, `PlayerWeaves`, `PlayerRestyle`), rules in `WeaveRules` (Items) and `RestyleRules` (CharacterCreation), windows in `UI/Runtime/Restyle/`. Each window opens the equipment page beside it and shows the choice on the doll before anything is paid.

| NPC | What it does | Cost | Where the number is |
| --- | --- | --- | --- |
| **Holoweaver** | Puts a look from the Weave Library (a **Weave**) over the piece worn in a slot: Weapon, Armor, Gloves, Boots, Cape. Its sixth tab, **Morphweave**, puts a **Morph** over the pet that is out. Taking either off again is free. | 5,000 Creds (placeholder), a Weave and a Morph alike | `EconomySettings.weaveApplyFee`, `weaveClearFee` |
| **Outfitter** | Dyes the outfit piece a slot shows in one of the 24 colourways (the gear sets' own themes). Its own colours back: free. Also a shop: one of the four colour cells each in-game day, in turn (1,500 Creds, placeholder). | 1 **Chroma Cell** an outfit piece, 1 **Prism Cell** a weapon | `EconomySettings.dyeItemId`, `weaponDyeItemId`; the shelf is `VS_Outfitter.rotation` |
| **Stylist** | Hair, brows, facial hair, hair colour. | 1 **Restyle Pass**, however much is changed at once | `EconomySettings.restyleItemId` |
| **Reshaper** | Head, eyes, ears, nose, teeth, skin tone, eye and frame colours, scars, eyeliner, grime. | 1 **Restyle Pass** | `EconomySettings.restyleItemId` |
| **Coach** | Build and muscle. | 1 **Training Pass** | `EconomySettings.trainingItemId` |

- **The Weave Library** belongs to the account. A look is collected, by gear line, when its piece is earned (bought from an NPC, taken from a Hostile's drop, granted) or worn. A piece that only passed through a trade, a Kiosk or another player's hands counts once it is worn (`PlayerInventory.Earned`, `WorldDrop.Earned`). A character's gear from before this existed is collected the first time it loads.
- **A Weave must suit what is worn**: the same slot, a look the character's faction and body can use, and for a weapon the same class. It changes what is drawn and nothing else: stats and reach are the worn piece's, and the Temper glow of the weapon really held is drawn on the Weave's model. A Weave stays on the slot when the piece in it changes. A dye is on the piece (`GearItemState.Dye`, 11 Oct 2026): it goes with it into the Pack, a trade, a Kiosk or the Vault, its tooltip says "Dyed <colourway>", a piece put on in its place wears its own colours, and a dyed piece still counts as untouched for Fusion and Reforge. A Weave over a dyed piece is drawn in that dye; a weapon of another class shows itself until the class matches again.
- **A worn piece's tooltip names what its slot shows** (12 Oct 2026): "Weave: Commander" (the set the Weave is from; a weapon's line, which has no set) after "Dyed <colourway>", and "Morph: Tidecaller, Apex" on the pet.
- **Morphweave** (12 Oct 2026, the owner's rules; `MorphRules`, `Design/12_pet_evolution_system.md`). A Morph is the look of one pet at one of its five growth stages. The account earns it when a pet evolves into that stage while out with one of its characters, or is put out at that stage; buying, trading, storing or carrying a pet earns nothing. So a pet bought at 50 % and grown to 100 % gives three Morphs (50, 75, 100 %), never the two before. A Morph goes over a pet of any kind, but not above the stage the pet that is out has reached: the 100 % look needs a 100 % pet under it. Like a Weave it stays on the slot, is silently not shown over a pet that has not grown that far, and changes what is drawn and nothing else: bonus, growth, hunger and XP are the real pet's. A pick is tried on the pet beside the player before it is paid for, since a pet has no doll.
- **A restyle never trusts the request**: the server takes only the fields that service owns out of the look it is sent and keeps the rest of the saved look, then checks the result like a new character's. Faction, lineage, body type and Discipline are never changed.
- **Where the items come from**: a Chroma Cell is crafted at the Fabricator from a Cyan, a Magenta, a Yellow and a Black Cell, and may drop; a Prism Cell is made of the same four and may drop as well; the four colours drop from Hostiles and the Outfitter sells one a day. Restyle and Training Passes are for the Nova Store: today only `gm.passes` gives them.
- **The day** the Outfitter's shelf goes by is the in-game calendar day (`WorldClock.Day`): it turns over at in-game midnight and goes on from one session to the next.

## Crafting at the Fabricator (row 53; built 11 Oct 2026)

One bench, at the Fabricator (who stands at the Elder hall, and takes stat resets and castle entry later). Code: `Gameplay/Crafting/` (`PlayerCrafting`, `CraftRules`, `CraftingSettings`), window `UI/Runtime/Crafting/CraftWindowController`. The recipes are data (`CraftingSettings.asset`, built by Krytical Nova ▸ Items ▸ 11. Build Crafting Recipes, which keeps a tuned recipe).

| Makes | From | Needs |
| --- | --- | --- |
| Iridion (+6 %) | 4 Alumite | |
| Orichalc (+9 %) | 4 Iridion | |
| Eversteel (+12 %) | 4 Orichalc | |
| Chroma Cell | 1 Cyan Cell, 1 Magenta Cell, 1 Yellow Cell, 1 Black Cell | |
| Prism Cell | 1 Cyan Cell, 1 Magenta Cell, 1 Yellow Cell, 1 Black Cell | |
| Backlash / Iron Nerve / Shatterpoint Dossier | its Pages I to IV | level 80 |

- No recipe costs Creds today (`CraftRecipe.creds` is there for when one should). Materials are taken from the Pack, the Vault and the Deep Vault; what is made goes into the Pack, into the room its materials freed.
- **Drops** (`HostileSettings.materialDrops`, each row rolled on its own for every kill, scaled by the level gap and the server drop rate; all placeholders): each colour cell 1.5 %, a Chroma Cell 0.2 %, a Prism Cell 0.2 %, each Dossier Page 0.25 % (owner: rarer than the first 0.5 %; to be tuned) and each Dossier 0.02 %, the last two from Hostiles of level 70 and up.
- **Skill Chips of the two-hit attacks** (17 Oct 2026): 18, ids 9510 to 9527 (`CraftingSettings.TwoHitChipFirst`), one for each class's level 60 and level 80 two-hit attack, on the same drop rules as the other chips of their level; `gm.chips` gives a character its faction's six.
- **A Dossier** is read from the Pack (level 80): it unlocks its skill, which is then learned with skill points like any other.
- **Zenith Dossiers** (11 Oct 2026; Twelve Sky 2: the War God skills): nine, one for each weapon class's Zenith technique (slot 8), with no Pages and no recipe. Each unlocks its own class's technique only (`SkillDefinitionSO.LockKey`; the rank stays the slot's). Drop: 0.02 % from Hostiles of level 113 and up (`CraftingBuilder.ZenithDropLevel`, the first Prime ground), so none drops until Prime grounds exist; `gm.zenith` gives a character its faction's three.

## Sponsor / Recruit (row 17; built 11 Oct 2026)

| | |
| --- | --- |
| Who | A Sponsor is level 112 (the cap; Prime 1 and up once Prime levels exist). A Recruit is of the same faction and **more than 5 levels below** the Sponsor. A Sponsor has up to 3 Recruits; a Recruit has one Sponsor; nobody is both. |
| Asking | "Sponsor Request" in a player's menu, `/sponsor name`, or the name box of the Sponsor window (opened by `/sponsor` or the SPONSOR button in Contacts). Either may ask; their levels decide who sponsors. The other answers on screen within 30 s. Both must be online; a block refuses it like every other request. |
| The bonus | While the Sponsor is within 40 m in the same map and both are alive, the Recruit's Hostile kills pay a bonus (owner, 11 Oct 2026): +10 %; in the Sponsor's Squad +15 % with two of that Sponsor's Recruits in the Squad and near the Sponsor, +20 % with three. In the Sponsor's Squad it adds to the Squad boost (best case +40 %: three Recruits in a full Squad); anywhere else the larger of the two counts. The kill line says "Sponsor +10%", and a System line says when it goes on and off. |
| The Sponsor in a Squad | A Sponsor may Squad with its Recruits whatever their level, and others may join (the level rule leaves the Sponsor out, both ways; the others must still be within 10 levels of each other). It is there to help, not to hunt: a Hostile the Sponsor hits first pays XP to no one, and one 5 or more levels below the Sponsor drops nothing; the Sponsor gets no Squad boost; its Recruits are paid by their own level gap, not the Sponsor's. A member who is not a Recruit gets the Squad boost only. |
| Sponsor points | 250 for each full minute the Sponsor spends near a Recruit, however many are near. Stored and shown; nothing spends them yet. |
| Ending | Either ends it in the window. **It ends itself when the Recruit is within 5 levels of the Sponsor** (owner's rule, 11 Oct 2026). |
| Kept | Links are the world document `world/sponsors`, so they outlive both logging out; the points are the character's. |
| Numbers | `SponsorSettings`: `sponsorMinLevel`, `levelGap`, `maxRecruits`, `experienceBonusSteps`, `addsToSquadBoost`, `sponsorNoDropLevels`, `rangeMetres`, `pointsPerMinute`, `requestSeconds`, `checkSeconds`; the Squad boost is `SquadSettings.maxExperienceBoost` (0.2). The bonus steps are the owner's, the 250 is from the reference. |

## Contacts (row 29)

- **Mutual, per character.** One asks, the other accepts; then each is in the other's list. Removing takes both out. A request waits while either is offline.
- **The window (F):** Contacts (favourites first, then online), Requests, Recent (Squad mates, trade partners, Callout opponents and whisperers of this session), Blocked. Each contact shows level, weapon class and when they were last seen, and has a private note.
- **Block** is one-sided and never announced. It ends the contact and stops whispers, Squad invitations, Callouts, trade, contact requests and Crew invitations in both directions: every one of those asks `SocialGate` first.
- `/contact add|remove <name>`, `/block <name>`, `/unblock <name>`; Add Contact and Block are in the player menu (Shift+click a player).

## Crews (row 33)

- **Founding:** at a Vaultkeeper, level 30, 10,000,000 Creds *(reference page 28)*. A name of 3 to 16 characters, unique, one of 12 symbols and one of 12 colours for it (`CrewSettings.symbolColours`; the Leader can change both later). Every symbol is a white picture, so it takes the colour whole; on the namecard it stands where the faction's emblem would, for a player in a Crew (owner, 11 Oct 2026). A symbol is a picture from the Crew's own faction's Synty interface pack (`CrewSymbolLibrary`, made by Krytical Nova ▸ UI ▸ Build Crew Symbols from the table in `CrewSymbolBuilder`; 12 a faction, never the faction's own emblem). It shows in the Crew window, in the Leader's picker, and before the Crew's name on every member's namecard. Which pictures is the owner's choice from the option sheets of 11 Oct 2026; until he chooses, the 12 a faction are a first pick.
- **Ranks:** one **Leader**, up to 3 **Officers**, **Members**. 20 members at Crew level 1 (the table has later levels; Crews do not level yet).

| | Member | Officer | Leader |
| --- | --- | --- | --- |
| Crew chat (`/g`), leave | yes | yes | yes |
| Invite, expel Members, write the announcement | | yes | yes |
| Appoint and remove Officers, Crew Titles, the symbol, expel Officers, hand over, disband | | | yes |

- A Leader with other members hands the Crew over before leaving; the last member out closes it.
- **The window (G):** roster with rank, title, level, class and last seen; the announcement, which reaches every online member when it changes and is shown at login.
- `/cinvite [name]`, `/caccept`, `/cdecline`, `/cleave`, `/ckick <name>`. **GM:** `gm.guild [name|none]`, `gm.guild.list`.

## Crew Halls: the Hall sandbox (row 62; Stage 1, steps 1 to 3, built 11 Oct 2026)

The plan is `../../Plans/crew-halls-plan.md`: the building is made playable first, the server part after. What is built is a **sandbox**: one Lot with a Hall on it for the character, decided on the machine it is played on (offline or as host) and saved there. A player who has joined someone else's game has their Furnishing Crate but is told the sandbox is the host's for now.

- **The Lot** (`HousingLot`, prefab `HousingLot_Sandbox`, built by **Krytical Nova ▸ Housing ▸ 1. Build Sandbox Lot**): 36 x 48 m inside a fence, a pad of 16 x 14 m for the Hall, a path from the gate. It is set down far from the starter town (`HousingSettings.sandboxOrigin`, x = 6500) when the player goes there, with a NavMesh made on the spot, and removed when they leave. The Hall on the pad is the vendor's own building for the character's faction (`HousingSettings.facadeByFaction`), made smaller when it would not fit the pad.
- **Furnishings are items** (`FurnishingDefinition`, `FurnishingCatalog`, built by **Krytical Nova ▸ Housing ▸ 2. Build Furnishings**): 89 to start with (29 Hegemony from 20001, 27 Null from 23001, 33 Covenant from 26001), each an item with a picture of its model for an icon. How a piece mounts (floor, on a surface, rug, wall, ceiling), whether things stand on it, and its budget cost (1, 2 or 4 by size) are worked out from the vendor prefab. Wall and ceiling pieces wait for the inside of a Hall.
- **Replaced on 12 Oct 2026: only a Facade is its faction's own; furnishings and Finishes are anyone's (see the next section).** As first built: **A faction's furnishings are for that faction only** (owner, 11 Oct 2026; `ItemDefinition.boundFaction`, `ItemFactionRules`). A character can place, crate, be traded, buy from a Kiosk and pick up only its own faction's. A Kiosk shows another faction's furnishing and will not sell it, on neutral ground too. They are kept in the **Vault** and refused by the **Deep Vault**, which an account's characters of any faction share.
- **The Furnishing Crate** (`PlayerCrate`): a character's own store of furnishings, apart from the Pack, counted by kind, saved with the character. A piece set down comes out of it; a piece put away goes back. Right-click a furnishing in the Pack with the window open to store it; right-click one in the Crate to take it into the Pack (Shift: all of them).
- **Arrange** (`ArrangeController`, key **L**): the Crate window. At the Hall, a click on a furnishing puts a preview under the cursor, green where it may stand and red with the reason where it may not; a click sets it down, Shift-click sets down another. Pieces snap to a 0.5 m grid (Ctrl: 0.125 m; Alt: off the grid and free to overlap), turn 15 degrees at a press (Q and E), and small pieces stand on tables and shelves and move with them. A click on a standing piece picks it up; Delete puts it away; Ctrl+Z and Ctrl+Y undo and redo up to 50 steps. While the window is open at the Hall the left button and the hotbar keys are the tool's; the right button still turns the camera.
- **Who decides** (`IHomeAuthority`): the tool asks for every edit and draws what comes back. In the sandbox the authority is local (`LocalHomeAuthority`) and runs the same `HousingRules` a server will: the piece is the player's faction's and is in the Crate, it stands inside the Lot and off the pad and the door's clear space, what it stands on has a top, and the budget is not passed. Two pieces overlapping is the tool's own courtesy, not a rule.
- **Saved** as world document `housing/sandbox/<characterId>`: each piece is its number, what it stands on, its item, a position to 1/64 m from the middle of the Lot, three angles, a size, a colour variant, flags and its owner.

| `HousingSettings` | Value (all first picks) |
| --- | --- |
| `lawnBudget` | 200 points for the two lawns of a Lot |
| `interiorBudgetByTier` | 300 / 450 / 600 (not used yet) |
| `crateStackCap` | 9,999 of one furnishing |
| `gridStep` / `fineGridStep` / `rotateStep` | 0.5 m / 0.125 m / 15 degrees |
| `undoSteps` / `maxStackDepth` | 50 / 3 |

GM: `gm.home`, `gm.home.leave`, `gm.furnish.kit [n]`, `gm.furnish.clear`, `gm.home.reset`, `gm.home.fill [n]`. The GO TO MY HALL button at the foot of the Crate window does what `gm.home` does, until Halls have a way in of their own.

Not built: the inside of a Hall, Finishes and Facades (step 4); Advanced mode, eyedropper, duplicate, group move, the overhead camera (step 5); everything shared (Stage 2: Crew Quarters, Lots claimed by Crews, the Crew Fund, visitors, pieces going back to their owners, a Furnisher, drops). Pieces are walked through. The Lot's ground is plain; the minimap shows nothing useful there; there is no fade on the way.

## Crew Halls: inside, Facades, Finishes, solid pieces and the comfort set (row 62; Stage 1, steps 4 and 5, built 12 Oct 2026)

This section is later than the one above and wins where they differ: the Lot is now **40 x 52 m** with a **20 x 16 m**
pad, there are **223 furnishings** (65 / 70 / 88, the Covenant's with the Viking Realm pack), and pieces are **no longer walked through**.

**Trips.** Going to the Hall, through its door, up or down a stair and back to town are one thing: a curtain closes
(`curtainSeconds` 0.25), the place arrived at is made ready over as many frames as it needs and never in less than
`travelSeconds` (1.2), the player is set down, the curtain opens. Making ready means: the place itself, its
furnishings a few a frame (`warmPiecesPerFrame` 24), the ground to walk on, and one drawing nobody sees so that
meshes, textures and shaders are loaded behind the curtain.

**The inside is an instance.** It does not exist until its player walks onto the door mark at the front of the pad
(`doorRadius` 1.2 m; or GO INSIDE; or `gm.home.inside`), and it is destroyed when they go out. It is made
`roomOffset` (150 m down) from the Lot, which is not drawn meanwhile; nothing falls there whatever the weather.
It has **three floors** one above the other (`floorGap` 1 m): the ground floor, upstairs, the basement, each
`roomSizeByTier` 20 x 15 m and `roomHeightByTier` 6 m at tier 1. Only the floor the player is on is drawn and lit.
A stair is a marked 3 m place (`stairSize`) in a back corner, stepped onto like the door; UP A FLOOR / DOWN A FLOOR
and `gm.home.floor up|ground|basement` do the same. The walls the camera would look through and the ceiling when the
camera is above it are not drawn but still keep the sun out. The three floors share **one budget**
(`interiorBudgetByTier` 400 / 500 / 600) and **one set of Finishes**.

**What a Hall wears.** One Facade and three Finishes (walls, floor, ceiling). Each is an item kept in the
Furnishing Crate (15 Facades, 35 Finishes); a click on one at the Hall makes the Hall wear it and puts the one
it replaces back in the Crate. Each faction has a standard look for each of the four, which every Hall has and no
Crate holds; the four buttons under the Crate's grid put a standard back. Item ids: Finishes 29000 to 29499,
Facades 29500 to 29999. A Facade is stood at the front of the pad with its door to the gate, at up to 1.5 times the
vendor's size (the scale standard's "heroic").

**Solid pieces.** A furnishing that stands on the floor and is taller than `blockHeight` (0.35 m) stops a walker,
with the part of its mesh between the ground and `walkBand` (2.1 m): a tree's trunk, not its crown. The walkable
ground is cut away under it, so a click behind a table finds the way round. Rugs, flowers, arches and what hangs
stop nobody. A piece is refused where the player stands (`standClear` 0.45 m), the door and the stairs are kept
clear, and BACK TO THE DOOR gets a player out of a corner they have built shut. Tall pieces between the camera and
the player fade like any other prop.

**Hanging.** A wall piece is set on a wall of the floor the player is on, its back against it, on the grid along
and up the wall; Q and E set it askew. A ceiling piece hangs above the place pointed at. Set down freely (Alt, or
Advanced) either may hang anywhere. Lamps carry a light: the `interiorLights` (10) nearest the camera are lit
indoors, `lawnLights` (4) on the lawns at night.

**Arrange, the comfort set.** A click on bare ground walks there. Shift-click or a dragged box picks several pieces
(`maxGroup` 60), a click on one of them moves them as one (Q / E turn them), Delete puts them away. **D** takes
another like the piece under the cursor. **Tab** is Advanced: pieces are set down freely, and the piece in hand or
the one piece picked is lifted (`liftStep` 0.25 m, PgUp / PgDn), tilted and leaned (`tiltStep` 15 degrees) and
sized (`scaleStep` 0.125 between `minPieceScale` 0.5 and `maxPieceScale` 2, the brackets); Ctrl makes the steps
fine. **V** looks straight down (arrow keys move, the wheel brings it nearer). Middle-click marks a favourite;
the Crate also lists the last sixteen used. PUT ALL AWAY asks twice. Undo and redo are kept per floor. A window key
pressed with Ctrl no longer opens its window, so Ctrl+Z undoes without starting Autopilot.

**Who may deal with whom (owner, 12 Oct 2026; this replaces the faction row for furnishings in the table above).**
Only a **Facade** is its faction's own, so that every Hall fits the Crew Quarter it stands in: it is never traded
to, sold by a Kiosk to or picked up by another faction, the Deep Vault refuses it, and a Hall of another faction
cannot wear it. **Furnishings and Finishes are anyone's**: a furnishing's faction is the style it is made in and
filed under, and a player may own, trade, store and set down any of them, indoors and on the lawns. A new Hall
starts with its faction's standard looks (Hegemony: Long Hab, Hull Plating walls, Deck Plates, Deck Head; Null:
Grim Manor, Fortress Stone, Flagstones, Stone Vault; Covenant: Longhouse, Elven Hall, Elven Paving, Oak Rafters),
chosen by the owner from lettered sheets. `gm.furnish.kit 5 all` fills a Crate with every style.

**Checked, 12 Oct 2026:** 718 EditMode tests pass (30 for housing). In Play as a Hegemony host on a test port with
a scratch save: the trip to the Hall took 3.5 s; a table lengthened the walk past it from 10.0 to 10.5 m; a Facade
was changed; the trip inside took 1.7 s with no frame over 36 ms and made three floors; a doorway, a stair, a wall
piece away from a wall, a ceiling piece at head height and a tree indoors were each refused with their reason; a
Finish was changed; upstairs and the basement were reached and furnished; going out left no room behind and the
layout was saved. **Not checked:** real mouse and key play (the tool's clicks, drags and keys were not driven), a
second client (its Editor was stopped on a dialog), and a built game.

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

## Squad: following a member (row 16)

- **Follow** is in the player menu of a Squad member (Shift+click them, MORE on the target strip, or a click on their Squad frame, which now opens that menu) and on `/follow [name]` (no name: the selected player). The character walks after the member by itself and stops a short way off.
- The server does it (`PlayerFollow`, rules in `FollowRules`): it moves every player and knows where each one is however far apart they are, so following works beyond what the follower's own game can see. It walks through the player's own move order (`PointAndClickPlayer.ServerMoveTo`): the way taken, vaults and the Flux a run costs are those of a click, paid once each time the follower sets off, not at every look.
- It **ends** when the follower gives a move order of their own (a click, a jump, map travel, Escape on a journey), fights (strikes, casts a skill, is struck, or is in a Callout), or is defeated; when the two are no longer in one Squad; when the member leaves the map (their player is gone: there is one map today); or when the member is more than the maximum distance ahead. The follower is told why in the System channel.
- **Jumps are copied (15 Oct 2026).** When the member jumps, the server tells the follower where they took off and landed (`PlayerJumpMotor.JumpStarted`). The follower walks to the take-off and, within 0.8 m of it, jumps to the landing with its own Jump (`PointAndClickPlayer.ServerJumpTo`: the follower's rank, Flux and arc, the same layers a click uses), so two cross a river together. Up to 4 jumps are remembered, each for 15 s; one the follower is already past is forgotten. A copied jump is not a move order of the follower's own, so following goes on; a jump the follower cannot make (Jump not learned, no Flux, too far for its rank) ends following with the reason. While a jump waits to be copied the member may be far across the water: the distance limit is not applied until it is done.
- **Numbers** (`SquadSettings.asset`): stand 3.5 m behind (owner, 15 Oct 2026: three to four), set off again once the member is 5 m away, lost beyond 60 m, a look every 0.4 s; a jump is copied within 0.8 m of the take-off, 4 remembered, 15 s each.

## Autobuff (row 9; built 15 Oct 2026)

A licence by the clock, like a Kiosk's, that buffs for the player: up to eight buff skills are slotted in the Autopilot window (two rows of four cells; a learned buff is dragged in from the Arsenal, the Hotbar or another cell; right-click clears), and the **AUTOBUFF** tile dragged to a Hotbar key casts every slotted buff that is down, one per action tick at Autopilot's own pace (0.3 to 0.5 s), through the ordinary cast path: its rank, its Flux, its cooldown, nothing a hand press would not do. Autopilot keeps the buffs up through it, in place of the two buff rows it had. It works with or without Autopilot time; with no running licence the tile is greyed and says so.

| Autobuff Pass (item) | Adds | Rarity | Where from | Nova Store |
| --- | --- | --- | --- | --- |
| Autobuff Pass (3 Days), 9031 | 3 days | Uncommon | Hostiles of level 10 and up, 0.3 % (placeholder); the store | $0.99 (placeholder) |
| Autobuff Pass (14 Days), 9032 | 14 days | Rare | the store | $2.99 (placeholder) |
| Autobuff Pass (30 Days), 9033 | 30 days | Epic | the store | $4.99 (placeholder) |

- The days count by the wall clock whether the character plays or not (`AutobuffRules.Extend`: a running licence is lengthened from its end, one that ran out starts from now). At most **90 days** may be held: a Pass past that is refused, not wasted. Numbers in `AutopilotSettings` (`autobuffTiers`, `autobuffMaxDays`).
- The server keeps the expiry and the eight cells, saved with the character (`PlayerAutobuff`, key `autobuff`); the owner's game presses the buffs (`AutobuffCaster`). The 14 and 30-day Passes share the 3-day one's picture until they are given their own in the Icon Library.
- GM: `gm.autobuff.days <days>` (0 ends it), `item.give "Autobuff Pass (3 Days)" [n]`, and buttons in the GM window.

## Autopilot Passes (row 9; built 17 Oct 2026)

Autopilot time, on the same clock as the Autobuff (owner, 15 Oct 2026; built 17 Oct 2026). It replaced the Autopilot Chip, which added 5 hours that ran only while Autopilot was on; a chip's hours were not carried over (no players yet), and the chip's item became the 3-day Pass.

| Autopilot Pass (item) | Adds | Rarity | Where from | Nova Store |
| --- | --- | --- | --- | --- |
| Autopilot Pass (3 Days), 9030 | 3 days | Uncommon | Hostiles of level 10 and up, 0.3 % (placeholder); the store | $0.99 (placeholder) |
| Autopilot Pass (14 Days), 9034 | 14 days | Rare | the store | $2.99 (placeholder) |
| Autopilot Pass (30 Days), 9035 | 30 days | Epic | the store | $4.99 (placeholder) |

- The days count by the wall clock whether Autopilot is on or off and whether the character plays or not (`PassClock.Extend`, shared with the Autobuff). At most **90 days** may be held: a Pass past that is refused, not wasted. Numbers in `AutopilotSettings` (`autopilotTiers`, `autopilotMaxDays`).
- Magnet needs Autopilot time too (see "Item pickup"). The server keeps the expiry, saved with the character (`PlayerAutopilot`, key `autopilot`: the expiry and the player's choices) and told to its owner only. The 14 and 30-day Passes show the 3-day one's picture.
- GM: `gm.autopilot.time <days>` (0 ends it), `item.give "Autopilot Pass (3 Days)" [n]` (14, 30), and buttons in the GM window.

## Serums (row 49)

Twelve Sky 2's Elixirs: a consumable that raises a stat for good, each kind a limited number of times per character.

| Serum (item) | Each one gives, for good | TS2 |
| --- | --- | --- |
| Strength Serum (9200) | +3 Attack Power | STR Elixir, +3 ATK |
| Vitality Serum (9201) | +20 Max HP | VIT Elixir, +20 Health |
| Agility Serum (9202) | +2 Hit Rate, +2 Evasion | AGI Elixir, +2 Hit, +2 Dodge |
| Spirit Serum (9203) | +25 Max Flux | Sprite Elixir, +25 Force |
| Elemental Strike Serum (9204) | +10 Elemental Attack | A.ATK Elixir |
| Elemental Ward Serum (9205) | +10 Elemental Defense | A.DEF Elixir |

- **Numbers** (`Gameplay/Progression/Data/Assets/SerumSettings.asset`): the kinds and what each gives; `baseCap` 100 of each kind per character; `expansionCap` 200 with `expansionEnabled` off (the later system; TS2 allows 200 and then 200 more Expansion elixirs). All Rare, stack 99, 0.25 s between two uses.
- **Using one** is an ordinary item use from the Pack (right-click). `PlayerSerums` is the `IItemUseHandler` for `ItemUseEffect.Serum`: at the cap it refuses, and the inventory then takes nothing from the Pack. The owner is told the new count.
- **What is kept** is a count per kind (`SerumState`), saved with the character under `serums` by item id, and replicated to everyone; each machine turns it into flat stat modifiers (`SerumRules.AddModifiers`), so it is in every total a stat shows and survives a stat reset. A Serum raises the stat it names, not an attribute.
- **Shown**: the item tooltip ("+3 Attack Power each, for good", "Taken 37 / 100  ·  +111 Attack Power so far"), and the Character Sheet, whose last section (SERUMS, under the stats) lists every kind: its name with "37 / 100", and under it the total it adds ("+111 Attack Power"). Every kind is listed, from the start: one not taken yet reads "(0/100)   +0 ..." (9 Oct 2026: the section was hidden until the first Serum, and was missed).
- **GM**: `gm.serums [count]` gives some of each; `gm.serum <kind> <taken>` sets a count; `gm.serum` lists them.
- **Not built**: any source (GM only); the Expansion tier's own items; TS2's take-ten and take-all items. In a Kiosk a Serum is capped like any stackable item (`kioskStackablePriceCap`).

## XP boosters (Nova Store stock; built 11 Oct 2026)

Three items, used from the Pack. Each has a clock of its own; while a clock has time left its share is added to the XP of every Hostile kill.

| Item | Adds | One lasts |
| --- | --- | --- |
| Field Booster (9500) | +50 % character XP (x1.5) | 60 min |
| Companion Booster (9501) | +100 % pet XP (x2) | 60 min |
| Tandem Booster (9502) | +50 % character XP and +50 % pet XP (x1.5 each) | 60 min |

- **They add up** (owner, 11 Oct 2026): Field and Tandem together are character XP x2.0, Companion and Tandem pet XP x2.5; never multiplied with each other. They also add to the Squad and Sponsor boosts: a kill pays the Hostile's XP x level gap x (1 + Squad or Sponsor boost + boosters) x the server rate.
- **The same one again adds its hour** to what is left, up to `boosterMaxHours` (24) of that booster; one more than that is refused and stays in the Pack.
- **Numbers** (`EconomySettings.boosters`: item, character %, pet %, minutes; `boosterMaxHours`; `boosterTimeRunsOnlyOnline`, on: a clock runs only while the character is in the game, which is my pick). The names, the 24 hour limit and the icons are my picks too.
- **Code**: `Gameplay/Progression/Runtime/PlayerExperienceBoosts` (the clocks, saved with the character under `xpboosts` by item id, the time left told to the owner; the `IItemUseHandler` for `ItemUseEffect.ExperienceBoost`) and `ExperienceBoostRules` (the sums). Character XP: `HostileKillRewards.Pay`. Pet XP: `PlayerPet.ServerCreditHostileKill` (a pet's kill XP had no multiplier before).
- **Shown**: a badge for each running booster after the buffs (its icon and the time left; what it adds in the badge's tooltip), the item's tooltip (what one adds, for how long, and the time running), and the combat log line of a kill ("· Booster +100%").
- **Sold nowhere yet**: they are Nova Store stock (row 32). Until the store is built: `gm.boosters [n]` gives n of each, `gm.boost` prints what runs, `gm.boost.clear` ends them; the GM Tools window has the same three buttons on its Items tab.
- **Verified** with a second client (11 Oct 2026): a level-50 Hostile paid a level-50 character 104 XP with no booster, 156 with Field, 208 with Field and Tandem; a pet's kill was worth 4 units, 8 with Companion, 10 with Companion and Tandem; a second Field made 2 h; the clocks came back after leaving and entering the game.

## The ticker: news for the whole server

- Since 15 Oct 2026 (owner) the news runs on a **ticker**: a thin band across the top of the HUD on which the line runs right to left **three passes** (`ToastSettings.passes`, at 160 px/s), so whoever missed it the first time reads it the next. The kind is a tag at the front of the line in its colour (GM gold, TEMPERING orange) and the names the news is about (the player, the piece of gear) are lit in the same colour: the server sends them with the text (`IAnnouncementSink.Announce`'s `highlight`, "|" between them). The band fades in and out over 0.35 s and takes no room while there is nothing to say. Before that it was a toast, a short panel shown once for 5 s; the rest of this section still holds.
- **What it carries (15 Oct 2026, owner):** a GM message (gold); a piece tempered to 120 % (**red** for an Epic or better piece, **orange** for a Rare or below: its name, the word "tempered", the tag and the frame); and an **Epic find**, an item of Epic rarity or above that falls from a Hostile, a boss or another source: "Arinis found Sunlord Blade." under an EPIC FIND tag with the item's name in purple, a Legendary one in ember. The rarity travels with the announcement (`IAnnouncementSink.Announce`'s `rarity`, `AnnouncementKind.Drop = 2`). A source calls `LootAnnouncements.ServerFound` once for each item it hands out; Hostile kills do (gear and materials). `HostileSettings.announceDrops` switches it, `announceDropsFrom` is the lowest rarity (Epic).
- A **toast** was a short panel near the top of the screen, in the player's faction skin, for news every player on the server is told. Two things use it: a **GM message** (`gm.say "text"`, or the host's own `/gm text`) and a piece of gear **tempered to 120 %** (the player's and the item's name).
- The toast is added to the chat line, which stays as it was: the server sends both in one message (`PlayerChat.ServerAnnounce` → `AnnounceRpc`, to each player on their own player object, so distance does not matter), and the player's game puts the line in chat and hands the same text to `ClientPresentationRegistry.Announcements` (`ToastHudController`).
- One at a time: a toast waits until the one on screen has had its time and faded (`ToastQueue`). **Numbers** (`UI/HUD/Toast/ToastSettings.asset`): 5 s on screen, 0.35 s fade, 8 may wait (one more and the oldest waiting is dropped). The Temper that is announced is `temper.announceFrom` in `GearProgressionSettings.asset`.
- The older one-line notice in the middle of the screen (`IGameMessageSink`, the zone toast) is a different thing: the player's own notices, no queue. It is unchanged.

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
| Sponsor / Recruit | yes | **never** |
| Picking up a dropped item (after its 20 s) | yes | yes |
| **A faction's own item** (a Facade; since 12 Oct 2026 furnishings and Finishes are anyone's): trading it, buying it from a Kiosk, picking it up | yes | **never**, on neutral ground too; a Kiosk still shows it |

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
| **F** | Contacts | | **J** | Kiosk (was **T** until 17 Oct 2026) |
| **G** | Crew | | **~** | Pick up the nearest item |
| **B** (held) | Emote wheel | | **Esc** | Game menu (when nothing else is open) |
| **L** | Arrange (the Furnishing Crate) | | | |

No new keys came with the systems of 11 Oct 2026: the Holoweaver, Outfitter, Stylist, Reshaper, Coach and Fabricator windows open from their NPCs, the Sponsor window from `/sponsor` and from Contacts.

| GM command | What it does |
| --- | --- |
| `gm.weave [off]`, `gm.dye` | Opens the Holoweaver / the Outfitter's dye window here; both then work with no NPC near |
| `gm.weave.unlock <all\|morphs\|item>`, `gm.weave.clear`, `gm.weave.list` | The Weave Library (every gear look, every Morph, or one item's look) and what each slot and the pet show |
| `gm.restyle <stylist\|reshaper\|coach\|off>` | Opens that window here |
| `gm.passes [n]` | Chroma Cells, Prism Cells, Restyle Passes and Training Passes |
| `gm.craft [off]`, `gm.craftkit [times]`, `gm.dossier` | The Fabricator here; the materials of every recipe; the three Dossiers |
| `gm.sponsor [clientIdA clientIdB]`, `gm.sponsor.end`, `gm.sponsor.points [n]`, `gm.sponsor.window` | Links two players whatever the rules say (the higher sponsors); with no ids prints the links |

Every one of them has a button in the Editor's GM Tools window (Items and Social tabs).

## Not built

The Exchange (decided 15 Oct 2026: a search over every open Kiosk at the Broker, buying at a distance for a commission); selling Kiosk Permits (Nova Store); Contracts as a Creds faucet; every shop but the Apothecary's and the Armorer's (Store Clerk, Fixer, Instructors), a source for the forge materials the Armorer does not sell; Crew levels; the Crew Title and symbol on the namecard (the Crew's name is there); any Renown source; emotes for two players; handing a session on when its host leaves; trade zones in faction Home Cities; the dedicated non-combat zone; real sign-in.

## Verified

EditMode tests for every rules class (242 pass on 5 Oct 2026). Host plus a Multiplayer Play Mode client, two factions: pickup and its gesture, drop hold, Vault, Trade, Kiosk (open, buy, tax, away summary, expiry, collect), Contacts (request, offline request, favourite, note, block), Crew (found, ranks, title, announcement, chat, hand over, close), and the neutral-ground rules. Saves survive a restart.

**11 Oct 2026.** 555 EditMode tests pass (new: `ItemCostRules`, `WeaveRules`, `RestyleRules`, `CraftRules`, `SponsorRules`, the rotating shelf and material drops). Host plus a Multiplayer Play Mode client on a test port with a scratch save: a craft took two Alumite from the Pack and two from the Vault away from any NPC; a Weave on armour and on a weapon, and a dye, showed on the other machine, with the reach and the Temper glow of the weapon really held; the client's own requests (dye, Coach, crafting) went through and the Coach could not change scars; the client's look followed into its roster; the Sponsor bonus went off when the Sponsor walked away and on again when they came back; a Dossier unlocked its skill; all six windows opened in the Hegemony skin. Not run in Play: buying from the Outfitter at its stall, a drop from the material table, and the fix for the client's Kiosk window made after this pass. A second pass the same day, after the dye moved onto the piece and the Sponsor's Squad rules came in (EditMode 646 of 646; the same two Editors on port 7788): a dye asked for by the host and by the client went onto the worn piece, used one Chroma Cell, left the slot when the piece came off and came back with it, and each machine saw the other's; a Recruit near its Sponsor had +10 %; in a Squad with it (put together by hand on the server, the two test characters being of different factions) a Hostile the Recruit killed paid 46 XP against 40 plain, the 10 % and the Squad's 5 % together; a Hostile the Sponsor killed paid XP to neither and dropped nothing. A third pass for the weapon dye (EditMode 654 of 654): a weapon dyed by the host used one Prism Cell and no Chroma Cell, its armour one Chroma Cell and no Prism Cell; the weapon showed its repainted material on the host and on the client, lost it when it came off and had it again when it went back on; the client's own weapon dye went through. Not run: a Squad invitation between a Sponsor and a Recruit of one faction, the +15 % and +20 % steps, the "Dyed" line of the tooltip, a dyed piece through a trade or a Kiosk.

**12 Oct 2026, deeper weapon dye, the Weave on the tooltip, Morphweave.** 709 EditMode tests pass (15 new: `MorphRules`, the looks everyone sees carrying a Morph, a Weave's name, second metals, gold as trim, lights, dark parts). Host plus a Multiplayer Play Mode client on test port 7788 with a scratch save: a Kitsune Spirit handed on at 50 % and put out gave its 50 % Morph only, and growing it to 100 % (GM command) the 75 and 100 % ones, never the two before; the Tidecaller's 100 % Morph went on for 5,000 Creds and both machines drew the Tidecaller while the bonus stayed the Kitsune's Attack Power; a new Emberwing put out showed itself and the Morph was kept; the reasons a Morph is refused read as written; the Morph, the library and a dyed, woven weapon were all there again after leaving and rejoining; the tooltip lines read "Dyed Deathsworn · Weave: Training Blade", "Weave: Recruit" and "Morph: Tidecaller, Apex"; the client drew the host's weapon with its dyed material. The Morphweave tab was looked at in the Hegemony skin. Found and fixed on the way: swapping one pet for another of the same kind gave the pet that came out the growth and hunger of the one put away (a 100 % Kitsune became 0 %); the pet system now reads the pet again when the worn item's state is not what it last wrote. Not run: a Morph bought with a real click at the Holoweaver NPC (the window was driven by its own methods, with `gm.weave`), the other two faction skins, a Morph tried by a client rather than the host.

**11 Oct 2026, Crew Halls.** 688 EditMode tests pass (18 new: setting down, moving, stacking, the budget, the faction rule, the grid, overlapping footprints, undo and redo). Host plus a Multiplayer Play Mode client on test port 7788 with a scratch save, a Hegemony host and a Null client: the host's Crate filled with its 27 furnishings, the trip to the Lot and back on walkable ground, a table set down through the tool on the grid, a monitor stood on it, refusals for a piece in the way, on the pad and outside the Lot, the table moved with the monitor following, undo, redo, put away and undo of the put away with the monitor back on the table, the layout and the Crate still there after leaving the Lot and after a new Play session, 73 pieces on the lawns at the same frame time as none. The client's Crate arrived over the network, took 2 into the Pack and 1 back, and the sandbox refused the client with its notice. The steps of the tool were driven by calling its own methods: the Editor was not the focused window, so **real mouse clicks, the keys and dragging from the Pack were not exercised**, and neither were the trade, Kiosk, Deep Vault and pickup refusals for another faction's furnishing.
