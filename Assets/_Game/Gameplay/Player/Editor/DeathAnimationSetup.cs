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
    /// Builds the player's deaths: first-party copies of the katana set's knockdowns and lying loops, the travel each
    /// knockdown makes (baked from its own root motion, so the body's travel follows the animation), the KINEMATION
    /// Motion Warping template the death motor shapes for each death, and the Player.prefab wiring
    /// (<see cref="PlayerDeathMotor"/>, sharing the jump's warp component). Re-runnable: clips and travel are rebuilt
    /// from the constants below, distances and timings tuned on the prefab are kept.
    ///
    /// The copies differ from the vendor clips in two import settings the player needs. The animator does not apply
    /// root motion, so a clip that keeps its height in the root would leave the body lying in the air: the copies bake
    /// height into the pose. The lying poses loop.
    /// </summary>
    public static class DeathAnimationSetup
    {
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string PlayerModelPath = "Assets/_Game/Characters/Player/Player_01/Player_01.prefab";
        private const string WarpFolder = "Assets/_Game/Gameplay/Player/Data/Assets";
        private const string WarpAssetPath = WarpFolder + "/Warp_Player_Death.asset";
        private const string AnimationFolder = "Assets/_Game/Characters/Player/Animations/Death";
        private const string WarpChildName = "Jump Warp";
        private const float SampleRate = 60f;
        /// <summary>The body counts as down once its centre is this low (metres above the ground).</summary>
        private const float DownHeight = 0.4f;

        private const string Damages = "Assets/_ThirdParty/Grruzam Powerful Sword Animation(Great Sword, Katana)/Animation/M_Katana_Blade/4_Damages/";

        // The set names its knockdowns for the side the blow comes from: "Front" is hit from the front and goes over
        // backwards (measured on the rig: the head ends 1.3 m behind the feet), "Back" is hit from behind and goes
        // down forwards onto the face. The "Big" ones throw the body about 3.5 m: those are the critical deaths.
        private const string FrontFall = Damages + "5__KnockDown/M_katana_Blade@KnockDown_Front_ver_A.FBX";
        private const string BackFall = Damages + "5__KnockDown/M_katana_Blade@KnockDown_Back_ver_A.FBX";
        private const string CritFrontFall = Damages + "5__KnockDown/M_katana_Blade@KnockDown_Front_Big.FBX";
        private const string CritBackFall = Damages + "5__KnockDown/M_katana_Blade@KnockDown_Back_Big.FBX";
        // Lying on the back (the death loop) and lying face down.
        private const string OnBackLoop = Damages + "6__Die/M_Katana_Blade@Damage_Die_Loop.FBX";
        private const string FaceDownLoop = Damages + "5__KnockDown/M_katana_Blade@KnockDown_Back_Loop.FBX";

        private readonly struct Baked
        {
            public readonly AnimationClip Clip;
            public readonly AnimationCurve Travel;
            public readonly float Distance, ImpactTime;
            public Baked(AnimationClip clip, AnimationCurve travel, float distance, float impactTime)
            {
                Clip = clip; Travel = travel; Distance = distance; ImpactTime = impactTime;
            }
        }

        [MenuItem("Krytical Nova/Player/Set Up Death Animations")]
        public static void Build()
        {
            WeatherVfxBuilder.EnsureFolder(AnimationFolder);
            WeatherVfxBuilder.EnsureFolder(WarpFolder);
            float scale = HumanScale();

            if (!Fall(FrontFall, "Death_Front", scale, out Baked front) || !Fall(BackFall, "Death_Back", scale, out Baked back)
                || !Fall(CritFrontFall, "Death_Crit_Front", scale, out Baked critFront) || !Fall(CritBackFall, "Death_Crit_Back", scale, out Baked critBack))
                return;
            AnimationClip onBack = Copy(OnBackLoop, "Death_Loop_OnBack", loop: true);
            AnimationClip faceDown = Copy(FaceDownLoop, "Death_Loop_FaceDown", loop: true);
            if (onBack == null || faceDown == null) return;

            var asset = AssetDatabase.LoadAssetAtPath<MotionWarpingAsset>(WarpAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<MotionWarpingAsset>();
                AssetDatabase.CreateAsset(asset, WarpAssetPath);
            }

            // A template: the death motor writes the clip, the travel, the height and the phase for each death.
            asset.animation = critFront.Clip;
            asset.rootX = critFront.Travel;
            asset.rootY = AnimationCurve.Constant(0f, critFront.Clip.length, 0f);
            asset.rootZ = critFront.Travel;
            asset.useLinear = new VectorBool(false, true, false);
            asset.useAnimation = new VectorBool(false, true, false);
            asset.useWarping = VectorBool.Enabled;
            asset.useCollision = false;
            asset.playRateBasis = 1f;
            asset.phasesAmount = 1;
            asset.warpPhases = new List<WarpPhase>
            {
                new WarpPhase { startTime = 0f, endTime = critFront.Clip.length, minRate = 1f, maxRate = 1f, totalRootMotion = Vector3.one }
            };
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            if (Wire(asset, front, back, critFront, critBack, onBack, faceDown))
                Debug.Log($"[Death] Player deaths built. Front {front.Distance:0.00} m (down at {front.ImpactTime:0.00} s), back {back.Distance:0.00} m " +
                          $"({back.ImpactTime:0.00} s), critical front {critFront.Distance:0.00} m ({critFront.ImpactTime:0.00} s), " +
                          $"critical back {critBack.Distance:0.00} m ({critBack.ImpactTime:0.00} s). Player prefab wired.");
        }

        private static bool Wire(MotionWarpingAsset asset, Baked front, Baked back, Baked critFront, Baked critBack, AnimationClip onBack, AnimationClip faceDown)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                Transform child = root.transform.Find(WarpChildName);
                var warp = child != null ? child.GetComponent<WarpComponent>() : null;
                if (root.GetComponent<PlayerAnimancerMotor>() == null || warp == null)
                {
                    Debug.LogError($"[Death] {PlayerPrefabPath} needs the motion-warped jump first (Krytical Nova ▸ Player ▸ Build Jump Warp Asset).");
                    return false;
                }

                var motor = root.GetComponent<PlayerDeathMotor>();
                if (motor == null) motor = root.AddComponent<PlayerDeathMotor>();

                var so = new SerializedObject(motor);
                so.FindProperty("warpTemplate").objectReferenceValue = asset;
                so.FindProperty("motionWarping").objectReferenceValue = warp;
                Chain(so.FindProperty("front"), front, onBack);
                Chain(so.FindProperty("back"), back, faceDown);
                Chain(so.FindProperty("critFront"), critFront, onBack);
                Chain(so.FindProperty("critBack"), critBack, faceDown);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Chain(SerializedProperty chain, Baked fall, AnimationClip loop)
        {
            chain.FindPropertyRelative("fall").objectReferenceValue = fall.Clip;
            chain.FindPropertyRelative("loop").objectReferenceValue = loop;
            chain.FindPropertyRelative("travel").animationCurveValue = fall.Travel;
            chain.FindPropertyRelative("distance").floatValue = fall.Distance;
            chain.FindPropertyRelative("impactTime").floatValue = fall.ImpactTime;
        }

        /// <summary>A knockdown: its first-party copy, and from the source's root motion how far and how it travels.</summary>
        private static bool Fall(string sourceFbx, string name, float scale, out Baked baked)
        {
            baked = default;
            AnimationClip source = LoadClip(sourceFbx);
            AnimationClip clip = Copy(sourceFbx, name, loop: false);
            if (source == null || clip == null) return false;

            AnimationCurve x = RootCurve(source, "RootT.x"), y = RootCurve(source, "RootT.y"), z = RootCurve(source, "RootT.z");
            if (x == null || y == null || z == null)
            {
                Debug.LogError($"[Death] {source.name} has no root motion curves to bake its travel from.");
                return false;
            }

            float length = source.length;
            var start = new Vector2(x.Evaluate(0f), z.Evaluate(0f));
            Vector2 net = new Vector2(x.Evaluate(length), z.Evaluate(length)) - start;
            float distance = net.magnitude * scale;
            Vector2 along = net.sqrMagnitude > 0.000001f ? net.normalized : Vector2.up;

            // Share of the way covered at each moment. It never goes back: a body that rocks on landing stays put.
            int steps = Mathf.Max(2, Mathf.CeilToInt(length * SampleRate));
            var keys = new Keyframe[steps + 1];
            float furthest = 0f, total = Mathf.Max(0.0001f, net.magnitude);
            float impact = length * 0.5f;
            bool down = false;
            for (int i = 0; i <= steps; i++)
            {
                float t = length * i / steps;
                float covered = Vector2.Dot(new Vector2(x.Evaluate(t), z.Evaluate(t)) - start, along) / total;
                furthest = Mathf.Clamp01(Mathf.Max(furthest, covered));
                keys[i] = new Keyframe(t, i == steps ? 1f : furthest);
                if (!down && i > 0 && y.Evaluate(t) * scale <= DownHeight)
                {
                    down = true;
                    impact = t;
                }
            }
            var travel = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) AnimationUtility.SetKeyBroken(travel, i, true);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(travel, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(travel, i, AnimationUtility.TangentMode.Linear);
            }

            baked = new Baked(clip, travel, distance, impact);
            return true;
        }

        /// <summary>
        /// The player's own copy of a vendor clip, with its height baked into the pose (the animator discards root
        /// motion, so the body must go down in the pose itself) and, for the lying poses, set to loop.
        /// </summary>
        private static AnimationClip Copy(string sourceFbx, string name, bool loop)
        {
            AnimationClip source = LoadClip(sourceFbx);
            if (source == null) return null;

            string path = $"{AnimationFolder}/{name}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = Object.Instantiate(source);
                clip.name = name;
                AnimationUtility.SetAnimationEvents(clip, System.Array.Empty<AnimationEvent>());
                AssetDatabase.CreateAsset(clip, path);
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            settings.loopBlendPositionY = true;   // height lives in the pose
            settings.keepOriginalPositionY = true;
            settings.heightFromFeet = false;
            settings.loopBlendPositionXZ = false; // ground travel is the warp's job
            settings.loopBlendOrientation = true; // the body's turn as it falls lives in the pose
            settings.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static AnimationCurve RootCurve(AnimationClip clip, string property)
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), property));
            return curve != null && curve.length >= 2 ? curve : null;
        }

        /// <summary>Root curves are in humanoid units; the player's rig says how many metres one of them is.</summary>
        private static float HumanScale()
        {
            float scale = 1f;
            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
            if (modelPrefab == null) return scale;
            var instance = Object.Instantiate(modelPrefab);
            instance.hideFlags = HideFlags.HideAndDontSave;
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman) scale = animator.humanScale;
            Object.DestroyImmediate(instance);
            return scale;
        }

        private static AnimationClip LoadClip(string fbxPath)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) Debug.LogError($"[Death] No animation clip found in {fbxPath}.");
            return clip;
        }
    }
}
