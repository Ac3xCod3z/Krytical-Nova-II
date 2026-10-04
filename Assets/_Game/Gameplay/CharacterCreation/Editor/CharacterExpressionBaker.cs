using System.Collections.Generic;
using System.IO;
using System.Linq;
using KryticalNova.CharacterCreation;
using KryticalNova.Player;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Fills the expression library the character creator previews: faces from Sidekick's face-pose clips (their
    /// blend-shape values are read here, because clips cannot be read at runtime), and the idles and emotes found in
    /// the project. Clips that are missing are skipped with a warning, so the list grows as animation packs arrive:
    /// add a line to <see cref="Idles"/> or <see cref="Emotes"/> and bake again.
    /// </summary>
    public static class CharacterExpressionBaker
    {
        public const string LibraryPath = "Assets/_Game/Gameplay/CharacterCreation/Data/Assets/CharacterExpressionLibrary.asset";
        private const string FacePoseFolder = "Assets/Synty/SidekickCharacters/Animations/FacePoses";
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string BasicMotions = "Assets/_ThirdParty/Emerald AI/Demo/Demo Source/Animations/Villager and Basic/Basic Motions/Animations/";

        // Poses that are not expressions (eye direction, blink) or near-duplicates.
        private static readonly string[] SkippedFaces = { "Blink", "EyeDown", "EyeLeft", "EyeRight", "EyeUp", "Angry2", "Happy2", "ReferencePose" };

        // (name shown in the creator, clip asset). The player's own combat-ready idle is added first, from its prefab.
        private static readonly (string name, string path)[] Idles =
        {
            ("At Ease", "Assets/Synty/SidekickCharacters/_Demos/Animations/BodyCycles/A_Body_IdleSubtle.fbx"),
            ("Relaxed", BasicMotions + "Idles/BasicMotions@Idle01.fbx"),
            ("Restless", BasicMotions + "Idles/BasicMotions@Idle02.fbx"),
        };

        private static readonly (string name, string path)[] Emotes =
        {
            ("Talk", BasicMotions + "Social/Conversation/BasicMotions@Talk01.fbx"),
            ("Jump", BasicMotions + "Movement/BasicMotions@Jump01.fbx"),
        };

        [MenuItem("Krytical Nova/Characters/5. Bake Expressions (Faces, Idles, Emotes)")]
        public static CharacterExpressionLibrary Bake()
        {
            var library = AssetDatabase.LoadAssetAtPath<CharacterExpressionLibrary>(LibraryPath);
            if (library == null)
            {
                WeatherVfxBuilder.EnsureFolder(Path.GetDirectoryName(LibraryPath).Replace('\\', '/'));
                library = ScriptableObject.CreateInstance<CharacterExpressionLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var shapes = new List<string>();
            var faces = BakeFaces(shapes);
            var idles = new List<CharacterExpressionLibrary.Motion>();
            var ready = PlayerIdle();
            if (ready != null) idles.Add(new CharacterExpressionLibrary.Motion { name = "Ready", clip = ready });
            idles.AddRange(LoadMotions(Idles));
            var emotes = LoadMotions(Emotes);

            library.SetContents(shapes, faces, idles, emotes);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Characters] Expressions: {faces.Count} faces over {shapes.Count} blend shapes, {idles.Count} idles, {emotes.Count} emotes in {LibraryPath}.");
            return library;
        }

        private static List<CharacterExpressionLibrary.Face> BakeFaces(List<string> shapes)
        {
            var faces = new List<CharacterExpressionLibrary.Face>();
            var paths = AssetDatabase.FindAssets("t:AnimationClip", new[] { FacePoseFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".anim") && Path.GetFileName(p).StartsWith("A_FacePose_"))
                .OrderBy(p => p)
                .ToList();
            foreach (string path in paths)
            {
                string name = Path.GetFileNameWithoutExtension(path).Substring("A_FacePose_".Length);
                if (SkippedFaces.Contains(name)) continue;
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) continue;

                var weights = new List<CharacterExpressionLibrary.ShapeWeight>();
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!binding.propertyName.StartsWith("blendShape.")) continue;
                    float weight = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(0f);
                    if (weight < 0.5f) continue;
                    // "blendShape.MESHBlends.browRaiseLeft" -> "browRaiseLeft": the name our merged mesh keeps.
                    string shape = binding.propertyName.Substring(binding.propertyName.LastIndexOf('.') + 1);
                    int index = shapes.IndexOf(shape);
                    if (index < 0)
                    {
                        index = shapes.Count;
                        shapes.Add(shape);
                    }
                    weights.Add(new CharacterExpressionLibrary.ShapeWeight { shape = (ushort)index, weight = Mathf.Min(weight, 100f) });
                }
                var face = new CharacterExpressionLibrary.Face { name = name, weights = weights.ToArray() };
                if (name == "Neutral") faces.Insert(0, face); // the default comes first
                else faces.Add(face);
            }
            if (faces.Count == 0) Debug.LogWarning($"[Characters] No face poses found in {FacePoseFolder}.");
            return faces;
        }

        private static List<CharacterExpressionLibrary.Motion> LoadMotions((string name, string path)[] entries)
        {
            var motions = new List<CharacterExpressionLibrary.Motion>();
            foreach (var (name, path) in entries)
            {
                // An FBX holds its clip as a sub-asset, next to hidden preview clips.
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip == null)
                {
                    Debug.LogWarning($"[Characters] Clip for '{name}' not found at {path}; skipped.");
                    continue;
                }
                motions.Add(new CharacterExpressionLibrary.Motion { name = name, clip = clip });
            }
            return motions;
        }

        private static AnimationClip PlayerIdle()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var motor = player != null ? player.GetComponentInChildren<PlayerAnimancerMotor>(true) : null;
            return motor != null ? motor.IdleClip : null;
        }
    }
}
