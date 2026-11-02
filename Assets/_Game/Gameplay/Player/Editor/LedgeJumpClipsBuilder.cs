using System.Linq;
using System.Text;
using KryticalNova.Player;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The route jump's clip (part 5b, owner 1 Nov: the Synty Sporty backflip off a roof's edge or across a gap, `_Masc` / `_Femn` by body
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
            clips.masculine = masc;
            clips.feminine = femn;
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
            int keys = KeepHumanoidCurvesOnly(copy);
            report.AppendLine($"[LedgeJump] {suffix}: kept the humanoid curves only ({before} curves -> {AnimationUtility.GetCurveBindings(copy).Length}), {keys} keys after thinning");
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
        private static int KeepHumanoidCurvesOnly(AnimationClip clip)
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
                AnimationCurve thin = Thin(curve);
                AnimationUtility.SetEditorCurve(clip, binding, thin);
                kept += thin.length;
            }
            return kept;
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
