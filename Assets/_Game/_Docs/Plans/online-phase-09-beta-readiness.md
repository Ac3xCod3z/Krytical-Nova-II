# Online phase 9: beta readiness (Steam and our own launcher)

Part of `online-roadmap.md`. Written 11 Oct 2026. **Not built.** Needs Phase 8.

## Goal

Strangers can get the game, keep it up to date, sign in, be supported and, when needed, be removed. The
owner is covered by terms he has had checked.

## Decided (owner, 11 Oct 2026)

- The game goes on **Steam**.
- The game is also downloaded from **our own website**, through **our own launcher**. The website is
  made later.

So there are two ways in, and one game world behind them.

## The two builds

| | Steam build | Website build |
| --- | --- | --- |
| Install and update | Steam | Our launcher |
| Sign-in | The player's Steam account, through Unity Authentication's Steam sign-in | An email account (Unity Player Accounts, or our own through Unity Authentication) |
| Purchases (Phase 10) | The Store Clerk opens Steam's own purchase window | The Store Clerk opens the Nova Store in the browser (Stripe's hosted checkout) |
| Beta access | Steam Playtest or keys | Invite codes on the account |

Both sign-ins end in the same kind of verified account (Phase 2 and Phase 4). A player can link the two
so their characters are the same either way.

## Steam

1. A Steamworks account and an app for the game (a one-time fee per app; the store page needs art, text
   and a review by Valve before it can be shown).
2. The Steamworks library in the client; Steam sign-in passed to Unity Authentication.
3. Builds uploaded to Steam's depots by script, from the same numbered build the launcher uses.
4. **Steam Playtest** for the beta: Steam hands out access, no keys to manage.
5. The server address is not in the build: the Steam client asks the world service where to go, like any
   client (roadmap rule 4).
6. Check Steam's rules for an online game with its own account system and for a game that is also sold
   elsewhere, before the store page is submitted.

## Our launcher

A small Windows program, separate from the game:

1. **Updates itself**, then the game.
2. **Downloads and patches**: it fetches a list of the game's files with their checksums for the current
   build number, downloads only what changed, checks every file, and can repair a broken install.
3. **Signs the player in** (email account) and starts the game with a short-lived, one-use token. The
   player's password is typed into the sign-in provider's page, never into our program.
4. **Shows news and server status** from the website.
5. **Is signed** with a code-signing certificate, as is the game. Without one, Windows warns every
   player that the download is unsafe. This is a yearly cost. Antivirus programs agree: Norton flagged every
   unsigned build of the server as **IDP.Generic** (a new file nobody has run) and blocked it (16 Oct 2026). The
   game, the server and the launcher are signed with the same certificate (an EV one builds trust fastest), so a
   new build is known by its publisher, not by its age. Steam's copies start trusted.
6. Files are served from storage with a content delivery network in front; the launcher never downloads
   from a game server.

The launcher is the highest-value target for an attacker after the servers: whoever controls the update
list controls every player's PC. The update list is signed with a key kept offline, and the launcher
refuses a list that is not signed.

The website (made later) needs, for the game: account creation and linking, the launcher download, the
terms and the privacy policy, server status, and later the shop.

## Keeping clients current

- The build number is checked at connect (Phase 2). An old client is refused with "update the game".
- A patch day: put up the notice, stop arrivals, save and stop the servers, roll the build (Phase 8's
  script), upload to Steam and to the launcher's storage, start, check, open. The runbook has the order.

## Rules, support and removal

1. **Terms of service, privacy policy, age rule, refund policy**, written or checked by a qualified
   person. Shown and accepted on first sign-in; a changed version must be accepted again.
2. **Conduct rules** for names and chat.
3. **Report** a player from their namecard or a chat line; the report carries the recent lines.
4. **GM tools on the live server**: mute, kick, ban (time-limited and permanent), rename, look up an
   account's ledger. The GM commands that create items, Creds or levels are not on the live server's
   list at all.
5. **A support address** and a place where bans can be appealed.
6. **Crash reports** from the client and the server, with the build number.
7. **A status page** the launcher and the website read.
8. **Wipe policy**, said in advance: whether beta characters are kept at launch.
9. **Name reservations** and the reserved-word list (`CharacterNameRules`) reviewed.

## Work items in order

1. Decide the beta's size and length; set the caps and machine from that.
2. Steamworks app, Steam sign-in, the upload script, Playtest.
3. Email accounts and linking.
4. The launcher (update list, patching, signing, sign-in hand-off).
5. Terms and policies; the acceptance screen.
6. Report, mute, kick, ban; the live GM list.
7. Crash reports; status page.
8. A closed test with invited players before anything public.

## Done when

- A stranger installs from Steam, another from the launcher, and they meet in the same Line.
- A new build reaches both within one patch day and old clients are told to update.
- A reported player is muted and banned by a GM who never opens the server console.
- The terms and policy have been reviewed and are accepted in game.
- The launcher refuses an unsigned or altered update list.
