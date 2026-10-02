# Zone 01 — Starter Town, Outer Map & Portal — Plan

## Context
The game has only one scene, `SampleScene`. It holds a placeholder terrain plus all the systems: networking, the sky and weather, the camera, and a runtime NavMesh. The user wants the first real map, laid out like their reference image:
- a walled **starter town** in the centre, built from **small** POLYGON Sci-Fi City buildings. Its layout follows the second reference: a central plaza with vendor NPCs around it, a main gate, and a side wing.
- an **outer field** of rolling terrain with rivers, corner lakes, dirt paths and a mountain rim.
- a **portal** in the north-east corner, beyond the river, using the PolygonParticleFX portal VFX. The next map doesn't exist yet.

Decisions the user made:
- The map goes in a **new scene**.
- The terrain is a **generated base that the user then polishes by hand**.
- NPCs are **placed and clickable**, but their interactions are stubs for now.
- The portal is a **stub checked by the server**.

Everything follows the existing pattern: regenerable editor menus under **Krytical Nova → …**, as in `Assets/_Game/World/Editor/WorldEnvironmentSetup.cs`. Scene edits go through the live Editor via the unity-cli skill, not hand-written YAML.

## Findings that shape the plan
- The main Sci-Fi City materials (`PolygonScifi_01_*`, roads) use Shader Graph and work in URP. **The pack's FX materials** (`FX_*`, `Halo_01`, `Hologram_Base_01`) and **all PolygonParticleFX materials** use built-in/legacy shaders and show up magenta. Any FX we use (portal, beacon) needs URP copies. The existing `WeatherVfxBuilder.ToUrp()` / `SetupUrpParticle()` in `Assets/_Game/World/Editor/WeatherVfxBuilder.cs` already do this conversion, so I'll make them `internal` and reuse them.
- The project has no NPC, interaction, teleport, spawn-point or scene-loading code.
- `PointAndClickPlayer.TryMoveToPointer()` (`Assets/_Game/Gameplay/Player/Runtime/PointAndClickPlayer.cs:349`) already raycasts and checks `IDamageable` first. That is where NPC clicks hook in.
- The UI is built with UI Toolkit (`ProgressionHUD`, `CombatFeedbackManager`), so the NPC panel will use it too.
- `RuntimeNavMeshBuilder` bakes navigation at startup when none is baked. For the new scene I'll bake navigation in the Editor instead, because the map is much bigger.
- `SpatialInterestManager` culls objects beyond 75 m. NPCs and the portal are scene objects, so the server-relevant ones get `AlwaysRelevantNetworkObject` only where needed.

## 1. Scene
- Copy `SampleScene` to `Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity`. It keeps the NetworkManager, camera rig, World Environment, Environment Presenter, Global Volume, offline Player and UI.
- In the copy, remove the old Terrain and the Combat Dummy (move the dummy to the training yard).
- Make the new scene **build index 0**. SampleScene stays in the list as a sandbox.
- Root hierarchy: `--- Environment ---` (Terrain, Water, Nature), `--- Town ---` (Walls, Buildings, Props, Lights), `--- NPCs ---`, `--- Portals ---`, `--- Spawn ---`, then the existing systems.

## 2. Terrain — `Assets/_Game/World/Editor/ZoneTerrainGenerator.cs`
Menu: **Krytical Nova → Zone → Generate Zone01 Terrain**. Settings live in a `ZoneTerrainProfile` ScriptableObject at `Assets/_Game/World/Zones/Zone01/ZTP_Zone01.asset`, so they can be tweaked and the terrain regenerated.
- **Size:** 600 × 600 m, 80 m height range, heightmap 513, alphamap 512. Data goes in `Assets/_Game/World/Zones/Zone01/Zone01_TerrainData.asset`. The old `Assets/_Game/Scenes/Development/SampleScene_TerrainData.asset` is not touched.
- **Height layers, combined in order:**
  1. Low FBM noise for rolling hills.
  2. A **mountain rim** that rises steeply over the outer 40 m. It keeps players inside and frames the edges, as in the reference.
  3. A **flattened town plateau**, 150 × 150 m in the centre, with a smooth 25 m falloff.
  4. **River channels** carved along spline control points that follow the reference: one river from the NE corner curving west along the north edge of town, and its branch flowing south-east.
  5. **Lake basins** at the NW, W, SW-bottom and SE positions.
  6. A flattened **portal pad** in the NE corner.
  7. Flattened **path strips** from each town gate out to the field and the portal, plus a bridge crossing point.
- **Terrain layers:** new `.terrainlayer` assets in `Assets/_Game/World/Zones/Zone01/TerrainLayers/`, made from the PolygonGeneric `Generic_Grass` (+ normals), `Generic_Dirt` and `Generic_Rock` textures plus Sci-Fi City `Dirt_01`.
  - Painted by rule: grass by default, rock on slopes over 30°, dirt on paths and riverbanks, darker dirt under the town.
- **Water:** flat plane meshes at water level in the river and lakes, using a simple URP transparent water material tinted to the comic palette. They are on the Default layer, not Ground, so clicks don't walk into water. NavMesh modifiers mark the water as not walkable.
- **Details:** scatter Sci-Fi City `SM_Env_Tree_01–03`, `SM_Env_Small_Rocks_01/02` and `SM_Env_Grass_Patch_*` (or PolygonGeneric nature props) as terrain trees and details using a seeded Poisson spread. The town plateau, paths and water are kept clear.
- **Protecting hand edits:** the generator refuses to overwrite an existing TerrainData unless **Force Regenerate** is ticked. A separate menu, **Repaint Layers Only**, reruns the splat rules without touching heights.
- The Terrain GameObject goes on the **Ground** layer, so click-to-move works.

## 3. Town — `Assets/_Game/World/Editor/StarterTownBuilder.cs`
Menu: **Krytical Nova → Zone → Build Starter Town**. Pieces are placed from a layout table in code, using **small buildings only**. Everything is parented under `--- Town ---` and marked static. Rebuilding deletes and recreates that root.
- **Walls:** a perimeter of `SM_Bld_City_Wall_01` with `SM_Bld_City_Wall_Tower_01` on the corners, about 120 × 110 m. There are gate gaps north (the main gate, facing the portal road), south, east and west, matching the four roads in the reference.
- **Plaza:** `SM_Env_Ground_Tile_*` / `SM_Env_Floor_01` paving in the centre, `SM_Env_Sidewalk_*` borders and `SM_Prop_Streetlight_01` around the edge. The centrepiece is `FX_Beacon_01` or `FX_Hologram_Base_01`, converted to URP. It plays the role of the yellow centre square in the reference.
- **Main gate:** on the north gate, `SM_Bld_Security_WatchStation_01` on each side plus a `FX_LightRayCube_01` / beam (URP copy). This echoes the blue gate beam in the reference.
- **Buildings, small only, one per vendor where it fits:**

| Spot | Building / props | NPC |
|---|---|---|
| NE plaza | `SM_Bld_Shop_Open_01` + `SM_Prop_MarketTable/Cover_*` | Herb Master |
| SW | `SM_Bld_Chopshop_01` + barrels/crates | Blacksmith |
| N training yard | open yard, `SM_Prop_Barrier_*`, Combat Dummy moved here | Trainers ×2 |
| E wing (the reference's side annex) | `SM_Bld_Bank_01` + `SM_Prop_Bank_Booth_01` | Trader |
| W | `SM_Bld_FoodHole_01` + vending machines | Mall Manager |
| N gate | `SM_Bld_Security_WatchStation_01` | Guard Captain |
| SE | `SM_Building_Shack_01/02` + `SM_Prop_Atm_01` | Agent |
| Filler | `SM_Bld_Background_Small_*`, `SM_Building_Shack_*` along the walls | — |

- **Props for life:** benches, trash cans, dumpsters, hedges, `SM_Env_Railing_*`, and a few signs.
- The **town paving** and all ground tiles go on the **Ground** layer. Buildings, walls and props get colliders and carve the NavMesh, either as non-walkable or by being excluded from the Ground layer, so clicks on them aren't treated as move targets.
- Extra slots from the first image (Elder, Tailor, Collector, Gate Master, Guild Director) are left as empty, named placeholder transforms for later.

## 4. NPCs (runtime) — `Assets/_Game/Gameplay/NPC/Runtime/` (namespace `KryticalNova.NPC`)
- **`NpcDefinition`** ScriptableObject, created with `[CreateAssetMenu(menuName = "Krytical Nova/NPC/Definition")]` like the existing data SOs. Fields: `displayName`, `NpcRole` enum (HerbMaster, Blacksmith, Trainer, Trader, MallManager, GuardCaptain, Agent), `title`, `greeting`, `interactRange = 3 m`. One asset per vendor goes in `Assets/_Game/Gameplay/NPC/Data/Assets/`, each with the role description the user gave as its placeholder text.
- **`NpcInteractable`** (MonoBehaviour plus a capsule collider on a new **Interactable** layer) implements `IInteractable { string DisplayName; Vector3 InteractPoint; void Interact(GameObject player); }`. Turning the NPC toward the player and opening the panel happen locally. These are client-side stubs, and the server-validated `RequestInteractServerRpc` comes later with real shops and quests (marked `// TODO(server)`).
- **`PointAndClickPlayer.TryMoveToPointer` edit:** right after the `IDamageable` branch, if the hit has an `IInteractable`:
  - out of range: move to the nearest NavMesh point within range, and keep a pending interact target.
  - in range: call `Interact`.
  - A check in `Update` fires the pending interaction on arrival. Clicking elsewhere clears it.
- **`NpcNameplate`:** a world-space label for the name and title above the head. I'll reuse the UI Toolkit world-label approach from `CombatFeedbackManager` if it fits, or otherwise a single UI Toolkit overlay that projects the visible plates and culls by distance. There is no per-NPC Canvas.
- **`NpcDialoguePanel`:** a UI Toolkit panel (UXML + USS in `Assets/_Game/UI/NPC/`) showing the name, role title and greeting, with a disabled "Shop / Quests — coming soon" button and Close (ESC closes it too).
- **Models**, from Sci-Fi City `Prefabs/Characters`:
  - Herb Master: `SM_Chr_Medical_Male_01`
  - Blacksmith: `SM_Chr_Junky_Male_01` or a similar worker
  - Trainers: `SM_Chr_Monk_Male_01` and `SM_Chr_CyborgNinja_01`
  - Trader: `SM_Chr_Rich_Female_01`
  - Mall Manager: `SM_Chr_Hologram_Female_01`
  - Guard Captain: `SM_Chr_Cop_01`
  - Agent: `SM_Chr_Hacker_Female_01`
  - Each is a prefab variant in `Assets/_Game/Characters/NPC/Prefabs/` with an Animator playing an existing humanoid idle clip from the project, so none stand in T-pose.
- NPCs are plain scene objects, not NetworkObjects, for now. They're static and identical on every peer, so they cost no bandwidth.

## 5. Portal — `Assets/_Game/World/Runtime/Portals/`
- **VFX:** `Assets/_Game/VFX/Environment/Portals/PFX_Portal_Zone01.prefab` is a URP copy of PolygonParticleFX `FX_Portal_Round_01`, with `FX_GlowSpot`/`FX_Sparkle_Orbit` accents. It's built by a `PortalVfxBuilder` menu that reuses the `WeatherVfxBuilder` conversion. Particle lights are removed and particle counts capped.
- **`ZonePortal`** (`NetworkBehaviour` on a scene-placed NetworkObject with a trigger collider):
  - Field `destinationZoneId` (string, empty for now) and `arrivalPointId`.
  - **Server:** `OnTriggerEnter` with a player NetworkObject checks that the player is really inside the radius and adds a 3 s cooldown per player. It then calls `IZoneTransferHandler.TryTransfer(player, destination)`.
  - **Default handler:** `SealedPortalHandler` sends the owning client a `PortalMessageClientRpc("The way beyond is sealed… for now.")`, shown through the existing `CommandMessage` / HUD message path.
  - Later the handler swaps to one built on `ZoneSessionManager` or a scene load (marked `// TODO(zone2)`).
  - Offline (no NetworkManager): the same check runs locally.
- A small stone/sci-fi pad (`SM_Env_Ground_Tile_*` ring plus `SM_Prop_Barrier_*`) sits on the flattened NE terrain pad. A dirt path leads there from the north gate across a bridge (`SM_Env_Railing` + floor tiles) over the river.

## 6. Spawn and navigation
- **`PlayerSpawnPoint`** markers (several, around the plaza). `NetworkConnectionManager` / connection approval sets the spawn position and rotation from a random free spawn point. The offline Player is placed on the plaza, so `OfflinePlayerHandoff` keeps working unchanged.
- **NavMesh:** the scene's `Navigation` NavMeshSurface collects the Ground layer (terrain + town paving), with `NavMeshModifier`s making water and building footprints not walkable. It's **baked in the Editor**, so `RuntimeNavMeshBuilder` reuses the baked data. Rebaking is needed after hand terrain edits, and the menu **Krytical Nova → Zone → Rebake NavMesh** does it.

## 7. One-click setup
**Krytical Nova → Zone → Build Zone01 (All Steps)** creates the scene if it's missing, then runs in order: terrain → town → NPCs → portal VFX + placement → spawn points → NavMesh bake → build settings. Each step is also its own menu item. Running it again never overwrites hand terrain edits or tuned SO assets.

## Critical files
- New editor files: `Assets/_Game/World/Editor/ZoneTerrainGenerator.cs`, `StarterTownBuilder.cs`, `PortalVfxBuilder.cs`, `Zone01Setup.cs`.
- New runtime files: `Assets/_Game/Gameplay/NPC/Runtime/*` (NpcDefinition, NpcInteractable, IInteractable, NpcNameplate, NpcDialoguePanel), `Assets/_Game/World/Runtime/Portals/*` (ZonePortal, IZoneTransferHandler, SealedPortalHandler), `Assets/_Game/World/Runtime/ZoneTerrainProfile.cs`, `Assets/_Game/Networking/Runtime/PlayerSpawnPoint.cs`.
- Edited files:
  - `Assets/_Game/Gameplay/Player/Runtime/PointAndClickPlayer.cs`: interaction branch and pending interact.
  - `Assets/_Game/World/Editor/WeatherVfxBuilder.cs`: make `ToUrp` / `SetupUrpParticle` internal.
  - `Assets/_Game/Networking/Runtime/NetworkConnectionManager.cs`: spawn point in connection approval.
  - The Tags & Layers settings: add an Interactable layer.
- Assets: `Assets/_Game/World/Zones/Zone01/*`, `Assets/_Game/Gameplay/NPC/Data/Assets/*`, `Assets/_Game/Characters/NPC/Prefabs/*`, `Assets/_Game/VFX/Environment/Portals/*`, `Assets/_Game/UI/NPC/*`.
- Save a copy of this plan as `Assets/_Game/_Docs/Plans/zone01-starter-town-plan.md` (the existing convention).

## Verification
1. Run **Build Zone01 (All Steps)** in the live Editor (unity-cli). The console should show no errors and no magenta materials. Take a top-down screenshot and compare it with the reference: town centred, rivers and lakes placed, portal in the NE.
2. **Play offline:** spawn on the plaza and walk out every gate. Water and walls should block movement, and buildings shouldn't count as move targets. Cross the bridge to the portal: walking in shows the "sealed" message, and the cooldown works.
3. Click each of the 7 NPCs: from afar the player walks over, then the panel opens with the right name, role and greeting. ESC closes it. Nameplates are readable at night and in fog.
4. **Host + Client** (Multiplayer Play Mode): both spawn at plaza spawn points. The portal message appears only on the client that entered. NPC clicks work on the client.
5. **Hand-polish check:** sculpt a hill with Terrain Tools, then rerun **Build Starter Town** and **Repaint Layers Only**. The sculpt must survive. Then run **Rebake NavMesh**.
6. **Performance:** Frame Debugger / Profiler in the town at 1080p. Static batching is active, the portal and beacon VFX have no particle lights, and NPC/portal/nameplate scripts allocate 0 B GC per frame.

---

## Implementation status (2026-10-01)

Built and verified in the Editor, both offline and as a local Host. **Krytical Nova → Zone → Build Zone01 (All Steps)** rebuilds everything. The individual steps are: Generate Zone01 Terrain (plus a Force Regenerate variant), Repaint Terrain Layers Only, Rescatter Trees and Rocks, Build Portal + Beacon VFX, Build Starter Town, Create NPC Assets, and Rebake NavMesh.

Differences from the plan above:
- **Town size:** the town is more compact than planned. The walls sit at ±50 m and the plaza at ±17.5 m, because a 120 m town built from small buildings looked empty. The terrain plateau `townHalfSize` is 62.
- **Town ground:** the terrain layer is `TL_TownStone` (Generic_Concrete). Sci-Fi City `Dirt_01` rendered almost white.
- **Tiles:** the "Ground_Tile_Base" pieces are only an underside. The bridge deck and the portal pad put a `Ground_Tile_01` on top of each base tile.
- **Portal pad:** it is ringed by `SM_Bld_Power_04` pylons instead of barrier posts.
- **Spawning:** connection approval stays off. `PlayerSpawnService` places every player, the host's included, on `OnClientConnectedCallback`, before the player's first `Start`.
  - This fixes a pre-existing problem: `Player.prefab` is stored at (437, 1, 305), which is off this map, so the player failed to attach to the NavMesh.
- **Water:** besides the Not Walkable collider, a `NavMeshModifierVolume` under the water sheet stops riverbeds and lake floors from baking as walkable islands.
- **Click mask:** the `Interactable` layer (6) was added to `PointAndClickPlayer.raycastLayers` in the scene and on `Player.prefab`.
- **Pointer blocking:** `ClientPresentationRegistry` now holds several pointer blockers, so both the HUD and the NPC UI block world clicks.

Verified:
- Clicking an NPC walks the player over and opens the dialogue.
- The dialogue closes when the player walks away or presses ESC.
- 8 nameplates are shown and the NPC idle animations play.
- The portal sends the "sealed" message to the entering client by RPC when hosting, or directly when offline.
- NavMesh paths are complete from the plaza to all 4 gates, across the bridge, to the portal and to every NPC.
- Water, riverbeds and lake floors are not walkable.

Known noise:
- Every terrain generate logs "tree … must use the Nature/Soft Occlusion shader". These are Synty mesh trees used as terrain trees. They render correctly, but they have no billboards.
- The pack's convex collision meshes log "Mesh object at version 8" warnings.
- One "Ran out of Graphics Ring Buffer space" warning appears on entering Play mode.

Not yet verified:
- A remote client on a second instance.
- Day/night readability of the nameplates.
- Moving hand-sculpted terrain and then running Rebake NavMesh.

TODO(zone2): swap `ZonePortal.TransferHandler` (`SealedPortalHandler`) for real zone travel once the next map exists.
TODO(server): route NPC shops and quests through a server-validated interact RPC.
