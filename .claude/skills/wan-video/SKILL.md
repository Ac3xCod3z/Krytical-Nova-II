---
name: wan-video
description: Turn a picture into a short video clip with Wan 2.2 (image-to-video) run hidden in the local ComfyUI, and send the owner the MP4. Use whenever the user wants a picture animated, a concept clip, trailer shot, turntable-like motion, a moving pet or creature, a comic recap shot, image-to-video, AI video, or mentions Wan - even if they do not name the skill.
---

# Wan video (image-to-video, driven by me)

The owner never opens ComfyUI. I render or pick the first frame, write the motion prompt, make the clip hidden in the
background and send the MP4 into the chat (videos as MP4, never GIF). Nothing goes into the game or a build.

## Install (done 20 Oct 2026)
- `C:\AI\Install Texture Pipeline.bat` first (ComfyUI), then `C:\AI\Install Video Workflow.bat`: one click, curl,
  no account. Wan 2.2 **TI2V 5B** as repackaged by Comfy-Org (model 9.3 GB, umt5 text encoder 6.3 GB, VAE 1.3 GB),
  **Apache-2.0** (commercial use allowed). The graph is ComfyUI's own Wan nodes (from the built-in
  `video_wan2_2_5B_ti2v` template); `negative.txt` is that template's negative prompt.
- The 14B I2V model is sharper and much heavier (two 14B experts); not installed. Ask before adding it.

## Make a clip
```
python -I .claude/skills/wan-video/scripts/wan.py <picture> --prompt "<what moves and how>" --name <Name_v01> --out <folder> [--seconds 5] [--size auto] [--steps 20] [--seed 1]
```
- The picture is frame one; `--size auto` keeps its shape at about 1280x704 pixels (multiples of 32). 24 fps;
  length rounds to 4n+1 frames (5 s = 121).
- Frames are saved by ComfyUI and encoded by ffmpeg (H.264, CRF 16): ComfyUI's SaveVideo has nested settings
  the API cannot fill reliably.
- The prompt describes motion and camera, not the look (the picture gives the look): "the fox lifts its head,
  its nine tails sway slowly, the camera drifts left". Keep it to one action.
- Runs share the ComfyUI started by `../ultratex/scripts/run.py` (log `../ultratex/.work/comfyui.log`).
- Check the MP4 with ffprobe (frames, length) and a few extracted frames (that it moves, no melt) before sending.

## Where things go
- Results live in Google Drive (owner, 20 Oct 2026: results to Drive, models stay in `C:\AI`): clips go in `G:\My Drive\KryticalNova\AI Outputs\Video\<Batch>\` (pass it as `--out`). A still of a pet: render it from its .blend
  (see the first test's start frame, `WanTest_2026-10-20/KitsuneFox_start.png`).

## Close ComfyUI when done (owner, 23 Oct 2026)
ComfyUI must not sit open holding the graphics card and RAM. When the job is finished, stop it: the scripts call
`stop_comfy()` from `ultratex/scripts/run.py` at the end (it waits for an empty queue) unless `--keep-open` is passed;
use that only between back-to-back runs of one task, then stop it. This applies even if another session started it.
