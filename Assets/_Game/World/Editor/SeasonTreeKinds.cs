using System.Collections.Generic;
using System.Text;
using KryticalNova.World;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Stamps each tree kind on its prefabs (owner, 14 Nov 2026: every kind of tree its own fall colours, evergreens stay green). The kinds
    /// and the prefab names that make them are <see cref="SeasonSettings.leafKinds"/>; a Foliage Wind prefab whose name holds one of a
    /// kind's words gets that kind's number (1 up) in its renderers' rendering layer bits 8-11, which the foliage shader reads (no script on
    /// any tree; per-renderer data, so the SRP batcher keeps batching). Run it again after adding kinds or trees; it only writes prefabs
    /// whose kind changed, and logs what it did.
    /// </summary>
    public static class SeasonTreeKinds
    {
        const string Shader = "KryticalNova/Foliage Wind";
        const uint KindBits = 0xF00u;

        [MenuItem("Krytical Nova/World/Seasons/Tag Tree Kinds")]
        public static void TagFromMenu() => Debug.Log(Tag());

        /// <summary>Tags every Foliage Wind prefab under Assets/_Game; returns the report line.</summary>
        public static string Tag()
        {
            SeasonSettings settings = SeasonSettings.Instance;
            if (settings == null || settings.leafKinds == null) return "[Seasons] no SeasonSettings (Resources/SeasonSettings): nothing tagged.";
            var report = new StringBuilder("[Seasons] tree kinds:");
            var counts = new Dictionary<int, int>();
            int changed = 0, untagged = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || !UsesFoliageWind(prefab)) continue;
                int kind = KindOf(settings, prefab.name);
                counts.TryGetValue(kind, out int n);
                counts[kind] = n + 1;
                if (kind == 0) untagged++;
                if (!NeedsWrite(prefab, kind)) continue;
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                        if (IsFoliage(r)) r.renderingLayerMask = (r.renderingLayerMask & ~KindBits) | ((uint)kind << 8);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changed++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            for (int k = 0; k <= settings.leafKinds.Count && k < 16; k++)
            {
                counts.TryGetValue(k, out int n);
                report.Append(' ').Append(k == 0 ? "no kind" : settings.leafKinds[k - 1].name).Append(' ').Append(n).Append(',');
            }
            report.Append(" prefabs written ").Append(changed).Append(untagged > 0 ? $" ({untagged} foliage prefabs with no kind: bushes and grass keep the default)" : "");
            return report.ToString();
        }

        /// <summary>The kind (1 up) whose words the prefab's name holds, 0 for none. The first kind that matches wins.</summary>
        public static int KindOf(SeasonSettings settings, string prefabName) => settings.LeafKindOf(prefabName);

        static bool UsesFoliageWind(GameObject prefab)
        {
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true)) if (IsFoliage(r)) return true;
            return false;
        }

        static bool IsFoliage(Renderer r)
        {
            foreach (Material m in r.sharedMaterials) if (m != null && m.shader != null && m.shader.name == Shader) return true;
            return false;
        }

        static bool NeedsWrite(GameObject prefab, int kind)
        {
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                if (IsFoliage(r) && ((r.renderingLayerMask & KindBits) >> 8) != (uint)kind) return true;
            return false;
        }
    }
}
