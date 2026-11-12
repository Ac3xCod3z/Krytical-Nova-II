# Krytical Nova Publisher: pushing game updates without Unity

Guide, 8 Nov 2026. The plan and its checklist: `_Docs/Plans/own-cdn-local-standin-and-publisher.md`. The bigger move
(Cloudflare R2 instead of Unity's Cloud Content Delivery) is `_Docs/Plans/own-cdn-replace-ccd-plan.md`.

## What it is

- **The Publisher** is a desktop app in the Krytical Nova look. It sends a finished release to a line (TEST or DOTS),
  shows what every line holds, promotes TEST to LIVE and rolls LIVE back. Unity does not need to be open.
- **The stand-in** is a small bucket on this PC (`fake_r2.py`). It works like Cloudflare R2 will: the same S3 requests,
  every one signed, files that can never be overwritten. It costs nothing and nothing leaves the PC. The Publisher starts
  it when it opens and stops it when it closes.
- **Today's real updates are not changed.** Publish to Test and Promote to Live in Unity still use Cloud Content
  Delivery exactly as before. Players' launchers still read Cloud Content Delivery. The Publisher only reaches R2 once you
  have set R2 up and filled in its settings (section 5).

## 1. Install it (once)

1. Open the project folder `Assets\_Game\Release\Tools~\Publisher`.
2. Double-click **`Build Publisher.bat`**. It builds the app into `Builds\Publisher` and puts **Krytical Nova Publisher**
   on your desktop. Run it again after an update to rebuild.
3. You need Python (already on this PC). The app finds it as `python`; if not, put its full path in
   `Builds\Publisher\publisher-settings.json`, field `python`.

## 2. Upload a build to TEST (or DOTS)

1. Make a release the usual way: Unity, **Krytical Nova ▸ Release ▸ Publish to Test**. It stages the release into
   `Builds\Release\Store` (a folder with `manifest.signed` and `files\`).
2. Open the Publisher and click the **UPLOAD** tab.
3. The folder box already points at `Builds\Release\Store`. To use another one, click **BROWSE**. The box under it shows
   the build number, the version, the date and the size. If it turns red, the folder is not a staged release.
4. Click **TEST** (or **DOTS**), then **UPLOAD**. A box asks first; click **UPLOAD** in it.
5. Only files the bucket does not have yet are sent. The list goes up last, so a launcher never sees half a build.
   LIVE never receives an upload.

## 3. See what each line holds

Click **CHANNELS**. Three cards: **TEST** (cyan), **LIVE** (red), **DOTS** (gold). Each shows the build number, the
version, when it was made, the download size, the installed size and since when it is on the line. **REFRESH** reads them
again.

## 4. Promote and roll back

- **Promote TEST to LIVE:** CHANNELS tab, **PROMOTE TEST > LIVE**. The box says which build goes live and what LIVE holds
  now. Click **PROMOTE**. It is instant: the files are already there, only the list is copied.
- **Roll back LIVE:** pick a build in **ARCHIVED BUILD** (it starts on the one before the newest), click
  **ROLL BACK LIVE**, then **ROLL BACK** in the box. Launchers go back to that build the next time they open.
- **COPY TO TEST** puts an archived build back on TEST in the same way.
- After a promote or a rollback, the game server must run the same build (the game checks the version when a player joins).

## 5. Point it at Cloudflare R2 later (only when you say go)

Nothing is created on Cloudflare by this work. When you have done the hand-steps in `_Docs/Plans/own-cdn-owner-guide.md`
(domain, account, bucket `kn-game`, a key file in `Documents\KryticalNova-Secrets`), open
`Builds\Publisher\publisher-settings.json` in Notepad and fill in:

| Field | What goes in |
| --- | --- |
| `target` | `r2` (back to `local` for the stand-in) |
| `r2Endpoint` | `https://<Account ID>.r2.cloudflarestorage.com` |
| `r2Bucket` | `kn-game` |
| `r2KeyFile` | the full path of the key file (never the key itself) |
| `r2PublicBase` | `https://cdn.<your domain>` |

Save, then reopen the Publisher. The pill at the top right says **TARGET: CLOUDFLARE R2**. While any field is empty the
app refuses to send anything.

## 6. A test launcher that reads the stand-in

Players' launchers are never pointed at the stand-in. To try a download yourself:

1. Copy `Builds\Release\My Test Launcher` to a new folder (for example `Builds\StandinLauncher`).
2. In the Publisher, open the **STAND-IN** tab and click **COPY LAUNCHER.JSON**.
3. In the new folder, open `launcher.json` in Notepad, select everything, paste and save. Change `"test"` to `"live"` to
   follow LIVE.
4. Start the launcher while the Publisher is open. It downloads from the stand-in.

The new `files` line in `launcher.json` tells the launcher where the packed files sit. On our own bucket they sit once in
`files/`, beside the `test/`, `live/` and `dots/` lines. Without that line the launcher works exactly as before.

**Note:** the stand-in only holds builds that were uploaded to it. A launcher built with the real key accepts only lists
signed with the real key, so use releases from Publish to Test.

## 7. When something goes wrong

- **LOG** tab: every step and every answer from the bucket. **OPEN LOG FILE** opens `publisher.log`.
- "the stand-in bucket is not running": STAND-IN tab, **START**. If it still fails, another program is using port
  7920. Change `localPort` in `publisher-settings.json` and copy the launcher.json again.
- An upload that stops half way can simply be run again. Files already there are skipped.
- Where the stand-in keeps its files: `%USERPROFILE%\KryticalNova-LocalCDN\bucket` (**OPEN FOLDER**). Deleting that
  folder (with the Publisher closed) empties every line.

## 8. For Claude sessions

- Tool: `Release/Tools~/KnPublish/kn_publish.py` (`upload`, `promote --from --to`, `rollback --channel --build`,
  `status`, `verify`). Tests: `python -I test_kn_publish.py` (28, all against the stand-in).
- App self-test, on the stand-in only:
  `"Krytical Nova Publisher.exe" --selftest <out> --store <A> --store2 <B> --launcher <folder>`. It runs upload, a launcher
  download, promote, upload B, promote, rollback and a DOTS upload, and saves a picture of every step plus `report.txt`.
  Use test releases signed with a throwaway key and a launcher built with that key. Never use the owner's real
  signing key for tests.
- Never upload to or promote on the real Cloud Content Delivery lines from here. Set `target` to `r2` only on the owner's word.
