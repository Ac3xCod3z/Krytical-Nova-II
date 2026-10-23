# Character work brief: the Synty Mannequin Modeling scene

The owner's brief of 22 Oct 2026, for a session dedicated to all character work. Read it in full before starting.
Use the `synty-blender` skill (Sidekick job: `references/sidekick-spec.md`). This brief is the owner's request,
restated and checked back with the owner; it is not yet built.

## 1. The mannequin scene (build first)
- A new Blender scene, named **Synty Mannequin Modeling** (e.g. `Assets/_ArtSource/Blender~/Sidekick/SyntyMannequinModeling.blend`).
- **Every lineage the game has, two each: one male, one female**, as the game shows a player with NO gear on
  (the stripped / bare body per lineage; see the "Bare body & world reveal decisions" memory and the game's character
  creator for which bodies those are).
- Lined up side by side, in **T-pose**, on the Sidekick rig, **with all their blend shape (shape) keys kept**.
- This scene is where new gear parts come in after they are textured in the AI-texturing / TTS / video workflow
  sessions (ultratex and the related skills).

## 2. Body sliders (build and TEST in Blender now, in this session)
- **Feminine bodies: bust slider and butt slider**, to make them more attractive.
- **Male humanoid bodies: a more defined body slider** (muscle definition).
- **Height slider for all**, with a reasonable min/max that does not break gear.
- Test every slider at its extremes and in combination, with the bare bodies and with gear on.
- Then write the owner an **exact explanation of how to ask another session to hook these sliders up in the game's
  current character creator** (what the shape keys are called, their ranges, how the creator should drive them, how
  gear follows them, what to test).

## 3. Clipping
- **Hair and beards must not clip through any blend-shape body** (every slider, every lineage, both sexes): find and
  fix the cause.
- **Every new part is tested for the same clipping**, aiming for as little as possible.

## 4. Standing job, every time this session is used
- Take an **FBX from the owner's Google Drive**, bring it into Blender, **add it to the right lineage**, and **fit the
  gear to both the male and the female version**.
- The two silhouettes: a **chestplate for male, a breastplate for female** is the most noticeable change; everything
  else just has to fit properly.
- **Parts will nearly always arrive as a male chestplate only. The session itself makes the female breastplate from
  it** (owner, 22 Oct 2026: "you will be in charge of making it into a breastplate"): reshape the chest to the female
  body's bust, keep the part's look and texture, and make it follow the bust slider like the rest of the body.
- **If a breastplate comes in instead, do the reverse**: make the male chestplate from it, flat-chested to the male
  body and following the male definition slider.
- **If boots look like high heels, make a male counterpart** with a flat heel and sole for the male body, keeping the
  boot's look and texture. Make every other gendered piece the same way: one fitted version for each sex.
- Gear must work across **all blend shapes and both genders**, and be clip-tested (section 3).

## Constraints that already stand (from earlier sessions)
- No FBX exports or other output files while iterating, until the owner gives the go-ahead.
- Vendor (Synty) folders stay untouched; work on copies.
- Never write files, or create or remove scenes, from a live-bridge tool (it crashed Blender).
- Review by pictures and MP4 videos sent into the chat (GIFs do not play on the owner's phone).
- Other sessions edit this checkout at the same time: commit only your own files, by exact path.
