using System.Linq;
using KryticalNova.Player;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Gives the player its hit reactions: the katana set's damage clips, assigned to <see cref="PlayerAnimancerMotor"/>
    /// on the Player prefab by the side the blow comes from. Safe to re-run; a clip already chosen in the Inspector
    /// is kept.
    /// </summary>
    public static class HitReactionSetup
    {
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string Damages = "Assets/_ThirdParty/Grruzam Powerful Sword Animation(Great Sword, Katana)/Animation/M_Katana_Blade/4_Damages/";

        // The set names its side clips for the way the body is thrown, which is away from the blow: a blow from the
        // left throws the body right (measured on the rig: Damage_Left carries the head 0.6 m to the left).
        private const string FromFront = Damages + "1__Front/M_katana_Blade@Damage_Front_Small_ver_A.FBX";
        private const string FromFrontHeavy = Damages + "1__Front/M_katana_Blade@Damage_Front_Big_ver_A.FBX";
        private const string FromBack = Damages + "2__Back/M_katana_Blade@Damage_Back_Small_ver_B.FBX";
        private const string FromLeft = Damages + "4__Right/M_katana_Blade@Damage_Right_Small_ver_A.FBX";
        private const string FromRight = Damages + "3__Left/M_katana_Blade@Damage_Left_Small_ver_A.FBX";

        [MenuItem("Krytical Nova/Player/Assign Hit Reaction Clips")]
        public static void Assign()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var motor = root.GetComponent<PlayerAnimancerMotor>();
                if (motor == null)
                {
                    Debug.LogError($"[HitReaction] {PlayerPrefabPath} has no PlayerAnimancerMotor.");
                    return;
                }

                var so = new SerializedObject(motor);
                int assigned = Set(so, "hitFrontClip", FromFront) + Set(so, "hitFrontHeavyClip", FromFrontHeavy)
                    + Set(so, "hitBackClip", FromBack) + Set(so, "hitLeftClip", FromLeft) + Set(so, "hitRightClip", FromRight);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log($"[HitReaction] {assigned} hit reaction clip(s) assigned on {PlayerPrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static int Set(SerializedObject so, string property, string fbxPath)
        {
            SerializedProperty p = so.FindProperty(property);
            if (p == null || p.objectReferenceValue != null) return 0; // keep a clip chosen in the Inspector
            AnimationClip clip = LoadClip(fbxPath);
            if (clip == null) return 0;
            p.objectReferenceValue = clip;
            return 1;
        }

        private static AnimationClip LoadClip(string fbxPath)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) Debug.LogWarning($"[HitReaction] No animation clip found in {fbxPath}.");
            return clip;
        }
    }
}
