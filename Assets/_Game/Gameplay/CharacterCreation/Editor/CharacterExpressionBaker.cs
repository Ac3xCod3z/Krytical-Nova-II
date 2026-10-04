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
    /// blend-shape values are read here, because clips cannot be read at runtime), and the stances and emotes of
    /// Synty's ANIMATION - Idles and ANIMATION - Emotes and Taunts packs (the Sidekick set, since the player is a
    /// Sidekick character). The packs are scanned, so a pack update only needs a new bake. Each motion is paired
    /// with its masculine and feminine performance. Left out: the clips that mime with a prop the display character
    /// does not hold, and the root-motion variants that would walk it off its stage.
    /// </summary>
    public static class CharacterExpressionBaker
    {
        public const string LibraryPath = "Assets/_Game/Gameplay/CharacterCreation/Data/Assets/CharacterExpressionLibrary.asset";
        private const string FacePoseFolder = "Assets/Synty/SidekickCharacters/Animations/FacePoses";
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string IdlePack = "Assets/Synty/AnimationIdles/Animations/Sidekick";
        private const string EmotePack = "Assets/Synty/AnimationEmotesAndTaunts/Animations/Sidekick";
        private const string IdlePrefix = "A_MOD_IDL_";
        private const string EmotePrefix = "A_MOD_EMOT_";
        private const string DefaultStance = "Base";

        // Mimed with a prop in the hand (cup, food, phone, watch, comb): the display character shows none.
        private static readonly string[] PropGroups = { "Drink", "Eat", "UsePhone", "CheckWatch" };
        private static readonly string[] PropMotions = { "PlayWithHair_CombBack" };
        // The same emote again, moved by root motion (the animator of a display character discards it).
        private static readonly string[] RootMotionSuffixes = { "_RM", "_RMH", "_RMV" };
        // The emote pack's own neutral idle is not an emote.
        private static readonly string[] SkippedEmotes = { "Base_Idle" };

        // Poses that are not expressions (eye direction, blink) or near-duplicates.
        private static readonly string[] SkippedFaces = { "Blink", "EyeDown", "EyeLeft", "EyeRight", "EyeUp", "Angry2", "Happy2", "ReferencePose" };

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
            var emotes = new List<CharacterExpressionLibrary.Motion>();
            BakeIdlePack(idles, emotes);
            BakeEmotePack(emotes);
            if (idles.Count == 0)
            {
                // Without the pack the character still needs something to stand in: the player's own idle.
                var ready = PlayerIdle();
                if (ready != null) idles.Add(new CharacterExpressionLibrary.Motion { name = "Ready", group = "Ready", clip = ready });
                Debug.LogWarning($"[Characters] No stances found under {IdlePack}. Import Synty ANIMATION - Idles and bake again.");
            }

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

        /// <summary>One motion of a pack: its key (file name without prefix and body suffix) and its two performances.</summary>
        private sealed class Found
        {
            public string group;
            public AnimationClip masculine, feminine;
        }

        /// <summary>
        /// Every clip of a pack by key. A file is &lt;prefix&gt;&lt;Key&gt;_Masc.fbx or ..._Femn.fbx under
        /// &lt;pack&gt;/Masculine|Feminine/&lt;Group&gt;/...; the clip is loaded by path, because the take names inside
        /// the files do not follow the file names.
        /// </summary>
        private static SortedDictionary<string, Found> Scan(string pack, string prefix)
        {
            var found = new SortedDictionary<string, Found>(System.StringComparer.Ordinal);
            if (!AssetDatabase.IsValidFolder(pack)) return found;
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { pack }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(path);
                if (!file.StartsWith(prefix)) continue;
                bool feminine = file.EndsWith("_Femn");
                if (!feminine && !file.EndsWith("_Masc")) continue;
                string key = file.Substring(prefix.Length, file.Length - prefix.Length - "_Masc".Length);

                // <pack>/<Body>/<Group>/...
                string[] folders = path.Substring(pack.Length + 1).Split('/');
                if (folders.Length < 3) continue;

                // An FBX holds its clip as a sub-asset, next to hidden preview clips.
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip == null) continue;

                if (!found.TryGetValue(key, out Found entry)) found[key] = entry = new Found { group = folders[1] };
                if (feminine) entry.feminine = clip;
                else entry.masculine = clip;
            }
            return found;
        }

        /// <summary>The idle pack: "..._Loop" clips are stances (with their "..._Enter"), single clips are gestures.</summary>
        private static void BakeIdlePack(List<CharacterExpressionLibrary.Motion> stances, List<CharacterExpressionLibrary.Motion> gestures)
        {
            var found = Scan(IdlePack, IdlePrefix);
            foreach (var pair in found)
            {
                string key = pair.Key;
                Found clips = pair.Value;
                if (PropGroups.Contains(clips.group) || PropMotions.Contains(key)) continue;
                if (HasToken(key, "Enter") || HasToken(key, "Exit")) continue; // the way into and out of a stance

                if (key == DefaultStance)
                {
                    stances.Insert(0, Motion("Relaxed", "Base", clips, null)); // the default comes first
                }
                else if (key.EndsWith("_Loop"))
                {
                    string stance = key.Substring(0, key.Length - "_Loop".Length);
                    found.TryGetValue(stance + "_Enter", out Found enter);
                    stances.Add(Motion(DisplayName(stance, clips.group), Words(clips.group), clips, enter));
                }
                else
                {
                    gestures.Add(Motion(DisplayName(key, clips.group), Words(clips.group), clips, null));
                }
            }
        }

        private static void BakeEmotePack(List<CharacterExpressionLibrary.Motion> emotes)
        {
            var packed = new List<CharacterExpressionLibrary.Motion>();
            foreach (var pair in Scan(EmotePack, EmotePrefix))
            {
                string key = pair.Key;
                if (SkippedEmotes.Contains(key) || RootMotionSuffixes.Any(key.EndsWith)) continue;
                packed.Add(Motion(DisplayName(key, pair.Value.group), Words(pair.Value.group), pair.Value, null));
            }
            // Emotes proper first, the idle pack's small gestures after them.
            emotes.InsertRange(0, packed);
        }

        private static CharacterExpressionLibrary.Motion Motion(string name, string group, Found clips, Found enter) =>
            new CharacterExpressionLibrary.Motion
            {
                name = name,
                group = group,
                clip = clips.masculine,
                feminine = clips.feminine,
                enter = enter != null ? enter.masculine : null,
                enterFeminine = enter != null ? enter.feminine : null,
            };

        private static bool HasToken(string key, string token) => key.Split('_').Contains(token);

        /// <summary>"ArmsFolded_Casual" in group ArmsFolded reads "Casual"; "Lean_L_HandOnHip" reads "Left Hand On Hip".</summary>
        private static string DisplayName(string key, string group)
        {
            string rest = key.StartsWith(group + "_") ? key.Substring(group.Length + 1) : key == group ? string.Empty : key;
            if (rest.Length == 0) return Words(group);
            var parts = rest.Split('_').Select(part =>
                part == "L" ? "Left" : part == "R" ? "Right" : part == "B" ? "Back" : part == "F" ? "Forward" : Words(part));
            return string.Join(" ", parts);
        }

        /// <summary>"HandsOnHips" reads "Hands On Hips".</summary>
        private static string Words(string camelCase)
        {
            var words = new System.Text.StringBuilder(camelCase.Length + 4);
            for (int i = 0; i < camelCase.Length; i++)
            {
                char c = camelCase[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(camelCase[i - 1])) words.Append(' ');
                words.Append(c);
            }
            return words.ToString();
        }

        private static AnimationClip PlayerIdle()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var motor = player != null ? player.GetComponentInChildren<PlayerAnimancerMotor>(true) : null;
            return motor != null ? motor.IdleClip : null;
        }
    }
}
