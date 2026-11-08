# Own CDN: local stand-in and the Krytical Nova Publisher

Plan and live checklist, 8 Nov 2026 (owner request via Master). Parent plan: `own-cdn-replace-ccd-plan.md` (this
covers its Stage 1 tool, made testable on this PC, and the desktop app). Guide: `_Docs/Guides/Publisher-Guide.md`.

## Goal

A free stand-in for our own CDN on this PC, built so the same code later points at Cloudflare R2. Also a themed desktop
app that uploads a finished build, shows the lines and promotes or rolls back without Unity. The live Publish path (Unity,
CCD badges) stays untouched.

## Choices made

- **Stand-in = `fake_r2.py`** (Python standard library, from the Stage 1 branch) instead of MinIO. Nothing is downloaded
  or installed, it checks AWS Signature V4 like R2, and it refuses to overwrite `files/` like the R2 bucket lock. Port
  7920 on 127.0.0.1 only (far from 7777/7778/7801/7802/8090).
- **One bucket layout** (owner decision 7): `files/<sha256>.gz`, `test/`, `live/`, `dots/` `manifest.signed`,
  `archive/build-<n>.signed`.
- **Launcher:** an optional `files` address in `launcher.json` (the files sit once beside the lines). Without it the
  launcher behaves exactly as today.
- **Publisher app** = WinForms on .NET Framework 4.7.2 in the Server app's look. Every bucket action is one run of
  `kn_publish.py`, so the Editor and the app share one implementation.
- **Rollback** puts an archived signed list back on a line. That is fine for today's launchers. The v-next launcher (parent
  plan Stage 2) will refuse lower builds; then rollback must become a re-signed copy under a higher number.

## Checklist

- [x] Read the own-CDN plan, owner guide, ReleasePublisher CCD steps, the launcher's channel logic and the Server app
- [x] Bring `KnPublish` (kn_publish, fake_r2, tests) from `claude/cdn-stage1-r2-publish`; its ReleasePublisher dual publish NOT brought (live path)
- [x] kn_publish: `dots` line, `promote --from/--to`, `rollback`, `status` (lines + archive, ListObjectsV2); fake_r2 lists and sends Last-Modified
- [x] Tests: 28 pass (5 new: status, dots, rollback, unknown archive, same-line promote)
- [x] Launcher: optional `files` address (`LauncherConfig.files`, `Updater.FileUrl`)
- [x] Publisher app: CHANNELS / UPLOAD / STAND-IN / LOG tabs, themed confirm step, starts and stops the stand-in, R2 target refused until set up
- [x] `Build Publisher.bat`: builds into `Builds\Publisher`, makes the desktop shortcut
- [x] End to end on the stand-in (self-test, 8 Nov 12:47): upload A to TEST, launcher on TEST downloads build 1; promote, launcher on LIVE is up to date; upload B and promote, launcher gets build 2; roll back, launcher goes back to build 1; DOTS upload. 15 pictures in `G:\My Drive\KryticalNova\Docs\Publisher-proof-2026-11-08`. Test builds signed with a throwaway key; the real key was not used.
- [x] Guide (`Publisher-Guide.md`) and this checklist
- [ ] Owner: double-click `Build Publisher.bat` after the merge (makes the desktop shortcut) and try one upload with a real Publish to Test store
- [ ] Later, owner's word: R2 settings in `publisher-settings.json` (parent plan hand-steps 1 to 4), first upload by the runbook
- [ ] Later: a Release menu in Unity that calls the same tool (parent plan Stage 1 dual publish), only with the owner's word (live path, HIGH risk)
- [ ] Later: rollback as a re-signed higher build once the v-next launcher refuses lower builds
