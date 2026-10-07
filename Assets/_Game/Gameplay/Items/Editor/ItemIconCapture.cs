using System.Collections.Generic;
using System.IO;
using System.Linq;
using KryticalNova.CharacterCreation;
using KryticalNova.Items;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Renders inventory icons for items in an isolated preview scene: orthographic camera, three-point lighting and a
    /// transparent background, framed from each item's bounds with a per-slot view angle. Rings/amulets get simple
    /// procedural jewellery meshes (the Synty packs have none). Output: 256px sprites in Gameplay/Items/Data/Assets/Icons.
    /// </summary>
    public static class ItemIconCapture
    {
        public const string IconFolder = SidekickGearGenerator.ItemsRoot + "/Icons";
        private const int Size = 256;
        private const int Supersample = 2; // MSAA targets break URP render requests; supersample + box filter instead
        private const string SidekickMaterialPath = "Assets/_Game/Characters/Player/Player_01/Materials/Player_01.mat";
        private static readonly int ColorMapId = Shader.PropertyToID("_ColorMap");

        private static readonly BodyRegion[] ArmorIconRegions =
        {
            BodyRegion.Torso, BodyRegion.ArmUpperLeft, BodyRegion.ArmUpperRight, BodyRegion.Hips,
            BodyRegion.AttachmentShoulderLeft, BodyRegion.AttachmentShoulderRight, BodyRegion.AttachmentHipsFront,
            BodyRegion.AttachmentHipsBack, BodyRegion.AttachmentHipsLeft, BodyRegion.AttachmentHipsRight
        };

        /// <summary>
        /// What gets an icon of its own: every look template a gear line wears (its tiers share that icon) and every
        /// catalog item that is not ladder gear (pets, hand-made items).
        /// </summary>
        private static List<ItemDefinition> IconSubjects()
        {
            var subjects = GearStatBaker.LookTemplates();
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            if (catalog != null)
                subjects.AddRange(catalog.Items.Where(i => i != null && !i.IsLadderGear && i.IsEquippable && !subjects.Contains(i)));
            return subjects;
        }

        [MenuItem("Krytical Nova/Items/2. Capture Item Icons (All)")]
        public static void CaptureAll()
        {
            Capture(IconSubjects());
            GearStatBaker.Bake(); // hand each look's icon to its tiers
            if (CaptureLineIcons(missingOnly: false) > 0) GearStatBaker.Bake();
        }

        /// <summary>Only the items that have no icon yet (new gear), leaving every existing icon as it is.</summary>
        [MenuItem("Krytical Nova/Items/Capture Icons (Missing Only)")]
        public static void CaptureMissing()
        {
            var missing = IconSubjects().Where(i => i.icon == null).ToList();
            if (missing.Count > 0)
            {
                Capture(missing);
                GearStatBaker.Bake();
            }
            int lines = CaptureLineIcons(missingOnly: true);
            if (lines > 0) GearStatBaker.Bake();
            if (missing.Count == 0 && lines == 0) Debug.Log("[Items] Every look and every themed line already has an icon.");
        }

        [MenuItem("Krytical Nova/Items/Capture Icons (Selected Items)")]
        public static void CaptureSelected()
        {
            Capture(Selection.objects.OfType<ItemDefinition>().ToList());
        }

        /// <summary>
        /// The icons of the lines that wear a colour theme: one per line, drawn in its set's colours, because two
        /// lines can share a look and differ only in colour. The line's first tier stands for all of them, so the
        /// ladder has to be baked first. Returns how many were captured.
        /// </summary>
        public static int CaptureLineIcons(bool missingOnly)
        {
            var dictionary = AssetDatabase.LoadAssetAtPath<GearDictionary>(SidekickGearGenerator.DictionaryPath);
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            if (dictionary == null || catalog == null) return 0;

            var lines = dictionary.Lines.ToList();
            var subjects = new List<ItemDefinition>();
            var lineOf = new Dictionary<ItemDefinition, int>();
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (!line.IsValid || line.look == null || string.IsNullOrEmpty(line.theme)) continue;
                if (missingOnly && line.icon != null) continue;
                var first = catalog.Get(line.FirstItemId);
                if (first == null || first.colorTheme == 0) continue;
                subjects.Add(first);
                lineOf[first] = i;
            }
            if (subjects.Count == 0) return 0;

            Capture(subjects, item => $"{lines[lineOf[item]].look.name}_{lines[lineOf[item]].theme}", (item, sprite) =>
            {
                var line = lines[lineOf[item]];
                line.icon = sprite;
                lines[lineOf[item]] = line;
            });
            dictionary.SetLines(dictionary.Parts, lines);
            EditorUtility.SetDirty(dictionary);
            AssetDatabase.SaveAssets();
            return subjects.Count;
        }

        public static void Capture(List<ItemDefinition> items) =>
            Capture(items, item => item.name, (item, sprite) =>
            {
                item.icon = sprite;
                EditorUtility.SetDirty(item);
            });

        private static void Capture(List<ItemDefinition> items, System.Func<ItemDefinition, string> fileName,
            System.Action<ItemDefinition, Sprite> assign)
        {
            if (items.Count == 0) return;
            WeatherVfxBuilder.EnsureFolder(IconFolder);
            var sidekickMat = AssetDatabase.LoadAssetAtPath<Material>(SidekickMaterialPath);
            var characters = AssetDatabase.LoadAssetAtPath<CharacterCreationDatabase>(CharacterDataBaker.DatabasePath);
            var packMaterials = new Dictionary<string, Material>();
            var scene = EditorSceneManager.NewPreviewScene();
            var rt = new RenderTexture(Size * Supersample, Size * Supersample, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var temp = new List<Object>();
            int done = 0;
            // Icons are rendered at 2x and downsampled, so the preview's dots would shrink to grain: enlarge for capture.
            var comic = PreviewComicFeature();
            float savedDotSize = comic != null ? comic.halftone.dotSize : 0f;
            if (comic != null) comic.halftone.dotSize = savedDotSize * IconDotScale;
            try
            {
                var cam = CreateRig(scene);
                var written = new List<(ItemDefinition item, string path)>();
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    if (EditorUtility.DisplayCancelableProgressBar("Capturing item icons", item.displayName, (float)i / items.Count)) break;
                    var material = PackMaterial(item, sidekickMat, characters, packMaterials, temp);
                    var subject = BuildSubject(item, material, temp, out Vector3 viewDir);
                    if (subject == null) continue;
                    SceneManager.MoveGameObjectToScene(subject, scene);
                    string path = $"{IconFolder}/{fileName(item)}.png";
                    RenderToPng(cam, rt, subject.transform, viewDir, path);
                    Object.DestroyImmediate(subject);
                    written.Add((item, path));
                }

                // A new file has no importer until the database has seen it, and imports are deferred inside the batch.
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.StartAssetEditing();
                try { foreach (var w in written) ImportAsSprite(w.path); }
                finally { AssetDatabase.StopAssetEditing(); }

                foreach (var w in written)
                {
                    assign(w.item, AssetDatabase.LoadAssetAtPath<Sprite>(w.path));
                    done++;
                }
                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (comic != null) comic.halftone.dotSize = savedDotSize;
                rt.Release();
                Object.DestroyImmediate(rt);
                foreach (var o in temp) if (o != null) Object.DestroyImmediate(o);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            Debug.Log($"[Items] Captured {done} icons into {IconFolder}.");
        }

        private static Camera CreateRig(Scene scene)
        {
            var camGo = new GameObject("IconCamera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            // Game camera on the preview renderer: gets the comic outlines + Ben-Day dots, but no SSAO.
            cam.cameraType = CameraType.Game;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50f;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false; // post would overwrite alpha
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            data.SetRenderer(PreviewRendererIndex());

            AddLight(scene, "Key", new Vector3(35f, -35f, 0f), 1.25f, new Color(1f, 0.96f, 0.9f));
            AddLight(scene, "Fill", new Vector3(15f, 140f, 0f), 0.55f, new Color(0.75f, 0.85f, 1f));
            AddLight(scene, "Rim", new Vector3(-20f, 180f, 0f), 0.9f, Color.white);
            return cam;
        }

        private const float IconDotScale = 2f;

        private static KryticalNova.Rendering.ComicStyleFeature PreviewComicFeature() =>
            AssetDatabase.LoadAllAssetsAtPath("Assets/_Game/Rendering/Settings/UIPreview_Renderer.asset")
                .OfType<KryticalNova.Rendering.ComicStyleFeature>().FirstOrDefault();

        private static int PreviewRendererIndex()
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                var list = urp.rendererDataList;
                for (int i = 0; i < list.Length; i++)
                    if (list[i] != null && list[i].name == "UIPreview_Renderer") return i;
            }
            Debug.LogWarning("[Items] UIPreview_Renderer not found; run Krytical Nova/Setup/Zone Scene (Open Scene) first. Icons will lack the comic pass.");
            return -1;
        }

        private static void AddLight(Scene scene, string name, Vector3 euler, float intensity, Color color)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.rotation = Quaternion.Euler(euler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
        }

        /// <summary>
        /// Gear is drawn in its own pack's colours: the palette the lineage of that pack starts with, and over it the
        /// colours of the piece's set when it has a theme. A piece without one (the basic outfit) takes its wearer's
        /// palette in the game, so this is the look it has on the characters it was made for.
        /// </summary>
        private static Material PackMaterial(ItemDefinition item, Material baseMaterial, CharacterCreationDatabase characters,
            Dictionary<string, Material> cache, List<Object> temp)
        {
            string pack = PackOf(item);
            if (pack == null || characters == null || baseMaterial == null) return baseMaterial;
            string key = $"{pack}:{item.colorTheme}";
            if (cache.TryGetValue(key, out var cached)) return cached;

            var result = baseMaterial;
            foreach (var lineage in characters.Lineages)
            {
                if (lineage == null || lineage.ownPack != pack) continue;
                var recipe = RecipeEdit.Default(lineage, BodyType.Masculine);
                var pixels = new Color32[CharacterColorMap.PixelCount];
                CharacterColorMap.Fill(recipe, characters, pixels);
                CharacterColorMap.OverlayGear(item, pixels);
                var map = CharacterColorMap.CreateTexture($"IconPalette_{key}");
                map.SetPixels32(pixels);
                map.Apply(false);
                result = new Material(baseMaterial) { name = $"IconMaterial_{key}" };
                result.SetTexture(ColorMapId, map);
                temp.Add(map);
                temp.Add(result);
                break;
            }
            cache[key] = result;
            return result;
        }

        private static string PackOf(ItemDefinition item)
        {
            if (item.partCatalog == null || item.appearancePartIds == null) return null;
            foreach (ushort id in item.appearancePartIds)
                if (item.partCatalog.TryGet(id, out var entry)) return entry.pack;
            return null;
        }

        private static GameObject BuildSubject(ItemDefinition item, Material sidekickMat, List<Object> temp, out Vector3 viewDir)
        {
            var root = new GameObject($"Icon_{item.name}");
            Vector3 front34 = new Vector3(-0.55f, 0.25f, 1f).normalized; // camera sits in front-left, above
            viewDir = front34;
            switch (item.equipSlot)
            {
                case EquipSlot.Armor:
                    AddParts(root, item, sidekickMat, p => ArmorIconRegions.Contains(p.region));
                    break;
                case EquipSlot.Gloves:
                    AddParts(root, item, sidekickMat, p => p.region == BodyRegion.HandRight);
                    viewDir = new Vector3(-0.2f, 0.35f, 1f).normalized;
                    break;
                case EquipSlot.Boots:
                    AddParts(root, item, sidekickMat, _ => true);
                    viewDir = new Vector3(-0.8f, 0.35f, 1f).normalized;
                    break;
                case EquipSlot.Cape:
                    AddParts(root, item, sidekickMat, _ => true);
                    viewDir = new Vector3(-0.55f, 0.25f, -1f).normalized; // from behind
                    break;
                case EquipSlot.Weapon:
                    if (item.weaponPrefab == null) break;
                    var w = (GameObject)Object.Instantiate(item.weaponPrefab, root.transform, false);
                    // Blade (+Z) runs bottom-left to top-right, the classic inventory diagonal, broad side (Y) to the camera.
                    w.transform.localRotation = Quaternion.LookRotation(new Vector3(1f, 1f, 0f).normalized, new Vector3(-1f, 1f, 0f).normalized);
                    viewDir = Vector3.back;
                    break;
                case EquipSlot.Pet:
                    if (item.petPrefab == null) break;
                    Object.Instantiate(item.petPrefab, root.transform, false);
                    break;
                case EquipSlot.Ring:
                    AddJewellery(root, item, temp, amulet: false);
                    viewDir = new Vector3(-0.3f, 0.9f, 1f).normalized;
                    break;
                case EquipSlot.Amulet:
                    AddJewellery(root, item, temp, amulet: true);
                    viewDir = new Vector3(-0.15f, 0.15f, 1f).normalized;
                    break;
            }

            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            if (root.GetComponentsInChildren<Renderer>(true).Length == 0)
            {
                Object.DestroyImmediate(root);
                Debug.LogWarning($"[Items] Nothing to render for '{item.name}'.");
                return null;
            }
            return root;
        }

        /// <summary>Parts are drawn unskinned: their vertices are already assembled in the shared bind pose.</summary>
        private static void AddParts(GameObject root, ItemDefinition item, Material mat, System.Func<SidekickPartRef, bool> filter)
        {
            var all = item.GetAppearanceParts();
            var parts = all.Where(p => p.IsValid).Where(filter).ToList();
            if (parts.Count == 0) parts = all.Where(p => p.IsValid).ToList();
            foreach (var p in parts)
            {
                var go = new GameObject(p.mesh.name);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
                // Sidekick parts have several submeshes that all use the one palette material.
                go.AddComponent<MeshRenderer>().sharedMaterials = Enumerable.Repeat(mat, p.mesh.subMeshCount).ToArray();
            }
        }

        private static void RenderToPng(Camera cam, RenderTexture rt, Transform subject, Vector3 viewDir, string assetPath)
        {
            var bounds = SidekickGearGenerator.LocalBounds(subject); // subject sits at the origin, identity
            Vector3 center = bounds.center;
            float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
            cam.transform.position = center + viewDir * (radius * 3f + 1f);
            cam.transform.rotation = Quaternion.LookRotation(-viewDir, Vector3.up);

            // Fit the projected bounds tightly, with a small margin.
            float maxX = 0f, maxY = 0f;
            for (int i = 0; i < 8; i++)
            {
                var corner = center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var local = cam.transform.InverseTransformPoint(corner);
                var c0 = cam.transform.InverseTransformPoint(center);
                maxX = Mathf.Max(maxX, Mathf.Abs(local.x - c0.x));
                maxY = Mathf.Max(maxY, Mathf.Abs(local.y - c0.y));
            }
            cam.orthographicSize = Mathf.Max(maxX, maxY) * 1.12f;
            cam.farClipPlane = radius * 6f + 2f;

            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }

            int big = Size * Supersample;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(big, big, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, big, big), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            var tex = Downsample(read);
            Object.DestroyImmediate(read);

            string full = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>Box filter with alpha-weighted colour so transparent edges don't bleed dark fringes.</summary>
        private static Texture2D Downsample(Texture2D src)
        {
            var px = src.GetPixels32();
            int big = src.width;
            var outPx = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int sy = 0; sy < Supersample; sy++)
                for (int sx = 0; sx < Supersample; sx++)
                {
                    var c = px[(y * Supersample + sy) * big + x * Supersample + sx];
                    float ca = c.a / 255f;
                    r += c.r * ca; g += c.g * ca; b += c.b * ca; a += ca;
                }
                int n = Supersample * Supersample;
                outPx[y * Size + x] = a > 0f
                    ? new Color32((byte)(r / a), (byte)(g / a), (byte)(b / a), (byte)Mathf.RoundToInt(a / n * 255f))
                    : new Color32(0, 0, 0, 0);
            }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels32(outPx);
            tex.Apply();
            return tex;
        }

        private static void ImportAsSprite(string assetPath)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = Size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        // ---------------- Procedural jewellery ----------------

        private static readonly Color[] RarityGem =
        {
            new Color(0.75f, 0.78f, 0.82f), new Color(0.25f, 0.85f, 0.35f), new Color(0.2f, 0.5f, 1f),
            new Color(0.7f, 0.3f, 1f), new Color(1f, 0.55f, 0.1f)
        };

        private static void AddJewellery(GameObject root, ItemDefinition item, List<Object> temp, bool amulet)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var metal = new Material(shader) { name = "IconMetal" };
            metal.SetColor("_BaseColor", item.rarity >= ItemRarity.Rare ? new Color(1f, 0.78f, 0.35f) : new Color(0.78f, 0.8f, 0.85f));
            metal.SetFloat("_Metallic", 0.35f); // no reflection probe in the preview scene
            metal.SetFloat("_Smoothness", 0.7f);
            var gem = new Material(shader) { name = "IconGem" };
            Color gemColor = RarityGem[Mathf.Clamp((int)item.rarity, 0, RarityGem.Length - 1)];
            gem.SetColor("_BaseColor", gemColor);
            gem.SetFloat("_Smoothness", 0.9f);
            gem.EnableKeyword("_EMISSION");
            gem.SetColor("_EmissionColor", gemColor * 0.6f);
            temp.Add(metal); temp.Add(gem);

            if (!amulet)
            {
                var band = Torus(0.5f, 0.1f, 40, 14, temp);
                AddMesh(root, "Band", band, metal, Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one);
                AddMesh(root, "Setting", Torus(0.16f, 0.05f, 24, 10, temp), metal, new Vector3(0f, 0f, 0.58f), Quaternion.identity, Vector3.one);
                AddMesh(root, "Gem", Gem(temp), gem, new Vector3(0f, 0f, 0.62f), Quaternion.Euler(-90f, 0f, 0f), Vector3.one * 0.2f);
            }
            else
            {
                // Chain loop, a pendant frame and a gem.
                AddMesh(root, "Chain", Torus(0.55f, 0.025f, 48, 8, temp), metal, new Vector3(0f, 0.55f, 0f), Quaternion.identity, new Vector3(1f, 1.2f, 1f));
                AddMesh(root, "Frame", Torus(0.22f, 0.06f, 32, 10, temp), metal, new Vector3(0f, -0.15f, 0f), Quaternion.identity, Vector3.one);
                AddMesh(root, "Gem", Gem(temp), gem, new Vector3(0f, -0.15f, 0f), Quaternion.identity, new Vector3(0.3f, 0.36f, 0.2f));
            }
        }

        private static void AddMesh(GameObject root, string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.SetLocalPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>Torus in the XY plane (hole along Z).</summary>
        private static Mesh Torus(float major, float minor, int segments, int sides, List<Object> temp)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                var ringCenter = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * major;
                var outward = ringCenter.normalized;
                for (int k = 0; k <= sides; k++)
                {
                    float b = k / (float)sides * Mathf.PI * 2f;
                    var n = outward * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                    verts.Add(ringCenter + n * minor);
                    normals.Add(n);
                }
            }
            int row = sides + 1;
            for (int s = 0; s < segments; s++)
            for (int k = 0; k < sides; k++)
            {
                int i0 = s * row + k, i1 = i0 + row, i2 = i0 + 1, i3 = i1 + 1;
                tris.AddRange(new[] { i0, i2, i1, i2, i3, i1 });
            }
            var mesh = new Mesh { name = "Torus" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            temp.Add(mesh);
            return mesh;
        }

        /// <summary>Faceted octahedron gem (flat-shaded).</summary>
        private static Mesh Gem(List<Object> temp)
        {
            Vector3[] p = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            int[][] faces =
            {
                new[] { 0, 4, 3 }, new[] { 0, 3, 5 }, new[] { 0, 5, 2 }, new[] { 0, 2, 4 },
                new[] { 1, 3, 4 }, new[] { 1, 5, 3 }, new[] { 1, 2, 5 }, new[] { 1, 4, 2 }
            };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var f in faces)
            {
                foreach (int idx in f) { tris.Add(verts.Count); verts.Add(p[idx]); }
            }
            var mesh = new Mesh { name = "Gem" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            temp.Add(mesh);
            return mesh;
        }
    }
}
