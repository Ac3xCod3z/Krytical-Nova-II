using KryticalNova.Housing;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.EditorTools.Housing
{
    /// <summary>
    /// Builds the Lot of the Hall sandbox and the settings asset that points at it: a fenced piece of ground with a
    /// pad for the Hall, a path from the gate, and the points a player arrives at and the Hall stands on. The Lot
    /// is a prefab that is set down when a player goes to their Hall, so no scene is touched. The Halls that stand
    /// on it are the vendor's own buildings for now, one per faction; baked Facades of one footprint come later.
    /// </summary>
    public static class HousingLotBuilder
    {
        public const string Root = "Assets/_Game/Gameplay/Housing";
        public const string PrefabFolder = Root + "/Prefabs";
        public const string MaterialFolder = Root + "/Materials";
        public const string DataFolder = Root + "/Data/Assets";
        public const string LotPath = PrefabFolder + "/HousingLot_Sandbox.prefab";
        public const string SettingsPath = DataFolder + "/HousingSettings.asset";
        public const string GhostMaterialPath = MaterialFolder + "/M_ArrangeGhost.mat";
        public const string FurnishingLayerName = "Furnishing";
        private const string GroundLayerName = "Ground";

        public const string PlainMaterialPath = MaterialFolder + "/M_Room_Plain.mat";
        public const string SurroundMaterialPath = MaterialFolder + "/M_Room_Surround.mat";
        public const string DoorMarkMaterialPath = MaterialFolder + "/M_Door_Mark.mat";

        // The Lot's measurements, in metres. Every Lot of a faction will share them.
        private static readonly Vector2 LotSize = new Vector2(40f, 52f);
        private static readonly Vector2 PadCenter = new Vector2(0f, 6f);
        public static readonly Vector2 PadSize = new Vector2(20f, 16f);
        private const float Apron = 5f;          // ground outside the fence, so the gate can be walked up to
        private const float GateWidth = 4f;
        private const float PathWidth = 2.4f;
        private const float Outskirts = 1200f;   // the country round the Lot, as far as the eye goes
        private const float DoorMarkDepth = 1.6f;

        private const string FencePrefab = "Assets/Synty/PolygonFantasyKingdom/Prefabs/Props/SM_Prop_Fence_01.prefab";
        // The door that stands in the room's front wall, by faction (index = FactionId): none, Hegemony, Null, Covenant.
        private static readonly string[] RoomDoors =
        {
            "Assets/Synty/PolygonElvenRealm/Prefabs/Buildings/SM_Bld_Door_01.prefab",
            "Assets/Synty/PolygonCyberCity/Prefabs/Buildings/SM_Bld_Door_Double_Large_01.prefab",
            "Assets/Synty/PolygonDarkFortress/Prefabs/Buildings/SM_Bld_Door_03.prefab",
            "Assets/Synty/PolygonElvenRealm/Prefabs/Buildings/SM_Bld_Door_01.prefab",
        };

        // The stair that stands where a floor's way up is, by faction.
        private static readonly string[] RoomStairs =
        {
            "Assets/Synty/PolygonElvenRealm/Prefabs/Buildings/SM_Bld_Stairs_interior_01.prefab",
            "Assets/Synty/PolygonElvenRealm/Prefabs/Buildings/SM_Bld_Stairs_interior_01.prefab",
            "Assets/Synty/PolygonElvenRealm/Prefabs/Buildings/SM_Bld_Stairs_interior_01.prefab",
            "Assets/Synty/PolygonElvenRealm/Prefabs/Buildings/SM_Bld_Stairs_interior_01.prefab",
        };

        [MenuItem("Krytical Nova/Housing/1. Build Sandbox Lot")]
        public static void Build()
        {
            WeatherVfxBuilder.EnsureFolder(PrefabFolder);
            WeatherVfxBuilder.EnsureFolder(MaterialFolder);
            WeatherVfxBuilder.EnsureFolder(DataFolder);
            EnsureLayer(FurnishingLayerName);

            Material lawn = Lit("M_Lot_Lawn", new Color(0.33f, 0.52f, 0.27f));
            Material pad = Lit("M_Lot_Pad", new Color(0.46f, 0.45f, 0.43f));
            Material path = Lit("M_Lot_Path", new Color(0.62f, 0.56f, 0.45f));
            Material country = Lit("M_Lot_Country", new Color(0.27f, 0.43f, 0.24f));
            Material plain = Lit("M_Room_Plain", new Color(0.55f, 0.53f, 0.5f));
            Material surround = Unlit("M_Room_Surround", new Color(0.035f, 0.04f, 0.05f, 1f), seeThrough: false);
            Material doorMark = Unlit("M_Door_Mark", new Color(1f, 0.82f, 0.35f, 0.55f), seeThrough: true);
            Material ghost = Ghost();

            var root = new GameObject("HousingLot_Sandbox");
            try
            {
                int ground = LayerMask.NameToLayer(GroundLayerName);
                // Everything that is seen of the Lot, so that it can be left undrawn while its player is indoors.
                Transform outside = Point(root.transform, "Outside", Vector3.zero);
                Vector3 groundSize = new Vector3(LotSize.x + Apron * 2f, 1f, LotSize.y + Apron * 2f);
                GameObject slab = Slab(outside, "Ground", new Vector3(0f, -0.5f, 0f), groundSize, lawn, keepCollider: true);
                slab.layer = ground >= 0 ? ground : 0;
                // Open country round the Lot, so it does not stand in the sky. Looked at, never walked on.
                Slab(outside, "Surroundings", new Vector3(0f, -0.56f, 0f), new Vector3(Outskirts, 1f, Outskirts), country, keepCollider: false);

                Slab(outside, "Pad", new Vector3(PadCenter.x, 0.01f, PadCenter.y), new Vector3(PadSize.x, 0.02f, PadSize.y), pad, keepCollider: false);
                float front = -LotSize.y * 0.5f - Apron;
                float padFront = PadCenter.y - PadSize.y * 0.5f;
                Slab(outside, "Path", new Vector3(0f, 0.012f, (front + padFront) * 0.5f), new Vector3(PathWidth, 0.02f, padFront - front), path, keepCollider: false);
                // Where the Hall's door is, whatever Facade stands behind it.
                Slab(outside, "Door Mark", new Vector3(PadCenter.x, 0.03f, padFront - DoorMarkDepth * 0.5f), new Vector3(PathWidth, 0.02f, DoorMarkDepth), doorMark, keepCollider: false);

                Fence(outside);

                Transform arrival = Point(root.transform, "Arrival", new Vector3(0f, 0f, -LotSize.y * 0.5f + 2.5f));
                Transform facade = Point(outside, "FacadeAnchor", new Vector3(PadCenter.x, 0f, PadCenter.y));
                Transform pieces = Point(outside, "Pieces", Vector3.zero);

                var lot = root.AddComponent<HousingLot>();
                Vector2 doorClear = new Vector2(PathWidth + 0.6f, 3.5f);
                lot.Configure(LotSize, PadCenter, PadSize, new Vector2(0f, padFront - doorClear.y * 0.5f), doorClear, arrival, facade, pieces, outside);

                // Walked on from the moment the Lot is set down: the mesh is made then, from the Lot's own colliders.
                var surface = root.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.Children;
                surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                surface.layerMask = (1 << (ground >= 0 ? ground : 0)) | 1;

                PrefabUtility.SaveAsPrefabAsset(root, LotPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            var settings = AssetDatabase.LoadAssetAtPath<HousingSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<HousingSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            settings.lotPrefab = AssetDatabase.LoadAssetAtPath<HousingLot>(LotPath);
            settings.ghostMaterial = ghost;
            settings.roomPlainMaterial = plain;
            settings.roomSurroundMaterial = surround;
            settings.doorMarkMaterial = doorMark;
            settings.facadeByFaction = new GameObject[0]; // Facades are looks in the catalog now
            if (settings.roomDoorByFaction == null || settings.roomDoorByFaction.Length != RoomDoors.Length) settings.roomDoorByFaction = new GameObject[RoomDoors.Length];
            for (int n = 0; n < RoomDoors.Length; n++)
            {
                if (settings.roomDoorByFaction[n] != null) continue; // chosen by hand: left alone
                settings.roomDoorByFaction[n] = AssetDatabase.LoadAssetAtPath<GameObject>(RoomDoors[n]);
                if (settings.roomDoorByFaction[n] == null) Debug.LogWarning($"[Housing] No room door for faction {n}: {RoomDoors[n]} is missing.");
            }
            if (settings.roomStairsByFaction == null || settings.roomStairsByFaction.Length != RoomStairs.Length) settings.roomStairsByFaction = new GameObject[RoomStairs.Length];
            for (int n = 0; n < RoomStairs.Length; n++)
                if (settings.roomStairsByFaction[n] == null) settings.roomStairsByFaction[n] = AssetDatabase.LoadAssetAtPath<GameObject>(RoomStairs[n]);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Housing] Sandbox Lot built: {LotPath} ({LotSize.x} x {LotSize.y} m, pad {PadSize.x} x {PadSize.y} m).");
        }

        private static GameObject Slab(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool keepCollider)
        {
            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.SetParent(parent, false);
            slab.transform.localPosition = position;
            slab.transform.localScale = size;
            slab.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!keepCollider) Object.DestroyImmediate(slab.GetComponent<Collider>());
            return slab;
        }

        private static Transform Point(Transform parent, string name, Vector3 position)
        {
            var point = new GameObject(name).transform;
            point.SetParent(parent, false);
            point.localPosition = position;
            return point;
        }

        // The vendor's fence, run along the four sides with a gap at the front for the gate.
        private static void Fence(Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FencePrefab);
            if (prefab == null)
            {
                Debug.LogWarning($"[Housing] No fence: {FencePrefab} is missing. The Lot is built without one.");
                return;
            }
            Bounds bounds = PrefabBounds(prefab);
            bool alongX = bounds.size.x >= bounds.size.z;
            float length = Mathf.Max(0.5f, alongX ? bounds.size.x : bounds.size.z);
            float middle = alongX ? bounds.center.x : bounds.center.z;
            var fence = new GameObject("Fence").transform;
            fence.SetParent(parent, false);

            float halfX = LotSize.x * 0.5f, halfZ = LotSize.y * 0.5f;
            Run(fence, prefab, new Vector3(-halfX, 0f, halfZ), new Vector3(halfX, 0f, halfZ), length, middle, alongX);     // back
            Run(fence, prefab, new Vector3(-halfX, 0f, -halfZ), new Vector3(-halfX, 0f, halfZ), length, middle, alongX);   // left
            Run(fence, prefab, new Vector3(halfX, 0f, -halfZ), new Vector3(halfX, 0f, halfZ), length, middle, alongX);     // right
            Run(fence, prefab, new Vector3(-halfX, 0f, -halfZ), new Vector3(-GateWidth * 0.5f, 0f, -halfZ), length, middle, alongX);
            Run(fence, prefab, new Vector3(GateWidth * 0.5f, 0f, -halfZ), new Vector3(halfX, 0f, -halfZ), length, middle, alongX);
        }

        private static void Run(Transform parent, GameObject prefab, Vector3 from, Vector3 to, float length, float middle, bool alongX)
        {
            Vector3 along = to - from;
            float distance = along.magnitude;
            int count = Mathf.Max(1, Mathf.RoundToInt(distance / length));
            float stretch = distance / (count * length);
            Vector3 direction = along / distance;
            // The piece's long side is turned to lie along the run.
            Quaternion turn = Quaternion.LookRotation(alongX ? Vector3.Cross(direction, Vector3.up) : direction, Vector3.up);
            for (int n = 0; n < count; n++)
            {
                var piece = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                Vector3 scale = piece.transform.localScale;
                if (alongX) scale.x *= stretch; else scale.z *= stretch;
                piece.transform.localScale = scale;
                Vector3 centre = from + direction * ((n + 0.5f) * length * stretch);
                piece.transform.localRotation = turn;
                piece.transform.localPosition = centre - turn * (alongX ? new Vector3(middle * stretch, 0f, 0f) : new Vector3(0f, 0f, middle * stretch));
            }
        }

        /// <summary>A prefab's box in its own space, from its renderers.</summary>
        public static Bounds PrefabBounds(GameObject prefab)
        {
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                // Meshes only: a flame's or a glow's box says nothing about where the piece stands.
                bool any = false;
                Bounds bounds = default;
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
                {
                    if (!IsMesh(renderer)) continue;
                    if (any) bounds.Encapsulate(renderer.bounds); else bounds = renderer.bounds;
                    any = true;
                }
                return any ? bounds : new Bounds(Vector3.zero, Vector3.one);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>A renderer that draws a mesh it actually has (not a particle effect, a trail, or an empty slot).</summary>
        public static bool IsMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh != null;
            return renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null;
        }

        private static Material Lit(string name, Color colour)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", 0.05f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // See-through and unlit: the Arrange tool colours it green or red.
        private static Material Ghost()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
            if (material != null)
            {
                // Drawn from both sides: the frame round a piece is a flat line seen from above.
                if (material.GetFloat("_Cull") != 0f) { material.SetFloat("_Cull", 0f); EditorUtility.SetDirty(material); }
                return material;
            }
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "M_ArrangeGhost" };
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(0.35f, 1f, 0.55f, 0.55f));
            material.SetFloat("_Cull", 0f);
            AssetDatabase.CreateAsset(material, GhostMaterialPath);
            return material;
        }

        private static Material Unlit(string name, Color colour, bool seeThrough)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
            if (seeThrough)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            material.SetColor("_BaseColor", colour);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>Gives the project a layer of this name when it has none. Returns its number, or -1.</summary>
        public static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int n = 6; n < layers.arraySize; n++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(n);
                if (!string.IsNullOrEmpty(layer.stringValue)) continue;
                layer.stringValue = name;
                tagManager.ApplyModifiedProperties();
                return n;
            }
            Debug.LogError($"[Housing] No free user layer for '{name}'.");
            return -1;
        }
    }
}
