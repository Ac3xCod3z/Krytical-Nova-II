# Release Guide: the launcher and how a new build reaches players

A running guide, in plain words, to getting the game to players and keeping it up to date. Updated after every
release step.

**Last updated:** 18 Oct 2026, publishing in the background (the build checkout and the Unity Accelerator).

## In one minute

- Players get the game through the **Krytical Nova Launcher**, one small program you hand them **once**. After that,
  every new build and every new launcher reaches them through the launcher itself.
- When a player opens the launcher it checks for a new build. If there is one it says **NEW BUILD READY** and
  downloads **only the files that changed**, checks every file, and starts the game with **PLAY**.
- You publish from Unity: **Krytical Nova ▸ Release ▸ Publish to Test**. That builds the game, packs what changed,
  **signs** it with your key and uploads it to **Unity Cloud Content Delivery** (CCD) on the **TEST line**.
- You check it with **your test launcher**, then **Krytical Nova ▸ Release ▸ Promote Test to Live**: from then on every
  player's launcher gets it.
- A launcher refuses any update not signed with **your key**, so nobody else can push files to your players.

## What works today

| Part | State |
| --- | --- |
| The launcher (check, NEW BUILD READY, update only what changed, self-update, repair, play) | Built 16 Oct 2026, tested on this PC |
| The release tool (pack, sign, verify) and the signing key | Built 16 Oct 2026; your key made 16 Oct 2026 |
| Publish to Test, Promote, Make Launcher for Players (Unity menus) | Built 16 Oct 2026 |
| Unity Cloud Content Delivery (the bucket, the upload, the TEST and LIVE lines) | Set up 16 Oct 2026 |
| Code signing (so Windows and Norton trust the launcher) | Before the beta (phase 9) |
| The version check inside the game | Online step 4 |

## Words used here

| Word | Means |
| --- | --- |
| Launcher | `Krytical Nova Launcher.exe`: keeps the game up to date and starts it. The game lives in a `Game` folder beside it. |
| Build | One version of the game. Each publish gets the next number: build 1, build 2, ... |
| Update list | `manifest.signed`: your signature on the first line, then every file of a build with its size and fingerprint (SHA-256). |
| Signing key | Your private key that signs every update list. Kept only in your Windows account (and your backup). |
| CCD | Unity Cloud Content Delivery: Unity's file hosting, where players download from. Free up to 50 GB of transfer a month. |
| TEST line / LIVE line | Two labels ("badges") on CCD. Publish moves TEST to the new build; Promote moves LIVE to whatever TEST has. |

## 1. Publish a new build to TEST

1. In Unity: **Krytical Nova ▸ Release ▸ Publish to Test**, then **Publish**. Since 18 Oct 2026 this builds in the
   background, in the build checkout (section 1a): **your Editor stays free**, keep working.
2. It takes a while: build 7 (18 Oct 2026, in the Editor) took 75 minutes, 67 of them the game build (IL2CPP and its
   shaders) and 6 the server.
3. When done, the Console says `[Release] Background publish done: OK, build N`. It can take a few minutes before your
   test launcher sees it (section 6). The full story of each step is in `Builds/Release/release-log.txt`, and the
   background Unity's own log in `Builds/Release/background-publish.log`.

**Only committed work is published.** The background build uses the commit your folder is on, not files you changed
and did not commit; the Publish box says how many there are. To publish everything as it is on your screen, use
**Publish to Test (in This Editor)**: the old way, which locks the Editor until it is done.

What happens inside: the game is built into `Builds/Release/Game` with its files kept apart (a change to one map only
changes that map's files); the release tool fingerprints every file, compresses the new ones into
`Builds/Release/Store`, writes the update list and signs it; the Store is uploaded to CCD (only changed files travel)
as a new release labelled TEST. Unity's debug folder (`..._BackUpThisFolder_ButDontShipItWithYourGame`) is never
shipped.

## 1a. Publishing in the background: the build checkout and the Unity Accelerator

Rule (owner, 18 Oct 2026): every publish goes through the Unity Accelerator, and never locks the Editor you work in.

**The Unity Accelerator** is a free program from Unity, installed on this PC on 18 Oct 2026 (v1.5.137). It runs by
itself as a Windows service (Services ▸ **Unity Accelerator**, starts with Windows) and remembers the work Unity does
when it imports an asset: compressed textures, compiled shaders, and the import results themselves. A second copy of
the project then fetches them instead of doing the work again.

| What | Value |
| --- | --- |
| Address Unity uses | `127.0.0.1:10443` (this version; not the old 10080) |
| Its page | `https://localhost/dashboard/` (the browser warns about the certificate: it is your own PC, go on), with the login you chose when installing |
| The cache | `C:\ProgramData\Unity\accelerator` (about 48 GB after the first fill) |
| Unity's setting | Edit ▸ Preferences ▸ Asset Pipeline ▸ Cache Server: Enabled, `127.0.0.1:10443`. It is per user, so every Editor of this project reads it: the main one, the Player 2 clone and every worktree |

Both Publish menus refuse to start when the Editor is not connected to it, and say what to do.

**The build checkout** is a second copy of the project, only for building: `C:\Users\dlaco\KN-worktrees\build` (a git
worktree, made 18 Oct 2026). It has its own Library, so building there never touches yours. Its `Builds` folder is a
link to this project's `Builds`, so build numbers, the Store, the server folder and `server-settings.json` are the same
ones. Never work in it: every publish first throws away whatever is changed in it and moves it to your commit.

What **Publish to Test** does now: checks the Accelerator, moves the build checkout to your commit, and starts a second
Unity there with no window (batch mode, below normal priority so your Editor keeps the processor first). That Unity
runs the same publish as before (game build, staging, signing, upload to TEST, the matching server) and quits; your
Editor's Console then says how it went. It is started from your Editor, so it runs as you and finds your signing key
and your UGS login (a terminal inside the Claude app cannot: its AppData is elsewhere).

**Memory.** The second Unity uses about 4 GB while it imports and more while it builds. Close the Player 2 clone before
a publish. On 18 Oct 2026, with the main Editor open, 3 to 6 GB stayed free; Windows lends more from the page file, so a
build is slower when memory is short, but does not fail.

**If the build checkout is missing** (a new PC, or it was deleted), make it again from a PowerShell window:

```
git -C "C:\Users\dlaco\Krytical Nova II" worktree add --detach "C:\Users\dlaco\KN-worktrees\build" main
New-Item -ItemType Junction -Path "C:\Users\dlaco\KN-worktrees\build\Builds" -Target "C:\Users\dlaco\Krytical Nova II\Builds"
```

Its first import then takes about 11 minutes with a warm Accelerator, or 25 without.

**What it measured (18 Oct 2026, this PC, a fresh copy of the project, 92,019 imports):**

| | Time |
| --- | --- |
| First import, Accelerator empty | 25 min 32 s |
| Same, Accelerator holding textures and shaders only | 28 min 10 s (no gain: the time is in the other imports) |
| Same, Accelerator holding the import results too | **10 min 55 s** (83,074 fetched, 8,946 imported) |

Since Unity 6.5, sharing import results is **off by default** (Unity's own caution: an importer that reads files it does
not declare could hand back an old result). The background publish switches it on for its own run
(`-cacheServerEnableImportResultCaching`); none of our code imports files that way. A batch Unity reads results from
the Accelerator but does not put new ones in by itself: to fill it, run once, in PowerShell, with no Unity open on that
folder:

```
& "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Unity.exe" -batchmode -quit -projectPath "C:\Users\dlaco\KN-worktrees\build" -buildTarget Win64 -EnableCacheServer -cacheServerEndpoint 127.0.0.1:10443 -cacheServerEnableDownload true -cacheServerEnableUpload true -cacheServerEnableImportResultCaching -cacheServerUploadExistingImports -cacheServerWaitForUploadCompletion
```

It takes about a minute. On 18 Oct 2026 Unity crashed while quitting after it (the uploads had already landed).

**Unity Build Automation** (Unity's cloud builds) was looked at the same day and not taken: its free time is about 200
Windows minutes a month on small machines, the project is very large, and the signing key and the CCD login live in
your Windows account.

## 2. Check it, then Promote to LIVE

1. **Krytical Nova ▸ Release ▸ Make My Test Launcher** makes `Builds/Release/My Test Launcher` (a launcher with a
   `launcher.json` that says `{"channel":"test"}`). Run it: it shows **TEST LINE** in the corner and installs or updates
   to what TEST has.
2. Play it. If it is good: **Krytical Nova ▸ Release ▸ Promote Test to Live**. Every player's launcher gets it the next
   time it opens.

## 3. Give a player the launcher (once)

**Krytical Nova ▸ Release ▸ Make Launcher for Players** makes `Builds/Release/Krytical Nova Launcher.zip`: a folder
"Krytical Nova" with the launcher in it. Send that zip. The player unzips it anywhere (any drive, no admin rights)
and opens the launcher. It installs the game into `Krytical Nova\Game` beside itself.

Never again: a new launcher travels with a build like any other file, and the old one replaces itself.

## 4. What players see

| When | The launcher says |
| --- | --- |
| First start | WELCOME TO KRYTICAL NOVA, the size of the download, **INSTALL** |
| A new build is out | NEW BUILD READY, which build, how much to download, **UPDATE** |
| Up to date | READY TO PLAY, **PLAY** |
| Downloading | a progress bar with MB of MB |
| A new launcher is out | UPDATING THE LAUNCHER, then it reopens by itself |
| No internet, or CCD down | CAN'T REACH THE UPDATES; **PLAY** still starts the build they have |
| The game is still running | CLOSE THE GAME FIRST |
| Something is damaged | **REPAIR** checks every file and fetches what is wrong |

Files a player has in the Game folder that the launcher never installed (performance logs, for instance) are left
alone. What the launcher did is in `launcher.log` beside it: ask a player for it when something goes wrong.

## 5. Your signing key (important)

- Made once on 16 Oct 2026, kept in `C:\Users\dlaco\AppData\Roaming\KryticalNova\release\update-signing-key.dat`,
  locked to your Windows account. It is never in the project or in git.
- **Back it up now:** **Krytical Nova ▸ Release ▸ Back Up the Signing Key...**, save it to a USB stick, and keep that
  stick in a drawer. If this PC dies or Windows is reinstalled without the backup, no update can be signed again and
  every player would need a new launcher by hand.
- Anyone with the key can sign updates: never send it to anyone, never put it in a chat or a cloud drive.
- On a new PC: `ReleaseTool import-key <the backup file>` (in `Builds/Release/Tools/ReleaseTool`).

## 6. Cloud Content Delivery (set up 16 Oct 2026)

| What | Value |
| --- | --- |
| Project | Krytical Nova II (`f2ed0dbf-063b-4143-9e21-0f4bb3b3aa81`), organization ac3xanimuslabs |
| Environment | production |
| Bucket | `krytical-nova-game`, **public** to read (downloads need no secret; your signature is the protection), written only by the service account |
| Service account | `kn-release` (Organization ▸ Service accounts on cloud.unity.com), roles: Cloud Content Delivery API Public Editor, and Unity Environment Admin on the project |
| Command-line tool | `ugs.exe` v2.0.0 in `%LOCALAPPDATA%\KryticalNova\tools` |
| Settings the menus use | `Assets/_Game/Release/Tools~/release-settings.json` (no secrets in it) |

How it was set up, for a new PC or a new key:

1. On **cloud.unity.com**: your account menu ▸ **Manage organization**; in the left menu under **Organization**,
   **Service accounts** ▸ **New** (`kn-release`) ▸ **Create**; then **Manage organization roles** (Cloud Content
   Delivery API Public Editor) and **Manage project roles** (Krytical Nova II: Unity Environment Admin);
   **Keys ▸ Add key** shows the Key ID and Secret once.
2. In **PowerShell from the Start menu** (not a terminal inside the Claude app, which keeps logins where Unity cannot
   see them): `& "$env:LOCALAPPDATA\KryticalNova\tools\ugs.exe" login`, then the Key ID and the Secret.

Costs: storage is free; downloads and uploads together are free up to **50 GB a month**, then $0.08 a GB. A first
install is about 0.9 GB, a patch only what changed, an upload only what changed.

**A new build can take a few minutes to show up.** The content network may keep an address for a few minutes, so
right after Publish or Promote a launcher can still see the build before. That is why the update list carries its
signature inside it (`manifest.signed`): a list and a signature never come from different builds.

## 7. When something goes wrong

| What you see | Why | What to do |
| --- | --- | --- |
| Publish says "the game did not build" | A build error | `Builds/Release/BuildReport.txt` and the Console. |
| Publish says "staging failed" with "No signing key" | This Windows account has no key | Import your backup (section 5). |
| Your test launcher still shows the build before | The content network keeps the TEST address for a few minutes | Wait five minutes, open it again. |
| A player's launcher says "not signed by Krytical Nova" | The list on CCD was changed, or signed with another key | Publish again from your PC. Never make a second key. |
| Norton or Windows SmartScreen warns about the launcher | It is a new, unsigned program | For now: "More info ▸ Run anyway" and a Norton exclusion. Code signing before the beta fixes it. |
| A player's download stops | Internet dropped | Press **RETRY**: what was downloaded is kept. |
| Publish says "The Unity Accelerator does not answer" | The service is stopped | Services ▸ Unity Accelerator ▸ Start, then publish again. |
| Publish says "switched off for this Editor" | The cache server preference is off | Edit ▸ Preferences ▸ Asset Pipeline ▸ Cache Server: Enabled, `127.0.0.1:10443`. |
| Publish says "There is no build checkout" | It was deleted, or this is a new PC | Make it again (section 1a). |
| The Console says "Background publish FAILED" | The build, the upload or the Accelerator check failed | `Builds/Release/release-log.txt`, then `Builds/Release/background-publish.log`. |

## For developers

| Piece | Where | What it does |
| --- | --- | --- |
| `ReleasePublisher` | `Release/Editor` | The Release menus: release build, tools build, stage, CCD upload, promote, player zip, test launcher. |
| `BackgroundPublisher` | `Release/Editor` | Publish to Test in the build checkout: the Accelerator check (`AcceleratorProblem`, also used by the in-Editor publish), moving the checkout, starting the batch Unity (`RunInBatch`), and the Console message when it is done. |
| `TestBuildBuilder.Output` | `Tools/Editor/Build` | Lets the release build share the test build's code with its own folder and packing (no LZ4HC single file). |
| `UpdateManifest`, `UpdateCrypto` | `Release/Tools~/Shared` | The update list, SHA-256, ECDSA P-256 signature check, safe paths. |
| ReleaseTool | `Release/Tools~/ReleaseTool` | `keygen`, `export-key`, `import-key`, `stage`, `verify`. .NET Framework 4.7.2, key kept with DPAPI. |
| Launcher | `Release/Tools~/Launcher` | Windows Forms, .NET Framework 4.7.2 (in every Windows 10 and 11). `UpdateKey.pub` and `UpdateSource.txt` are built in; `launcher.json` beside it overrides channel and source; `--headless` checks and updates without a window (exit 0 done, 3 replaced itself, 1 failed). |

How a file travels: every file is stored once on CCD as `files/<sha256>.gz`. The launcher compares sizes and
fingerprints (it remembers what it installed in `Game\.launcher\installed.json`, so it only re-reads a file that was
touched), downloads the missing ones into `Game\.launcher\download`, checks each against its fingerprint, and only
then moves them all into place and removes the files the new build no longer has.

Tested on this PC on 16 Oct 2026 with a stand-in build served locally: a first install, a patch (only the 2 changed
files of 4 fetched; a removed file deleted; a player's own file kept), the launcher replacing itself, a tampered
update list refused, a damaged download refused with nothing changed, and the install finishing once the file was
good again.

## Change log

- 16 Oct 2026: written. The launcher, the release tool, the signing key and the Release menus built and tested locally.
- 16 Oct 2026: Cloud Content Delivery set up (bucket, service account, ugs); the signature moved inside the update list
  (`manifest.signed`) after the content network served a list and a signature from different builds.
- 18 Oct 2026: the Unity Accelerator installed and every Editor pointed at it; the build checkout made; Publish to
  Test now builds there in the background and refuses to start without the Accelerator; the old way kept as Publish to
  Test (in This Editor). Measured: a fresh import 25 min 32 s, 10 min 55 s with the import results in the Accelerator.
