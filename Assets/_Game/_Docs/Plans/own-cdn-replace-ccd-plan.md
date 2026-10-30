# Own CDN: moving game updates off Unity Cloud Content Delivery (CCD)

Plan, 29 Oct 2026 (mini-boss session "cdn", parent: Master). **Status: plan only. No account made, no money spent, no
script written, nothing published.** Live checklist: tick boxes as stages land. Research notes with every source and
date: `side-session-notes/.../cdn-pricing-research.md` and `cdn-tech-research.md` (items marked *unconfirmed* below
are not proven; verify before relying on them).

## 1. Recommendation in one minute

- **Host the files on Cloudflare R2** behind a custom domain (egress is free, storage $0.015/GB-month). At 50,000 active
  players the bill is single-digit dollars of storage plus operation fees; CCD would be about $10,000 to $22,000 a month
  for the same traffic (table in section 4).
- **Keep the launcher design.** It already stores every file once as `files/<sha256>.gz`, signs one `manifest.signed`
  and reads only a `{channel}` + `{path}` URL. Only the *upload* (`ugs ccd entries sync`) and the *promote*
  (`ugs ccd badges create`) are CCD-specific; the download side already works against any HTTPS origin.
- **Run both, then switch.** New launcher build reads a *signed list of sources* (R2 first, CCD second), ships to
  players through CCD, then CCD becomes the cold fallback. CCD stays free below 50 GB a month, so it costs nothing as a
  parachute.
- **Hetzner is not the cheapest sound option** for this job: a single box is a single point of failure, the US
  locations include only 1 TB of traffic (EU 20 TB; *both unconfirmed from a Hetzner page*), and release day spikes
  hit a 1 Gbit port. Keep Hetzner for game servers.
- Next biggest wins after the host: **split or chunk the 727 MB `world_common_assets_all` bundle** (a one-asset change
  today re-downloads 638 MB) and **Authenticode-sign the launcher** (Smart App Control blocks unsigned replacement).

## 2. Audit: what ships today (real sizes, build 12, 25 Oct 2026)

| Thing | Fact (source) |
| --- | --- |
| Build pipeline | `ReleasePublisher.Publish`: IL2CPP game into `Builds/Release/Game` (6.4 GB raw), `ReleaseTool stage` fingerprints, gzips changed files into `Builds/Release/Store/files/<sha256>.gz` (**1.93 GB packed, 89 files, 4.68 GB unpacked** in build 12's manifest) and writes `manifest.signed` (ECDSA P-256 signature line + manifest). 67 min game build (Release Guide). |
| Upload | `ugs ccd entries sync Store -b krytical-nova-game -u test` (changed files only). Promote = `ccd badges create <release> live`. Both in `Release/Editor/ReleasePublisher.cs`. |
| Channels | Two CCD badges, `test` and `live`, on bucket `krytical-nova-game`; launcher URL is `.../release_by_badge/{channel}/entry_by_path/content/?path={path}` baked from `UpdateSource.txt`; `launcher.json` beside the exe can override channel and source. |
| Addressables | **Not on CCD.** `m_BuildRemoteCatalog: 0`, `m_CCDEnabled: 0`. Map bundles are local `StreamingAssets/aa/StandaloneWindows64/*.bundle`, shipped as ordinary files inside the Game folder through the launcher store. |
| Big files (packed) | `world_common_assets_all` 727 MB raw / **638 MB**; `sharedassets0.assets.resS` 175; `sharedassets3.assets.resS` 129; `resources.assets` 128; `sharedassets1.assets` 121; `world_zone01_startertown` bundle 89; other maps 15 to 32 MB each. All bundles together: 1,089 MB packed. |
| Launcher | .NET Framework 4.7.2 WinForms, 3.8 MB. Verifies the signature against the built-in public key, downloads missing files 4 in parallel, 4 retries, whole-file restart on a break (no HTTP Range), gzip, hash + size check, moves in only when all are good, repair, self-replace via `.new`/`.old`. Not code-signed (Smart App Control blocks a replaced unsigned launcher, Release Guide section 7). |
| Server list | Second CCD bucket, badge `latest`, `servers.json` (`ServerList.ListUrl`, in the game itself, so every shipped client has CCD baked in). |
| Server builds | Not distributed through CCD: built next to each release (`Builds/Server`, 4.7 GB raw); the Linux deploy path is `Deploy~/deploy.sh` (server-plan session). |
| Cadence | Builds 11 and 12 on 22 and 25 Oct: about one every 3 days in development. |
| CCD cost rule | Free to 50 GB a month combined, then $0.08/GB (Release Guide; Unity support article, Feb 2025). Pricing page snippet suggested $0.06 after 50 TB: *unconfirmed*. No sunset notice found. |

Findings that shape the plan:

1. Delta today is **file-level only**. A change inside the 638 MB bundle costs every player 638 MB.
2. The signed manifest has no expiry and no monotonic check in the launcher, so a stale (but validly signed) list
   can be replayed. Fix it while changing format.
3. The launcher cannot resume mid-file; on a flaky link a 638 MB file restarts.
4. Two CCD URLs are compiled into shipped binaries (launcher source, server list). Both must move through a
   transition, not a flag day.
5. Hetzner has **no spending cap** (only alerts, `Deploy~/PREP-CHECKLIST.md`); R2 and Bunny likewise bill usage, so
   a billing alert and a hot-link guard are part of the plan.

## 3. Options compared (prices from official pages, seen 29 Oct 2026)

| Option | Price facts | Fit for us | Verdict |
| --- | --- | --- | --- |
| **Cloudflare R2 + custom domain** | $0.015/GB-mo; Class A $4.50/M, Class B $0.36/M; free 10 GB, 1 M A, 10 M B; **egress free**. `r2.dev` URL is rate-limited: test only. ToS: R2 is under the Developer Platform terms, no large-file clause found; the CDN large-file clause hits Free-plan zones proxying a *non-R2* origin (my reading, not Cloudflare's). | Content-addressed immutable blobs are the ideal workload. S3 API for the upload; per-bucket scoped tokens. | **Recommended** |
| Bunny.net CDN + Storage | CDN $0.01/GB EU+NA, $0.03 Asia/Oceania, $0.045 S. America, $0.06 ME/Africa; Volume network $0.005/GB; $1/month minimum. Storage $0.01 to $0.025/GB-mo by region count. | Pay per GB: $250 to $2,750 a month at 10k to 50k players. Good fallback if Cloudflare terms ever bite. | Second choice |
| Backblaze B2 + Cloudflare | $6.95/TB-mo; free egress to Cloudflare (partner); downloads not charged. | Needs a Cloudflare-proxied zone: the ToS reading above applies to the proxy. More moving parts than R2. | Not needed |
| Hetzner (VM serving files) | EU: CX23 EUR 5.49, CX33 8.49, 20 TB included; US (Ashburn/Hillsboro) only CPX: CPX11 17.49, CPX21 31.99, **1 TB included** (*traffic figures unconfirmed from Hetzner's own page*); overage EUR 1/TB; 1 Gbit limit; prices after the 15 Jun 2026 rise. Hetzner Object Storage EUR 6.49 incl. 1 TB stored + 1 TB egress, overage egress EUR 1/TB. | Cheap in bytes, but one box = one failure, one region latency, you run TLS/patching/DDoS yourself, no spend cap. EU players only fast. | Good as an *extra mirror* if wanted, not primary |
| Cheap VPS fleet behind Cloudflare | Contabo VPS 4 EUR 5.50 (200 Mbit) to VPS 16 EUR 37 (1 Gbit), "unlimited" fair-use; OVH VPS-1 $4.54, VPS-2 $8.50 | Bandwidth fair-use clauses, many servers to keep patched, proxied through a Free zone = the ToS risk. Complexity beats R2's $2 to $6. | No |
| GitHub Releases / LFS / Pages | Releases: file under 2 GiB, 1000 assets/release, no stated bandwidth cap; LFS free 10 GiB storage + 10 GiB bandwidth; Pages 1 GB site, 100 GB/month soft | Our LFS budget is already over; Releases for a commercial game CDN is outside their intent and can be pulled. Fine only for a tiny beta. | No |
| Steam | $100 per app fee, free bandwidth (*not verified on a Steam page*) | A different product decision (store listing); not an update CDN for a custom launcher. | Out of scope; note for later |
| AWS CloudFront | First 1 TB free, then $0.085/GB (next 9 TB), $0.080 (next 40 TB); flat plans $15 / $200 per 50 TB (*check terms*) | 10x to 20x R2 per GB. | No |
| **Keep CCD** | 50 GB free, then $0.08/GB | Costs scale linearly; fine for 100 players, painful beyond. | **Fallback mirror only** |

## 4. Cost model (monthly, USD, this plan's assumptions)

Assumptions (edit them, then rerun the arithmetic): 25% of active players are new installs each month; scenario A
= today (install 2 GB, 2 GB of patches per player per month, three-day cadence, file-level patches); B = launch size
(install 6 GB, patches 4 GB per month); C = B with chunk-level delta (patches 1.2 GB per month). 60 GB of kept blobs.

| Players | Egress/mo A | CCD A | R2 A | Bunny std A | Egress/mo B | CCD B | R2 B | Bunny std B | Bunny std C | CCD C |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 100 | 0.25 TB | $16 | ~$1 | $2 | 0.55 TB | $40 | ~$1 | $6 | $3 | $18 |
| 1,000 | 2.5 TB | $196 | ~$1 | $25 | 5.5 TB | $436 | ~$1 | $55 | $27 | $212 |
| 10,000 | 25 TB | $1,996 | ~$1 | $250 | 55 TB | $4,396 | ~$2 | $550 | $270 | $2,156 |
| 50,000 | 125 TB | $9,996 | ~$3 | $1,250 | 275 TB | $21,996 | ~$6 | $2,750 | $1,350 | $10,796 |

R2 column is storage plus Class B operations (about 90 requests per patch or install against 10 M free), with
**zero egress**. A realistic R2 bill is a few dollars to about $20; the domain (~$10 to $15 a year) and Cloudflare's
free plan for DNS add nothing. Hetzner EU at 125 TB would be EUR 5.5 + EUR 105 overage, with a 1 Gbit port that
saturates on release day (5,000 players updating at 1 GB each in an hour needs ~11 Gbit): it is *cheap* but
the port is the problem, not the bill. A Bunny/CloudFront-style per-GB CDN is the fallback.

Chunk delta (C) pays on the CDN side only at Bunny-like prices; on R2 it matters for **player experience** (a 40 MB
patch instead of 638 MB), not cost.

## 5. Technical design

**Layout on R2 (two buckets):**
- `kn-blobs`: `files/<sha256>.gz` (and later `chunks/<sha256>`). Immutable, `Cache-Control: public,
  max-age=31536000, immutable`. Bucket lock on the `files/` prefix (indefinite) so even a leaked write token cannot overwrite
  or delete; R2 has no versioning (*Put/GetBucketVersioning unimplemented*), so the lock is the protection.
- `kn-channels`: `test/manifest.signed`, `live/manifest.signed`, `archive/build-<n>.signed`, `servers.json`.
  Cache TTL 30 to 60 s; purge by URL on promote.
- Public read via custom domain `cdn.<ourdomain>` (Cloudflare caches it). Tokens: **upload token** (blobs +
  `test/`) on the build PC; **promote token** (channels bucket write) in a separate DPAPI-protected file used only
  by Promote. Neither token ever goes in the repo (project secrets rule).

**Signed manifest v2** (`format` 2, launcher refuses higher formats it cannot read):
- Keeps the current ECDSA P-256 signature inside `manifest.signed`; add `build` (monotonic), `channel`, `expires`
  (e.g. 14 days; build PC re-signs weekly in a release or by a tiny "refresh" command), `sources[]` (ordered HTTPS bases,
  covered by the signature, so mirrors/migration cost no launcher release), and a `codec` field per file
  (`gzip` now, `zstd` later).
- Launcher remembers the highest `build` seen per channel and refuses lower (replay/freeze defence); a **Rollback**
  is a republish of the old content as a higher build number, never a lower one. Two embedded public keys with key
  ids for rotation. Signing key stays on this PC (DPAPI) with the USB backup rule already in the Release Guide; a
  hardware token is an upgrade, not a blocker. *Verify the ECDSA signature encoding (P1363 vs DER) on .NET Framework 4.7.2 before changing it; the current code already works.*

**Downloads:** keep SHA-256 + size verification; add `Range: bytes=N-` resume of the `.part` file (do not rely on
ETag; `If-Range` is not documented on R2); keep the 4-way parallelism; try `sources[]` in order on connect failure or
repeated 5xx. Test Range behaviour with curl against R2 before coding (Cloudflare/Bunny edge cases *unconfirmed*).

**Delta:** (1) cheapest: split `world_common_assets_all` into several Addressables groups so one asset change touches
a small bundle; (2) then build bundles *uncompressed* (Unity's own Addressables FAQ says smallest patches) and store
them as FastCDC-style ~1 MiB content-defined chunks (sha256 each, gzip each, manifest has an ordered chunk list +
whole-file hash). The launcher fetches only missing chunks and reassembles; no patch pairs, no native code. Measure
first: build twice with one asset changed and compare chunk overlap (**gain is not yet measured**). Binary patch tools
(zstd `--patch-from`, bsdiff, xdelta3, HDiffPatch) need one patch per old version and native code: rejected.
Uncompressed packed size grows the install on disk by nothing (Windows build is already 4.68 GB unpacked) but the first
download would use gzip/zstd on chunks, so transfer is similar.

**Code signing:** Microsoft Artifact Signing (formerly Trusted Signing) is $9.99/month, needs a paid Azure
subscription, and for **individuals is open only in the US and Canada** (organisations: US, Canada, EU, UK). An
OV certificate is roughly $220 to $440 a year. EV no longer gives instant SmartScreen trust. Smart App Control accepts any
trusted-CA signature. SignPath Foundation is open-source only: not for us. Sign the launcher exe in `BuildTools`;
whether the game's own exe/dll also need signing for Smart App Control is *unverified*; test on a PC with it on.

**Security:** public bucket is read-only; no unauthenticated write path exists (R2 has no anonymous write). Kill switch =
repoint `live/manifest.signed` to a previous archived signed manifest *republished with a higher build and fresh expiry*
(one command), plus Cloudflare cache purge of that one URL. Billing: set a Cloudflare billing/usage notification; add a
hot-link rule (Referer/WAF rate limit) on the custom domain; budget alert at $20 and $100.

## 6. How it plugs into the Publish to Test / Live scripts

Same menus, same one-step Promote:
- **Publish to Test**: unchanged through `stage`. Replace `RunUgs("ccd entries sync ...")` with `kn-publish upload`
  (a small tool in `Release/Tools~`, S3 SDK or `rclone copy --immutable --s3-no-check-bucket`; not `sync`): copy new
  `files/*.gz` to `kn-blobs`, then `manifest.signed` to `kn-channels/test/`, purge that URL. Dual-publish to CCD while
  stage S2 is live.
- **Promote Test to Live**: server-side copy `test/manifest.signed` to `live/manifest.signed` (after copying it to
  `archive/build-<n>.signed`), purge the URL. Blobs are already there, so promote is instant and tiny.
- **Rollback**: new menu item that re-signs/republishes the previous archived manifest as a higher build (needs the
  signing key; same menu pattern).
- **Test launcher** (`launcher.json` channel test) and **Make Launcher for Players** are unchanged: only the built-in
  `UpdateSource.txt` and the first `sources[]` change.
- `Settings`/`release-settings.json` gain an `r2` block (account id, bucket names, public base URL; **no secrets**).
- Server list: move `servers.json` to `kn-channels`; the game's `ServerList.ListUrl` changes once, with CCD kept as
  fallback URL in the same build (it is baked into the client, so it ships with the next release).

## 7. Staged migration and cost per stage

| Stage | What | Cost | Done when |
| --- | --- | --- | --- |
| S0 | Owner decisions (section 9); owner makes Cloudflare account, domain, buckets, tokens (section 8) | Domain ~$12/year; R2 free tier | Decisions recorded, tokens in DPAPI files |
| S1 | `kn-publish` tool + dual publish (CCD and R2); download and Range tests with curl from this PC only; **no player affected** | ~$0 | Same build visible on both, test launcher via `launcher.json source` override reads R2 |
| S2 | Launcher v-next: manifest v2 (`sources[]`, monotonic build, expiry, codec), Range resume, mirror failover, signed exe; ships through CCD like any build | Cert $120 to $440/yr or $120/yr Artifact Signing, if chosen | Test channel soaks 1 week with owner + 1 other PC |
| S3 | Switch: new launcher's first source is R2; Make Launcher for Players points at R2; `servers.json` moves | R2 ~$1 to $20/month | New installs and updates come from R2; CCD traffic near 0 |
| S4 | Delta: split big bundle, then chunks; measure | Dev time only | A one-asset change patches < 50 MB |
| S5 | Retire CCD as primary: keep bucket as a cold second source; stop dual publish (or keep, it is free below 50 GB) | $0 | CCD only used on an R2 outage |
| S6 | (Optional, if traffic merits) add Bunny as a second mirror in `sources[]`; failover tested | $0 until used, then per-GB | Failover drill passes |

## 8. Owner hand-steps (I never create accounts, enter credentials or spend)

- [ ] Choose and own a domain (or a subdomain of an existing one) for `cdn.`; add it to a Cloudflare account (free plan).
- [ ] Cloudflare: enable R2 (needs a payment method; free tier covers the start), create `kn-blobs`, `kn-channels`, set the
      bucket lock on `files/`, connect custom domain `cdn.<domain>`, set a billing/usage notification.
- [ ] Create two R2 API tokens (blobs+test upload; channels write) with the narrowest scope; store them in
      `Documents\KryticalNova-Secrets` (never in the repo or chat) and tell me only the file names.
- [ ] Decide code signing (section 9), then do the identity verification yourself.
- [ ] Back up the update signing key (already a Release Guide rule) before S2.
- [ ] Park: nobody touches CCD buckets or the live badge during S1 to S3.

## 9. Decisions for the owner (sent to Master as multiple choice)

1. **Primary host.** (a) Cloudflare R2 + custom domain (recommended); (b) Bunny.net Storage+CDN (pay per GB, no Cloudflare ToS
   questions); (c) Hetzner Object Storage/VM in one EU region (cheap bytes, one region); (d) stay on CCD.
2. **Domain and account.** (a) New domain for `cdn.` + Cloudflare (recommended); (b) existing domain I name; (c) `r2.dev`
   (testing only: rate-limited, not for players).
3. **Big-bundle delta.** (a) Split the group now, measure chunking later (recommended); (b) go straight to chunking;
   (c) defer, accept 638 MB re-downloads until after launch.
4. **Code signing.** (a) Artifact Signing, $9.99/month (only if you are an individual in the US/Canada or have a registered
   organisation in an eligible country) (recommended if eligible); (b) OV certificate from a reseller, $220 to $440/yr;
   (c) defer to phase 9 as planned.

## 10. Live checklist

- [x] Plan reviewed (Opus/Plan, 29 Oct 2026); corrections in section 11
- [ ] Owner decisions 1 to 4 answered
- [ ] S0 owner hand-steps done
- [ ] S1 upload tool + dual publish + curl Range test
- [ ] S2 launcher v-next, manifest v2, signing, soak
- [ ] S3 switch sources, move `servers.json`, update `Release-Guide.md`, `Server-Guide.md`, the Encyclopedia
- [ ] S4 split big bundle, measure chunking
- [ ] S5 CCD becomes cold fallback
- [ ] S6 optional second mirror

## 11. Review corrections (Opus/Plan review, 29 Oct 2026): these OVERRIDE sections 5 to 9 where they differ

1. **Keep manifest `format` at 1.** `Updater.Check` throws when `format > CurrentFormat` before it looks at the launcher entry, so a
   format-2 list would strand every old launcher. New fields (`expires`, `sources`, `codec`) are optional members
   (the serializer skips unknown ones); raise `format` only when v-next is the only launcher left.
2. **CCD stays as a permanent bridge** (not "retired" in S5): old launchers read only CCD's live badge, so CCD live must
   keep serving a signed manifest whose `launcher` entry is v-next, plus that blob, for as long as old launchers exist.
   Risk: if Unity ends CCD's free tier or CCD itself, unmigrated players need a new zip by hand.
3. **Manifest URLs are built into v-next** (ordered: R2 first, CCD second). Signed `sources[]` only lists file mirrors,
   because the manifest must be fetched before its `sources[]` can be read. Put R2-first in the S2 launcher so S3 needs no
   second launcher swap (every swap is a Smart App Control break while the launcher is unsigned).
4. **Two hostnames or one bucket.** A custom domain maps to one bucket. v-next takes separate manifest and file base
   URLs (or use a single bucket with `files/` and `test/`, `live/` prefixes, the simplest). `launcher.json` `channel`/`source`
   overrides keep working (My Test Launcher).
5. **No `channel` field in the signed manifest**, so Promote stays a plain copy with no signing key (a copied test
   manifest would otherwise say `test`). The launcher keeps the highest build seen per channel instead.
6. **Rollback and the build counter.** `LastReleaseBuild()` reads `Builds/Release/Store/manifest.json`: Rollback must
   write it so the next Publish is higher. The game checks its own `Application.version` against the server on join, so
   Rollback must roll the server back too; the launcher's build number and the game's differ after a rollback.
7. **Range resume works on the packed file.** `DownloadOnce` unpacks while downloading, so `.part` holds unpacked bytes;
   v-next saves `<sha>.gz.part` first (resume with `Range: bytes=<packed length>-`), then unpacks and hashes.
8. **Expiry is soft or absent**: a missed re-sign (holiday, dead PC, wrong clock) must not lock players out. Use about
   90 days and only warn; PLAY always works. The highest-build check allows `>=` so a re-sign of the same build passes.
9. **R2 tokens are per bucket, not per prefix.** To make Test/Live a real access split use separate test and live
   channel buckets; otherwise one token plus the signature (replay already needs a write token, and HTTPS stops others).
10. **No purge credential:** a Cloudflare Cache Rule that never caches `*/manifest.signed` (one Class B read per
    launcher open, inside the 10 M free reads) replaces cache purge and its extra API token.
11. **Bucket lock 30 to 90 days, not indefinite** (an indefinite lock means a mistakenly shipped secret or asset can
    never be deleted and storage grows for ever). Blobs are named by hash, so an overwrite with other bytes already fails
    verification. Storage grows 0.5 to 1 GB per build: budget it, not a fixed 60 GB.
12. **Files over 512 MB are not cached** on the Cloudflare Free plan: the 638 MB bundle always comes straight from R2
    (slower, every request billed). One more reason to do decision 3.
13. **Billing guard:** the launcher sends no Referer, so a hot-link rule does nothing. The exposure is Class B reads on
    cache misses (random URLs, 404s): add a WAF rate-limit rule, cache 404s, usage notification. Cloudflare has no hard
    spend cap (say so in decision 1). Realistic R2 Class B at 50k players: about 45 M requests a month, roughly $13.
14. **`servers.json`** is uploaded by the server's "Publish List" (`ServerLaunchOptions`, via `ugs`), not by
    ReleasePublisher. Moving it needs: a write token on the machine running the control page (internet-facing on Linux),
    consider signing the list (an unsigned list plus a leaked token sends players to a fake server), and a short
    dual-write to CCD and R2 until the next launcher/client release.
15. **Promote across two hosts:** order R2 first, then CCD; retry; the dialog says plainly if one failed (players would
    sit on two builds and only one joins the server).
16. **Code signing comes before or with v-next** (S2 ships a signed exe): decision 4 loses "defer". Whether the unsigned
    IL2CPP `GameAssembly.dll` is blocked under Smart App Control is untested.
17. **Pre-launch insurance, with a trigger:** at about 100 players CCD costs $0 to $16 a month. Migrate before the first of: CCD
    over 200 GB a month, or the beta date. Bunny Volume ($0.005/GB) halves its column. Fixes: the Release Guide says first
    install about 0.9 GB, the current store is 1.93 GB; `Updater.cs` says CCD answers "no-store" while Release Guide section 6
    says it caches for minutes (find which is true before relying on either); the signature-line limit (200 bytes) caps a
    key-id prefix; the P1363-vs-DER question is moot while the format stays 1. Cloudflare's 2023 terms update is said
    to allow large files from R2: cite and verify.
18. **Decision 3 (delta) is independent of the CDN** and goes to the Release backlog now. Added decisions: (5) CCD as a
    permanent bridge for old launchers (recommended) or a cut-off date; (6) expiry none or soft (recommended) or hard;
    (7) one channels bucket or separate test and live buckets.

## 12. Owner decisions (29 Oct 2026, via Master) and the implementation checklist: these OVERRIDE sections 7 to 9

| # | Decision | Answer |
| --- | --- | --- |
| 1 | Host | **Cloudflare R2 + custom domain**; Bunny.net is the fallback mirror |
| 2 | Domain | **A new `cdn.` subdomain on Cloudflare's free plan** |
| 3 | Big bundle | *Default, owner may change:* split the group now, measure chunking later (backlog item, independent of the CDN) |
| 4 | Code signing | **Decide later.** The launcher stays unsigned until a beta date. Options and the Smart App Control note stay in section 5 and 11 item 16. Consequence: the one launcher swap (v-next) will still be blocked on PCs with Smart App Control on, so **v-next is the only launcher swap before signing**, and its first manifest source is already R2 |
| 5 | CCD after the switch | **Bridge until the R2 path is proven, then the owner decides the cut-off.** Not permanent by default. **Gate:** CCD stays until the R2 path has delivered one full test update and one full live update to a real client |
| 6 | Manifest expiry | *Default, owner may change:* soft, about 90 days, warn only, PLAY always works |
| 7 | Channels | *Default, owner may change:* **one bucket** `kn-game` with prefixes `files/`, `test/`, `live/`, `archive/` and `servers.json` (one hostname, the signature is the protection; the 30 to 90 day lock is on `files/`). Separate test/live buckets stay possible later |

### Owner hand-steps, in order (nothing before step 1 costs or changes anything)

1. [ ] Pick the domain name for `cdn.` (a new domain, or a subdomain of one you own). Domain registration is the only paid
       item before R2: about $10 to $15 a year.
2. [ ] Create a Cloudflare account (free plan) and add the domain. **From here nothing costs money** until R2 is switched on.
3. [ ] Enable R2 (a payment method is requested; the free tier is 10 GB, 1 M writes, 10 M reads a month, then storage
       $0.015/GB-month). Create the bucket `kn-game`, connect the custom domain `cdn.<domain>`, set a usage/billing notification.
4. [ ] Create one R2 API token (Object Read & Write, this bucket only). Save it in `Documents\KryticalNova-Secrets` and tell Master
       only the file name (secrets rule: never in chat or the repo).
5. [ ] Add two Cloudflare rules in the dashboard: Cache Rule "never cache `*/manifest.signed`"; WAF rate-limit rule on the
       hostname. (I write the exact rule text in `Stage 1` notes.)
6. [ ] Back up the update signing key to USB (Release Guide section 5) before Stage 3.
7. [ ] Later, any time: code-signing decision and the CCD cut-off after the gate.

### What costs money, and when

| When | Cost |
| --- | --- |
| Now to hand-step 2 | $0 |
| Domain (step 1) | ~$10 to $15 a year |
| R2 enabled (step 3), pre-launch | $0 to about $1 a month (inside the free tier at first); egress always $0 |
| 1,000 players | ~$1 a month; 10,000 players ~$2 to $5; 50,000 players ~$6 to $20 (storage grows 0.5 to 1 GB per build; Class B reads) |
| CCD while it is the bridge | $0 below 50 GB a month, then $0.08/GB; trigger to look again: 200 GB a month |
| Code signing | $0 until decided; then $9.99 a month or $220 to $440 a year |

### Staged implementation checklist (no scripts written yet; each stage needs Master's approval)

- [~] **Stage 1: R2 publish path, no player affected.** *Status 29 Oct 2026: built and verified locally (branch `claude/cdn-stage1-r2-publish`, 23 tests + a full-size run against a stand-in bucket); the live test is BLOCKED on the owner's hand-steps 1 to 4 (`own-cdn-owner-guide.md`); held, not pushed.* *Changes:* new `kn-publish` tool (`Release/Tools~/`, S3 SDK, upload
      with `If-None-Match`/no overwrite, cache headers) and `release-settings.json` gets an `r2` block (no secrets);
      `ReleasePublisher.Publish` calls it after the CCD sync (dual publish); `Promote` copies `test/manifest.signed` to
      `live/` and `archive/build-<n>.signed` (R2 first, then CCD badge). *Files:* `ReleasePublisher.cs`, new tool project,
      `release-settings.json`, Release Guide. *Risk:* a failed half-promote (R2 ok, CCD not): order R2 then CCD, retry, clear dialog.
      *Test:* upload a stand-in store from this PC; `curl` the manifest and one blob (also a `Range` request); an existing launcher
      with `launcher.json source` pointing at R2 installs a build. *Rollback:* delete the `r2` block; menus act as today.
- [ ] **Stage 2: v-next launcher (the only swap).** *Changes:* built-in ordered manifest URLs (R2, then CCD), file base URL;
      optional manifest fields (`expires`, `sources`, `codec`) with `format` kept at 1; highest-build memory per channel
      (`>=`, no `channel` field in the manifest); soft expiry warning; `.gz.part` + `Range` resume; mirror failover; signing step
      hook present but off. *Files:* `Updater.cs`, `LauncherConfig.cs`, `UpdateManifest.cs`, `ReleaseTool` stage, Launcher
      news/status text. *Risk:* unsigned swap blocked by Smart App Control on some PCs (documented fix: start `Game\KryticalNovaII.exe`
      or switch SAC off); a bug strands players: therefore CCD live keeps serving a manifest with the working launcher.
      *Test:* old launcher updates itself from the CCD test line to v-next; v-next reads from R2 with CCD blocked (hosts file) and
      the reverse; truncated download resumes; stale lower build refused; tampered manifest refused. *Rollback:* republish the
      previous launcher as a higher build on CCD (old launchers follow it).
- [ ] **Stage 3: soak on the test channel.** Owner + a second PC run `channel: test` for a week; at least one real full
      update from R2 on a real client. *Rollback:* none needed (live untouched).
- [ ] **Stage 4: switch live (the gate).** Promote a build to live on R2 and CCD together; a real client updates from R2
      (full test update and a full live update delivered = gate passed). `Make Launcher for Players` now builds the
      R2-first launcher. *Rollback:* Rollback menu republishes the previous archive as a higher build (also rolls back the
      server; the build counter file is updated).
- [ ] **Stage 5: move `servers.json`** to `kn-game` (the server's "Publish List", `ServerLaunchOptions`; add a write
      token on that machine, consider signing the list; dual-write to CCD for a while). *Risk:* a leaked token could list a
      fake server: scope the token, sign the list.
- [ ] **Stage 6: owner decides the CCD cut-off** after the gate; until then CCD is the bridge (free below 50 GB).
- [ ] **Stage 7 (backlog, independent): big-bundle split, then chunk delta.** Build twice with one asset edited and measure
      chunk overlap before committing; the 638 MB file is also over Cloudflare's 512 MB cache limit.
- [ ] **Stage 8 (later): code signing** when the owner picks the option.
- [ ] Docs at each stage: Release Guide, Server Guide, Encyclopedia of Nova (build list Status, "What is left", change log, then `build_encyclopedia.py` and publish), and tick this checklist.

### First real upload, once the token exists (runbook; nothing here runs before the owner says go)

1. Owner confirms (via Master): domain, Account ID, and that `Documents\KryticalNova-Secrets\r2-kn-game.json` is saved. Claude never reads the file's contents aloud.
2. Cloudflare dashboard shows `cdn.<domain>` Active on bucket `kn-game`.
3. Claude runs from the worktree, with the key file path only (no secret on the command line):
   `python -I kn_publish.py --endpoint https://<AccountID>.r2.cloudflarestorage.com --bucket kn-game --key-file <the file> upload --store <a small stand-in store of a few KB, NOT the real Store>` (a scratch release, build number 0, so nothing resembles a real release; the `test/` line is the only one written).
4. `python -I kn_publish.py verify --base-url https://cdn.<domain> --channel test --deep` and a `curl -I -H "Range: bytes=0-15"` on one blob: expects 206 with Content-Range.
5. Same upload again: expects 0 uploaded. One overwrite attempt of a blob: expects refusal.
6. Only after that, with Master's OK, one run with the real Store (1.9 GB, free tier covers it). No `promote` and no change to CCD or the live badge in this stage.
7. Everything written is under `files/` and `test/` (and `archive/build-0.signed`); delete the scratch objects in the dashboard afterwards.

## Unconfirmed and risks

- Hetzner traffic allowances (20 TB EU / 1 TB US) come from third-party pages; re-check at hetzner.com/cloud.
- Cloudflare ToS reading for large files is mine; R2 under the Developer Platform terms looks fine, a proxied non-R2
  origin on a Free zone does not. Ask Cloudflare support or keep Bunny as an exit.
- Chunk-delta gain on Unity bundles is unmeasured; Range behaviour on edge caches unmeasured; P1363 vs DER
  signature encoding on .NET 4.7.2 unmeasured; country eligibility for Artifact Signing unknown.
- CCD price changes after Feb 2025 not confirmed (pricing page snippet showed "$0.06/GB after 50 TB").
