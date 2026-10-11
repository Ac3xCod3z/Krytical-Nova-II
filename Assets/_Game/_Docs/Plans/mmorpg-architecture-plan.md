> **11 Oct 2026:** the plan for dedicated servers, Lines, security, saving across servers and the beta is
> now `online-roadmap.md` with its phase documents. It replaces Phase 4 below ("Scalability, Interest
> Management & MMO Performance") and the "Production MMORPG Phase" notes. Phases 1 to 3 below are built.

# Project Overview
- **Game Title**: Krytical Nova II
- **High-Level Concept**: A martial-arts action MMORPG featuring point-and-click movement, fluid vertical traversal jumps, martial arts skill progression, and real-time stance/buff-driven combat across shared persistent zones.
- **Players**: Massively Multiplayer Online (Designed from day one for large-scale world scaling with hundreds+ concurrent players across partitioned zone shards down the road, while utilizing lightweight local host/client prototyping for zero-cost rapid development today).
- **Inspiration / Reference Games**: Lineage II, Diablo, Albion Online, Ragnarok Online.
- **Tone / Art Direction**: Western comic-book action RPG — bold ink outlines, cel-shaded color blocking, dramatic comic-panel framing, and punchy onomatopoeia-style combat feedback with fluid motion and dynamic visual effects. Twelve Sky 2 is used as a mechanics/systems reference only, not as an art reference.
- **Target Platform**: PC (Standalone Windows 64-bit).
- **Screen Orientation / Resolution**: Landscape 1920x1080 (and arbitrary desktop widescreen resolutions).
- **Render Pipeline**: Universal Render Pipeline (URP 17.6.0).

---

# Game Mechanics

## Core Gameplay Loop
1. **Explore & Navigate**: Players traverse persistent open zones using point-and-click navigation and Chi-powered martial jumps across terrain and obstacles.
2. **Engage & Target**: Click-to-target enemies, monsters, and rival players. Real-time positioning dictates melee attack ranges, line-of-sight, and skill telegraphs.
3. **Execute Martial Skills**: Activate stance buffs (e.g., Heavenly Shield, Fierce Tiger, Shadow Dance), spend Chi and Stamina resources, and trigger combat skills from a quick-access hotbar.
4. **Resolve Authoritative Combat**: Damage, defenses, critical strikes, dodges/evasions, shield shatters, and reflect counters are computed authoritatively on the server using `CombatCalculator`, with instantaneous client-side audio/visual feedback numbers via `CombatFeedbackManager`.
5. **Progress & Scale**: Earn experience, allocate skill points, unlock rare martial arts treatises/books, and upgrade character stats to conquer higher-tier zones and boss encounters.

## Controls and Input Methods
- **Left Mouse Button (LMB)**: Click to move (raycasted to Ground layer) or click on an entity to target/attack.
- **Right Mouse Button (RMB)**: Click-and-drag to orbit the camera smoothly around the player without modifying movement commands.
- **Mouse Wheel**: Smooth zoom in/out with obstacle-aware Cinemachine distance clamping.
- **Hotbar Keys (1–5 / Q, W, E, R, Space)**: Instant trigger for martial skills, stance toggles, and Chi jumps.
- **ESC Key**: Deselect target or open settings/session menu.

---

# UI
- **HUD Layout**:
  - **Top-Left**: Player status plate (Player Name, Level, Health Bar, Chi Bar, Stamina Bar, active Buff/Stance icons with duration countdowns).
  - **Top-Center**: Target vitals plate (Target Name, Level, HP Bar, Target Class/Type, active status effects).
  - **Bottom-Center**: Action Hotbar (Slots 1–8 with skill icons, keybind labels, cooldown sweeps, Chi cost indicators, active stance glow).
  - **Bottom-Right**: Traversal and system buttons (Map, Inventory, Character Sheet, Disconnect/Quit).
  - **Screen Space Floating**: World-space combat text (Damage numbers, Evade, Crit, Shattered, Reflect) generated via `CombatFeedbackManager`.
  - **Top-Right**: Server / Zone connectivity indicator (Ping/Latency, Zone Name, Player Count).

---

# Key Asset & Context

### 1. Networking Stack & Unity Multiplayer Services Integration
- **Package**: Netcode for GameObjects (`com.unity.netcode.gameobjects`) integrated with the main Unity multiplayer package (`com.unity.services.multiplayer`) and Unity Transport (`com.unity.transport`).
- **Connection Model**:
  - *Development Phase*: Local host/client execution or mediated connectivity (brokering connections without requiring dedicated cloud servers or router port forwarding).
  - *Production MMORPG Phase*: Headless Dedicated Game Server (`UNITY_SERVER`) builds running zone instances, with clients joining zones via session discovery and automatic pairing.
- **Core APIs & Types**:
  - `Unity.Services.Core.UnityServices.InitializeAsync()`
  - `Unity.Services.Authentication.AuthenticationService.Instance.SignInAnonymouslyAsync()`
  - `Unity.Services.Multiplayer.IMultiplayerService` / `MultiplayerService.Instance`
  - `Unity.Services.Multiplayer.SessionOptions` with `.WithRelayNetwork()` or `.WithDirectNetwork()`
  - `Unity.Netcode.NetworkManager`, `NetworkBehaviour`, `NetworkVariable<T>`, `NetworkTransform`

### 2. Security & Anti-Cheat Principles (Server-Authoritative MMO Architecture)
- **Input as Intent**: The client sends only *user intents* (`RequestMoveDestinationServerRpc(Vector3 destination)`, `RequestCastSkillServerRpc(string skillId, ulong targetNetworkId)`, `RequestJumpServerRpc(Vector3 targetPoint)`). Clients never dictate their position, health, or combat damage.
- **Server-Side Validation**:
  - *Movement*: Server samples requested destination against NavMesh, verifies path validity, checks movement speed limits, and prevents teleportation or speed hacks.
  - *Combat*: `CombatCalculator.CalculateAttack()` executes strictly on the server. The server verifies target range, line-of-sight, resource costs (Chi/Stamina), and cooldown timestamps before dealing damage.
  - *Resources*: `PlayerResources` (HP, Chi, Stamina) are modified solely by server code and synchronized via read-only `NetworkVariable`s to clients.
- **Data Protection**: Sensitive player state (e.g., hidden inventory, account flags) is restricted to `NetworkVariableReadPermission.Owner` or kept strictly server-memory only.

### 3. Performance & MMO Scalability (High Player Density & Massive World Scaling)
- **Multi-Tier Spatial Interest Management (Area of Interest / AOI & Distance LOD)**:
  - In a large-scale MMORPG, a global broadcast is impossible ($O(N^2)$ network saturation). To scale beyond small limits to hundreds or thousands of players in a world:
    - *Tier 1 (Immediate Radius, 0–35m)*: Full simulation frequency (20–30 Hz), high-precision transforms, real-time animation parameter sync.
    - *Tier 2 (Medium Radius, 35–80m)*: Low frequency updates (5–10 Hz) with deadband interpolation and compressed rotation.
    - *Tier 3 (Distant / Culled, >80m)*: Complete network culling via NGO custom visibility callbacks (`NetworkObject.CheckObjectVisibility`). Entities outside the player's interest bubble transmit zero packets to that client.
- **Zone Sharding & Multi-Server Partitioning (Down-the-Road Scalability)**:
  - Scalability to massive player populations requires horizontal partitioning (world sharding):
    - The world is divided into discrete contiguous zones or dynamic shards, each hosted on an independent dedicated server process.
    - Seamless zone handoff architecture: when players approach zone boundaries, the client queries and seamlessly transitions to the neighboring zone session via the multiplayer service API, preserving player state without central server bottlenecks.
- **Network Representation (Proxy Pattern)**:
  - When spawning a player entity:
    - *IsOwner (Local Player)*: Enable `PlayerInput`, Cinemachine camera target, local audio listener, UI bindings, and click raycaster.
    - *Remote Player (Proxy)*: Disable `PlayerInput`, `CinemachineOrbitalFollow`, local UI raycasters, and local `NavMeshAgent` collision avoidance. Remote players receive position/rotation via smooth interpolated `NetworkTransform` (client-side prediction and interpolation).
- **Server Tick Budget & Zero-Allocation Hot Paths**:
  - Headless server tick rates optimized for MMOs (20 Hz fixed tick rate). *As built the tick rate is 30 (`NetworkManager.prefab`, `DedicatedServerOptimizer`); 30 is the figure the online plan uses.*
  - Pre-allocated reusable buffers, static structs for RPC serialization, and pooling for dynamic entities to maintain near-zero garbage collection on the server process.

---

# Implementation Steps

### Phase 1: Package Setup & Multiplayer Services Foundation
- **Step 1.1: Install Netcode for GameObjects**
  - **Description**: Add `com.unity.netcode.gameobjects` to `Packages/manifest.json` ensuring compatibility with Unity 6000.6 and the installed `com.unity.services.multiplayer` (v2.3.3) and `com.unity.transport` (v2.7.3).
  - **Assigned role**: developer
  - **Dependencies**: None
  - **Parallelizable**: No

- **Step 1.2: Implement NetworkConnectionManager**
  - **Description**: Create `Assets/_Game/Networking/Runtime/NetworkConnectionManager.cs` utilizing the main Unity multiplayer package (`IMultiplayerService` / `MultiplayerService.Instance`). Implement flows for:
    - Unity Services & Authentication initialization.
    - Starting a local development host (Host mode for immediate playtesting without servers).
    - Connecting as a client via mediated connectivity or direct IP.
    - Dedicated server session initialization prepared for headless deployment (`#if UNITY_SERVER`).
  - **Assigned role**: developer
  - **Dependencies**: Step 1.1
  - **Parallelizable**: No

- **Step 1.3: Create Network Bootstrap & HUD Scene Rig**
  - **Description**: Create a persistent `NetworkManager` prefab in `Assets/_Game/Networking/Prefabs/NetworkManager.prefab` equipped with `UnityTransport`, default player prefab slot, and a lightweight development connection overlay UI (`NetworkDebugHUD.cs`) allowing quick Host / Client / Server launches.
  - **Assigned role**: developer
  - **Dependencies**: Step 1.2
  - **Parallelizable**: Yes

---

### Phase 2: Player Controller & Movement Decoupling (Server Authority)
- **Step 2.1: Implement Networked Player Proxy Separation**
  - **Description**: Refactor `PointAndClickPlayer.cs` or create `NetworkedPointAndClickPlayer.cs` (inheriting `NetworkBehaviour`).
    - On `OnNetworkSpawn()`: If `!IsOwner`, disable `PlayerInput`, `CinemachineOrbitalFollow`, and local camera controls.
    - If `IsOwner`, bind local pointer clicks and transmit movement intents to the server.
    - Remote clients render interpolated movement via `NetworkTransform`.
  - **Assigned role**: developer
  - **Dependencies**: Step 1.3
  - **Parallelizable**: No

- **Step 2.2: Implement Server-Authoritative Movement & NavMesh Validation**
  - **Description**: On the server (Host or Dedicated Server), the player object executes `NavMeshAgent` path calculation upon receiving `RequestMoveDestinationServerRpc(Vector3 destination)`.
    - Server verifies destination is within playable zone bounds and samples valid NavMesh points using `NavMesh.SamplePosition`.
    - Server updates the entity position which is replicated smoothly to clients via `NetworkTransform`.
  - **Assigned role**: developer
  - **Dependencies**: Step 2.1
  - **Parallelizable**: No

- **Step 2.3: Networked Jump Motor Traversal**
  - **Description**: Adapt `PlayerJumpMotor.cs` for multiplayer:
    - Client requests jump via `RequestJumpServerRpc(Vector3 targetPoint)`.
    - Server validates takeoff point, landing NavMesh clearance, and Chi resource cost before confirming jump flight.
    - Traversal arc is broadcast via `JumpTriggeredClientRpc` so all nearby observers render the parabolic DOTween jump animation and landing dust simultaneously.
  - **Assigned role**: developer
  - **Dependencies**: Step 2.2
  - **Parallelizable**: Yes

---

### Phase 3: Server-Authoritative Combat, Buffs & Progression
- **Step 3.1: Networked Health, Chi & Resource Sync (`NetworkPlayerResources`)**
  - **Description**: Convert `PlayerResources.cs` to synchronize `Health`, `Chi`, and `Stamina` via `NetworkVariable<float>` with `NetworkVariableWritePermission.Server`.
    - Natural regeneration ticks run only on the server.
    - Clients listen to `OnValueChanged` to update `ProgressionHUD` smoothly.
  - **Assigned role**: developer
  - **Dependencies**: Step 2.1
  - **Parallelizable**: Yes

- **Step 3.2: Networked Combat Entity & Authoritative Attack Pipeline**
  - **Description**: Refactor `CombatEntity.cs` to inherit from `NetworkBehaviour`.
    - When player clicks an enemy to attack, client sends `RequestAttackEntityServerRpc(ulong targetNetworkObjectId)`.
    - Server resolves target existence, proximity check (e.g., within weapon range), executes `CombatCalculator.CalculateAttack()`, applies damage to the target entity, and broadcasts `CombatFeedbackClientRpc` for floating damage numbers and impact VFX.
  - **Assigned role**: developer
  - **Dependencies**: Step 3.1
  - **Parallelizable**: No

- **Step 3.3: Networked Skills & Stance Buff Management**
  - **Description**: Refactor `PlayerCombatSkills.cs` and `CharacterBuffManager.cs` to run authoritative cast validation on the server:
    - Client issues `RequestCastSkillServerRpc(string skillId)`.
    - Server verifies skill unlock, rank, cooldown timer, and Chi cost before applying buffs to `CharacterBuffManager`.
    - Stance states are synchronized via a lightweight bitmask `NetworkVariable<uint>` or active buff struct list.
  - **Assigned role**: developer
  - **Dependencies**: Step 3.2
  - **Parallelizable**: Yes

---

### Phase 4: Scalability, Interest Management & MMO Performance
- **Step 4.1: Multi-Tier Spatial Interest Management (Area of Interest / AOI)**
  - **Description**: Implement a hierarchical spatial partitioning system (`SpatialInterestManager.cs`) using NGO's custom visibility callbacks (`NetworkObject.CheckObjectVisibility`).
    - Divide zone into a 2D spatial grid (cells of 48m x 48m).
    - Only replicate NetworkObjects to clients whose avatars are within perception radius (~60-80m), culling distant objects completely.
    - Prevents quadratic network packet flooding ($O(N^2)$), enabling large player densities without overwhelming client bandwidth.
  - **Assigned role**: developer
  - **Dependencies**: Step 3.2
  - **Parallelizable**: Yes

- **Step 4.2: Network Object Pooling for Combat VFX & Projectiles**
  - **Description**: Create a client-side pooled visual feedback system (`NetworkCombatVFXPool.cs`) so that high-frequency spell impacts, slashes, and floating combat text do not cause GC alloc spikes or frame drops during large group battles.
  - **Assigned role**: developer
  - **Dependencies**: Step 3.2
  - **Parallelizable**: Yes

- **Step 4.3: Dedicated Server (`UNITY_SERVER`) Headless Optimization Pipeline**
  - **Description**: Configure assembly definition constraints and scripting defines (`UNITY_SERVER`) so the dedicated server builds compile completely headlessly (excluding rendering, audio listeners, and client cameras), stripping graphic memory overhead and maximizing tick budget throughput for cloud instances down the road.
  - **Assigned role**: developer
  - **Dependencies**: Step 1.2, Step 4.1
  - **Parallelizable**: Yes

- **Step 4.4: Zone Sharding & Session Handoff Architecture Foundation**
  - **Description**: Implement a zone management abstraction (`ZoneSessionManager.cs`) that handles seamless player handoff between independent zone server sessions. Prepares the architecture for horizontal scaling across multiple servers (hundreds to thousands of players across interconnected zones) when moving to cloud infrastructure.
  - **Assigned role**: developer
  - **Dependencies**: Step 1.2, Step 4.3
  - **Parallelizable**: Yes

---

# Verification & Testing
1. **Local Host/Client Dual-Instance Test**:
   - Run two instances (Editor PlayMode + Standalone Windows Build).
   - Host initializes in instance 1; Client connects to Host in instance 2.
   - Verify both avatars spawn on the NavMesh without camera conflicts.
2. **Movement & Traversal Validation**:
   - Issue click-to-move commands on client; verify smooth interpolation on host and vice-versa.
   - Execute martial jump; verify jump trajectory, landing, and Chi deduction sync correctly without desyncing NavMesh agents.
3. **Authoritative Combat & Anti-Cheat Validation**:
   - Attack the `Combat Dummy` from Client 1.
   - Confirm damage calculation executes on the server; floating combat text and HP reduction replicate accurately on both instances.
   - Attempt an illegal move (e.g., calling attack while out of range or spamming skills with 0 Chi); confirm server rejects the action gracefully.
4. **Proxy & Performance Check**:
   - Inspect remote client in Hierarchy: confirm `PlayerInput`, Cinemachine orbit, and local audio listener are disabled on proxies.
   - Verify network profiler shows bounded packet rates under 15-20 KB/s per client during active movement and skill casting.
