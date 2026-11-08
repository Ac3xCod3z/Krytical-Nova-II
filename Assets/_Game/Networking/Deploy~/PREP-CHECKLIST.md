# Zero-cost Hetzner prep (no server exists, so nothing bills)

Facts below are from Hetzner's own docs, read 29 Oct 2026. Prices change; the console is the final word.

## What bills, and what does not

| Thing | Bills? | Source (docs.hetzner.com) |
| --- | --- | --- |
| Server | **Yes, by the started hour, capped at the monthly price** (the lower of the two). A few minutes = one full hour | Cloud > Billing FAQ (last changed 2020-07-07) |
| Server **powered off / stopped** | **Yes, the same.** It bills for as long as it exists. Only deleting stops it | Billing FAQ ("you pay for a server that has completed the creation process"); Servers FAQ ("recommended to delete") |
| Snapshot | Yes, per GB per month (compressed size) | Billing FAQ |
| Server Backups (the feature) | Yes, 20% of the server price a month. **Off by default; we never turn it on** | Billing FAQ |
| Volume | Yes, per GB per month, hourly with a monthly cap (about EUR 0.044 per GB in the docs table; re-check) | Cloud > Volumes overview |
| Primary IPv4 | Yes, EUR 0.50 / $0.60 a month, billed even when unassigned. IPv6 is free | Primary IPs overview + Billing FAQ |
| Floating IP | Yes, monthly | Billing FAQ |
| Traffic | Only **outgoing**, beyond the included amount, in 100 MB blocks; alerts at 75% and 100% "do not cap or stop it". Incoming and private traffic free | Billing FAQ |
| Cloud Firewall | **Free** ("We do not charge for our Cloud Firewalls"); 50 per account, 500 rules each | Firewalls overview |
| Project, API token | No price is listed (a project is just a container; a project is billed in its owner's currency) | Billing FAQ; API token page |
| SSH keys, networks (private) | No price is listed in the pages read; private-network traffic counts as free internal traffic | Billing FAQ (not stated for keys: assume free, check the invoice) |
| Invoices | Calendar months; Hetzner may issue them up to 28 days after the month ends | Billing FAQ |
| Account verification | Not a charge by itself, but Hetzner may ask for a **government ID** or a **prepayment by your own card** (a credit balance, not a fee; refundable only when the account is closed and invoices paid) | Fraud prevention FAQ; payment overview |

**There is no spending cap.** Hetzner has only usage *alerts*, "a convenience feature rather than a spending limit". The cost guards are
ours: delete the box the same day (`teardown.sh`), and `cost-check.sh` must say CLEAN.

New accounts also have default resource limits; raising them needs the account to be a month old with the first invoice paid.

## Do these once, by hand, with NO server (all free)

- [ ] 1. **Account** at accounts.hetzner.com. Expect the ID or prepayment check (above); do the one you are comfortable with. If you
  prepay, write down the amount: it is credit, not a fee.
- [ ] 2. **Payment method** and tax details (a US address is fine; no VAT note is listed for US customers in the docs read).
- [ ] 3. **Project** in the Cloud Console: name it `krytical-nova-test`. Use a project only for the test box.
- [ ] 4. **Usage/billing alert** if the console offers one (Cloud project > alerts / account > billing). It only warns.
- [ ] 5. **API token**: the project > Security > API tokens > Generate API token, permission **Read & Write**, copy it once (it is
  never shown again). Keep it in the `HCLOUD_TOKEN` Windows environment variable; never in chat, files or git.
- [ ] 6. **SSH key**: `ssh-keygen -t ed25519 -f ~/.ssh/kn_hetzner` (give it a passphrase and load it with ssh-agent). Upload the
  `.pub` in the console (Security > SSH keys) or let `create.sh` do it.
- [ ] 7. **Firewall** (free): `create.sh` makes it from `server-settings.test.json`; you can also pre-make `kn-test-fw` in the console
  (SSH in, UDP 7777 and 7778 in, nothing else).
- [ ] 8. **DNS** (optional, free at your registrar): a name like `test.<yourdomain>` can wait. Friends can use the IP in the F1 panel.
  Do not buy a domain for this.
- [ ] 9. **Tools on the PC**: `winget install HetznerCloud.hcloud`, then `hcloud context create krytical-nova-test` (it asks for the
  token; it stores it in your user profile, not in the repo). Check: `hcloud server list` shows an empty list.
- [ ] 10. Run `bash cost-check.sh`: it must print `CLEAN: nothing billable in this project.` This is also the check after every test.

## The test day, so it costs cents

1. `./create.sh` shows the hourly and monthly price from Hetzner and **refuses without `--confirm-spend`**. It records the start time.
2. Test for an hour or two. `./cost-check.sh` any time shows the running box and its age.
3. A pop-up reminds you after 8 hours (`KN_MAX_HOURS`) and every hour after; it never deletes anything by itself.
4. `./teardown.sh` copies the test database to the PC, asks DESTROY, deletes server, firewall and key (the server's own IPv4 is
   deleted with it), then runs `cost-check.sh --expect-nothing`. Never leave the box idle overnight.

Price check made 29 Oct 2026: a 4 vCPU / 8 GB shared type in Ashburn is about EUR 63 a month, so a 3-hour test is roughly EUR 0.30
(the hourly rate times 3, rounded up per started hour). The script prints Hetzner's exact figure before it asks.
