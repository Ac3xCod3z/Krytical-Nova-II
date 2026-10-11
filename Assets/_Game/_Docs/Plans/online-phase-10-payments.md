# Online phase 10: payments (the Nova Store)

Part of `online-roadmap.md`; the money rules are in `online-security-model.md`. Written 11 Oct 2026.
**Not built**, and not started until the game is ready to sell. Needs Phase 9.

This is engineering guidance, not legal or tax advice. Selling to the public needs a business that can
take money, tax handled in each place sold to, and terms checked by a qualified person.

## Goal

A player buys something for real money and receives it in game, once, without a card number ever
touching our game or our servers.

## What the design already says

- Build list row 32: the **Nova Store** (Twelve Sky 2: Item Mall) is a real-money storefront at the Store
  Clerk only. Ascension and Nova Standings are earned at the Herald and are not sold.
- Kiosk Permits are the first priced goods: 14 days $1.99, 30 days $3.99 (`05_Economy_And_Social.md`).
- Open: whether Autopilot Chips are free, earned or sold.

## Two rails, one grant

| | Steam build | Website build |
| --- | --- | --- |
| Who takes the money | Steam (the player's Steam Wallet or card, in Steam's own window) | Stripe, as merchant of record, through Unity In-App Purchasing 5.4+ (hosted checkout in the browser), or the shop on our website |
| Who handles cards, tax, fraud | Steam | Stripe |
| What we receive | Steam tells our service the order is paid | Stripe sends our service a signed message that the order is paid |
| What we never receive | A card number | A card number |

Whichever rail, the grant is the same and happens in one place, the world service:

1. The game asks our service to start an order for this account and this item.
2. The player pays in the payment company's window.
3. The payment company tells **our service** (not the game) that the order is paid, in a message we can
   verify.
4. The service records the order as paid, once. A second message for the same order does nothing.
5. The goods are added to the account's delivery box, and the map server hands them over at the Store
   Clerk.
6. A refund or a chargeback arrives the same way and takes the goods back, or marks the account when
   they are already used.

## Rules

- No payment key, and no way to mark an order as paid, exists in the client build.
- The game client saying "I paid" grants nothing.
- Prices and the catalogue live on the service; the client only displays them.
- Every order and every grant is a line in the ledger (Phase 4).
- Purchases are refused for accounts that have not accepted the current terms.
- **Do not use an asset-store payment kit that shows a card form inside the game or ships a secret key
  in the build.** If a kit is used at all, it must be one that only opens the payment company's hosted
  page.

## Things to confirm when this phase starts

1. Steam's current rule for purchases inside a game sold on Steam (the expectation is that they go
   through Steam's own system) and its fee.
2. Whether Unity In-App Purchasing's in-game web checkout supports a Windows desktop build. On
   11 Oct 2026 its documentation listed phones for the in-game flow and described the browser webshop as
   cross-platform. If it does not, the website shop is the website build's rail.
3. Whether one account may buy on both rails, and how prices are kept the same.
4. What may be sold at all: the law on paid random rewards differs by country. The design sells fixed
   goods only (permits, convenience items); keep it that way unless reviewed.
5. Age rules for purchases.

## Work items

1. The catalogue and orders on the world service; the delivery box; the ledger lines.
2. The Store Clerk's window in each faction's skin.
3. The Steam rail.
4. The website rail.
5. Refund and chargeback handling.
6. A test mode on both rails (both companies provide one) and a full buy, refund and re-buy run before
   any real money.

## Done when

- A test purchase on each rail delivers once; replaying the paid message delivers nothing more.
- A refund removes the goods.
- A modified client that claims a purchase receives nothing.
- No secret is found by searching the client build.
- The terms, the refund policy and the business side have been reviewed.
