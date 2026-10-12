---
name: sound-scout
description: Find, judge and bake sound effects for Krytical Nova II from the sound library in Downloads/SFx without being able to hear - search the whole library by description with a local audio model, read sounds as spectrogram pictures and measurements, build an audition page for the user to choose by ear, then bake the picks levelled into Assets/_Game/Audio. Use this whenever the user asks for a sound, sound effect, SFX, audio for a slot, a variation of a sound, "find me a sound like", weather or ambience loops, to change or replace a sound, or says a sound is wrong, too loud, too long or repetitive - even if they do not name the skill.
---

# Sound Scout

Claude cannot hear. This skill turns sound into things Claude can read, and leaves the last word to the user's ear:

1. **Words.** A local audio model (CLAP) scores every library file against a description.
2. **Numbers.** Length, level, brightness, attack, decay, pitch, onsets for every file.
3. **Pictures.** A waveform and mel spectrogram per candidate, on one scale.
4. **The user's ear.** An audition page with the candidates, played as the game will play them.

Never tell the user a sound "sounds" a certain way. Say what the model scored, what the picture shows, and that
they should listen.

## Where things are

| What | Where |
|---|---|
| The library (never copied whole into the project) | `~/Downloads/SFx` (override with env `KN_SOUND_LIBRARY`) |
| Tools | `.claude/skills/sound-scout/scripts/` |
| The slots: what each is for, how it is searched, volume and pitch range | `slots.json` |
| Where every baked file came from | `sources.json` |
| Cache, not in git: Python venv, model, catalogue, sheets, audition page | `Library/KryticalNova.SoundIndex/` |
| Baked sounds | `Assets/_Game/Audio/SFX/<Group>/SFX_<Slot>_<nn>.wav`, `Assets/_Game/Audio/Ambience/Weather/` |

Run every tool with the venv's Python, from `scripts/`:

```bash
cd ".claude/skills/sound-scout/scripts" && ../../../../Library/KryticalNova.SoundIndex/venv/Scripts/python.exe <tool>.py ...
```

If the venv is missing (a fresh clone, a cleared Library), make it again; ask the user before downloading:

```bash
winget install --id Gyan.FFmpeg -e --source winget
python -m venv Library/KryticalNova.SoundIndex/venv
Library/KryticalNova.SoundIndex/venv/Scripts/python.exe -m pip install torch --index-url https://download.pytorch.org/whl/cpu
Library/KryticalNova.SoundIndex/venv/Scripts/python.exe -m pip install numpy pillow soundfile transformers
```

The model runs on the CPU on purpose: the graphics card's memory belongs to the Unity Editors.

## The tools

- `index.py` catalogues the library (about four files a second; resumable). Run it after the user adds packs.
- `find.py "description" [more descriptions] [filters]` ranks the library. Filters: `--min-length/--max-length`
  (seconds of sound, silence excluded), `--min-centroid/--max-centroid` (Hz; under 800 is dull, over 4000 is bright),
  `--min-attack/--max-attack` (ms from first sound to peak; under 30 is a hit, over 300 is a swell), `--max-onsets`,
  `--min-tone/--max-tone` (0 noise, 1 a pure note), `--under <text in path>`, `--exclude <text in path>`,
  `--not "description to move away from"`, `--like <file>` (more like this one), `--spread` (how alike two results may
  be), `--json out.json`.
- `sheet.py out.png <files...>` or `--json list.json` draws the pictures. Read the PNG. A click is a thin vertical
  line; a whoosh a soft cloud that swells and fades; a tone horizontal lines; a clang a vertical line with horizontal
  lines trailing; two vertical lines are two hits (wrong for a single-hit slot); a loop should look the same from
  left to right, and a bright vertical stripe in it is an event that will repeat every time round.
- `scout.py [--only "<slot>"] [--sheets]` runs the slots' own descriptions and writes
  `Library/KryticalNova.SoundIndex/shortlist.json`. Edit the shortlist by hand (drop what the pictures rule out, add
  `find.py` results; an entry may carry `"start": seconds` to begin later in a long file).
- `audition.py [--only "<slot>"]` builds `Library/KryticalNova.SoundIndex/audition/index.html` from the shortlist,
  with every preview shaped exactly as the bake shapes it. Serve it and give the user the address: opened as a
  local file the page shows but stays silent (seen 2026-10-10 in the Claude app's browser pane). In the background:
  `python -m http.server 8765 --bind 127.0.0.1` from the `audition` folder, then `http://127.0.0.1:8765/`. They tick,
  press **Copy my picks** and paste a line starting `KN-PICKS` into the chat.
- `bake.py <file with the pasted text>` writes the picks into `Assets/_Game/Audio` and checks them
  (`bake.py --check` alone re-measures everything). Then, in the Editor, **Krytical Nova ▸ Setup ▸ Sounds**
  (`AudioSetup.Sounds()`), which sets import settings, fills the feel prefabs' sound slots and the weather.

- `tempo.py` is for music a mechanic is timed to (Cadence, `Assets/_Game/_Docs/Design/17_music_and_cadence.md`), not
  for sound effects. The music library is `~/Downloads/Music` (env `KN_MUSIC_LIBRARY`), only read.
  `tempo.py scan` measures every track (beats a minute, the time of the first beat, a guess at the first beat of
  the bar, how far the beat drifts from an even grid, how much of the track has a clear beat) into
  `Library/KryticalNova.SoundIndex/music/tempo.jsonl`; `tempo.py list` prints the steady ones; `tempo.py show <file>`
  measures one now; `tempo.py selftest` checks the measuring against made-up drums. A reading may be half or double
  the tempo a listener would tap, and the bar is a guess: the owner checks every pick by ear with a click on the beat.

## Working a request

- **A new or replaced sound for an existing slot:** `scout.py --only "<slot>" --sheets`, read the sheet, prune, then
  `audition.py --only "<slot>"`. If the user's note says what was wrong, put it into the slot's `queries` / `avoid`
  in `slots.json` first.
- **A new slot:** add it to `slots.json` (purpose, descriptions, limits, volume, pitch range, how many variations)
  and to the table in `Assets/_Game/GameFeel/Editor/GameFeelBuilder.Sounds.cs`, which is what the game uses; the
  volume and pitch in `slots.json` are only what the audition page plays.
- **Variations:** anything heard many times a minute wants 3 to 5 different takes (the game never plays the same one
  twice running and shifts pitch within the slot's range). Rare moments want one.

## What the model is and is not good at

Reliable for kinds of sound (rain, thunder, whoosh, click, explosion, metal hit). Weak on mood words ("proud",
"satisfying") and on telling a good take from a bad one: for those, shortlist wide and let the user choose.
Descriptions work best as plain phrases of what makes the sound ("sword swing whoosh through the air"), and two or
three phrasings averaged beat one. Always check the top hits against file names that say what they are before
trusting a search in a new area.

## Levels

Baked one-shots are levelled to -16 dBFS over their loudest 400 ms, loops to -24 dBFS, nothing peaks over -2 dBFS.
How loud a slot plays in the game is its volume in `GameFeelBuilder.Sounds.cs` (none over 0.7), and every slot
answers to one Settings slider: Effects, Interface or Ambience, under Master.
