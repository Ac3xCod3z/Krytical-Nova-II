# Network Runtime Wiring: AOI, VFX Pool, Zone Gate, Input Routing

## Objective
Bring the three inert networking subsystems online (spatial interest, combat VFX pool, zone transitions) and route weapon-draw and cancel input through the Input Action asset so UI suppression is respected.

## Changes
- 'Assets/Scripts/Networking/SpatialInterestManager.cs' - self-reconciling entity registration
- 'Assets/Scripts/Networking/NetworkCombatVFXPool.cs' - timed auto-return so pooled FX actually recycle
- 'Assets/Scripts/Networking/ZoneSessionManager.cs' - transitions gated behind an explicit flag
- 'Assets/Scripts/Combat/PlayerMeleeCombat.cs' - spawn pooled impact FX on accepted strikes
- 'Assets/Scripts/Player/PointAndClickPlayer.cs' - route draw/cancel through actions, not `Keyboard.current`
- 'Assets/InputSystem_Actions.inputactions' - add `ToggleWeapon`, delete the dead legacy `Player` map
- 'Assets/Prefabs/Networking/NetworkManager.prefab' - `predefinedPools` configured

## Relevant Assets and Quirks
- `NetworkManager.OnObjectSpawned` does not exist in Netcode for GameObjects 2.13.3 (reflection: not found), and `NetworkSpawnManager` exposes only `PlayerObjects`, `SpawnedObjects`, `SpawnedObjectsList` with no spawn event. Self-reconciliation on the existing server tick is the only zero-touch registration route. `NetworkObject.CheckObjectVisibility` is confirmed present as a `VisibilityDelegate` field.
- `SpatialInterestManager.Update()` already early-returns unless `nm.IsServer` and already ticks on `updateInterval` (0.5s) - the reconcile pass belongs in that tick. `NetworkShow` / `NetworkHide` are already implemented in `UpdateAllVisibility` and are what actually applies culling.
- 'Assets/DefaultNetworkPrefabs.asset' holds exactly one entry (the Player prefab). The Combat Dummy is an in-scene-placed `NetworkObject`, so it also appears in `SpawnedObjectsList`; the existing `entity.OwnerClientId == clientId` guard already prevents hiding a client's own object.
- `NetworkCombatVFXPool.Spawn` has no recycle path today - `Despawn` exists but nothing calls it and pooled particles never return, so the pool only grows. `PoolEntry` carries only `poolKey` / `prefab` / `initialCount`, so lifetime must come from the caller.
- Usable impact FX prefabs already exist: 'Assets/Synty/PolygonParticleFX/Prefabs/FX_Explosion_01.prefab', `FX_Electricity_01.prefab`, `FX_BloodSplat_01.prefab`, `FX_Fire_01.prefab`.
- Only `PointAndClick` and `SkillCommands` are read by code (every `FindActionMap` call verified). The `UI` map **must stay**: `EventSystem` carries an `InputSystemUIInputModule` and the Player's `PlayerInput` links `uIInputModule: "EventSystem"`. Only the legacy `Player` map is dead - and its `Sprint` binding is the asset's only `leftShift` binding, so `ToggleWeapon` must bind both shifts itself.
- `PlayerSkillHotbar.InputSuppressed` is already set during HUD drags and must gate the new action reads.
- `PointAndClickPlayer.Update` currently also reads `Keyboard.current.escapeKey`, duplicating the existing `CancelOrbit` action - collapse to one source.

## Steps
- [ ] 1. Add `ReconcileRegistrations()` to `SpatialInterestManager`, called from the existing server tick: register any `SpawnedObjectsList` entry not yet tracked (assigning `CheckObjectVisibility` once) and unregister objects no longer spawned [depends: none]
- [ ] 2. Add a `logCulling` toggle plus `Debug.Log` on first registration and on each visibility change, so AOI can be verified without console spam [depends: 1]
- [ ] 3. Add `Spawn(string poolKey, Vector3 position, Quaternion rotation, float lifetime)` to `NetworkCombatVFXPool` that returns the instance through `Despawn` after the lifetime [depends: none]
- [ ] 4. Call the new pooled overload from `PlayerMeleeCombat.ExecuteAuthoritativeStrike` at the target position, and set `predefinedPools` on `NetworkManager.prefab` with key `hit_impact` -> `FX_Explosion_01.prefab`, `initialCount` 12 [depends: 3]
- [ ] 5. Add `[SerializeField] private bool allowZoneTransitions;` to `ZoneSessionManager`; make `TransitionToZoneAsync` log an error and return false when disabled, leaving it off on the scene component [depends: none]
- [ ] 6. Add a `ToggleWeapon` Button action to the `PointAndClick` map bound to `<Keyboard>/leftShift` and `<Keyboard>/rightShift`, and delete the unused legacy `Player` action map [depends: none]
- [ ] 7. In `PointAndClickPlayer.Update`, replace the `Keyboard.current` shift and escape reads with the `ToggleWeapon` and `CancelOrbit` actions, gated by `hotbar.InputSuppressed` [depends: 6]

## Verify
- Host + client pair: move one client past 75m from the other. With `logCulling` on, the server must log a visibility drop then a re-show on return, and the remote avatar must disappear and reappear for the observer.
- Confirm a client is never hidden from itself.
- Strike the Combat Dummy: pooled impact FX appear, and after ~50 hits the Hierarchy must show reused instances rather than unbounded growth.
- With transitions disabled, calling `TransitionToZoneAsync` must log a clear error and return false while the client stays connected.
- Play Mode: Shift draws and sheathes the weapon; Escape still cancels attack, ends orbit and closes HUD windows; while dragging a hotbar tile, Shift must not toggle the weapon.
- The `Player` map is gone from the Input Actions asset and hotbar/window keys still work.
