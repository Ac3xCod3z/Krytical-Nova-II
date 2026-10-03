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
    /// Builds the motion-warped jump: a KINEMATION Motion Warping asset whose root-height curve is stitched from the
    /// jump clips, plus the Player.prefab wiring (warp component, clip chain and its timings).
    /// </summary>
    public static class JumpWarpAssetBuilder
    {
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string WarpFolder = "Assets/_Game/Gameplay/Player/Data/Assets";
        private const string WarpAssetPath = WarpFolder + "/Warp_Player_DoubleJump.asset";
        private const string WarpChildName = "Jump Warp";

        // The jump is a chain of the Grruzam katana clips: the ground take-off, then the Double Jump set (flip, fall
        // loop, landing; those start in mid-air, which is why the take-off comes first).
        //
        // With root motion off, how much of a humanoid clip's vertical motion Unity leaves in the pose depends on the
        // clip's import settings (see PoseHeight). The warp curve therefore carries exactly the height the played poses
        // leave out:   root height = authored height of the jump - height the pose already shows.
        // The air clips use the in-place "_ZeroHeight" variants; the landing uses the full clip, whose height is baked
        // into the pose, so its crouch and recovery sit on the ground once the root is back on the NavMesh.
        private const string Movements = "Assets/_ThirdParty/Grruzam Powerful Sword Animation(Great Sword, Katana)/Animation/M_Katana_Blade/1_Movements/";
        private static readonly string[] AuthoredClips =
        {
            "6__Jump/M_katana_Blade@Jump_Start.FBX",
            "7__Double_Jump/M_katana_Blade@Double_Jump_Start.FBX",
            "7__Double_Jump/M_katana_Blade@Double_Jump_Loop.FBX",
            "7__Double_Jump/M_katana_Blade@Double_Jump_End.FBX",
        };
        private static readonly string[] PlayedClips =
        {
            "6__Jump/M_katana_Blade@Jump_Start_ZeroHeight.FBX",
            "7__Double_Jump/M_katana_Blade@Double_Jump_Start_ZeroHeight.FBX",
            "7__Double_Jump/M_katana_Blade@Double_Jump_Loop_ZeroHeight.FBX",
            "7__Double_Jump/M_katana_Blade@Double_Jump_End.FBX",
        };
        private const int Takeoff = 0, Flip = 1, Fall = 2, Land = 3;
        /// <summary>The warp component only reads this clip's length; it must outlast the touchdown.</summary>
        private const string LengthClipPath = Movements + "7__Double_Jump/M_katana_Blade@Double_Jump_ALL.FBX";

        private const float SampleRate = 60f;
        /// <summary>How long the fall-loop pose is held between the flip and the landing clip.</summary>
        private const float FallSeconds = 0.3f;
        /// <summary>The landing clip opens with a near-instant drop; it is entered this long before its touchdown instead.</summary>
        private const float LandLeadSeconds = 0.04f;
        /// <summary>The synthesized fall speeds up to at most this (m/s).</summary>
        private const float MaxFallSpeed = 12f;
        /// <summary>Root height above standing that counts as "feet off the ground".</summary>
        private const float AirborneHeight = 0.05f;
        /// <summary>The root curve is averaged over this window so clip joins don't step while the poses cross-fade.</summary>
        private const float SmoothSeconds = 0.08f;

        // ------------------------------------------------------------------ jump

        [MenuItem("Krytical Nova/Player/Build Jump Warp Asset")]
        public static void BuildJumpWarp()
        {
            int count = AuthoredClips.Length;
            var authored = new AnimationCurve[count];  // root height with the flight in it
            var played = new AnimationClip[count];
            var playedHeight = new AnimationCurve[count];
            for (int i = 0; i < count; i++)
            {
                AnimationClip authoredClip = LoadClip(Movements + AuthoredClips[i]);
                played[i] = LoadClip(Movements + PlayedClips[i]);
                if (authoredClip == null || played[i] == null) return;
                authored[i] = RootHeight(authoredClip);
                playedHeight[i] = RootHeight(played[i]);
                if (authored[i] == null || playedHeight[i] == null) return;
            }
            AnimationClip lengthClip = LoadClip(LengthClipPath);
            if (lengthClip == null) return;

            float step = 1f / SampleRate;
            float standing = authored[Takeoff].Evaluate(0f);

            // Touchdown inside the landing clip: its root is back at the height it finally settles at.
            float landLength = played[Land].length;
            float settled = authored[Land].Evaluate(landLength);
            float touchdown = 0f;
            for (float t = 0f; t <= landLength; t += step)
                if (authored[Land].Evaluate(t) <= settled + AirborneHeight) { touchdown = t; break; }
            float landOffset = Mathf.Max(0f, touchdown - LandLeadSeconds);

            // Timeline of the chain: when each clip starts, and where inside the clip it starts.
            float[] offsets = { 0f, 0f, 0f, landOffset };
            float[] starts =
            {
                0f,
                played[Takeoff].length,
                played[Takeoff].length + played[Flip].length,
                played[Takeoff].length + played[Flip].length + FallSeconds,
            };
            float total = starts[Land] + landLength - landOffset;

            // The authored height of the whole chain, zero at the standing start; each clip continues from where the
            // last one ended. The fall loop holds one pose, so its descent is synthesized: it speeds up from the flip's
            // exit speed toward the landing clip's entry speed, so the body neither hangs nor snaps down.
            float takeoffLength = played[Takeoff].length;
            float flipLength = played[Flip].length;
            float takeoffEnd = authored[Takeoff].Evaluate(takeoffLength) - standing;
            float flipEnd = takeoffEnd + authored[Flip].Evaluate(flipLength) - authored[Flip].Evaluate(0f);
            float exitSpeed = (authored[Flip].Evaluate(flipLength) - authored[Flip].Evaluate(flipLength - step)) / step;
            float entrySpeed = Mathf.Max(-MaxFallSpeed,
                (authored[Land].Evaluate(landOffset + step) - authored[Land].Evaluate(landOffset)) / step);
            float fallEnd = flipEnd + 0.5f * (exitSpeed + entrySpeed) * FallSeconds;

            float AuthoredHeight(float time)
            {
                if (time < starts[Flip]) return authored[Takeoff].Evaluate(time) - standing;
                if (time < starts[Fall]) return takeoffEnd + authored[Flip].Evaluate(time - starts[Flip]) - authored[Flip].Evaluate(0f);
                if (time < starts[Land])
                {
                    float t = time - starts[Fall];
                    return flipEnd + exitSpeed * t + 0.5f * (entrySpeed - exitSpeed) / FallSeconds * t * t;
                }
                return fallEnd + authored[Land].Evaluate(landOffset + time - starts[Land]) - authored[Land].Evaluate(landOffset);
            }

            float PoseAboveStanding(float time)
            {
                int clip = time < starts[Flip] ? Takeoff : time < starts[Fall] ? Flip : time < starts[Land] ? Fall : Land;
                float clipTime = Mathf.Min(offsets[clip] + time - starts[clip], played[clip].length);
                return PoseHeight(played[clip], playedHeight[clip], clipTime, standing) - standing;
            }

            var height = new AnimationCurve();
            int smooth = Mathf.Max(1, Mathf.RoundToInt(SmoothSeconds * SampleRate * 0.5f));
            for (float t = 0f; t <= total; t += step)
            {
                float sum = 0f;
                for (int k = -smooth; k <= smooth; k++)
                {
                    float sample = Mathf.Clamp(t + k * step, 0f, total);
                    sum += AuthoredHeight(sample) - PoseAboveStanding(sample);
                }
                height.AddKey(t, sum / (2 * smooth + 1));
            }

            // Key moments, read from the authored motion: feet leave the ground, top of the flip, feet touch down.
            float takeoff = 0f;
            for (float t = starts[Flip]; t >= 0f; t -= step)
                if (AuthoredHeight(t) <= AirborneHeight) { takeoff = t; break; }

            float apex = starts[Flip];
            float peak = float.NegativeInfinity;
            for (float t = starts[Flip]; t <= starts[Fall]; t += step)
            {
                float y = AuthoredHeight(t);
                if (y > peak) { peak = y; apex = t; }
            }

            float land = starts[Land] + touchdown - landOffset;

            if (!(takeoff < apex && apex < land) || land >= lengthClip.length)
            {
                Debug.LogError($"[JumpWarp] Unusable jump timing (take-off {takeoff:0.00}s, apex {apex:0.00}s, landing {land:0.00}s, " +
                               $"limit {lengthClip.length:0.00}s). The asset was not changed.");
                return;
            }

            WeatherVfxBuilder.EnsureFolder(WarpFolder);
            var asset = AssetDatabase.LoadAssetAtPath<MotionWarpingAsset>(WarpAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<MotionWarpingAsset>();
                AssetDatabase.CreateAsset(asset, WarpAssetPath);
            }

            asset.animation = lengthClip;
            asset.rootX = AnimationCurve.Constant(0f, total, 0f);
            asset.rootY = height;
            asset.rootZ = AnimationCurve.Constant(0f, total, 0f);
            asset.useLinear = new VectorBool(true, false, true);     // travel is even; height keeps the clips' own arc, rescaled
            asset.useAnimation = new VectorBool(false, true, false); // only the root height curve moves the body
            asset.useWarping = VectorBool.Enabled;
            asset.useCollision = false;
            asset.playRateBasis = 1f;
            asset.phasesAmount = 2;
            asset.warpPhases = new List<WarpPhase>
            {
                Phase(asset, takeoff, apex),
                Phase(asset, apex, land),
            };
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            WireJump(asset, played, starts, offsets);
            Debug.Log($"[JumpWarp] {WarpAssetPath}: take-off {takeoff:0.00}s, apex {apex:0.00}s, landing {land:0.00}s, chain ends {total:0.00}s; " +
                      $"root curve peaks at {height.Evaluate(apex):0.00} m for an authored {peak:0.00} m jump. Player prefab wired.");
        }

        /// <summary>
        /// Height of the pose's centre of mass when the clip plays with root motion off (as the player does):
        ///  • vertical motion baked into the pose: the pose follows the clip's root height;
        ///  • not baked, based on feet: the pose stays grounded at standing height;
        ///  • not baked, based on original: the pose stays at the clip's first-frame height.
        /// </summary>
        private static float PoseHeight(AnimationClip clip, AnimationCurve rootHeight, float clipTime, float standing)
        {
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.loopBlendPositionY) return rootHeight.Evaluate(clipTime);
            return settings.heightFromFeet ? standing : rootHeight.Evaluate(0f);
        }

        /// <summary>The clip's humanoid root height (RootT.y), which Unity treats as vertical root motion.</summary>
        private static AnimationCurve RootHeight(AnimationClip clip)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (binding.propertyName == "RootT.y") return AnimationUtility.GetEditorCurve(clip, binding);
            Debug.LogError($"[JumpWarp] '{clip.name}' has no RootT.y curve. Is it imported as Humanoid?");
            return null;
        }

        private static WarpPhase Phase(MotionWarpingAsset asset, float start, float end)
        {
            var phase = new WarpPhase { startTime = start, endTime = end, minRate = 1f, maxRate = 1f };
            Vector3 last = asset.GetVectorValue(start);
            for (float t = start; t <= end; t += 1f / SampleRate)
            {
                Vector3 value = asset.GetVectorValue(t);
                Vector3 delta = value - last;
                phase.totalRootMotion += new Vector3(Mathf.Abs(delta.x), Mathf.Abs(delta.y), Mathf.Abs(delta.z));
                last = value;
            }
            return phase;
        }

        private static void WireJump(MotionWarpingAsset asset, AnimationClip[] clips, float[] starts, float[] offsets)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var jumpMotor = root.GetComponent<PlayerJumpMotor>();
                var animMotor = root.GetComponent<PlayerAnimancerMotor>();
                if (jumpMotor == null || animMotor == null)
                {
                    Debug.LogError($"[JumpWarp] {PlayerPrefabPath} is missing PlayerJumpMotor or PlayerAnimancerMotor.");
                    return;
                }

                // Its own child: the warp component then finds no Animator / Rigidbody / CharacterController to toggle.
                Transform child = root.transform.Find(WarpChildName);
                if (child == null)
                {
                    child = new GameObject(WarpChildName).transform;
                    child.SetParent(root.transform, false);
                }
                var warp = child.GetComponent<WarpComponent>();
                if (warp == null) warp = child.gameObject.AddComponent<WarpComponent>();

                var warpSo = new SerializedObject(warp);
                warpSo.FindProperty("transformToWarp").objectReferenceValue = root.transform;
                warpSo.FindProperty("scalePlayRate").boolValue = false; // Animancer plays the clips; there is no WarpRate parameter
                warpSo.FindProperty("playAnimator").boolValue = false;
                warpSo.ApplyModifiedPropertiesWithoutUndo();

                var jumpSo = new SerializedObject(jumpMotor);
                jumpSo.FindProperty("warpAsset").objectReferenceValue = asset;
                jumpSo.FindProperty("motionWarping").objectReferenceValue = warp;
                jumpSo.ApplyModifiedPropertiesWithoutUndo();

                var animSo = new SerializedObject(animMotor);
                var clipList = animSo.FindProperty("warpedJumpClips");
                var startList = animSo.FindProperty("warpedJumpClipStarts");
                var offsetList = animSo.FindProperty("warpedJumpClipOffsets");
                clipList.arraySize = clips.Length;
                startList.arraySize = starts.Length;
                offsetList.arraySize = offsets.Length;
                for (int i = 0; i < clips.Length; i++)
                {
                    clipList.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
                    startList.GetArrayElementAtIndex(i).floatValue = starts[i];
                    offsetList.GetArrayElementAtIndex(i).floatValue = offsets[i];
                }
                animSo.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static AnimationClip LoadClip(string fbxPath)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) Debug.LogError($"[JumpWarp] No animation clip found in {fbxPath}.");
            return clip;
        }
    }
}
