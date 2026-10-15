# Stripe readiness for the Nova Store

Companion to `online-phase-10-payments.md` (the plan) and `online-security-model.md` (the money rules).
Written 12 Oct 2026 from Stripe's own documentation as it stood that day. **Paperwork only: nothing in
this file is built.** It says what to set up on the Stripe side, in what order, so that Stage 3 of the
plan can start the day it is asked for. Stripe changes its Dashboard and names often; re-read the linked
pages before doing each step.

This is engineering guidance, not legal or tax advice.

## Decided (owner, 12 Oct 2026)

- **Payments** (one-off purchases through Stripe's hosted Checkout) as the plan already says.
- **Billing**: one optional monthly subscription, **account-wide** (all three character slots). It
  bundles the middle tier of the season pass, a 30-day Kiosk Permit, extra Vault and Pack room, and
  an amount of the store currency; a subscriber upgrades the pass to premium for the price difference.
  The full catalogue (season pass tiers, one-off goods, the store currency, the gacha banners) is in
  the plan under "What is sold". Prices are not set.
- Keys are **never** typed into chat, a document, or the repository. They live in environment variables
  on the machine running the store service, and in a secrets vault once there is a rented machine.

## Checklist, in order

Each item says who does it. "Owner" items need the owner's Stripe sign-in and cannot be done by an
agent. Nothing here moves money.

### A. Account (owner, now, free)

1. Create the Stripe account at dashboard.stripe.com. Business details can be filled in later; a
   sandbox works before the account is activated for live payments.
2. In the Dashboard account picker, open **Sandboxes** and create a **general sandbox** named
   `nova-store-dev`. Stripe's current advice is a general sandbox for new integrations, not the old
   test mode. A second sandbox (`nova-store-ci`) is worth making later for automated tests so they do
   not disturb the dev one.
3. In the sandbox, open **Developers ▸ API keys** and note that three kinds of key exist:
   - publishable `pk_test_…`: safe to show, not needed by our design (the store page never talks to
     Stripe directly, Checkout is hosted);
   - restricted `rk_test_…`: what the store service uses. Create one named `nova-store-service`
     with only: Checkout Sessions write, Customers write, Products and Prices read, Refunds read,
     Subscriptions read (for Billing later). Nothing else;
   - secret `sk_test_…`: full access. Do not use it in the service.
4. Turn on **two-step sign-in** for the Stripe user, and when a second person joins, give them a
   Dashboard role rather than sharing the sign-in.

### B. Catalogue (owner or agent with the Stripe connector, before Stage 3)

5. Create the **products and prices** in the sandbox to match `NovaStoreSettings` once it exists.
   Each kind of thing in the catalogue maps to one Stripe shape:

   | What | Stripe shape | Notes |
   | --- | --- | --- |
   | The subscription | One product, one **recurring** monthly price; Checkout `mode=subscription` | The entitlement is per **account**. Its bundled goods (middle pass, 30-day Permit, extra room, currency) are granted by our service on `invoice.paid`, each period, not by Stripe |
   | Premium upgrade for subscribers | One-off price for the difference; Checkout `mode=payment` | Our service checks the account has an active subscription before offering it |
   | Season pass (middle, premium) for non-subscribers | One-off price per tier, `metadata.kn_season` | A new product per season, or one product with the season in metadata; decide when the first season is named |
   | One-off goods (Chips, Cells, passes, boosters, grind-zone pass, costumes, emotes) | One-off price each | Delivered to Pack, then Vault, as the plan says |
   | Store currency bundles | One-off price per bundle, `metadata.kn_currency_amount` | Credited to the account's ledger on the service. Stripe never sees pulls or spends |
   | Banner pulls, currency spends | **Not in Stripe at all** | Ledger entries on our service; the pulled thing becomes a delivery order |

6. Give every product a `metadata.kn_item` (or `kn_season`, `kn_currency_amount`) holding the game's
   id. The service grants by that id, never by product name or price, so a renamed product cannot
   grant the wrong thing.
7. The **customer portal** (Dashboard ▸ Settings ▸ Billing ▸ Customer portal) is where the player
   cancels the subscription or changes card, so we never build those screens. Turn it on with
   "cancel at period end" so the bundled goods already granted for the month stay.
8. When going live, recreate the same products **with the same ids** in live mode. Sandbox objects
   do not exist in live mode (Stripe's go-live checklist).

### C. Webhook, the only thing that grants (agent, Stage 3)

9. The store service exposes one `POST /stripe/webhook`. Stripe's rules for it, all of which the plan
   already requires in its own words:
   - verify the `Stripe-Signature` header against the endpoint's `whsec_…` secret with Stripe's
     library, on the **raw** request body; reject anything else with 400;
   - answer 200 **before** doing the grant; queue the work;
   - treat the **event id** as seen-once; a second delivery of the same event does nothing. Events
     also arrive out of order and are retried for up to three days in live mode, so nothing may
     depend on order;
   - subscribe only to the events we use.
10. Events to subscribe to:

    | Event | What the service does |
    | --- | --- |
    | `checkout.session.completed` | Fetch the session with line items, check `payment_status` is not `unpaid`, mark the order paid, queue delivery (one-off) or start the entitlement (subscription) |
    | `checkout.session.async_payment_succeeded` / `_failed` | Same as above for slow payment methods; failed marks the order unpaid and tells the player |
    | `charge.refunded` | Refund handling from the plan: unused item taken back, used item marks the account for a GM |
    | `charge.dispute.created` | Mark the account for a GM; the ledger line |
    | `invoice.paid` | The subscription's monthly grant: mark the account subscribed to the period end, grant that month's bundle (middle pass, 30-day Permit, extra room, currency). Once per invoice id |
    | `invoice.payment_failed` | Tell the player (System line, email from Stripe); the entitlement stays until the period end, then lapses unless paid |
    | `customer.subscription.updated` / `deleted` | Change or end the entitlement at the period end; apply the lapse rule for the extra Vault and Pack room |

11. The landing page (`success_url` with `{CHECKOUT_SESSION_ID}`) may **also** call the same
    fulfil function so the player sees the result at once, but the webhook is the one that is
    required. Checkout waits up to 10 s for the webhook reply before redirecting, so the handler
    must be fast.
12. Local testing needs no public address. Install the Stripe CLI, `stripe login` into the sandbox,
    then:

    ```
    stripe listen --forward-to localhost:4242/stripe/webhook
    ```

    prints a temporary `whsec_…` for the handler. `stripe trigger checkout.session.completed` and
    the published test cards (`4242 4242 4242 4242` pays; others decline or ask for confirmation,
    see docs.stripe.com/testing) drive every row of the plan's test table.

### D. Where the secrets live

| Secret | Dev (owner's PC) | Live (rented machine) | Never |
| --- | --- | --- | --- |
| `rk_…` restricted key | `KN_STRIPE_KEY` environment variable | the host's secrets vault | repo, client build, chat, `NovaStoreSettings` |
| `whsec_…` signing secret | `KN_STRIPE_WEBHOOK_SECRET` environment variable (the CLI's one while testing) | the vault; separate secret per endpoint, rolled on a schedule | same |
| Store link signing key (ours) | `KN_STORE_LINK_KEY` | the vault | same |

The service refuses to start if a variable is missing. A search of the client build for `sk_`, `rk_`
and `whsec_` is part of "Done when" in the plan.

### E. Before real money (owner, after Phase 9)

13. Activate the account: business details, bank account, tax settings. Stripe Tax can work out the
    tax per country at checkout if turned on; whether to use it is a business decision.
14. Create the **live** restricted key with the same permissions as the sandbox one, and attach an
    **access policy** (Dashboard ▸ API access policies) that allows only the rented machine's address.
15. Register the live webhook endpoint (HTTPS, public) and copy its own `whsec_…`.
16. Pin the **API version** in the service to the one the Stripe library ships with, and set the
    account to the same version in Workbench, so events and calls agree.
17. Turn on Stripe's receipts, set the statement descriptor to the studio name, and read the dispute
    settings once.
18. Rotate every key that was ever used outside the vault during development.
19. Stage 4 of the plan: one real purchase by the owner, refunded afterwards.

## Working with Stripe from this tool

The `stripe_implementation_planner` and the Stripe API tools live on Stripe's MCP server
(`https://mcp.stripe.com`). They were not reachable on 12 Oct 2026 (connector timed out; the Claude
CLI is not on the PC's path). Either fix works:

- claude.ai ▸ Connectors ▸ add the official Stripe connector, sign in with OAuth, pick the
  `nova-store-dev` sandbox and give it sandbox permissions only. Then start a new session.
- Or, from a terminal that has the Claude CLI:
  `claude mcp add --transport http stripe https://mcp.stripe.com/`, then `/mcp` to sign in.

OAuth is the sign-in for both. No API key is pasted anywhere for this. From 31 Oct 2026 Stripe's
MCP server stops accepting plain secret keys at all; only OAuth or agent-tagged keys.

## Open, waiting on the owner

1. **Prices**, for everything, and the name of the store currency.
2. **Lapse rule for the extra room.** When the subscription ends, items sitting in the extra Vault
   and Pack cells need a home: the usual choice is that those cells become take-out only until empty.
3. **Store currency and refunds.** A refunded or disputed currency bundle that was already spent on
   a pull: the plan's "used item marks the account for a GM" rule, or a negative balance. Also whether
   the currency is ever sold on the Steam build (Steam's purchase window would have to sell it there,
   and the balance would be shared across both builds).
4. **Banners and the law.** Paid random rewards are regulated by country: some countries forbid them
   or treat them as gambling, several require the odds to be published, and the console and phone
   stores require odds disclosure. Before the first banner: publish the odds on the banner page, keep
   a pity rule in the design, and have the terms reviewed. The 11 Oct note "keep to fixed goods unless
   reviewed" now means "reviewed before the first banner".
5. **Subscription on the Steam build.** Steam has its own rule for subscriptions sold inside a game
   on Steam; confirm whether the sub is sold there, or only on the website build with the entitlement
   shared across both.
6. Whether Stripe Tax is used, or tax is handled another way.

## Sources

docs.stripe.com: `mcp.md`, `keys.md`, `sandboxes.md`, `webhooks.md`,
`checkout/fulfillment.md` (hosted page), `billing/quickstart.md`, `get-started/checklist/go-live.md`.
All read 12 Oct 2026.
