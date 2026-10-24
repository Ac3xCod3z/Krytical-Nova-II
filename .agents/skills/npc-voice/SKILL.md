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
python -I .Codex/skills/npc-voice/scripts/voice.py <lines.csv> --out <folder>
python -I .Codex/skills/npc-voice/scripts/voice.py --npc Armorer --voice A --instruct "<voice>" --text "<line>" --out <folder>
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
- Samples go in `Assets/_ArtSource/Voices~/<Batch>/` (Unity ignores `~`), with `audition.html` beside them,
  published as an artifact with the MP3s as files and `capabilities: {db: {}}`.
- Picks land in the page's `picks/<Npc>` documents (`pick`: A / B / neither, `note`). Read them with
  ArtifactData `list` on collection `picks`.
- First batch, 20 Oct 2026: Apothecary, Armorer, Broker, Herald × voices A/B × greeting + one new line;
  page https://claude.ai/artifact/QdBpChQYTgXeLGmqpcdJXp
