using System.Collections.Generic;
using System.Linq;
using Kinemation.MotionWarping.Runtime.Core;
using Kinemation.MotionWarping.Runtime.Utility;
using KryticalNova.Factions;
using KryticalNova.Player;
using UnityEditor;
using UnityEngine;
using WarpComponent = Kinemation.MotionWarping.Runtime.Core.MotionWarping;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the vault: the KINEMATION Motion Warping template asset the vault motor shapes for each obstacle, the
    /// sets of vault animations (the default set, and one a faction from the Vault Animation Pack), the map that
    /// says who uses which, the traversal settings, and the Player.prefab wiring (PlayerVaultMotor on the root,
    /// sharing the jump's warp component). Re-runnable. A set that exists keeps a style that has a clip, so numbers
    /// tuned in the Inspector survive; the map's entries and the first-party clip copies are rewritten.
    /// </summary>
    public static class VaultWarpAssetBuilder
    {
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string DataFolder = "Assets/_Game/Gameplay/Player/Data/Assets";
        private const string WarpAssetPath = DataFolder + "/Warp_Player_Vault.asset";
        public const string MapPath = DataFolder + "/VaultAnimationMap.asset";
        public const string TraversalSettingsPath = DataFolder + "/TraversalSettings.asset";
        public const string DefaultSetPath = DataFolder + "/VaultSet_Default.asset";
        private const string AnimationFolder = "Assets/_Game/Characters/Player/Animations";
        private const string PackCopyFolder = AnimationFolder + "/Vaults";
        private const string WarpChildName = "Jump Warp";

        public static string SetPath(FactionId faction) => $"{DataFolder}/VaultSet_{faction}.asset";

        // ---------------------------------------------------------------- the default set

        // Middling obstacles (waist to chest): a leapfrog. The body dives level over the obstacle with the hands
        // reaching for its top, tucks and lands. It lives in a vendor demo folder (Boing Kit's Unity-chan sample), so
        // the player gets a copy of its own.
        private const string LeapfrogSource = "Assets/_ThirdParty/Boing Kit/Examples/Common/UnityChan/Animations/unitychan_UMATOBI00.fbx";
        private const string LeapfrogPath = AnimationFolder + "/Vault_Leapfrog.anim";

        // Low obstacles (shin to thigh): the katana set's tuck jump, the same family as the run it comes out of.
        private const string Katana = "Assets/_ThirdParty/Grruzam Powerful Sword Animation(Great Sword, Katana)/Animation/M_Katana_Blade/1_Movements/";
        private const string HurdlePath = Katana + "6__Jump/M_katana_Blade@Jump_ALL.FBX";

        // The tallest obstacles (chest to head height: dumpsters, hedges): the front flip the jump uses. It tumbles in
        // place, low to the ground line, so the root is lifted by nearly the obstacle's whole height. It needs longer
        // in the air than the others and a longer run either side, so the flip keeps the run's pace. The window stops
        // while the knees are still bent: after that the clip stretches the legs down past the ground line.
        private const string FlipPath = Katana + "7__Double_Jump/M_katana_Blade@Double_Jump_Start_ZeroHeight.FBX";

        // ---------------------------------------------------------------- the faction sets

        // The Vault Animation Pack (Voxel Vision): ten vaults, each led with the left and with the right. It has no
        // body-type and no height variants, so the split below is ours: by how high each one carries the legs over
        // (measured on the player's model), then one of each height class to a faction. The left-led clips are used.
        // The pack bakes the whole run into the pose; the vault motor moves the body itself, so each clip used gets a
        // first-party copy that keeps only its height in the pose.
        private const string PackFolder = "Assets/_ThirdParty/Vault Animation Pack/Assets/Animations/";

        private readonly struct PackVault
        {
            public readonly FactionId Faction;
            public readonly VaultHeight Height;
            public readonly int Number;
            /// <summary>
            /// The part of the clip played, seconds into it: the flight, cut to the same length either side of the
            /// top of the arc, so the body is at its highest over the middle of the obstacle.
            /// </summary>
            public readonly Vector2 Window;
            /// <summary>The lowest the legs and hips pass above the ground around the top of the arc.</summary>
            public readonly float Clearance;

            public PackVault(FactionId faction, VaultHeight height, int number, float from, float to, float clearance)
            {
                Faction = faction; Height = height; Number = number; Window = new Vector2(from, to); Clearance = clearance;
            }
        }

        private static readonly PackVault[] PackVaults =
        {
            // Each height class gets the three clips whose own clearance suits it (low: 0.3 to 0.8 m, middle: 0.9 to
            // 1.3 m, tall: 1.5 to 2 m), so the root is hardly lifted and the vault looks as it was performed.
            // The Hegemony: the lowest, most economical of each class.
            new PackVault(FactionId.Hegemony, VaultHeight.Low, 8, 1.09f, 1.65f, 0.48f),
            new PackVault(FactionId.Hegemony, VaultHeight.Middle, 1, 0.65f, 1.27f, 0.93f),
            new PackVault(FactionId.Hegemony, VaultHeight.Tall, 6, 0.87f, 1.79f, 1.50f),
            // The Null: the ones that throw the legs highest.
            new PackVault(FactionId.Null, VaultHeight.Low, 10, 0.61f, 1.93f, 0.81f),
            new PackVault(FactionId.Null, VaultHeight.Middle, 3, 0.60f, 1.60f, 1.21f),
            new PackVault(FactionId.Null, VaultHeight.Tall, 4, 0.43f, 1.37f, 1.99f),
            // The Covenant: a hand on the obstacle at the low end, a long carry higher up.
            new PackVault(FactionId.Covenant, VaultHeight.Low, 9, 0.64f, 1.14f, 0.27f),
            new PackVault(FactionId.Covenant, VaultHeight.Middle, 5, 0.73f, 1.53f, 1.33f),
            new PackVault(FactionId.Covenant, VaultHeight.Tall, 2, 0.65f, 1.47f, 1.59f),
        };

        /// <summary>Seconds in the air (shortest, longest) and the run either side, by height class, for the pack's vaults.</summary>
        private static void PackTiming(VaultHeight height, out Vector2 airTime, out float takeoff, out float landing)
        {
            switch (height)
            {
                case VaultHeight.Tall: airTime = new Vector2(0.6f, 1f); takeoff = 1.5f; landing = 1.5f; break;
                case VaultHeight.Middle: airTime = new Vector2(0.5f, 0.9f); takeoff = 1.1f; landing = 1.1f; break;
                default: airTime = new Vector2(0.45f, 0.8f); takeoff = 1f; landing = 1f; break;
            }
        }

        [MenuItem("Krytical Nova/Player/Build Vault Warp Asset")]
        public static void BuildVault()
        {
            AnimationClip leapfrog = FirstPartyCopy(LeapfrogSource, LeapfrogPath, "Vault_Leapfrog");
            AnimationClip hurdle = LoadClip(HurdlePath);
            AnimationClip flip = LoadClip(FlipPath);
            if (leapfrog == null || hurdle == null || flip == null) return;

            WeatherVfxBuilder.EnsureFolder(DataFolder);
            var asset = AssetDatabase.LoadAssetAtPath<MotionWarpingAsset>(WarpAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<MotionWarpingAsset>();
                AssetDatabase.CreateAsset(asset, WarpAssetPath);
            }

            // A template: the vault motor writes the height curve and the phase for each obstacle at run time. The clip
            // only gives the warp its length, which has to outlast the longest vault.
            asset.animation = leapfrog;
            asset.rootX = AnimationCurve.Constant(0f, leapfrog.length, 0f);
            asset.rootY = AnimationCurve.Constant(0f, leapfrog.length, 0f);
            asset.rootZ = AnimationCurve.Constant(0f, leapfrog.length, 0f);
            asset.useLinear = new VectorBool(true, false, true);     // even travel along the ground
            asset.useAnimation = new VectorBool(false, true, false); // the height curve lifts the body over the obstacle
            asset.useWarping = VectorBool.Enabled;
            asset.useCollision = false;
            asset.playRateBasis = 1f;
            asset.phasesAmount = 1;
            asset.warpPhases = new List<WarpPhase> { new WarpPhase { startTime = 0f, endTime = leapfrog.length, minRate = 1f, maxRate = 1f } };
            EditorUtility.SetDirty(asset);

            // The default set: what everyone vaulted with before there were sets, and still does where a faction's set has no clip.
            VaultAnimationSet defaults = EnsureSet(DefaultSetPath);
            //                                     clip      window                    pose clearance  air time                 take-off  landing
            Fill(defaults, VaultHeight.Low, hurdle, new Vector2(0.2f, 0.85f), 0.35f, new Vector2(0.3f, 0.55f), 0.9f, 0.85f);
            Fill(defaults, VaultHeight.Middle, leapfrog, new Vector2(0.3f, 0.95f), 1.2f, new Vector2(0.36f, 0.62f), 0.9f, 0.85f);
            Fill(defaults, VaultHeight.Tall, flip, new Vector2(0f, 0.5f), 0.25f, new Vector2(0.55f, 0.8f), 1.5f, 1.5f);

            var entries = new List<VaultAnimationMap.Entry>();
            foreach (FactionId faction in new[] { FactionId.Hegemony, FactionId.Null, FactionId.Covenant })
            {
                VaultAnimationSet set = EnsureSet(SetPath(faction));
                foreach (PackVault vault in PackVaults)
                {
                    if (vault.Faction != faction) continue;
                    AnimationClip clip = PackCopy(vault.Number);
                    if (clip == null) continue; // the pack is not in the project: that height falls back to the default set
                    PackTiming(vault.Height, out Vector2 airTime, out float takeoff, out float landing);
                    Fill(set, vault.Height, clip, vault.Window, vault.Clearance, airTime, takeoff, landing);
                }
                // Both body types: the pack has one performance of each vault. A set made for one body type is an
                // entry of its own, added in the Inspector.
                entries.Add(new VaultAnimationMap.Entry(faction, VaultBody.Any, set));
            }

            var map = AssetDatabase.LoadAssetAtPath<VaultAnimationMap>(MapPath);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<VaultAnimationMap>();
                AssetDatabase.CreateAsset(map, MapPath);
            }
            map.defaultSet = defaults;
            // Entries added by hand (a body type's own set) are kept; the three faction entries are this builder's.
            var kept = (map.entries ?? System.Array.Empty<VaultAnimationMap.Entry>()).Where(e => e.body != VaultBody.Any && e.set != null);
            map.entries = entries.Concat(kept).ToArray();
            EditorUtility.SetDirty(map);

            var traversal = AssetDatabase.LoadAssetAtPath<TraversalSettings>(TraversalSettingsPath);
            if (traversal == null)
            {
                traversal = ScriptableObject.CreateInstance<TraversalSettings>();
                AssetDatabase.CreateAsset(traversal, TraversalSettingsPath);
            }
            AssetDatabase.SaveAssets();

            if (Wire(asset, map, traversal))
                Debug.Log($"[Vault] {WarpAssetPath}, the default set and {entries.Count} faction sets built ({MapPath}). Player prefab wired.");
        }

        private static VaultAnimationSet EnsureSet(string path)
        {
            var set = AssetDatabase.LoadAssetAtPath<VaultAnimationSet>(path);
            if (set != null) return set;
            set = ScriptableObject.CreateInstance<VaultAnimationSet>();
            AssetDatabase.CreateAsset(set, path);
            return set;
        }

        /// <summary>Writes a height's style unless the set already has one with a clip (it may have been tuned by hand).</summary>
        private static void Fill(VaultAnimationSet set, VaultHeight height, AnimationClip clip, Vector2 window, float clearance, Vector2 airTime,
            float takeoffDistance, float landingDistance)
        {
            if (set.Has(height)) return;
            set.Set(height, new VaultStyle
            {
                clip = clip, window = window, poseClearance = clearance, airTime = airTime,
                takeoffDistance = takeoffDistance, landingDistance = landingDistance,
            });
            EditorUtility.SetDirty(set);
        }

        private static bool Wire(MotionWarpingAsset asset, VaultAnimationMap map, TraversalSettings traversal)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                Transform child = root.transform.Find(WarpChildName);
                var warp = child != null ? child.GetComponent<WarpComponent>() : null;
                if (root.GetComponent<PlayerJumpMotor>() == null || root.GetComponent<PlayerAnimancerMotor>() == null || warp == null)
                {
                    Debug.LogError($"[Vault] {PlayerPrefabPath} needs the motion-warped jump first (Krytical Nova ▸ Player ▸ Build Jump Warp Asset).");
                    return false;
                }

                var motor = root.GetComponent<PlayerVaultMotor>();
                if (motor == null) motor = root.AddComponent<PlayerVaultMotor>();

                var so = new SerializedObject(motor);
                so.FindProperty("warpTemplate").objectReferenceValue = asset;
                so.FindProperty("motionWarping").objectReferenceValue = warp;
                so.FindProperty("animations").objectReferenceValue = map;
                so.FindProperty("traversal").objectReferenceValue = traversal;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// The player's own copy of one of the pack's vaults (the left-led one). The copy leaves the run along the
        /// ground and the turn to the root, where the vault motor ignores them, and keeps the height in the pose, so
        /// the body rises and tucks on the spot while the motor carries it across. Rewritten on every run.
        /// </summary>
        private static AnimationClip PackCopy(int number)
        {
            string source = $"{PackFolder}AS_Vault_L_{number:00}.anim";
            var original = AssetDatabase.LoadAssetAtPath<AnimationClip>(source);
            if (original == null)
            {
                Debug.LogWarning($"[Vault] {source} is not in the project: that vault keeps the default animation.");
                return null;
            }
            WeatherVfxBuilder.EnsureFolder(PackCopyFolder);
            string path = $"{PackCopyFolder}/Vault_Pack_{number:00}.anim";
            var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (copy == null)
            {
                copy = Object.Instantiate(original);
                copy.name = $"Vault_Pack_{number:00}";
                AssetDatabase.CreateAsset(copy, path);
            }
            else
            {
                EditorUtility.CopySerialized(original, copy);
                copy.name = $"Vault_Pack_{number:00}";
            }
            AnimationUtility.SetAnimationEvents(copy, System.Array.Empty<AnimationEvent>());
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = false;
            settings.loopBlendPositionXZ = false;  // the run stays in the root: the motor moves the body
            settings.loopBlendOrientation = false; // so does any turn
            settings.loopBlendPositionY = true;    // the rise is the pose's own
            settings.keepOriginalPositionY = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            EditorUtility.SetDirty(copy);
            return copy;
        }

        private static AnimationClip FirstPartyCopy(string sourceFbx, string path, string name)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip != null) return clip;
            AnimationClip source = LoadClip(sourceFbx);
            if (source == null) return null;
            WeatherVfxBuilder.EnsureFolder(AnimationFolder);
            clip = Object.Instantiate(source);
            clip.name = name;
            AnimationUtility.SetAnimationEvents(clip, System.Array.Empty<AnimationEvent>());
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static AnimationClip LoadClip(string fbxPath)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) Debug.LogError($"[Vault] No animation clip found in {fbxPath}.");
            return clip;
        }
    }
}
