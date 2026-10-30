# Own CDN: your hand-steps 1 to 4, click by click

For the owner. Plain words. Part of `own-cdn-replace-ccd-plan.md` (section 12). Written 29 Oct 2026 from Cloudflare's usual
dashboard; button names can differ a little, so go by the words. **Never paste a token or key into a chat, a file in the project,
or an email.** Claude only ever needs the *file name* you saved it under.

## What costs money, step by step

| Step | What you do | Cost |
| --- | --- | --- |
| 1 | Choose and buy a domain | About $10 to $15 a year (`.com`). The only payment before R2 |
| 2 | Make a free Cloudflare account, add the domain | $0 |
| 3 | Turn on R2, make the bucket, connect `cdn.<domain>`, set a usage notification | $0 now. R2 asks for a payment method but its free tier (10 GB stored, 1 million writes, 10 million reads a month) covers the start; after that $0.015 per GB stored a month. **Downloads (egress) are always $0** |
| 4 | Make one API token and save it | $0 |

Cloudflare has **no hard spending cap**. That is why step 3 ends with a notification, and why the token is limited to one
bucket. Expect about $0 to $1 a month before launch.

## Step 1: the domain

1. Pick the name. Anything you like, short; the game will download from `cdn.<yourdomain>`. A name you may already own works too
   (then skip to step 2 and add *that* domain).
2. Easiest place to buy: **Cloudflare itself** (dash.cloudflare.com, left menu, *Domain Registration* then *Register Domains*).
   Cloudflare sells at cost (no markup; check the price it shows) and the domain is then already on Cloudflare, so step 2's
   "add the domain" is done for you. Any other registrar works, but then you change its *nameservers* to Cloudflare's two
   (step 2 shows them).
3. Note the domain name for Claude (a domain name is not secret).

## Step 2: the Cloudflare account and the domain

1. Go to **dash.cloudflare.com/sign-up** with your own email, make a password (use your password manager), confirm the email,
   and turn on two-step login (*Profile* then *Authentication*).
2. If you bought the domain at Cloudflare it is listed already. If not: **Add a domain** (or *Onboard a domain*), type it, choose
   the **Free** plan, and Cloudflare shows two nameservers; paste them at your registrar and wait (minutes to a few hours)
   until Cloudflare says *Active*.
3. Nothing in this step costs money. Leave all the other settings as they are.

## Step 3: R2, the bucket, the address, the alarm

1. Left menu: **R2 object storage** (name may read *R2*). Press **Purchase R2 / Get started**. It asks for a payment method;
   add yours. R2 stays inside the free tier at first. You are not charged for downloads.
2. **Create bucket**: name exactly `kn-game`; location: *Automatic* (or Eastern North America if it asks); storage class
   *Standard*. Create.
3. Open the bucket, **Settings**, **Custom Domains**, **Connect Domain**: type `cdn.<yourdomain>` (for example
   `cdn.mygame.com`), confirm. Cloudflare makes the DNS record itself. Wait until it says *Active*.
   (Do **not** turn on the "r2.dev public URL": it is rate-limited and for tests only.)
4. In the same bucket, **Settings**: leave everything else off for now. (The 30 to 90 day lock on `files/` and the two Cloudflare
   rules, "never cache `manifest.signed`" and a rate limit, are done later, together with Claude, from the plan; they do not block the first test.)
5. **Alarm**: left menu **Notifications**, **Add**; choose a usage or billing alert (for R2 usage or *Billing: usage*), set it at
   **$5**, then add a second at **$20**, sent to your email.
6. Note for Claude: the **Account ID** (R2 overview page, right side). It is not secret. The endpoint is then
   `https://<AccountID>.r2.cloudflarestorage.com`.

## Step 4: one token, saved safely

1. R2 overview page: **Manage API tokens** (or *API*), then **Create API token**.
2. Name: `kn-publish`. Permission: **Object Read & Write**. Scope: **Apply to specific buckets only**, then choose `kn-game`.
   No IP filter needed. TTL: forever (you can rotate it later). **Create API Token**.
3. The page now shows an **Access Key ID** and a **Secret Access Key**, **once only**. Do not close it yet.
4. On this PC open Notepad and make a file with exactly this (your two values in the quotes):

   ```
   {"access_key_id": "PASTE-THE-ACCESS-KEY-ID", "secret_access_key": "PASTE-THE-SECRET-ACCESS-KEY"}
   ```

   Save it as **`C:\Users\dlaco\Documents\KryticalNova-Secrets\r2-kn-game.json`** (the secrets folder that holds the other
   keys; make sure "Save as type" is *All files*, so it is not `.json.txt`). Then close the Cloudflare page.
5. **Do not** copy the token into the chat. Tell Claude only: the domain, the Account ID, and "the file r2-kn-game.json is saved".
6. If you ever lose or leak it: Cloudflare, R2, Manage API tokens, delete it and make a new one (the same steps).

Done with the hand-steps. Cost so far: the domain, and $0 to $1 a month.
