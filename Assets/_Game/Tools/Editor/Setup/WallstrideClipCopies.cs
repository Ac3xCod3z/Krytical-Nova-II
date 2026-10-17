using System.IO;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The climb's stand-in clips, as in-place copies of their own (owner, 17 Oct 2026). The vault and run clips carry
    /// their travel in the pose (the run's forward stride, the vault's hop): played on a climb, the body walked 4 m into
    /// the wall while the climber stayed outside it. The climb moves the body itself, so its copies leave all travel
    /// out and keep only the movement of the limbs. The originals stay untouched for the vault and the run, which
    /// use them; only these copies change. Replaced clip by clip once real climbing animations exist.
    /// </summary>
    public static class WallstrideClipCopies
    {
        public const string Folder = "Assets/_Game/Characters/Player/Animations/Wallstride";
        public const string Plant = Folder + "/Wallstride_Plant.anim";
        public const string Climb = Folder + "/Wallstride_Climb_Loop.anim";
        public const string Mantle = Folder + "/Wallstride_Mantle.anim";

        private const string VaultCopies = "Assets/_Game/Characters/Player/Animations/Vaults/";
        private const string VaultPack = "Assets/_ThirdParty/Vault Animation Pack/Assets/Animations/";

        private struct Copy
        {
            public string Source, Target;
            public bool Loop;
            public bool KeepBob; // the run's small up and down stays in the pose; a vault's hop does not
        }

        private static readonly Copy[] Copies =
        {
            new Copy { Source = VaultCopies + "Vault_Pack_01.anim", Target = Plant, Loop = false, KeepBob = false },
            new Copy { Source = VaultPack + "AS_Run_Loop_R.anim", Target = Climb, Loop = true, KeepBob = true },
            new Copy { Source = VaultCopies + "Vault_Pack_05.anim", Target = Mantle, Loop = false, KeepBob = false },
        };

        /// <summary>Makes any copy that is missing and sets every copy in place. Safe to run again.</summary>
        [MenuItem("Krytical Nova/Setup/Wallstride Clip Copies")]
        public static void EnsureCopies()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(Folder).Replace('\\', '/'), Path.GetFileName(Folder));
            foreach (Copy copy in Copies)
            {
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(copy.Target) == null)
                {
                    if (AssetDatabase.LoadAssetAtPath<AnimationClip>(copy.Source) == null)
                    {
                        Debug.LogWarning($"[Wallstride] No clip at {copy.Source}: {copy.Target} not made.");
                        continue;
                    }
                    AssetDatabase.CopyAsset(copy.Source, copy.Target);
                }
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(copy.Target);
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = copy.Loop;
                settings.loopBlendPositionXZ = false; // the stride's travel goes to root motion, which the climb does not use
                settings.loopBlendPositionY = copy.KeepBob;
                settings.keepOriginalPositionY = true;
                settings.loopBlendOrientation = true; // faces where the climb faces it
                settings.keepOriginalOrientation = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
