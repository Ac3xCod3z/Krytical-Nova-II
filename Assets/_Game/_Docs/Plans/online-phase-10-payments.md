# Online phase 10: payments (the Nova Store)

Part of `online-roadmap.md`; the money rules are in `online-security-model.md`. Written 11 Oct 2026,
rewritten the same day for the owner's decision on how the store is reached. **Not built.** Real money
is not taken until the game is ready to sell (after Phase 9), but most of this can be built and tested
long before that with pretend payments: see "Building and testing without real money".

This is engineering guidance, not legal or tax advice. Selling to the public needs a business that can
take money, tax handled in each place sold to, and terms checked by a qualified person.

## Decided (owner, 11 Oct 2026)

- The Nova Store is reached from a **stall in the game**: the player clicks the Store Clerk and the Nova
  Store opens **in the browser**.
- All spending is handled and settled there, not in the game.
- What was bought is sent to the buyer's **Pack**, or to their **Vault** when the Pack has no room.

This settles the open point about Unity's in-game checkout on a PC build: it is not needed. A page in the
browser works on any PC.

## How it works

1. **The stall.** The Store Clerk (`NpcRole.MallManager`, NPC id `MallManager`, the holo booth in the
   plaza) gets a new offer, Nova Store, beside the existing ones in `NpcOffer`.
2. **The link.** The player's game asks the server for a store link. The server makes one that says
   "this account, this character", works once, and runs out after a few minutes. The game opens it in
   the player's browser. The link signs the player in to the store only; it cannot be used to enter the
   game, and no password is typed anywhere.
3. **The store page.** It shows the catalogue with prices that come from our service, never from the
   game or the page itself. The player picks what they want.
4. **Paying.** The page hands the player to the payment company's own checkout page (Stripe's hosted
   checkout). The card is typed there. We never see it.
5. **The paid message.** The payment company tells **our service** that the order is paid, in a signed
   message. The browser coming back to our page proves nothing and grants nothing.
6. **Delivery.** The service records the order as paid, once, and tells the map server the character is
   on. Each item goes to the **Pack**; if the Pack has no room, to the **Vault**; if neither has room,
   it waits at the Store Clerk and nothing is lost. A toast and a System chat line say what arrived and
   where it went.
7. **Not in the game at that moment.** The order waits, and is delivered the next time that character
   enters the world.
8. **Refunds and chargebacks** arrive as signed messages too. An unused item is taken back; a used one
   marks the account for a GM to look at.

## Rules

- No payment key, and no way to mark an order as paid, exists in the client build.
- The game client saying "I paid" grants nothing. Only the signed message does.
- A grant happens once. A second message for the same order does nothing.
- Prices and the catalogue live on the service. The page and the game only display them.
- The store's address is fixed in the build. Players are told the store is only ever opened from the
  Store Clerk, so a link sent in chat is never the store.
- Every order, grant, refund and taken-back item is a line in the ledger (Phase 4).
- Purchases are refused for accounts that have not accepted the current terms.
- Faction-bound goods (a faction's furnishings) follow their own rule: the Vault, never the Deep Vault.
- **Do not use an asset-store payment kit that shows a card form inside the game or ships a secret key in
  the build.** Opening the payment company's hosted page is the only shape allowed.

## What is sold first

Fixed goods only, no paid random rewards. Already existing as items: Kiosk Permits (14 days $1.99,
30 days $3.99: `05_Economy_And_Social.md`), Restyle Pass and Training Pass, the three XP boosters (Field,
Companion, Tandem: build list row 32). Open: whether Autopilot Chips are sold. Ascension and Nova
Standings are earned at the Herald and are never sold.

## Steam

On the Steam build the same stall is used, but purchases made inside a game sold on Steam are expected
to go through Steam's own purchase window, not an outside web page. So the Store Clerk opens Steam's
window there, and Steam (not Stripe) tells our service the order is paid. Everything from step 5 on is
the same. To confirm against the Steamworks rules when this phase starts, with Steam's fee.

## Building and testing without real money

The store is built in four stages. Each one can be played and tested, and none of the first three moves
a cent. The same order, delivery and refund code runs in all four; only where the "paid" message comes
from changes.

| Stage | Where the "paid" message comes from | What it proves | Needs |
| --- | --- | --- | --- |
| **1. Pretend, in the game** | A GM command: `gm.store.order`, `gm.store.pay`, `gm.store.refund` | Orders, delivery to Pack then Vault then the stall, delivery to a player who is away, once-only grants, taking an item back | Nothing beyond today's game. Can be built now, with one player hosting |
| **2. Pretend store page** | A test page of our own, run on the owner's PC, with buttons instead of a card form | The whole trip: stall, link, browser, pay, signed message, delivery | A small store service on the PC |
| **3. The payment company's test mode** | Stripe's test mode: its real hosted checkout, test keys, and its published test card numbers | That our handling of their real messages is right: paid, declined, refunded, disputed | A Stripe account in test mode (free; no bank details needed to test) |
| **4. One real purchase** | A real card, a small amount, by the owner, refunded afterwards | That the live keys and the live address are set up right | The business side in place |

**Stage 1: pretend, in the game.** Built on the host game as it is today.
- The catalogue as a settings asset (`NovaStoreSettings`: item, price, how many, whether faction-bound).
- Orders as a world document (`world/orders`): order id, account, character, items, state (opened,
  paid, delivered, refunded). `ServerSave.EditOfflineAccount` already lets the server change an account
  that is not loaded, which is what delivery to an absent player needs.
- `StoreDeliveryRules` (pure, tested like `TradeRules`): given the Pack, the Vault and the items, decide
  where each goes.
- The GM commands stand in for the payment company.
- The Store Clerk's window can show the catalogue and a "pretend pay" button in the Editor only.

**Stage 2: pretend store page.** The first real web piece, still entirely on the owner's PC.
- The store service: makes the one-time link, serves the store page, opens orders, receives "paid"
  messages and checks their signature.
- A **pretend payment page** in place of Stripe, with one button for each thing a real payment can do:
  Pay, Decline, Pay and close the tab, Pay twice (sends the message again), Pay then refund, Pay then
  dispute, Send a forged message. It signs its messages with a test secret exactly as the real one
  will, so the receiving code is the code that ships.
- The game opens the link with the system browser.

**Stage 3: Stripe test mode.** Swap the pretend payment page for Stripe's hosted checkout using test
keys. Stripe publishes card numbers that only work in test mode (one that always pays, ones that are
declined, ones that need extra confirmation). Its command-line tool forwards its messages to a service
running on the owner's PC, and can fire a refund or a dispute on demand. No bank, no real card.
Steam has the same thing for its own purchases: a sandbox for in-game purchases, used with the Steam
build.

**Stage 4: one real purchase**, then a refund, before the store is opened to anyone.

### What every stage is tested against

| Case | Expected |
| --- | --- |
| Paid, Pack has room | Item in the Pack; toast and System line |
| Paid, Pack full | Item in the Vault; the message says so |
| Paid, Pack and Vault full | Item waits at the Store Clerk; collected later; nothing lost |
| Paid while the player is offline or on the title screen | Delivered when that character next enters the world |
| Account with two characters | Delivered to the character that opened the store, not the other |
| The same "paid" message twice | One delivery |
| Player closes the browser right after paying | Still delivered (the message, not the return, grants) |
| Player returns to the page without paying | Nothing delivered |
| A "paid" message with a wrong signature | Refused and logged; nothing delivered |
| Declined card | Order stays unpaid; nothing delivered; the page says so |
| Link opened twice, or after it ran out | Refused; the player is told to visit the Store Clerk again |
| Link opened by someone else | They can buy for that character and nothing more: no game access, no personal details shown |
| Price or item changed in the browser | Ignored: the service charges and grants from its own catalogue |
| Refund, item unused | Item removed from wherever it is; ledger line |
| Refund or dispute, item already used | Account marked for a GM; ledger line |
| Server stopped between "paid" and delivery | Delivered after restart, once |
| Faction-bound item, Pack full | Vault, never the Deep Vault |
| A modified client claims a purchase | Nothing |
| 100 orders by bot clients at once | All delivered once; ledger balances |

## Work items

1. `NovaStoreSettings`, the orders document, `StoreDeliveryRules`, the delivery component on the player
   (a row in `PlayerSystemsSetup`), the Nova Store offer on the Store Clerk, the GM commands. (Stage 1)
2. The store service: one-time links, the store page, orders, the signed-message receiver. It becomes
   part of the world service when that exists. (Stage 2)
3. The pretend payment page. (Stage 2)
4. The Stripe adapter and the test-mode run. (Stage 3)
5. The Steam adapter and its sandbox run.
6. Refund and chargeback handling; the GM view of an account's orders.
7. The Store Clerk's window and the stall's collect list in each faction's skin, keyboard prompts only.
8. The client-to-server calls this adds (ask for a store link, collect from the stall) go on the audit
   list of `online-security-model.md`.

## Things to confirm when real money is near

1. Steam's current rule and fee for purchases inside a game sold on Steam.
2. Whether one account may buy on both rails, and how prices are kept the same.
3. What may be sold at all: the law on paid random rewards differs by country. Keep to fixed goods
   unless reviewed.
4. Age rules for purchases; the refund policy; who the seller of record is in each country.

## Done when

- Every case in the table passes at stages 1, 2 and 3.
- A search of the client build finds no payment key.
- One real purchase is delivered and refunded.
- The terms, the refund policy and the business side have been reviewed.
