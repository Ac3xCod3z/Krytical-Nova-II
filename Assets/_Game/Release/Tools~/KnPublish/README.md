# kn_publish: the R2 side of Publish to Test / Promote to Live

Stage 1 of `Assets/_Game/_Docs/Plans/own-cdn-replace-ccd-plan.md`. Python standard library only (no boto3, no exe: unsigned
exes get frozen by Defender / Norton). Used by the **Krytical Nova Publisher** app (`../Publisher`, guide `_Docs/Guides/Publisher-Guide.md`); Unity's Publish to Test
does not call it yet (CCD flow unchanged). Lines: `test`, `live`, `dots`. Also `promote --from test --to live`, `rollback
--channel live --build N` and `status` (every line + archived builds, JSON).

| Command | What it does |
| --- | --- |
| `upload --store Builds/Release/Store` | Heads every blob the signed list names (`files/<sha256>.gz`), uploads the missing ones with `If-None-Match: *` (never overwrites; a blob already there with another size is an error), checks all are present, **then** writes `archive/build-N.signed` and `test/manifest.signed`. `--dry-run` reads nothing from the network. |
| `promote [--from-archive N] [--allow-lower] [--skip-verify]` | Reads `test/manifest.signed` (or an archived one), checks every blob exists, writes `archive/build-N.signed` and `live/manifest.signed`. Refuses a lower build than LIVE unless `--allow-lower`. No signing key needed: it copies the signed bytes. |
| `verify --base-url https://cdn.<domain> [--channel live] [--deep]` | What a launcher sees through the public address: the list, a `Range` probe and the size of every blob; `--deep` downloads, unpacks and checks every SHA-256. Does not check the signature (ReleaseTool `verify` and the launcher do). |

Settings: flags `--endpoint --bucket --key-file`, or `KN_R2_ENDPOINT`, `KN_R2_BUCKET`, `KN_R2_KEYFILE`. The key file is JSON
`{"access_key_id": "...", "secret_access_key": "..."}` kept in `Documents\KryticalNova-Secrets`, never in the repository.
Secrets are never printed or passed on a command line. The last output line is JSON (`{"ok": true, "build": 12, ...}`).

Test without any account: `python -I test_kn_publish.py` (28 tests against `fake_r2.py`, a stand-in bucket on 127.0.0.1 that
checks every AWS Signature V4, answers Range and `If-None-Match`, and refuses to overwrite or delete under `files/` like the R2
bucket lock). The signer is also checked against Amazon's published `get-vanilla` test vector. `python fake_r2.py --root <folder>
--port 9000` runs the stand-in by hand.


