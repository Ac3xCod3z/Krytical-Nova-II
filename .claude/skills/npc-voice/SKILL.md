---
name: npc-voice
description: Make spoken NPC voice lines for Krytical Nova II with Qwen3-TTS VoiceDesign (a voice from a written description), run hidden in the local ComfyUI, and give the owner an audition page to pick by ear. Use whenever the user wants NPC voices, voice lines, dialogue audio, a narrator, text-to-speech, TTS, placeholder voice acting, or asks how an NPC should sound - even if they do not name the skill.
---

# NPC voices (Qwen3-TTS, driven by me)

The owner never opens ComfyUI. I write the lines, make the clips hidden in the background, and publish an audition
page; the owner listens and picks. **Nothing goes into `Assets/_Game/Audio` or the game until the owner picks.**

## Install (done 20 Oct 2026)
- `C:\AI\Install Texture Pipeline.bat` first (ComfyUI + the `qwen3-tts-comfyui` node pack), then
  `C:\AI\Install NPC Voices.bat`: one click, curl, no account. Model: `Qwen3-TTS-12Hz-1.7B-VoiceDesign`
  (4.5 GB, **Apache-2.0**, commercial use allowed) in `ComfyUI\models\qwen-tts\`.
- ComfyUI is shared with the ultratex skill: `voice.py` imports `../ultratex/scripts/run.py` for start / post / poll.

## Make lines
```
python -I .claude/skills/npc-voice/scripts/voice.py <lines.csv> --out <folder>
python -I .claude/skills/npc-voice/scripts/voice.py --npc Armorer --voice A --instruct "<voice>" --text "<line>" --out <folder>
```
- CSV columns: `id, npc, voice, instruct, text[, seed]`. One npc + voice keeps one seed, so it sounds like one
  person across its lines; change `--seed-base` for a fresh take of every voice.
- Out: `<id>.mp3` (listening, V0) + `<id>.flac` (lossless, for the game later) + `clips.json`. About 10 s a line
  on the 5070 Ti once the model is loaded.
- `instruct` is a plain voice description: age, build, temper, pace, texture ("gravelly, slow, few words").
- I cannot hear: check every clip with ComfyUI's Python and `soundfile` (length fits the word count, peak and RMS
  sensible, voiced share > 45 %), and say plainly that the words themselves are for the owner's ears.

## Lines
- Read `Assets/_Game/_Docs/Design/Lore-Bible.md` before writing any line. Keep its Fixed Canon, keep rumor apart
  from history, and never reveal the Ronin (no Sunderpeak, no Kagero).
- Greetings already in the game are in `Assets/_Game/World/Editor/Zone01Setup.cs` (`NpcSeed.Greeting`); the
  model named there (`SM_Chr_Medical_Male_01` …) hints at the voice.

## Review
- Results live in Google Drive (owner, 20 Oct 2026: results to Drive, models stay in `C:\AI`): samples go in `G:\My Drive\KryticalNova\AI Outputs\Voices\<Batch>\` (pass it as `--out`), with `audition.html` beside them,
  published as an artifact with the MP3s as files and `capabilities: {db: {}}`.
- Picks land in the page's `picks/<Npc>` documents (`pick`: A / B / neither, `note`). Read them with
  ArtifactData `list` on collection `picks`.
- First batch, 20 Oct 2026: Apothecary, Armorer, Broker, Herald × voices A/B × greeting + one new line;
  page https://claude.ai/artifact/QdBpChQYTgXeLGmqpcdJXp

## Close ComfyUI when done (owner, 23 Oct 2026)
ComfyUI must not sit open holding the graphics card and RAM. When the job is finished, stop it: the scripts call
`stop_comfy()` from `ultratex/scripts/run.py` at the end (it waits for an empty queue) unless `--keep-open` is passed;
use that only between back-to-back runs of one task, then stop it. This applies even if another session started it.

## One character, one voice (owner, 23-24 Oct 2026; replaces making each line with VoiceDesign)
The owner scrapped a batch where each line was designed separately: the voice drifted between lines and some NPCs had a
voice of the other gender. "You are my voice, my sound director." Now:
- **One gender per NPC.** Every voice description starts "A man ..." / "A woman ..."; a robot gets one gender for all its voices.
- **Design once, clone every line:** `scripts/clone.py <batch.json> --out <folder>` designs a ~10 s `sample` line per
  voice (VoiceDesign 1.7B), then clones every line from that sample (VoiceClone, 1.7B Base; `ref_text` = the sample).
  `--only Npc,..`, `--voices B`, `--redo id,.. --cand --seed-shift N` (takes go to `candidates/`), `--keep-open`.
- **Prove it:** `scripts/sv_check.py` (ComfyUI's Python, WavLM speaker verification in `C:/AI/models/wavlm-base-plus-sv`):
  pass = >= 0.93 to its own sample AND >= 0.03 closer than to the NPC's other voice. Calibration: same voice ~0.97;
  two different people of one NPC 0.51-0.93. If an NPC's A and B score > 0.92 against each other, the two options are
  the same person: redesign B (other age, pitch, texture), don't remake lines.
- **Remake or scrap:** `scripts/best_take.py` keeps the best of the takes in `candidates/` that has a normal pace
  (1.0-4.2 words/s) and passes; otherwise the line moves to `scrapped/` and the page marks it, never plays it.
  Very short lines (4 words) score unsteadily: give them up to 6 takes before scrapping.
- `scripts/measure.py` for pace, level and silence; `scripts/build_page.py` + `audition_template.html` for the page
  (voice-sample button, same-voice score per line, picks in db `picks_v2/<Npc>`: voice, keep, note, newLine).
- Vendor batch v2: `vendors_v2_2026-10-23.json`, clips in `G:\My Drive\KryticalNova\AI Outputs\Voices\VendorVoices_v2_2026-10-23\`,
  page https://claude.ai/artifact/DnMyjGagXvXaMNsTMLZfPw (169 of 170 lines; Broker A line 1 scrapped).
  The code changes to play them in the game are on that page and in `vendor_code_changes.html`.


## Into the game (24 Oct 2026)
1. Save the page's picks (`ArtifactData list picks_v2`) to a picks file like `picks_v2_2026-10-24.json` (it also maps
   each NPC to its asset id: Apothecary = NPC_HerbMaster ...).
2. `python -I scripts/bake_game.py <batch.json> <picks.json> <clips folder>`: the picked voice's kept, speaker-checked
   lines only (a scrapped line is left out, never filled by another voice), levelled to -20 LUFS, mono 24 kHz OGG,
   into `Assets/_Game/Audio/Voices/NPC/<Npc>/VO_<Npc>_<n>.ogg` + `npc_voice_lines.json`.
3. In Unity: **Krytical Nova ▸ Setup ▸ NPC Voice Lines** (`NpcVoiceSetup`): makes the mixer's Voice group if needed,
   sets the clips' import settings and fills each `NpcDefinition.lines` (text + clip).
4. In the game: `NpcInteractable.PickLine` picks a line per visit (never the same twice running, `NpcLinePicker`),
   `NpcInteractionUI.PlayVoice` plays it into the Voice group; the Voices slider (Settings ▸ Audio) starts at 50 %.
   A spoken-only spelling (e.g. "Re-Call") goes in the batch JSON's `spoken` map; the shown text stays as written.
