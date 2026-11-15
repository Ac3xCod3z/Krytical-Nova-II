using System.Collections.Generic;
using System.IO;
using System.Linq;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Krytical Nova ▸ Items ▸ Build Drop Models (owner, 14 Nov 2026): the real 3D drop models replace the stand-in
    /// parcel. Sets up the import of each model in <see cref="SourceRoot"/> (an FBX and its basecolor texture), makes a
    /// URP Lit material and a ready prefab per model (centred, at its size, tipped over when long, lifted so it never
    /// dips into the ground), fills <see cref="LibraryPath"/> (models, then every item's model by the rules below) and
    /// gives the WorldDrop prefab the library. Sizes and tilts are seeded once from <see cref="Seeds"/>; after that the
    /// library asset holds them (edit it there and build again).
    /// </summary>
    public static class DropModelLibraryBuilder
    {
        public const string Root = "Assets/_Game/Gameplay/Items/DropModels";
        public const string SourceRoot = Root + "/Source";
        public const string MaterialsRoot = Root + "/Materials";
        public const string PrefabsRoot = Root + "/Prefabs";
        public const string LibraryPath = "Assets/_Game/Gameplay/Items/Data/Assets/DropModelLibrary.asset";
        public const string WorldDropPath = "Assets/_Game/Gameplay/Items/Prefabs/WorldDrop.prefab";
        private const string BuffChipHolder = "Chip_Aegis";
        private const float BobAmplitude = 0.08f;   // WorldDrop.Update bobs the visual this far up and down

        /// <summary>First sizes (largest side, metres) and tilts (degrees off upright for long things). Key = row name.</summary>
        private static readonly (string Key, string Folder, float Size, float Tilt)[] Seeds =
        {
            ("Ring", "Ring", 0.12f, 0f),
            ("Microchip", "microchip", 0.14f, 0f),
            ("AttackChip", "Attack Chip", 0.16f, 0f),
            ("BuffChip", "Buff Chip", 0.16f, 0f),
            ("RecallKey", "Recall Key", 0.16f, 0f),
            ("SerumVial", "Serum Vial", 0.16f, 0f),
            ("DyeCell", "Dye Cell", 0.16f, 0f),
            ("Amulet", "Amulate", 0.18f, 0f),
            ("Pass", "Pass", 0.18f, 0f),
            ("StimInjector", "Stim Injector", 0.20f, 0f),
            ("Permit", "Permit", 0.20f, 0f),
            ("Paper", "Paper", 0.22f, 0f),
            ("Booster", "Booster", 0.22f, 0f),
            ("Failsafe", "Failsafe", 0.22f, 0f),
            ("FusionCatalyst", "Fusion Catalyst", 0.22f, 0f),
            ("FluxFlask", "Flux Flask", 0.24f, 0f),
            ("Alumite", "Alumite", 0.26f, 0f),
            ("Iridion", "Iridion", 0.26f, 0f),
            ("Orichalc", "Orichalc", 0.26f, 0f),
            ("Eversteel", "Eversteel", 0.26f, 0f),
            ("TrinityPlatinum", "Trinity Platinum", 0.26f, 0f),
            ("PrimeAlloy", "Prime Alloy", 0.28f, 0f),
            ("CombatKnife", "MonoBlade", 0.28f, 70f),
            ("Dossier", "Dossier", 0.30f, 0f),
            ("PetRations", "Pet Rations", 0.30f, 0f),
            ("Gloves", "Gloves 3D", 0.30f, 0f),
            ("CarbinePistol", "Carbine Pistol", 0.35f, 0f),
            ("MountFeed", "mount feed", 0.35f, 0f),
            ("Boots", "Boots", 0.38f, 0f),
            ("PetDrop", "Pet drop", 0.40f, 0f),
            ("ServoClaws", "Servo claws", 0.45f, 0f),
            ("ArcCaster", "Arc Caster", 0.55f, 60f),
            ("Armor", "Armor 3D", 0.55f, 0f),
            ("Saddle", "Mount Drop", 0.55f, 0f),
            ("Harvester", "Harvester", 0.60f, 60f),
            ("Cape", "cape", 0.60f, 0f),
            ("Monoblade", "MonoBlade", 0.70f, 70f),
            ("Warbow", "Warbow", 0.70f, 70f),
            ("GreatAxe", "Great Axe", 0.80f, 70f),
            ("Claymore", "Claymore", 0.85f, 70f),
            ("Glaive", "Glaive", 0.90f, 75f),
        };

        [MenuItem("Krytical Nova/Items/Build Drop Models")]
        public static void Build()
        {
            EnsureFolder(MaterialsRoot);
            EnsureFolder(PrefabsRoot);
            DropModelLibrary library = AssetDatabase.LoadAssetAtPath<DropModelLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<DropModelLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var models = new List<DropModelLibrary.Model>();
            var index = new Dictionary<string, int>();
            int missing = 0;
            foreach (var seed in Seeds)
            {
                DropModelLibrary.Model row = library.models.FirstOrDefault(m => m.name == seed.Key);
                if (string.IsNullOrEmpty(row.name)) row = new DropModelLibrary.Model { name = seed.Key, size = seed.Size, tilt = seed.Tilt };
                row.size = Mathf.Min(row.size, library.maxSize);
                row.prefab = BuildPrefab(seed.Key, seed.Folder, row, library);
                if (row.prefab == null) { missing++; continue; }
                index[seed.Key] = models.Count;
                models.Add(row);
            }
            library.models = models.ToArray();

            var items = new List<DropModelLibrary.ItemRow>();
            var unmapped = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/_Game/Gameplay/Items/Data/Assets" }))
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item == null || item.itemId == 0 || LootRules.IsCreds(item.itemId)) continue;
                string key = KeyFor(item);
                if (key != null && index.TryGetValue(key, out int model)) items.Add(new DropModelLibrary.ItemRow { itemId = item.itemId, model = (byte)model });
                else unmapped.Add($"{item.itemId} {item.displayName}");
            }
            library.items = items.OrderBy(r => r.itemId).ToArray();
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            HookPrefab(library);

            Debug.Log($"[DropModels] {library.models.Length} models, {library.items.Length} items mapped, {missing} model folders missing" +
                      (unmapped.Count > 0 ? $", no model for: {string.Join(", ", unmapped.Take(40))}" : "."));
        }

        /// <summary>Which model row an item uses (null = the stand-in parcel).</summary>
        public static string KeyFor(ItemDefinition item)
        {
            string name = item.displayName ?? "";
            switch (item.equipSlot)
            {
                case EquipSlot.Weapon: return WeaponKey(item.weaponClass, name);
                case EquipSlot.Armor: return "Armor";
                case EquipSlot.Gloves: return "Gloves";
                case EquipSlot.Boots: return "Boots";
                case EquipSlot.Ring: return "Ring";
                case EquipSlot.Amulet: return "Amulet";
                case EquipSlot.Cape: return "Cape";
                case EquipSlot.Pet: return "PetDrop";
            }
            switch (item.useEffect)
            {
                case ItemUseEffect.RegisterMount: return "Saddle";
                case ItemUseEffect.FeedPet: return "PetRations";
                case ItemUseEffect.Recall: return "RecallKey";
                case ItemUseEffect.FluxFlask: return "FluxFlask";
                case ItemUseEffect.FeedMount: return "MountFeed";
                case ItemUseEffect.HealMount: return "StimInjector";
                case ItemUseEffect.Serum: return "SerumVial";
                case ItemUseEffect.AutopilotTime:
                case ItemUseEffect.AutobuffTime:
                case ItemUseEffect.NameChange: return "Pass";
                case ItemUseEffect.ExperienceBoost: return "Booster";
                case ItemUseEffect.Respec:
                case ItemUseEffect.MemoryWipe: return "Microchip";
                case ItemUseEffect.LearnSkill:
                    if (name.Contains("Dossier")) return "Dossier";
                    ItemDefinition holder = item.sharesIconOf != null ? item.sharesIconOf : item;
                    return holder.name == BuffChipHolder ? "BuffChip" : "AttackChip";
            }
            switch (name)
            {
                case "Alumite": return "Alumite";
                case "Iridion": return "Iridion";
                case "Orichalc": return "Orichalc";
                case "Eversteel": return "Eversteel";
                case "Trinity Platinum": return "TrinityPlatinum";
                case "Prime Alloy": return "PrimeAlloy";
                case "Fusion Catalyst": return "FusionCatalyst";
            }
            if (name.Contains("Failsafe")) return "Failsafe";
            if (name.Contains("Stim")) return "StimInjector";
            if (name.Contains("Permit")) return "Permit";
            if (name.Contains("Pass")) return "Pass";
            if (name.Contains("Cell")) return "DyeCell";
            if (name.Contains("Page")) return "Paper";
            if (name.Contains("Dossier")) return "Dossier";
            if (name.Contains("Chip")) return "Microchip";
            return null;
        }

        private static string WeaponKey(WeaponClassId weaponClass, string name)
        {
            switch (weaponClass)
            {
                case WeaponClassId.Monoblade: return "Monoblade";
                case WeaponClassId.Glaive: return "Glaive";
                case WeaponClassId.Carbine: return "CarbinePistol";   // owner: every Hegemony Carbine is a pistol
                case WeaponClassId.Harvester: return "Harvester";
                case WeaponClassId.ServoClaws: return "ServoClaws";
                case WeaponClassId.ArcCaster: return "ArcCaster";
                case WeaponClassId.Longaxe: return "GreatAxe";
                case WeaponClassId.Claymore: return "Claymore";
                case WeaponClassId.Warbow: return "Warbow";
            }
            // The hand-made weapons from before the classes.
            if (name.Contains("Knife")) return "CombatKnife";
            if (name.Contains("Great Sword") || name.Contains("Greatsword")) return "Claymore";
            if (name.Contains("Cleaver") || name.Contains("Axe")) return "GreatAxe";
            return "Monoblade";
        }

        private static GameObject BuildPrefab(string key, string folder, DropModelLibrary.Model row, DropModelLibrary library)
        {
            string dir = $"{SourceRoot}/{folder}";
            string fbx = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.fbx").Select(p => p.Replace('\\', '/')).FirstOrDefault() : null;
            if (fbx == null) { Debug.LogWarning($"[DropModels] No FBX in {dir}."); return null; }
            string texture = Directory.GetFiles(dir).Select(p => p.Replace('\\', '/'))
                .FirstOrDefault(p => p.EndsWith(".jpg") || p.EndsWith(".png"));

            if (AssetImporter.GetAtPath(fbx) is ModelImporter importer)
            {
                bool changed = importer.materialImportMode != ModelImporterMaterialImportMode.None || importer.importAnimation ||
                               importer.animationType != ModelImporterAnimationType.None || importer.importCameras || importer.importLights;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.importCameras = false;
                importer.importLights = false;
                importer.isReadable = false;
                if (changed) importer.SaveAndReimport();
            }
            bool tiny = row.size <= 0.2f;
            if (texture != null && AssetImporter.GetAtPath(texture) is TextureImporter textureImporter)
            {
                int max = tiny ? 512 : 1024;
                if (textureImporter.maxTextureSize != max || !textureImporter.mipmapEnabled)
                {
                    textureImporter.maxTextureSize = max;
                    textureImporter.mipmapEnabled = true;
                    textureImporter.SaveAndReimport();
                }
            }

            // One material per source model; the Combat Knife shares the MonoBlade's.
            string materialPath = $"{MaterialsRoot}/M_Drop_{folder.Replace(" ", "")}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetTexture("_BaseMap", texture != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(texture) : null);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.25f);
            material.SetFloat("_Metallic", 0f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var root = new GameObject($"Drop_{key}");
            try
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                foreach (var c in body.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers)
                {
                    r.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
                    r.shadowCastingMode = tiny ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
                if (renderers.Length == 0) { Debug.LogWarning($"[DropModels] {fbx} has no mesh."); return null; }

                // A long thing lies low: its flattest pose is found from its own vertices (Tripo models are often
                // modelled on a slant), its long side is turned along X, then one end rises so it sits `tilt` degrees
                // off upright. Anything else stays as modelled.
                var pivot = new GameObject("Pivot");
                pivot.transform.SetParent(root.transform, false);
                body.transform.SetParent(pivot.transform, true);
                List<Vector3> points = SamplePoints(renderers);
                if (row.tilt > 0f) pivot.transform.localRotation = LieLow(points, row.tilt);
                Quaternion turn = pivot.transform.localRotation;
                var bounds = new Bounds(turn * points[0], Vector3.zero);
                foreach (Vector3 point in points) bounds.Encapsulate(turn * point);
                float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                float scale = largest > 0.0001f ? row.size / largest : 1f;
                pivot.transform.localPosition = -bounds.center;   // centred on the drop

                float halfHeight = bounds.extents.y * scale;
                float lift = Mathf.Max(0f, halfHeight + BobAmplitude + library.groundClearance - library.hover);
                root.transform.localPosition = new Vector3(0f, lift, 0f);
                root.transform.localScale = Vector3.one * scale;
                return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabsRoot}/Drop_{key}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Up to about 3000 of the model's vertices in world space (the box corners if a mesh cannot be read).</summary>
        private static List<Vector3> SamplePoints(Renderer[] renderers)
        {
            var points = new List<Vector3>();
            foreach (Renderer r in renderers)
            {
                Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                Vector3[] vertices = null;
                try { if (mesh != null) vertices = mesh.vertices; } catch (System.Exception) { vertices = null; }
                if (vertices == null || vertices.Length == 0)
                {
                    Bounds b = r.bounds;
                    for (int i = 0; i < 8; i++)
                        points.Add(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    continue;
                }
                int step = Mathf.Max(1, vertices.Length / 3000);
                Matrix4x4 toWorld = r.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i += step) points.Add(toWorld.MultiplyPoint3x4(vertices[i]));
            }
            return points;
        }

        /// <summary>The turn that lays the points flat (least height), long side along X, then raised to <paramref name="tilt"/> degrees off upright.</summary>
        private static Quaternion LieLow(List<Vector3> points, float tilt)
        {
            Quaternion best = Quaternion.identity;
            float bestHeight = float.MaxValue;
            for (int x = 0; x < 180; x += 5)
            for (int z = 0; z < 180; z += 5)
            {
                Quaternion q = Quaternion.Euler(x, 0f, z);
                float height = Extent(points, q, Vector3.up);
                if (height < bestHeight - 0.0001f) { bestHeight = height; best = q; }
            }
            Quaternion along = best;
            float longest = -1f;
            for (int yaw = 0; yaw < 180; yaw += 5)
            {
                Quaternion q = Quaternion.Euler(0f, yaw, 0f) * best;
                float length = Extent(points, q, Vector3.right);
                if (length > longest) { longest = length; along = q; }
            }
            return Quaternion.Euler(0f, 0f, 90f - tilt) * along;
        }

        private static float Extent(List<Vector3> points, Quaternion turn, Vector3 axis)
        {
            Vector3 a = Quaternion.Inverse(turn) * axis;   // the axis in the points' own frame
            float min = float.MaxValue, max = float.MinValue;
            foreach (Vector3 p in points)
            {
                float d = Vector3.Dot(p, a);
                if (d < min) min = d;
                if (d > max) max = d;
            }
            return max - min;
        }

        private static void HookPrefab(DropModelLibrary library)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(WorldDropPath);
            try
            {
                var drop = contents.GetComponent<WorldDrop>();
                if (drop == null) return;
                var so = new SerializedObject(drop);
                so.FindProperty("dropModels").objectReferenceValue = library;
                SerializedProperty standIn = so.FindProperty("standIn");
                Transform visual = so.FindProperty("visual").objectReferenceValue as Transform;
                if (standIn.objectReferenceValue == null && visual != null && visual.childCount > 0)
                    standIn.objectReferenceValue = visual.GetChild(0);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, WorldDropPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
