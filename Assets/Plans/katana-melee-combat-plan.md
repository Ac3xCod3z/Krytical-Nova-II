# Project Overview
- **Game Title**: Krytical Nova II
- **High-Level Concept**: A martial-arts action MMORPG featuring point-and-click movement, fluid vertical traversal jumps, martial arts skill progression, and real-time stance/buff-driven combat across shared persistent zones.
- **Players**: Massively Multiplayer Online (Headless Dedicated Server & Host-Client prototyping, with server-authoritative combat, movement, and weapon replication).
- **Inspiration / Reference Games**: Lineage II, Black Desert Online, Vindictus, Albion Online.
- **Tone / Art Direction**: Stylized Eastern fantasy / Wuxia action RPG with dynamic martial arts animations, physical weapon hitboxes, and vibrant combat feedback.
- **Target Platform**: PC (Standalone Windows 64-bit).
- **Screen Orientation / Resolution**: Landscape 1920x1080 (and desktop widescreen resolutions).
- **Render Pipeline**: Universal Render Pipeline (URP 17.6.0).

---

# Game Mechanics

## Core Gameplay Loop
1. **Explore & Navigate**: The player traverses terrain using point-and-click navigation, with Animancer smoothly driving locomotion (Walk, Run) and multi-phase vertical jumps (Start, Loop, Land).
2. **Equip & Sheath Katana**: The Katana rests sheathed across the player's back (`spine_03` bone). Pressing `Shift` or clicking an enemy/dummy initiates a weapon draw animation (`Equip`), transferring the blade to the right hand (`hand_r / ik_hand_gun`). Pressing `Shift` while armed smoothly sheathes the Katana (`UnEquip`).
3. **Engage & Approach Target**: Clicking an enemy or Combat Dummy designates them as the active attack target. If the player is outside melee range (> 2.2m), the player draws the Katana and automatically pathfinds into striking distance.
4. **Execute 3-Part Attack Sequence**: Once in melee range, the player halts and executes a looping 3-hit basic attack sequence (`Attack_3Combo_1` -> `Attack_3Combo_2` -> `Attack_3Combo_3`). A brief recovery delay follows the 3rd strike before the combo restarts.
5. **Physical Weapon Hitbox & Server Authority**: During active weapon swing windows, the Katana's physical hitbox detects contact. The server validates that the attacker and target are within legitimate melee distance (preventing remote attack hacks) and calculates authoritative damage via `CombatCalculator.CalculateAttack()`.
6. **Continuous Auto-Attack & Interruption**: The attack sequence repeats until the target is defeated, or the player issues a movement command, selects another destination, or cancels targeting.

## Controls and Input Methods
- **Left Mouse Button (LMB)**:
  - *Click Ground*: Cancel attack/target and walk/run to clicked destination.
  - *Click Combat Entity*: Select target, draw Katana if sheathed, approach into melee range, and begin the 3-part attack sequence.
- **Left Shift Key**: Toggle drawing / sheathing the Katana (Equip / Unequip animations).
- **Right Mouse Button (RMB Drag)**: Orbit camera without altering movement or combat actions.
- **Mouse Wheel**: Smooth zoom in/out with obstacle-aware Cinemachine clamping.
- **Escape Key**: Cancel active target and break combat stance.

---

# UI

### HUD Layout & Wireframe
```
+----------------------------------------------------------------------------------+
| [Player Portrait]  HP [==================] 100%                                  |
| Level 10 Martialist Chi [================] 100%                                  |
|                                                                                  |
|                        +----------------------------+                            |
|                        | [Target Name: Combat Dummy]|                            |
|                        | HP [============.........] |                            |
|                        +----------------------------+                            |
|                                                                                  |
|                                                                                  |
|                                     (In-Game World)                              |
|                              [Damage: 142 CRIT] ^                                |
|                                   [Dummy]                                        |
|                                      |                                           |
|                                   [Player] <--- (Katana Drawn)                   |
|                                                                                  |
|                                                                                  |
|                                                                                  |
|  [Weapon Stance: DRAWN]                                                          |
|  [Shift]: Sheath/Draw                                                            |
|  +----------------------------------------------------------------------------+  |
|  | [1] Basic Atk | [2] Heavy Atk | [3] Dash | [4] Shield | [5] Buff | [...]   |  |
|  +----------------------------------------------------------------------------+  |
+----------------------------------------------------------------------------------+
```

### UI Elements & Usability Enhancements
- **Target Frame (Top Center)**: Displays targeted entity name, health bar, distance indicator, and active status effects. Appears automatically when clicked; clears when pressing Escape or targeting ground.
- **Weapon Stance Indicator (Bottom Left)**: Visual state badge displaying whether the Katana is Sheathed (`[SHEATHED - BACK]`) or Drawn (`[DRAWN - COMBAT]`), with hotkey prompt `[Shift]`.
- **Combat Feedback Numbers (`CombatFeedbackManager`)**: Dynamic floating text anchored over the target upon confirmed hitbox contact, color-coded for white (regular hit), yellow/gold (critical strike), blue (evaded/deflected), and red (reflected damage).
- **Hotbar & Ability Integration**: The 3-part strike combo acts as the primary auto-attack sequence when a target is engaged in melee range, without consuming Chi or competing with skill cooldowns.

---

# Key Asset & Context

### 1. 3D Character Model & Weapon Assets
- **Package**: `Powerful Sword Pack(Great Sword + Katana)` located at `Assets/Grruzam Powerful Sword Animation(Great Sword, Katana)/`.
- **Character Model**: `Modeling_T-Pose_Grrrru_Man(recommend).prefab` (Humanoid avatar `Modeling_T-Pose_Grrrru_Man(recommend)Avatar`, skinned mesh `Grruzam_Base_Modeling`, URP Lit materials).
- **Weapon Model**: `Modeling_Weapon_katana_Blade.prefab` (Mesh `Grru_Katana`, Material `GrruzamWeapon_mtl`).
- **Bone Sockets**:
  - *Sheathed Socket (Back)*: Parented under `root/pelvis/spine_01/spine_02/spine_03`, angled across the upper back.
  - *Equipped Socket (Hand)*: Parented under `hand_r/ik_hand_gun` with local rotation alignment.

### 2. Animation Clips (Humanoid Retargeting with Animancer)
- **Locomotion**:
  - Idle: `M_katana_Blade@Idle_ver_A.FBX`
  - Walk: `M_katana_Blade@Walk_ver_A_Front.FBX`
  - Run: `M_katana_Blade@Run_ver_A.FBX`
- **Jump**:
  - Takeoff: `M_katana_Blade@Jump_Start_ZeroHeight.FBX`
  - Airborne: `M_katana_Blade@Jump_Loop_ZeroHeight.FBX`
  - Landing: `M_katana_Blade@Jump_End_ZeroHeight.FBX`
- **Weapon Handling**:
  - Draw: `M_Big_Sword@Equip.FBX` (draw time ~0.45s)
  - Sheath: `M_Big_Sword@UnEquip.FBX` (sheath time ~0.50s)
- **3-Part Strike Sequence**:
  - Strike 1: `M_katana_Blade@Attack_3Combo_1_Inplace.FBX` (1.27s duration)
  - Strike 2: `M_katana_Blade@Attack_3Combo_2_Inplace.FBX` (1.17s duration)
  - Strike 3: `M_katana_Blade@Attack_3Combo_3_Inplace.FBX` (2.43s duration)

### 3. Server-Authoritative Multiplayer Architecture
- **State Synchronization**:
  - `NetworkVariable<bool> isWeaponDrawn`: Syncs Katana socket location (Back vs Hand) to all connected clients.
  - `NetworkVariable<ulong> targetEntityId`: Tracks the attacker's active target.
- **Server RPCs**:
  - `RequestToggleWeaponDrawServerRpc(bool draw)`: Validates and initiates weapon draw/sheath.
  - `RequestStartMeleeAttackServerRpc(ulong targetNetworkId)`: Validates target, approaches within striking distance, and starts combo.
  - `RequestMeleeStrikeDamageServerRpc(ulong targetNetworkId, int comboStep)`: Validates proximity ($\le 2.6\text{m}$ melee distance), calculates damage via `CombatCalculator`, and applies damage to target.
- **Client RPCs**:
  - `PlayComboStrikeClientRpc(int comboStep)`: Synchronizes the Animancer attack animation to all observing player proxies.
  - `PlayWeaponDrawClientRpc(bool draw)`: Synchronizes draw/sheath animation to observing proxies.

---

# Implementation Steps

### Phase 1: Model & Weapon Socket Rigging
- **Step 1.1: Rig Grruzam Character Model onto Player Prefab**
  - **Description**: Replace the primitive capsule/sphere visuals in `Player` with `Modeling_T-Pose_Grrrru_Man(recommend)`, configure the Humanoid `Animator`, and bind Animancer to the new avatar.
  - **Assigned role**: developer
  - **Dependencies**: None
  - **Parallelizable**: No

- **Step 1.2: Create Weapon Sockets & Weapon Hitbox Component**
  - **Description**: Add the `KatanaSheathSocket` on `spine_03` and `KatanaHandSocket` on `ik_hand_gun`. Instantiate `Modeling_Weapon_katana_Blade` and attach `WeaponHitbox.cs` with an oriented trigger collider along the blade.
  - **Assigned role**: developer
  - **Dependencies**: Step 1.1
  - **Parallelizable**: Yes

---

### Phase 2: Animancer Locomotion, Jump, and Weapon Draw State Machine
- **Step 2.1: Implement PlayerAnimancerMotor**
  - **Description**: Create `Assets/Scripts/Player/PlayerAnimancerMotor.cs` to drive Idle, Walk, Run, and Multi-stage Jump animations using Animancer based on `NavMeshAgent` speed and `PlayerJumpMotor` flight state.
  - **Assigned role**: developer
  - **Dependencies**: Step 1.1
  - **Parallelizable**: No

- **Step 2.2: Implement Katana Sheathing / Drawing Logic**
  - **Description**: Add draw and sheath state management in `PlayerAnimancerMotor` triggered via Left Shift or combat auto-engage. Animate the draw and smoothly reparent the Katana between the back socket and hand socket at the exact grasp keyframe.
  - **Assigned role**: developer
  - **Dependencies**: Step 1.2, Step 2.1
  - **Parallelizable**: No

---

### Phase 3: 3-Part Attack Sequence & Hitbox Collision Detection
- **Step 3.1: Implement 3-Part Strike Combo Controller**
  - **Description**: Create `Assets/Scripts/Combat/PlayerMeleeCombat.cs` managing the 3-step combo sequence (`Attack_3Combo_1_Inplace` -> `Attack_3Combo_2_Inplace` -> `Attack_3Combo_3_Inplace` -> pause delay -> repeat) with Animancer transitions.
  - **Assigned role**: developer
  - **Dependencies**: Step 2.2
  - **Parallelizable**: No

- **Step 3.2: Implement Weapon Hitbox Swing Windows & Strike Validation**
  - **Description**: Coordinate `WeaponHitbox.cs` with the attack animations so the trigger is only active during the physical swing arc. Detect contact with `IDamageable` colliders and pass hit data to the combat pipeline.
  - **Assigned role**: developer
  - **Dependencies**: Step 3.1
  - **Parallelizable**: Yes

---

### Phase 4: Target Auto-Approach, Attack Loop, and Server-Authoritative Synchronization
- **Step 4.1: Refactor PointAndClickPlayer Targeting & Auto-Approach Loop**
  - **Description**: Update `PointAndClickPlayer.cs` so clicking an entity:
    - Selects the entity and draws the Katana.
    - Pathfinds into melee striking range (2.2m).
    - Stops movement, faces target, and commences the continuous 3-part attack sequence until defeated or interrupted.
  - **Assigned role**: developer
  - **Dependencies**: Step 3.1, Step 3.2
  - **Parallelizable**: No

- **Step 4.2: Implement Server Authority & Multiplayer Strike Validation**
  - **Description**: Add server verification in `PointAndClickPlayer.cs` and `PlayerMeleeCombat.cs` ensuring strikes only resolve if the target is within true melee range ($\le 2.6\text{m}$). Execute `CombatCalculator.CalculateAttack()`, apply damage, and broadcast client RPCs for proxy animation and overhead combat numbers.
  - **Assigned role**: developer
  - **Dependencies**: Step 4.1
  - **Parallelizable**: No

---

# Verification & Testing
1. **Model & Visual Validation**:
   - Verify Grruzam character mesh, materials, and textures render correctly in URP.
   - Confirm Katana rests cleanly across the character's back when unequipped.
2. **Locomotion & Jump Testing**:
   - Issue move commands: verify Animancer smoothly blends Idle -> Walk -> Run.
   - Execute martial jump: confirm takeoff, loop, and landing animations play cleanly with DOTween traversal.
3. **Weapon Draw / Sheath Testing**:
   - Press Left Shift: verify character draws sword from back to hand; press again to return sword to back.
4. **Hitbox & Attack Sequence Testing**:
   - Click Combat Dummy from a distance: verify character draws sword, walks/runs into melee range (2.2m), stops, and loops Strike 1 -> Strike 2 -> Strike 3 -> slight delay -> repeat.
   - Confirm damage is only dealt when the physical Katana hitbox connects during swing frames.
   - Move away or click ground during combo: verify attack cleanly cancels and character moves to clicked location.
5. **Multiplayer Server Authority & Proxy Testing**:
   - Run Host and Client instances.
   - Verify proxy players see remote players walk, run, draw weapon, and swing in sync.
   - Test anti-cheat range check: attempt attack when artificially out of range and confirm server rejects the strike.
   - Check console logs to ensure 0 errors or warnings.
