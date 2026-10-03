using System.Collections.Generic;
using System.Linq;
using Kinemation.MotionWarping.Runtime.Core;
using Kinemation.MotionWarping.Runtime.Utility;
using KryticalNova.Player;
using UnityEditor;
using UnityEngine;
using WarpComponent = Kinemation.MotionWarping.Runtime.Core.MotionWarping;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the vault: the first-party copy of the leapfrog clip, the KINEMATION Motion Warping template asset the
    /// vault motor shapes for each obstacle, and the Player.prefab wiring (PlayerVaultMotor on the root, sharing the
    /// jump's warp component, with its three styles: hurdle, leapfrog, front flip). Re-runnable; the height limits
    /// tuned on the prefab are kept, the three styles are rewritten from the constants below.
    /// </summary>
    public static class VaultWarpAssetBuilder
    {
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string WarpFolder = "Assets/_Game/Gameplay/Player/Data/Assets";
        private const string WarpAssetPath = WarpFolder + "/Warp_Player_Vault.asset";
        private const string AnimationFolder = "Assets/_Game/Characters/Player/Animations";
        private const string WarpChildName = "Jump Warp";

        // Middling obstacles (waist to chest): a leapfrog. The body dives level over the obstacle with the hands
        // reaching for its top, tucks and lands. It is the only hands-on vault among the project's clips; it lives in a
        // vendor demo folder (Boing Kit's Unity-chan sample), so the player gets a copy of its own.
        private const string LeapfrogSource = "Assets/_ThirdParty/Boing Kit/Examples/Common/UnityChan/Animations/unitychan_UMATOBI00.fbx";
        private const string LeapfrogPath = AnimationFolder + "/Vault_Leapfrog.anim";
        /// <summary>Push-off to touchdown inside the leapfrog (seconds).</summary>
        private static readonly Vector2 LeapfrogWindow = new Vector2(0.3f, 0.95f);
        /// <summary>The tucked legs pass this far above the ground with the root left where it is.</summary>
        private const float LeapfrogClearance = 1.2f;

        // Low obstacles (shin to thigh): the katana set's tuck jump, the same family as the run it comes out of.
        private const string Katana = "Assets/_ThirdParty/Grruzam Powerful Sword Animation(Great Sword, Katana)/Animation/M_Katana_Blade/1_Movements/";
        private const string HurdlePath = Katana + "6__Jump/M_katana_Blade@Jump_ALL.FBX";
        private static readonly Vector2 HurdleWindow = new Vector2(0.2f, 0.85f);
        private const float HurdleClearance = 0.35f;

        // The tallest obstacles (chest to head height: dumpsters, hedges): the front flip the jump uses. It tumbles in place, low to the
        // ground line, so the root is lifted by nearly the obstacle's whole height. It needs longer in the air than
        // the others and a longer run either side, so the flip keeps the run's pace. The window stops while the knees
        // are still bent: after that the clip stretches the legs down past the ground line.
        private const string FlipPath = Katana + "7__Double_Jump/M_katana_Blade@Double_Jump_Start_ZeroHeight.FBX";
        private static readonly Vector2 FlipWindow = new Vector2(0f, 0.5f);
        private const float FlipClearance = 0.25f;

        [MenuItem("Krytical Nova/Player/Build Vault Warp Asset")]
        public static void BuildVault()
        {
            AnimationClip leapfrog = FirstPartyCopy(LeapfrogSource, LeapfrogPath, "Vault_Leapfrog");
            AnimationClip hurdle = LoadClip(HurdlePath);
            AnimationClip flip = LoadClip(FlipPath);
            if (leapfrog == null || hurdle == null || flip == null) return;

            WeatherVfxBuilder.EnsureFolder(WarpFolder);
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
            AssetDatabase.SaveAssets();

            if (Wire(asset, leapfrog, hurdle, flip))
                Debug.Log($"[Vault] {WarpAssetPath} built; {hurdle.name} for low obstacles, the leapfrog for middling ones, {flip.name} for the tallest. Player prefab wired.");
        }

        private static bool Wire(MotionWarpingAsset asset, AnimationClip leapfrog, AnimationClip hurdle, AnimationClip flip)
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
                //                               clip      window          pose clearance     air time                 take-off  landing
                Style(so.FindProperty("low"), hurdle, HurdleWindow, HurdleClearance, new Vector2(0.3f, 0.55f), 0.9f, 0.85f);
                Style(so.FindProperty("middle"), leapfrog, LeapfrogWindow, LeapfrogClearance, new Vector2(0.36f, 0.62f), 0.9f, 0.85f);
                Style(so.FindProperty("tall"), flip, FlipWindow, FlipClearance, new Vector2(0.55f, 0.8f), 1.5f, 1.5f);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Style(SerializedProperty style, AnimationClip clip, Vector2 window, float clearance, Vector2 airTime,
            float takeoffDistance, float landingDistance)
        {
            style.FindPropertyRelative("clip").objectReferenceValue = clip;
            style.FindPropertyRelative("window").vector2Value = window;
            style.FindPropertyRelative("poseClearance").floatValue = clearance;
            style.FindPropertyRelative("airTime").vector2Value = airTime;
            style.FindPropertyRelative("takeoffDistance").floatValue = takeoffDistance;
            style.FindPropertyRelative("landingDistance").floatValue = landingDistance;
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
