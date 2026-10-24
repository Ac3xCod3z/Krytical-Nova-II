---
name: ultratex
description: Texture a 3D model from one reference picture with UltraTex (FLUX.2 Klein + UltraTex LoRAs in a local ComfyUI), run hidden in the background, and hand the owner the textured result as a .blend opened in Blender with a 6-view review sheet. Use whenever the user wants to texture, paint, colour or re-texture a mesh from an image or concept art, mentions UltraTex, ComfyUI, Trellis or AI texturing, gives an FBX/OBJ/GLB plus a picture, or asks to run "the texturing workflow" - even if they do not name the skill.
---

# UltraTex texturing (ComfyUI, driven by me)

The owner gives a mesh and a picture; I run everything; they get the result in Blender.
Never ask the owner to open ComfyUI. Never export to FBX or Unity unless they say so.

## Install (already done once, 20 Oct 2026)
- One click: `C:\AI\Install Texture Pipeline.bat` (re-runnable: skips finished parts, resumes downloads).
  It runs ComfyUI-Easy-Install, then the PixelArtistry UltraTex installer (`NoPause`, nodes pinned),
  then downloads `flux-2-klein-base-9b-Q8_0.gguf` from unsloth.
- **No Hugging Face account.** The gated fp8 Klein model is skipped on purpose; we use the low-VRAM GGUF
  workflow (`PixelArtistry_UltraTex_03_LowVRAM_GGUF.json`) with the open Q8_0 GGUF (Q4_0 is the fallback
  in `config.json` -> `unet`).
- Text encoder: the fp8 `qwen_3_8b_fp8mixed.safetensors` through the stock `CLIPLoader` (run.py swaps the node;
  the GGUF loader misreads that file as the 4B shape). `config.json` -> `clip`.
- Norton inspects HTTPS: uv needs `UV_NATIVE_TLS=1` / `UV_SYSTEM_CERTS=1` (the installer sets them); Python's
  ModelScope downloader fails, so the installer fetches the UltraTex LoRAs + decoder with curl from modelscope.ai.
- Never edit `Install Texture Pipeline.bat` while it runs (cmd reads a running .bat by byte offset).
- Shells here set `NoDefaultCurrentDirectoryInExePath=1`; the installer clears it.
- Licence: UltraTex code MIT; FLUX.2 Klein weights FLUX Non-Commercial v2.1. BFL claims no ownership of
  outputs, but running the model is licensed non-commercially - the owner checks bfl.ai/licensing before
  shipping these textures in the paid game. Say this once when a texture is meant for the shipped game.

## Run
```
python -I .claude/skills/ultratex/scripts/run.py <mesh> <picture> --name <Name_v01> --out <folder> [--res 1024] [--bake 2048] [--yaw DEG]
```
- Starts ComfyUI hidden if it is not up (log `.claude/skills/ultratex/.work/comfyui.log`), preps the mesh
  (`prep_mesh.py`: one mesh, one material, transforms applied, decimate over 1M faces, `--yaw` turns it),
  converts the installed workflow to an API prompt via `/object_info`, queues it, reports every 10 s,
  copies GLB + Albedo/Roughness/Metallic PNGs to `--out`, builds `<Name>.blend` + `<Name>_sheet.png`
  (`to_blend.py`) and opens Blender. Run it in the background and relay the 10 s reports.
- `--dry-run` writes `.work/last_prompt.json` without queueing. `--set NODE_ID.input=value` overrides any input.
- First run loads models (several minutes); later runs are faster while ComfyUI stays up.
- Send `<Name>_sheet.png` to the owner; the owner's eye decides. Name versions `_v01`, `_v02`.

## Picture rules (from the workflow author)
- The picture should match the **front camera**. A 3/4 picture: turn the mesh with `--yaw` so its pose
  matches, or use a front-facing picture. The back is always guessed.
- Lighting/glow in the picture gets painted into the albedo. Evenly lit pictures work best.
- `--keep-uvs` only for clean 0-1 UVs, one mesh, one material, no overlapping or mirrored islands.

## Where things go
- Results live in Google Drive (owner, 20 Oct 2026: results to Drive, models stay in `C:\AI`): texture runs go in `G:\My Drive\KryticalNova\AI Outputs\Textures\<Pet or asset>\` (pass it as `--out`), and ComfyUI's own
  files in `AI Outputs\ComfyUI` (`run.py` starts ComfyUI with `--output-directory` there). Only what the game uses is
  copied into the project. Earlier pet tests stay in `G:/My Drive/KryticalNova/Art/Blender/Pets/<Pet>/UltraTex/`.
- Pet lineup of every stage + the Storm Sovereign: `G:/My Drive/KryticalNova/Art/Blender/Pets/PetLineup.blend`
  (rebuild with `build_lineup.py` beside it; it only links the pet files).

## Close ComfyUI when done (owner, 23 Oct 2026)
ComfyUI must not sit open holding the graphics card and RAM. When the job is finished, stop it: the scripts call
`stop_comfy()` from `ultratex/scripts/run.py` at the end (it waits for an empty queue) unless `--keep-open` is passed;
use that only between back-to-back runs of one task, then stop it. This applies even if another session started it.
