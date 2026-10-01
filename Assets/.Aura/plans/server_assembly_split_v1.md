# Assembly Split & Headless Server Stripping

## Objective
Introduce assembly definitions so client-only presentation code is excluded from `UNITY_SERVER` builds - delivering the project's own Step 4.3, which is currently impossible because all 36 runtime scripts compile into `Assembly-CSharp`.

## Changes
- 'Assets/Scripts/KryticalNova.Runtime.asmdef' - (create) gameplay, networking, navigation, combat, progression
- 'Assets/Scripts/UI/KryticalNova.UI.asmdef' - (create) with `defineConstraints: ["!UNITY_SERVER"]`
- 'Assets/Editor/KryticalNova.Editor.asmdef' - (create) editor-only, references Runtime
- 'Assets/Scripts/Core/ICombatFeedbackSink.cs' - (create) presentation contract owned by Runtime
- 'Assets/Scripts/Core/IPointerBlockQuery.cs' - (create) pointer-blocking contract owned by Runtime
- 'Assets/Scripts/Core/ClientPresentationRegistry.cs' - (create) static slot the client assembly populates
- 'Assets/Scripts/UI/CombatFeedbackManager.cs' and 'Assets/Scripts/UI/ProgressionHUD.cs' - implement and register the contracts
- 'Assets/Scripts/Combat/PlayerMeleeCombat.cs', 'Assets/Scripts/Player/PointAndClickPlayer.cs', 'Assets/Scripts/Progression/PlayerCombatSkills.cs' - call the registry instead of UI types

## Relevant Assets and Quirks
- The blocking couplings are `PlayerCombatSkills` -> `KryticalNova.UI.CombatFeedbackManager` (in `TriggerSkillCastClientRpc` and `ExecuteCastInternal`), plus `PointAndClickPlayer` -> `ProgressionHUD.BlocksWorldPointer` and `CombatFeedbackManager.ShowCombatFeedback`. Runtime must own none of these for the UI assembly to be excludable.
- `PlayerCombatSkills` already exposes a `SkillCast` event, so the feedback call is redundant wiring rather than a required dependency.
- Verified assembly names for the reference lists: `Kybernetik.Animancer` (from 'Packages/com.kybernetik.animancer/Runtime/Kybernetik.Animancer.asmdef'), `Kybernetik.Animancer.Editor`, `BoingKit` (from 'Assets/Boing Kit/BoingKit.asmdef'), plus `Unity.Netcode.Runtime`, `Unity.Services.Multiplayer`, `Unity.Services.Core`, `Unity.Services.Authentication`, `Unity.AI.Navigation`, `Unity.InputSystem`, `Unity.Cinemachine`. DOTween ships as precompiled DLLs under 'Assets/Plugins/Demigiant' and is auto-referenced.
- Runtime must stay on the server: `CombatEntity`, `PlayerResources`, `PlayerMeleeCombat`, `PlayerAnimancerMotor`, `PointAndClickPlayer` and the whole `Networking` folder derive from `NetworkBehaviour` and own `ServerRpc`s.
- `PlayerMeleeCombat` and `PlayerAnimancerMotor` reference Animancer but already guard every call behind null checks, so they compile server-side without conditional compilation.
- **Risk:** moving MonoBehaviours into a new assembly rewrites their `m_EditorClassIdentifier` (currently `Assembly-CSharp::KryticalNova.Progression.PlayerCombatSkills`). Script references are GUID-based and normally survive, but `SampleScene` and 'Assets/Prefabs/Player.prefab' must be re-checked for missing scripts afterwards.
- Create the Runtime assembly first: if the UI assembly is added before Runtime references `Unity.Netcode.Runtime`, the first compile fails with a large error wall.

## Steps
- [ ] 1. Create 'Assets/Scripts/Core/' with `ICombatFeedbackSink`, `IPointerBlockQuery` and a static `ClientPresentationRegistry` exposing `Feedback` and `PointerBlock` slots [depends: none]
- [ ] 2. Implement both interfaces on `CombatFeedbackManager` (feedback) and `ProgressionHUD` (pointer block), registering on `OnEnable` and clearing on `OnDisable` [depends: 1]
- [ ] 3. Replace every Runtime-side UI call in `PlayerCombatSkills`, `PlayerMeleeCombat` and `PointAndClickPlayer` with the matching registry call [depends: 1,2]
- [ ] 4. Create 'Assets/Scripts/KryticalNova.Runtime.asmdef' covering all of 'Assets/Scripts' except `UI`, using the verified reference list [depends: 3]
- [ ] 5. Create 'Assets/Scripts/UI/KryticalNova.UI.asmdef' with `defineConstraints: ["!UNITY_SERVER"]` referencing `KryticalNova.Runtime`, and 'Assets/Editor/KryticalNova.Editor.asmdef' referencing `KryticalNova.Runtime` [depends: 4]
- [ ] 6. Resolve any assembly reference errors the compiler reports, then re-open `SampleScene` and 'Assets/Prefabs/Player.prefab' and confirm no component shows a missing script [depends: 5]
- [ ] 7. Switch the build target to Dedicated Server and build; confirm it succeeds and that `KryticalNova.UI` types are absent from the build [depends: 5]

## Verify
- `check_compile_errors` reports zero errors after the split.
- `SampleScene` hierarchy and 'Assets/Prefabs/Player.prefab' show no missing MonoBehaviour references.
- Editor Play Mode still runs the full Phase 3 loop: allocate stats, cast Spiritual Rage, strike the Combat Dummy, see floating combat text, drag a skill to a hotbar slot.
- Dedicated Server build completes and prints the `DedicatedServerOptimizer` headless banner with tick rate 30.
- The UI assembly is confirmed absent from the server build by assembly list, not by eyeballing the scene.
