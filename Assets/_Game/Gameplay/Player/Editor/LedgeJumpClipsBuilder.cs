using System.Linq;
using System.Text;
using KryticalNova.Player;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The route jump's clips (part 5b): off a roof's edge, our own roof flip and freefall (owner v4, 7 Nov 2026; ONE clip each for every
    /// body, the owner: no masculine / feminine versions; they replace the Synty Sporty backflip of 1-2 Nov). Imports them Humanoid on the
    /// Sidekick avatar, writes the <see cref="LedgeJumpClips"/> asset and puts it on Player.prefab's wall motor. The gaps keep their clips
    /// (empty = the normal jump). Re-runnable.
    /// </summary>
    public static class LedgeJumpClipsBuilder
    {
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string AssetPath = "Assets/_Game/Gameplay/Player/Data/Assets/LedgeJumpClips.asset";
        private const string ClipFolder = "Assets/_Game/Characters/Player/Animations/LedgeJump";

        /// <summary>
        /// Our roof clips (keyed in Blender on the Sidekick rig, 30 fps, in place; the working files stay in Drive): the flip, a 3/4 backward
        /// turn on the hips ending lying flat (33 frames, not looping), and the freefall loop (49 frames, last = first).
        /// </summary>
        private const string RoofFlipFbx = ClipFolder + "/A_KN_RoofFlip.fbx";
        private const string RoofFreefallFbx = ClipFolder + "/A_KN_RoofFreefall.fbx";

        /// <summary>The Sidekick clip whose avatar the roof clips copy (the backflip they replace was played on it).</summary>
        private const string AvatarSourceFbx = "Assets/Synty/AnimationEmotesAndTaunts/Animations/Sidekick/Masculine/Sporty/A_MOD_EMOT_Sporty_Backflip_RM_Masc.fbx";

        [MenuItem("Krytical Nova/Player/Build Ledge Jump Clips")]
        public static void Build()
        {
            var report = new StringBuilder();
            var clips = AssetDatabase.LoadAssetAtPath<LedgeJumpClips>(AssetPath);
            if (clips == null)
            {
                clips = ScriptableObject.CreateInstance<LedgeJumpClips>();
                AssetDatabase.CreateAsset(clips, AssetPath);
            }
            clips.edgeDrop = RoofClip(RoofFlipFbx, "LedgeJump_RoofFlip", loop: false, report);
            clips.edgeDropFreefall = RoofClip(RoofFreefallFbx, "LedgeJump_RoofFreefall", loop: true, report);
            if (clips.edgeDrop == null) report.AppendLine("[LedgeJump] no roof flip: the edge drop plays the normal jump");
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
        /// One roof clip: Humanoid on the Sidekick avatar, the whole take as one clip, the root's turn kept in the clip (orientation not baked
        /// into the pose, the original Y), as the backflip copy had it, so the flip and the flat freefall show while the leg's arc moves the
        /// body. Null when its FBX is not in the project.
        /// </summary>
        private static AnimationClip RoofClip(string path, string clipName, bool loop, StringBuilder report)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) { report.AppendLine($"[LedgeJump] {path}: not in the project"); return null; }
            Avatar avatar = SidekickAvatar();
            if (avatar == null) { report.AppendLine($"[LedgeJump] {path}: no Sidekick avatar to copy ({AvatarSourceFbx})"); return null; }

            importer.animationType = ModelImporterAnimationType.Human;
            // The avatar is made from this file with the Sidekick avatar's bone map (7 Nov 2026): copying the Sidekick avatar was refused
            // ("Copied Avatar Rig Configuration mis-match"), as our rig has an armature node above 'root' and other dynamic bones than
            // Synty's clips; the humanoid bones carry the same names, so the clip retargets onto every Sidekick body as theirs do.
            HumanDescription map = avatar.humanDescription;
            map.skeleton = System.Array.Empty<SkeletonBone>();   // this file's own rest pose (the same Sidekick rig)
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.humanDescription = map;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.clipAnimations = System.Array.Empty<ModelImporterClipAnimation>();
            // the takes are read with these settings in force: an importer lists its default takes only after a reimport (first run, 7 Nov:
            // "no take" when they were read straight after the switch to Humanoid)
            importer.SaveAndReimport();
            importer = (ModelImporter)AssetImporter.GetAtPath(path);
            ModelImporterClipAnimation[] takes = importer.defaultClipAnimations;
            if (takes == null || takes.Length == 0)
            {
                report.AppendLine($"[LedgeJump] {path}: no take (animation type {importer.animationType}, avatar {(importer.sourceAvatar != null ? importer.sourceAvatar.name : "none")}, " +
                    $"clips {AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Count()})");
                return null;
            }
            ModelImporterClipAnimation take = takes[0];
            take.name = clipName;
            take.loopTime = loop;
            take.loopPose = false;
            take.lockRootRotation = false;
            take.lockRootHeightY = false;
            take.lockRootPositionXZ = false;
            take.keepOriginalOrientation = true;
            take.keepOriginalPositionY = true;
            take.keepOriginalPositionXZ = true;
            take.events = System.Array.Empty<AnimationEvent>();
            importer.clipAnimations = new[] { take };
            importer.SaveAndReimport();

            AnimationClip[] found = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            AnimationClip clip = found.FirstOrDefault(c => c.name == clipName) ?? found.FirstOrDefault();
            if (clip == null) { report.AppendLine($"[LedgeJump] {path}: imported, but no clip"); return null; }
            if (clip.name != clipName) report.AppendLine($"[LedgeJump] {path}: the clip came in as '{clip.name}', not '{clipName}'");
            Avatar made = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            report.AppendLine($"[LedgeJump] {path}: avatar {(made != null ? made.name : "none")}, valid {made != null && made.isValid}, humanoid {made != null && made.isHuman}");
            AnimationClipSettings s = AnimationUtility.GetAnimationClipSettings(clip);
            report.AppendLine($"[LedgeJump] {clipName}: {clip.length:F3} s at {clip.frameRate:F0} fps, humanoid {clip.isHumanMotion}, loop {s.loopTime}, " +
                $"orientation baked {s.loopBlendOrientation}, original Y {s.keepOriginalPositionY} (avatar {avatar.name})");
            return clip;
        }

        /// <summary>The avatar of the Sidekick backflip FBX (its own, or the one it copies).</summary>
        private static Avatar SidekickAvatar()
        {
            if (AssetImporter.GetAtPath(AvatarSourceFbx) is ModelImporter source && source.avatarSetup == ModelImporterAvatarSetup.CopyFromOther && source.sourceAvatar != null)
                return source.sourceAvatar;
            return AssetDatabase.LoadAllAssetsAtPath(AvatarSourceFbx).OfType<Avatar>().FirstOrDefault();
        }
    }
}
