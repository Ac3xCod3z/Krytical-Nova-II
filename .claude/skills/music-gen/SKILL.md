---
name: music-gen
description: Make original music for Krytical Nova II - title and menu themes, town and zone beds, fight music for Cadence, stingers, faction themes - with ACE-Step 1.5 XL in the local ComfyUI (sketches, free, no limit) and Google Flow Music in the owner's Chrome (finals, stems), then give the owner a soundtrack audition page that shows where every take would play. Use whenever the user wants music, a soundtrack, a theme, a track, a loop, battle or combat music, menu or title music, ambient music, a stinger, a remix or stems, mentions ACE-Step, Lyria or Flow Music, or says a track is wrong - even if they do not name the skill.
---

# Music (ACE-Step sketches, Flow finals, the owner's ear)

I cannot hear. I write the prompts, make the takes hidden in the background, measure them, and publish an audition
page. The owner listens and picks. **Nothing goes into `Assets/_Game/Audio` or the game until the owner picks.**
The sound identity, faction voices, tempo map and cue list are in `Assets/_Game/_Docs/Design/18_sound_design.md`
(the Sound Bible): read it before writing a prompt.

## The owner's rules (20 Oct 2026)
- **Instrumental only.** No voices anywhere in the music: no lyrics, choirs, chants or shouts. `compose.py` sends
  `[Instrumental]` and adds "instrumental, no vocals"; `check.py` flags any take where the CLAP model hears a voice.
- **Leaning Hi-Fi Rush.** Guitar-driven rhythm rock and big-beat electronics; fight music has a beat you can always find
  (Cadence judges a skill within 90 ms of it).
- **Every music file lives in Google Drive:** `G:\My Drive\KryticalNova\Audio\Music\KN-Originals\<batch>\`. Models stay
  in `C:\AI`. The bought music library beside it (`Audio\Music\<pack>`) is sound-scout's and is only read.
- **No useapi.net** (a third party with $15/month on top). Flow Music is used directly in the owner's Chrome.

## Install (done 20 Oct 2026)
ComfyUI 0.42 at `C:\AI\ComfyUI-Easy-Install\ComfyUI-Easy-Install` has the ACE-Step 1.5 nodes built in. Models from
Hugging Face `Comfy-Org/ace_step_1.5_ComfyUI_files` (MIT), no account:
`diffusion_models\acestep_v1.5_xl_sft_bf16.safetensors` (9.5 GB), `text_encoders\qwen_0.6b_ace15.safetensors`,
`text_encoders\qwen_4b_ace15.safetensors`, `vae\ace_1.5_vae.safetensors`. ComfyUI is shared with ultratex, npc-voice
and wan-video: `compose.py` imports `../ultratex/scripts/run.py` for start / post / poll.

## Make takes
A Flow download is recorded with `add_take.py <batch> <file> --cue <Cue> --model "Lyria 3.5" --prompt "..."`.

```
python -I .claude/skills/music-gen/scripts/compose.py .claude/skills/music-gen/sheets/<sheet>.json [--only CueA,CueB] [--takes 2]
```
- Run with ComfyUI's Python (`C:\AI\ComfyUI-Easy-Install\ComfyUI-Easy-Install\python_embeded\python.exe`).
- A sheet is a batch name, the **slots** (where a track plays: place, scene, what starts and ends it, kind = loop,
  sting, layer or fight), the **previews** (transitions to hear) and the **cues** (prompt as ACE-Step tags, BPM, key,
  length, takes, seed). `sheets/vol1_sketches.json` is the example.
- Out: `<cue>_t<n>.flac` (lossless, for the game later) + `.mp3` and one line per take in `cues.json`, with prompt,
  model, seed and date, which is the record of where every track came from. A rerun makes only missing takes.
- Settings are Comfy-Org's XL SFT template (50 steps, cfg 7, shift 3, LM audio codes on). About 1.5 min for a two-minute
  take on the 5070 Ti once the model is loaded; 20 takes in about 13 min.
- The model picks its own tempo unless the words say it: `compose.py` adds "<bpm> BPM, <key>" to the tags (Vol 1 was
  made before that: one title take came out at 120 for 96; the fight takes at about 150 for 155). The game keeps time
  with the *measured* BPM, so a near miss is fine; a drifting beat is not.
- A ComfyUI already running from an older start writes into `ComfyUI\output` instead of Drive; `compose.py` looks in
  both and, on any run, repairs takes recorded without their files.
- Takes end with a fade: they are pieces, not loops. The loop point is cut at bake time.

## Check takes (I cannot hear)
```
cd .claude/skills/sound-scout/scripts && ../../../../Library/KryticalNova.SoundIndex/venv/Scripts/python.exe ../../music-gen/scripts/check.py "<batch folder>"
```
Length, integrated LUFS, peak, silent head, BPM against the sheet (sound-scout `tempo.measure`), beat drift (fight music
must be <= 15 ms for Cadence), loop seam, and the CLAP voice check. Writes `check.json`. Say what was measured and that
the music itself is for the owner's ears; never say a take "sounds" a certain way.

## Audition page
```
(same venv) python ../../music-gen/scripts/audition.py "<batch folder>" --stage "<scratchpad>/music-audition/<batch>"
```
Builds `audition.html` + `audio/<take>.mp3` (192k, levelled to -14 LUFS so takes compare fairly; about 47 MB for 21
takes, under the 64 MB a publish may carry). Publish it as an artifact with the MP3s as `files` and
`capabilities: {db: {}}`; the page groups takes by game place, plays a loop seam, a click on every beat, the
transitions in the sheet's `previews` and a faction coat swelling over the Selection theme. Picks land in the page's
`picks/<Cue>` documents (`pick`, `maybes`, `none`, `note`): read them with ArtifactData `list` on collection `picks`.
The page also builds a `KN-MUSIC-PICKS` line for the chat. The store keeps one document per cue across batches:
before publishing a new batch to the same page, save the old picks as `picks.json` in the old batch folder, then
seed the new batch's keepers (`batch` = the new batch) and delete the rest. Check the page by reading it back, never in the browser pane.

## Finals on Flow Music
`flow.md`: the steps in the owner's Chrome. Remake the picked direction on Flow, split stems, download into the same
Drive batch folder and add `cues.json` lines with `tool: "Google Flow Music"` and the credits used (~5 a song; AI Plus
gives 3,000 a month).

## Into the game (after picks only)
sound-scout's `music_bake.py <picks> --zone <Zone>` makes `Assets/_Game/Audio/Music/MUS_<Name>.ogg` + `music.json`, then
**Krytical Nova ▸ Setup ▸ Music**. Fight music needs a measured steady beat (`tempo.py show`).

## Zone ambience (beds and 3D sounds)
1. Find recordings in the sound library with the sound-scout skill (`find.py`, file names, `sheet.py` pictures: a
   bed must look even from left to right; a bright stripe repeats every loop, so start the stretch after it).
2. Audition: a sheet with `takes` that name a library file, start and length; `excerpts.py <sheet>` cuts them into
   a Drive batch; slots carry `preview_lufs` (about 12 dB under the music) and `layer_gain` 1 with `over` = the
   music or bed to hear them under. `check.py` and `audition.py` as for music.
3. Into the game: a plan like `sheets/ambience_2026-10-21.json` (per zone: beds with `when` always / day / night,
   points with an anchor rule: `anchorNames`, `anchorParent` + `anchorPrefix`, or `anchorContains`), then
   `ambience_bake.py <plan>` makes seamless loops (level and limit first, loop second, encode with no filter: a
   filter after the loop shifts the join and it clicks) into `Assets/_Game/Audio/Ambience/Zones/AMB_*.ogg` and the
   sound-scout skill's `ambience.json`; then **Krytical Nova > Setup > Ambience** (`AmbienceSetup.BuildAll`).

## Batches
- `Vol1_Sketches_2026-10-20`: Title theme, Title outro, Selection theme, the three faction coats, Concord idle,
  Callout fight: 20 ACE-Step takes + `CalloutFight_flow1` ("Battle Rhythm", Flow, Lyria 3.5). Drive folder
  https://drive.google.com/drive/folders/1lhE93243p1pSmOB40TZ5b2wbPGcPN3TC ; audition page
  https://claude.ai/artifact/SfGK6fR1mzAcTq1boXC2GW (republish that file path / URL for the next pass). Owner's
  picks archived in that folder's `picks.json`.
- `Vol2_Flow_2026-10-20` (sheet `sheets/vol2_flow.json`): after Vol 1 and the owner's inspiration (RJ Pasin, Dante
  Swan: djent over trap, no jazz), all new takes from Flow: SelectBed, CoatHegemony, ConcordIdle and CalloutFight,
  two each (Flow makes two songs per prompt), beside copies of the Vol 1 keepers. Same audition URL (version 3).
- `Vol3_Flow_2026-10-21`: one Selection theme per faction skin, 12 more Cadence fight tracks (owner kept all but
  the Shieldwall Variation); baked into the game 21 Oct (`sheets/bake_2026-10-21.json`).
- `Vol4_Ambience_2026-10-21`: Concord and Transit Row ambience from the sound library; the owner kept all of it,
  in the game 21 Oct (`sheets/ambience_2026-10-21.json`).
