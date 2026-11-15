using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Seasons on the town's Synty art (item 21 part 3, owner's option A): moves the Synty materials the world maps use from Synty's
    /// Generic_Basic and Polygon_Triplanar graphs onto the KN copies (<c>KryticalNova/Synty/...</c>, Rendering/Shaders/Synty), which
    /// read the season (snow on roofs and ledges, slushy roads, prints and paths, each tree kind's fall colour, buds and blossom). The
    /// copies keep every property name, so a material keeps all its values; only the shader reference of the .mat changes. Synty's own
    /// shader files are never touched, and Put Back returns every moved material to Synty's graph.
    /// </summary>
    public static class SyntySeasonMaterials
    {
        /// <summary>Synty's graph -> the KN season copy (asset paths).</summary>
        static readonly (string synty, string season)[] Pairs =
        {
            ("Assets/Synty/PolygonGeneric/Shaders/Generic_Basic.shadergraph", "Assets/_Game/Rendering/Shaders/Synty/Generic_Basic.shadergraph"),
            ("Assets/Synty/PolygonSciFiCity/Shaders/Polygon_Triplanar.shadergraph", "Assets/_Game/Rendering/Shaders/Synty/Polygon_Triplanar.shadergraph"),
        };

        /// <summary>The maps whose Synty materials move: every scene under the world scenes folder (towns and field maps).</summary>
        const string WorldScenes = "Assets/_Game/Scenes/World";

        [MenuItem("Krytical Nova/World/Seasons/Move Synty Materials To Season Shaders")]
        public static void MoveFromMenu() => Debug.Log(Move());

        [MenuItem("Krytical Nova/World/Seasons/Put Synty Materials Back")]
        public static void PutBackFromMenu() => Debug.Log(PutBack());

        /// <summary>Moves the world maps' Synty materials onto the season copies; returns the report line.</summary>
        public static string Move()
        {
            var shaders = LoadPairs(out string missing);
            if (shaders == null) return "[Seasons] " + missing + ": nothing moved.";
            string[] scenes = AssetDatabase.FindAssets("t:Scene", new[] { WorldScenes }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
            var counts = new int[shaders.Count];
            int moved = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string path in AssetDatabase.GetDependencies(scenes, true))
                {
                    if (!path.EndsWith(".mat")) continue;
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null) continue;
                    for (int i = 0; i < shaders.Count; i++)
                    {
                        if (mat.shader != shaders[i].synty) continue;
                        Swap(mat, shaders[i].season);
                        counts[i]++;
                        moved++;
                        break;
                    }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.SaveAssets();
            return $"[Seasons] Synty materials moved to the season shaders: {moved} ({string.Join(", ", shaders.Select((s, i) => $"{s.synty.name} {counts[i]}"))}) " +
                   $"from {scenes.Length} world scenes.";
        }

        /// <summary>Returns every material on a season copy (anywhere in the project) to Synty's graph; returns the report line.</summary>
        public static string PutBack()
        {
            var shaders = LoadPairs(out string missing);
            if (shaders == null) return "[Seasons] " + missing + ": nothing put back.";
            int back = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string guid in AssetDatabase.FindAssets("t:Material"))
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (mat == null) continue;
                    foreach (var s in shaders)
                    {
                        if (mat.shader != s.season) continue;
                        Swap(mat, s.synty);
                        back++;
                        break;
                    }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.SaveAssets();
            return $"[Seasons] Synty materials put back on Synty's shaders: {back}.";
        }

        /// <summary>The shader changes; the queue, keywords and every value stay (the copies share Synty's property names).</summary>
        static void Swap(Material mat, Shader to)
        {
            int queue = mat.renderQueue;
            string[] keywords = mat.shaderKeywords;
            mat.shader = to;
            mat.renderQueue = queue;
            mat.shaderKeywords = keywords;
            EditorUtility.SetDirty(mat);
        }

        static List<(Shader synty, Shader season)> LoadPairs(out string missing)
        {
            missing = null;
            var list = new List<(Shader, Shader)>();
            foreach (var (synty, season) in Pairs)
            {
                var a = AssetDatabase.LoadAssetAtPath<Shader>(synty);
                var b = AssetDatabase.LoadAssetAtPath<Shader>(season);
                if (a == null || b == null) { missing = "missing " + (a == null ? synty : season); return null; }
                list.Add((a, b));
            }
            return list;
        }
    }
}
