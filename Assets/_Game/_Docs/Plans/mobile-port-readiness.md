# Mobile port: readiness

Written 15 Oct 2026. **Nothing here is built, and the port is not started.** The owner plans a phone version "in the
near future", after the PC version is in top shape and the dedicated server works. This page records what already
exists, what the port will need, and in what order, so that work done before then does not make it harder.

## The rules that already hold

- **Author for the top, step down** (`Design/Rendering-Bible.md` section 1): a phone is the lowest tier, and every new
  effect must degrade cleanly on it, by skipping its pass or with a cheaper fallback.
- **Forward+ / Forward, never Deferred**: Deferred costs tiled phone GPUs too much memory bandwidth.
- **One master asset**, never a copy per platform: phones get smaller textures through import overrides and build
  profiles (`ProjectOrganization.md` section 10).
- **Phones join the same servers.** The server, the rules and the save do not change; only input, interface and
  graphics differ (`Plans/online-roadmap.md`).

## What exists today (checked 15 Oct 2026)

| Piece | State |
| --- | --- |
| Build support | Android and iOS modules are installed in Unity 6000.6.3f1 |
| Quality level | "Mobile" (level 0) is the default on Android and iPhone; it uses `Mobile_RPAsset` |
| Render pipeline | `Mobile_RPAsset`: Forward (`Mobile_Renderer`, no renderer features), render scale 0.8, 1024 one-cascade main shadow, no additional-light shadows, legacy light probes (no APV), depth and opaque textures on |
| Sky | `_KN_SKY_LOW` keyword for the cheap sky |
| Project settings | Android: IL2CPP, ARM64, minimum API 26, Vulkan then GLES3, ASTC default compression. iOS: Metal, iOS 15. Package names are still the template's |
| Input | Keyboard and mouse only in practice: of the game's own actions, only pointer position and move-to-point have touch bindings |
| Interface | UI Toolkit HUD scales with screen size from 1280×720; front-end uGUI scales from 3840×2160 on height; prompts show keyboard glyphs only |
| Build size | Not measured for a phone; the 469 MB Sidekick `Resources` folder ships in every build |

## What the port needs

1. **Build size and memory.** Addressables, local-packed, with the Sidekick parts out of `Resources`
   (`ProjectOrganization.md` section 10). A memory budget per device class, measured.
2. **Rendering fallbacks**, one per renderer feature: comic outlines and halftone (cheaper variant or texture-based),
   SSAO (off), sun shafts (off), volumetric fog and lamp cones (off; a height fog and soft local "fog card" meshes
   instead), Beautify (a lighter post or none), glow halo (cheaper variant), comic lettering (keep: it is the look).
   APV is replaced by legacy probes or a lighter APV bake. Grass density and draw distance drop.
3. **A frame cap** of 30 by default, 60 on capable phones, to keep heat and battery in check.
4. **Touch controls**: tap to move and target (half there), on-screen orbit and zoom, the Hotbar as touch buttons,
   interact. Window controllers that read the keyboard directly (`Design/15_movement_wasd_controller_navworld.md`
   section 5) need a focus and tap path. The keyboard-glyphs-only rule needs a touch exception.
5. **Interface size**: larger hit targets, a phone layout for the HUD and windows, safe areas.
6. **Device tiers** chosen at start (memory, GPU), each mapped to a preset.
7. **Store work**: real package names, signing, store pages, phone payments through the stores
   (`Plans/online-phase-10-payments.md`).

## Order

1. Now, on PC: keep every new effect degradable (the Rendering Bible rule); the pre-server rendering pass ties
   volumetric quality to the presets, which a phone tier then reuses.
2. With the second map or before the port: Addressables and the Sidekick move (both help PC too).
3. Port start: a phone test build on one Android device, measure, then fallbacks, touch controls, interface, tiers.

## To decide when the port starts

- **Batching on the phone** (batching pass, 18 Oct 2026; `Design/Rendering-Bible.md` section 7, "Batching"):
  `Mobile_RPAsset` has the GPU Resident Drawer off and the Forward path. Measure on a device: the Resident Drawer
  on Vulkan with Forward+ against the SRP Batcher with static batching (which merges meshes and so needs LODGroups,
  not Mesh LOD). Run the Batching Report on each zone; the shadow bands of Low apply as they are.

- Android first, or both at once?
- Which phones are the floor (the oldest that must hold 30 frames a second)?
- Does the phone get every system, or a lighter set at first?
