using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VH = global::VHierarchy.VHierarchy;

namespace KryticalNova.EditorTools.Organization
{
    /// <summary>
    /// Groups a zone scene's loose root objects under "--- X ---" headers and colour/icon-codes every header with
    /// vHierarchy 2 (see ProjectOrganization.md, "Scene hierarchy"). Builders keep owning their own headers
    /// (Environment, Town, Outskirts, NPCs, Portals, Spawn, Traffic); this only files the hand-placed objects and
    /// fixes the order. Re-runnable after any rebuild.
    ///
    /// Rules: NetworkManager stays at the root (DontDestroyOnLoad only works on root objects) and so does the
    /// offline Player (OfflinePlayerHandoff owns its lifetime); both sit directly under their header instead.
    /// Headers are never EditorOnly because they parent runtime objects.
    /// </summary>
    public static class SceneHierarchyOrganizer
    {
        public const string DataFolder = "Assets/_Game/Tools/Editor/Hierarchy";
        private const string DataPath = DataFolder + "/vHierarchy Data.asset";

        // vHierarchy colour indices: 0 none, 1 grey, 2 red, 3 orange, 4 yellow-green, 5 green, 6 cyan, 7 blue, 8 violet, 9 magenta.
        private const int Grey = 1, Red = 2, Orange = 3, Yellow = 4, Green = 5, Cyan = 6, Blue = 7, Violet = 8, Magenta = 9;

        private struct Group
        {
            public string Header;
            public int Color;
            public string Icon;
            public string[] Children;   // moved under the header (by exact root name or by component type name)
            public string[] RootSiblings; // stay at the root, placed directly after the header
        }

        private static readonly Group[] Layout =
        {
            new Group { Header = "--- Systems ---", Color = Grey, Icon = "d_Settings",
                Children = new[] { "EventSystem", "Navigation", "World Environment", "PFX_GameFeel" },
                RootSiblings = new[] { "NetworkManager" } },
            new Group { Header = "--- Cameras ---", Color = Blue, Icon = "Camera Icon",
                Children = new[] { "Main Camera", "Player Orbit Camera" } },
            new Group { Header = "--- Lighting & FX ---", Color = Yellow, Icon = "Light Icon",
                Children = new[] { "Directional Light", "Global Volume", "Environment Presenter", "Lighting", "ProbeVolumePerSceneData" } },
            new Group { Header = "--- UI ---", Color = Violet, Icon = "Canvas Icon",
                Children = new[] { "Progression UI", "Zone UI", "World Markers", "Zone Minimap" } },
            new Group { Header = "--- Gameplay ---", Color = Red, Icon = "d_PlayButton On",
                Children = new[] { "Combat Dummy" },
                RootSiblings = new[] { "Player" } },
            new Group { Header = "--- Spawn ---", Color = Magenta, Icon = "d_AvatarPivot" },
            new Group { Header = "--- NPCs ---", Color = Cyan, Icon = "d_AvatarSelector" },
            new Group { Header = "--- Portals ---", Color = Magenta, Icon = "d_SceneViewFx" },
            new Group { Header = "--- Environment ---", Color = Green, Icon = "Terrain Icon" },
            new Group { Header = "--- Town ---", Color = Orange, Icon = "d_PreMatCube" },
            new Group { Header = "--- Outskirts ---", Color = Orange, Icon = "d_PreMatCylinder" },
            new Group { Header = "--- Traffic ---", Color = Grey, Icon = "NavMeshAgent Icon" },
        };

        /// <summary>Second-level groups that get their own tint (searched under the given header).</summary>
        private static readonly (string Header, string Child, int Color)[] SubGroups =
        {
            ("--- Town ---", "Walls", Grey),
            ("--- Outskirts ---", "Ruins", Red),
            ("--- Town ---", "Points of Interest", Magenta),
            ("--- Outskirts ---", "Points of Interest", Magenta),
            ("--- Lighting & FX ---", "Lighting", Yellow),
        };

        [MenuItem("Krytical Nova/Project/Organize Scene Hierarchy", priority = 120)]
        public static void OrganizeActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            EnsureDataAsset();
            int moved = Organize(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Hierarchy] {scene.name}: {moved} object(s) filed under headers; headers coloured with vHierarchy.");
        }

        public static int Organize(Scene scene)
        {
            int moved = 0;
            var roots = scene.GetRootGameObjects().ToList();
            var placed = new HashSet<GameObject>();
            int index = 0;

            foreach (var g in Layout)
            {
                bool hasContent = roots.Any(r => r.name == g.Header)
                    || (g.Children ?? new string[0]).Any(n => FindAnywhere(scene, n) != null)
                    || (g.RootSiblings ?? new string[0]).Any(n => roots.Any(r => r.name == n));
                if (!hasContent) continue;

                var header = roots.FirstOrDefault(r => r.name == g.Header);
                if (header == null)
                {
                    header = new GameObject(g.Header);
                    Undo.RegisterCreatedObjectUndo(header, "Create header");
                    SceneManager.MoveGameObjectToScene(header, scene);
                    roots.Add(header);
                }
                if (header.CompareTag("EditorOnly")) header.tag = "Untagged";
                Undo.RecordObject(header.transform, "Order header");
                header.transform.SetSiblingIndex(index++);
                placed.Add(header);
                Paint(header, g.Color, g.Icon);

                if (g.Children != null)
                    foreach (var name in g.Children)
                    {
                        var go = FindAnywhere(scene, name);
                        if (go == null || go == header) continue;
                        if (go.transform.parent != header.transform)
                        {
                            Undo.SetTransformParent(go.transform, header.transform, true, "File under header");
                            moved++;
                        }
                    }

                if (g.RootSiblings != null)
                    foreach (var name in g.RootSiblings)
                    {
                        var go = roots.FirstOrDefault(r => r.name == name);
                        if (go == null) continue;
                        Undo.RecordObject(go.transform, "Order root");
                        go.transform.SetSiblingIndex(index++);
                        placed.Add(go);
                        Paint(go, g.Color, null);
                    }
            }

            foreach (var (headerName, childName, color) in SubGroups)
            {
                var header = roots.FirstOrDefault(r => r.name == headerName);
                var child = header != null ? header.transform.Find(childName) : null;
                if (child != null) Paint(child.gameObject, color, null);
            }

            // Anything left at the root that isn't a known group is flagged so it gets a home on the next pass.
            foreach (var r in scene.GetRootGameObjects())
                if (!placed.Contains(r) && !r.name.StartsWith("---"))
                    Debug.LogWarning($"[Hierarchy] '{r.name}' is loose at the root of {scene.name}; add it to SceneHierarchyOrganizer.Layout.", r);
            return moved;
        }

        private static GameObject FindAnywhere(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                // Only one level down: a header's direct children.
                var t = root.name.StartsWith("---") ? root.transform.Find(name) : null;
                if (t != null) return t.gameObject;
            }
            return null;
        }

        private static void Paint(GameObject go, int color, string icon)
        {
            VH.SetColor(go, color, false);
            if (!string.IsNullOrEmpty(icon) && EditorGUIUtility.IconContent(icon)?.image != null)
                VH.SetIcon(go, icon, false);
        }

        /// <summary>vHierarchy stores colours/icons in one project asset; keep it in our tooling folder, not the vendor's.</summary>
        private static void EnsureDataAsset()
        {
            if (AssetDatabase.FindAssets("t:VHierarchyData").Length > 0) return;
            if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder("Assets/_Game/Tools/Editor", "Hierarchy");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<global::VHierarchy.VHierarchyData>(), DataPath);
            AssetDatabase.SaveAssets();
        }
    }
}
