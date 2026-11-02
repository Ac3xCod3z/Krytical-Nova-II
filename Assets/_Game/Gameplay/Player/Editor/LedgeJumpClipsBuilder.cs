using System.Linq;
using System.Text;
using KryticalNova.Player;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The route jump's clip (part 5b, owner 1-2 Nov: the Synty Sporty backflip off a roof's edge only, `_Masc` / `_Femn` by body
    /// type): measures the four variants of each (plain, RM, RMH, RMV: how far each moves its root and where), makes the player's own
    /// in-place copy of the one whose travel is all in the root (the leg's arc moves the body, so the root's travel is left out of the pose,
    /// as the vault's copies do), writes the <see cref="LedgeJumpClips"/> asset and puts it on Player.prefab's wall motor. Re-runnable.
    /// </summary>
    public static class LedgeJumpClipsBuilder
    {
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string AssetPath = "Assets/_Game/Gameplay/Player/Data/Assets/LedgeJumpClips.asset";
        private const string CopyFolder = "Assets/_Game/Characters/Player/Animations/LedgeJump";
        private const string SourceFolder = "Assets/Synty/AnimationEmotesAndTaunts/Animations/Sidekick/";
        private static readonly string[] Variants = { "", "_RM", "_RMH", "_RMV" };

        [MenuItem("Krytical Nova/Player/Build Ledge Jump Clips")]
        public static void Build()
        {
            var report = new StringBuilder();
            AnimationClip masc = BuildBody("Masculine", "Masc", report);
            AnimationClip femn = BuildBody("Feminine", "Femn", report);
            var clips = AssetDatabase.LoadAssetAtPath<LedgeJumpClips>(AssetPath);
            if (clips == null)
            {
                clips = ScriptableObject.CreateInstance<LedgeJumpClips>();
                AssetDatabase.CreateAsset(clips, AssetPath);
            }
            // the backflip only off a roof's edge (owner, 2 Nov); the gaps keep their clips (empty = the normal jump)
            clips.edgeDrop.masculine = masc;
            clips.edgeDrop.feminine = femn;
            EditorUtility.SetDirty(clips);

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var motor = root.GetComponent<PlayerWallRunMotor>();
                if (motor == null) report.AppendLine("[LedgeJump] Player.prefab has no PlayerWallRunMotor: not wired");
                else
                {
                    var so = new SerializedObject(motor);
                    so.FindProperty("ledgeJumpClips").objectReferenceValue = clips;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                    report.AppendLine("[LedgeJump] Player.prefab's wall motor uses " + AssetPath);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Measures a body's four variants (logged: length, the root's travel, humanoid) and copies the full root-motion one (`_RM`: all the
        /// travel and the rise carried by the root, which the copy leaves out of the pose), else the plain one.
        /// </summary>
        private static AnimationClip BuildBody(string folder, string suffix, StringBuilder report)
        {
            AnimationClip pick = null, plain = null;
            foreach (string variant in Variants)
            {
                string path = $"{SourceFolder}{folder}/Sporty/A_MOD_EMOT_Sporty_Backflip{variant}_{suffix}.fbx";
                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip == null) { report.AppendLine($"[LedgeJump] {path}: no clip"); continue; }
                Vector3 travel = Travel(clip, "RootT");
                report.AppendLine($"[LedgeJump] {clip.name}: {clip.length:F2} s, root travel x {travel.x:F2} y {travel.y:F2} z {travel.z:F2} m " +
                    $"(humanoid {clip.humanMotion}, root curves {clip.hasRootCurves}, average speed {clip.averageSpeed.magnitude:F2} m/s)");
                if (variant == "_RM") pick = clip;
                if (variant == "") plain = clip;
            }
            if (pick == null) pick = plain;
            string pickName = pick != null ? pick.name : null;
            if (pick == null) return null;
            WeatherVfxBuilder.EnsureFolder(CopyFolder);
            string copyPath = $"{CopyFolder}/LedgeJump_Backflip_{suffix}.anim";
            var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(copyPath);
            if (copy == null)
            {
                copy = Object.Instantiate(pick);
                AssetDatabase.CreateAsset(copy, copyPath);
            }
            else EditorUtility.CopySerialized(pick, copy);
            copy.name = $"LedgeJump_Backflip_{suffix}";
            AnimationUtility.SetAnimationEvents(copy, System.Array.Empty<AnimationEvent>());
            int before = AnimationUtility.GetCurveBindings(copy).Length;
            // gui-ledge-1 (2 Nov): the whole 3.5 s emote (wind-up, flip, landing pose) fitted into ~0.7 s of air hid the flip: only its window stays
            Vector2 window = FlipWindow(pick);
            int keys = KeepHumanoidCurvesOnly(copy, window);
            report.AppendLine($"[LedgeJump] {suffix}: the flip's window {window.x:F2}-{window.y:F2} s of {pick.length:F2} s (the body tipped past {FlipTipDegrees:F0} degrees); " +
                $"kept the humanoid curves only ({before} curves -> {AnimationUtility.GetCurveBindings(copy).Length}), {keys} keys after thinning");
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = false;
            settings.loopBlendPositionXZ = false;  // the travel stays in the root: the leg's arc moves the body
            settings.loopBlendPositionY = false;   // so does the rise: the arc lifts the body, the flip turns on the spot
            settings.loopBlendOrientation = false;
            settings.keepOriginalPositionY = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            EditorUtility.SetDirty(copy);
            report.AppendLine($"[LedgeJump] {suffix}: copied {pickName} to {copyPath} (travel in the root, left out of the pose)");
            return copy;
        }

        /// <summary>
        /// A humanoid clip plays from its muscle and root curves (no path); the FBX copy also carries every bone's transform curves, which the
        /// body never plays (26 MB of text per clip). Those go, and keys a straight line between their neighbours already gives (within
        /// <see cref="ThinTolerance"/>) are thinned out. Returns the keys kept.
        /// </summary>
        private static int KeepHumanoidCurvesOnly(AnimationClip clip, Vector2 window)
        {
            int kept = 0;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!string.IsNullOrEmpty(binding.path) || binding.type != typeof(Animator))
                {
                    AnimationUtility.SetEditorCurve(clip, binding, null);
                    continue;
                }
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;
                AnimationCurve thin = Thin(Window(curve, window));
                AnimationUtility.SetEditorCurve(clip, binding, thin);
                kept += thin.length;
            }
            return kept;
        }

        /// <summary>How far the body must tip from upright for the flip to have started (and be back within, for it to have ended).</summary>
        private const float FlipTipDegrees = 20f;

        /// <summary>A little before the body tips and after it is upright again, so the take-off and the landing pose are not cut short (s).</summary>
        private const float FlipMargin = 0.1f;

        /// <summary>
        /// The flip's own window in the clip: from the first frame the body (the humanoid root's rotation, <c>RootQ</c>) tips more than
        /// <see cref="FlipTipDegrees"/> away from how it stood at the start to the last such frame, with <see cref="FlipMargin"/> either side.
        /// The whole clip when it never tips that far.
        /// </summary>
        private static Vector2 FlipWindow(AnimationClip clip)
        {
            var q = new AnimationCurve[4];
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!binding.propertyName.StartsWith("RootQ.")) continue;
                int i = "xyzw".IndexOf(binding.propertyName[binding.propertyName.Length - 1]);
                if (i >= 0) q[i] = AnimationUtility.GetEditorCurve(clip, binding);
            }
            if (q.Any(c => c == null)) return new Vector2(0f, clip.length);
            Quaternion At(float t) => new Quaternion(q[0].Evaluate(t), q[1].Evaluate(t), q[2].Evaluate(t), q[3].Evaluate(t)).normalized;
            Quaternion start = At(0f);
            float first = -1f, last = -1f, step = 1f / Mathf.Max(1f, clip.frameRate);
            for (float t = 0f; t <= clip.length; t += step)
            {
                if (Quaternion.Angle(start, At(t)) < FlipTipDegrees) continue;
                if (first < 0f) first = t;
                last = t;
            }
            if (first < 0f) return new Vector2(0f, clip.length);
            return new Vector2(Mathf.Max(0f, first - FlipMargin), Mathf.Min(clip.length, last + FlipMargin));
        }

        /// <summary>The curve's part inside <paramref name="window"/>, moved to start at 0: its keys there, and its value at either end.</summary>
        private static AnimationCurve Window(AnimationCurve curve, Vector2 window)
        {
            var keys = new System.Collections.Generic.List<Keyframe> { new Keyframe(0f, curve.Evaluate(window.x)) };
            foreach (Keyframe k in curve.keys)
                if (k.time > window.x && k.time < window.y) keys.Add(new Keyframe(k.time - window.x, k.value));
            keys.Add(new Keyframe(window.y - window.x, curve.Evaluate(window.y)));
            return new AnimationCurve(keys.ToArray());
        }

        // muscles run -1..1 (0.004 = 0.2 % of the range) and the root (left out of the pose anyway) in metres: below what the eye sees on the body
        private const float ThinTolerance = 0.004f;

        /// <summary>The curve with every key dropped that a straight line between the keys kept either side matches within the tolerance.</summary>
        private static AnimationCurve Thin(AnimationCurve curve)
        {
            Keyframe[] keys = curve.keys;
            if (keys.Length <= 2) return curve;
            var kept = new System.Collections.Generic.List<Keyframe> { keys[0] };
            int anchor = 0;
            for (int i = 1; i < keys.Length - 1; i++)
            {
                // can the line from the anchor to the next key stand in for every key between them?
                Keyframe a = keys[anchor], b = keys[i + 1];
                bool fits = true;
                for (int k = anchor + 1; k <= i && fits; k++)
                {
                    float t = Mathf.InverseLerp(a.time, b.time, keys[k].time);
                    fits = Mathf.Abs(Mathf.Lerp(a.value, b.value, t) - keys[k].value) <= ThinTolerance;
                }
                if (fits) continue;
                kept.Add(keys[i]);
                anchor = i;
            }
            kept.Add(keys[keys.Length - 1]);
            var thin = new AnimationCurve(kept.ToArray());
            for (int i = 0; i < thin.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(thin, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(thin, i, AnimationUtility.TangentMode.Linear);
            }
            return thin;
        }

        /// <summary>How far a humanoid clip's root curves (<c>RootT.x/y/z</c>) move from their first key to their farthest.</summary>
        private static Vector3 Travel(AnimationClip clip, string property)
        {
            Vector3 range = Vector3.zero;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!binding.propertyName.StartsWith(property + ".")) continue;
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0) continue;
                float first = curve.keys[0].value, far = 0f;
                foreach (Keyframe k in curve.keys) far = Mathf.Max(far, Mathf.Abs(k.value - first));
                char axis = binding.propertyName[binding.propertyName.Length - 1];
                if (axis == 'x') range.x = far; else if (axis == 'y') range.y = far; else if (axis == 'z') range.z = far;
            }
            return range;
        }
    }
}
