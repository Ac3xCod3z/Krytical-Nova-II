using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using KryticalNova.Networking;
using KryticalNova.World;
using KryticalNova.World.Minimap;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Makes the blank maps of the World Atlas (Design/World-Atlas.md): for the Hegemony, Covenant and Null each a
    /// Home City, four field zones and their four Runs, and The Scar. The Ruptureworks and every Ronin map are left
    /// out. Each is Zone01 with its content taken away (town, NPCs, portals, Hostiles, trade zones, water, rocks) and
    /// everything that makes it a working map kept: the network stack, the HUD, cameras, sky and weather, game feel,
    /// spawn points, the GM console. Each gets its own flat terrain, NavMesh and minimap, a <see cref="ZoneRules"/>
    /// with its PvP tag (only Zone01 is neutral ground), a <see cref="SceneNetworkGuard"/> and the travel screen,
    /// and goes into the build list. Also makes the zone catalog (names, bands, lore from the Atlas, the concept art)
    /// and gives Zone01 the same three pieces. A map that exists already is left alone, so work done in it is never
    /// lost: delete the scene to have it made again.
    /// </summary>
    public static class WorldScenesBuilder
    {
        private const string Zone01Path = BuildSceneOrder.WorldScenePath;
        private const string ScenesRoot = "Assets/_Game/Scenes/World";
        private const string TemplatePath = ScenesRoot + "/_BlankMapTemplate.unity";
        private const string ZonesRoot = "Assets/_Game/World/Zones";
        private const string CatalogPath = "Assets/_Game/World/Data/Assets/ZoneCatalog.asset";
        private const string ArtFolder = "Assets/_Game/World/Travel/Art";
        private const string TitleFontPath = "Assets/_Game/UI/Travel/Fonts/Bangers.ttf";
        private const string BodyFontPath = "Assets/_Game/UI/Travel/Fonts/Roboto-Bold.ttf";
        private const string AtlasPath = "Assets/_Game/_Docs/Design/World-Atlas.md";
        private const string TerrainLayerFolder = ZonesRoot + "/Zone01/TerrainLayers";

        private const float FieldSize = 1000f, RunSize = 400f, TerrainHeight = 90f, GroundHeight = 20f;

        private static readonly (int min, int max)[] Bands = { (1, 22), (20, 45), (45, 67), (65, 90) };

        private sealed class RegionPlan
        {
            public string Prefix, Name, Faction, City, Art;
            public string[] Zones, Runs;
        }

        private static readonly RegionPlan[] Regions =
        {
            new RegionPlan
            {
                Prefix = "MeridianSprawl", Name = "Meridian Sprawl", Faction = "Hegemony", City = "New Meridian", Art = "concept-meridian-sprawl.jpg",
                Zones = new[] { "Transit Row", "Crestfall", "Alder County", "Gantry Ridge" },
                Runs = new[] { "Substation 9", "The Galleria", "Silo 12", "The Coreworks" },
            },
            new RegionPlan
            {
                Prefix = "EldersReach", Name = "Elders-Reach", Faction = "Covenant", City = "Aldhavn", Art = "concept-elders-reach.jpg",
                Zones = new[] { "Birchlight Vale", "Skeldfjord", "The Sylwood", "Barrowfell" },
                Runs = new[] { "Hearthbarrow", "Wrecktide Grotto", "The Rootvault", "Howlstone Crypts" },
            },
            new RegionPlan
            {
                Prefix = "Cessation", Name = "Cessation", Faction = "Null", City = "Terminus", Art = "concept-cessation.jpg",
                Zones = new[] { "The Palefields", "The Scrapline", "The Needlegarden", "Noct Caldera" },
                Runs = new[] { "The Idle Works", "The Disassembly", "Ossuary Sigma", "The Stirring Well" },
            },
        };

        private const string ScarName = "The Scar", ScarScene = "TheScar", ScarArt = "concept-scar.jpg";

        /// <summary>The owner's loading pictures (17 Oct 2026): the travel screen draws them at random with the regions' paintings.</summary>
        private static readonly string[] ExtraPictures = { "loading-street-fight.png", "loading-cliff-city.png", "loading-faction-brawl.png" };

        /// <summary>A map to make: where its scene goes and what the catalog says about it.</summary>
        private sealed class MapPlan
        {
            public ZoneEntry Entry;
            public string Path;
            public float Size;
            public int Layer; // index into Zone01's terrain layers: 0 grass, 1 dirt, 2 rock, 3 town stone
        }

        [MenuItem("Krytical Nova/World/Build World Scenes")]
        public static void BuildMenu() => Build();

        /// <summary>The scene paths of every planned map, in build order (Home Cities, then each region's zones and Runs, then The Scar).</summary>
        public static IEnumerable<string> ScenePaths()
        {
            foreach (MapPlan plan in Plans(null)) yield return plan.Path;
        }

        /// <summary>Makes the catalog, gives Zone01 its pieces and makes every missing map. Returns a report.</summary>
        public static string Build()
        {
            var log = new StringBuilder();
            if (EditorApplication.isPlaying) return "Stop Play mode first.";
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "Cancelled.";

            ImportArt();
            ZoneCatalog catalog = BuildCatalog(log);
            List<MapPlan> plans = Plans(catalog);

            // Zone01: the three pieces every map has, then a stripped copy as the template.
            Scene zone01 = EditorSceneManager.OpenScene(Zone01Path, OpenSceneMode.Single);
            if (AddMapPieces(zone01, catalog, "Zone01_StarterTown", "Central Plaza")) { EditorSceneManager.SaveScene(zone01); log.AppendLine("Zone01: added ZoneRules, SceneNetworkGuard, Travel Screen."); }

            var missing = plans.FindAll(p => !File.Exists(p.Path));
            if (missing.Count > 0)
            {
                Strip(zone01, log);
                Lightmapping.lightingDataAsset = null;
                EditorSceneManager.SaveScene(zone01, TemplatePath, true);
                foreach (MapPlan plan in missing)
                {
                    MakeMap(plan, catalog);
                    log.AppendLine($"Made {plan.Path}");
                }
                AssetDatabase.DeleteAsset(TemplatePath);
            }
            log.AppendLine($"{plans.Count - missing.Count} maps already existed and were left alone.");

            BuildSceneOrder.Apply();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(Zone01Path, OpenSceneMode.Single);
            string report = log.ToString();
            Debug.Log("[World] " + report);
            return report;
        }

        // ------------------------------------------------------------------ plans

        private static List<MapPlan> Plans(ZoneCatalog catalog)
        {
            var plans = new List<MapPlan>();
            foreach (RegionPlan r in Regions)
                plans.Add(Map(catalog, $"{r.Prefix}_{Squash(r.City)}", $"Towns/{r.Prefix}_{Squash(r.City)}", r.City, r.Name, ZoneKind.HomeCity, 0, 0, FieldSize, 3));
            foreach (RegionPlan r in Regions)
            {
                for (int i = 0; i < r.Zones.Length; i++)
                {
                    string scene = $"{r.Prefix}_Z{i + 1}_{Squash(r.Zones[i])}";
                    plans.Add(Map(catalog, scene, $"Overworld/{r.Prefix}/{scene}", r.Zones[i], r.Name, ZoneKind.FieldZone, Bands[i].min, Bands[i].max, FieldSize, 0));
                }
                for (int i = 0; i < r.Runs.Length; i++)
                {
                    string scene = $"{r.Prefix}_Run_{Squash(r.Runs[i])}";
                    plans.Add(Map(catalog, scene, $"Dungeons/{r.Prefix}/{scene}", r.Runs[i], r.Name, ZoneKind.Run, Bands[i].min, Bands[i].max, RunSize, 2));
                }
            }
            plans.Add(Map(catalog, ScarScene, $"Overworld/{ScarScene}/{ScarScene}", ScarName, ScarName, ZoneKind.Scar, 88, 105, FieldSize, 1));
            return plans;
        }

        private static MapPlan Map(ZoneCatalog catalog, string scene, string relative, string display, string region, ZoneKind kind, int min, int max, float size, int layer)
        {
            ZoneEntry entry = catalog != null ? catalog.Find(scene) : null;
            entry ??= new ZoneEntry { sceneName = scene, displayName = display, region = region, kind = kind, minLevel = min, maxLevel = max };
            return new MapPlan { Entry = entry, Path = $"{ScenesRoot}/{relative}.unity", Size = size, Layer = layer };
        }

        // ------------------------------------------------------------------ catalog

        private static void ImportArt()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                if (importer.textureType == TextureImporterType.Default && !importer.mipmapEnabled && importer.maxTextureSize == 2048 && importer.npotScale == TextureImporterNPOTScale.None) continue;
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 2048;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        private static ZoneCatalog BuildCatalog(StringBuilder log)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ZoneCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            string atlas = File.Exists(AtlasPath) ? File.ReadAllText(AtlasPath).Replace("\r\n", "\n") : string.Empty;

            catalog.regions.Clear();
            foreach (RegionPlan r in Regions)
                catalog.regions.Add(new ZoneRegion { name = r.Name, faction = r.Faction, art = Art(r.Art), lore = RegionLore(atlas, r.Name) });
            catalog.regions.Add(new ZoneRegion { name = ScarName, faction = "Contested", art = Art(ScarArt), lore = RegionLore(atlas, ScarName) });

            catalog.extraPictures.Clear();
            foreach (string file in ExtraPictures)
            {
                Texture2D picture = Art(file);
                if (picture != null) catalog.extraPictures.Add(picture);
                else log.AppendLine($"Missing travel picture {ArtFolder}/{file}.");
            }

            catalog.zones.Clear();
            catalog.zones.Add(new ZoneEntry
            {
                sceneName = "Zone01_StarterTown", displayName = "Concord", region = string.Empty, kind = ZoneKind.StarterTown,
                pvp = ZonePvpRule.Neutral,
                lore = "The starter town, and for now the only neutral ground: every faction trades, talks and settles its quarrels in a Callout here. Outside the walls the war goes on.",
            });
            foreach (MapPlan plan in Plans(null))
            {
                ZoneEntry e = plan.Entry;
                e.pvp = e.kind == ZoneKind.Scar ? ZonePvpRule.AlwaysOn : ZonePvpRule.FactionWar;
                e.lore = e.kind == ZoneKind.Run ? RunLore(atlas, e.displayName) : ZoneLore(atlas, e.kind == ZoneKind.HomeCity ? "Home City: " + e.displayName : e.displayName);
                if (string.IsNullOrEmpty(e.lore)) log.AppendLine($"No Atlas text for {e.displayName}.");
                catalog.zones.Add(e);
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            log.AppendLine($"Zone catalog: {catalog.zones.Count} maps, {catalog.regions.Count} regions.");
            return catalog;
        }

        private static Texture2D Art(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtFolder}/{file}");

        /// <summary>The Tone row of a region's section: how the land feels, in a sentence or two.</summary>
        private static string RegionLore(string atlas, string region)
        {
            string section = Section(atlas, "## " + region, "\n## ");
            if (section == null) return string.Empty;
            Match tone = Regex.Match(section, @"^\| Tone \| (.+?) \|$", RegexOptions.Multiline);
            return tone.Success ? Clean(Sentence(tone.Groups[1].Value)) : string.Empty;
        }

        /// <summary>The "*Tag.* description" line under a "### Zn: Name" (or "### Home City: Name") heading.</summary>
        private static string ZoneLore(string atlas, string heading)
        {
            Match m = Regex.Match(atlas, @"^### (?:Z\d: |Zone: )?" + Regex.Escape(heading) + @"\n\n\*[^*]+\.\* (.+)$", RegexOptions.Multiline);
            return m.Success ? Clean(m.Groups[1].Value) : string.Empty;
        }

        private static string RunLore(string atlas, string run)
        {
            Match m = Regex.Match(atlas, @"^\*\*Run: " + Regex.Escape(run) + @"\*\* \(([^)]+)\)\. (.+)$", RegexOptions.Multiline);
            return m.Success ? Clean(m.Groups[2].Value) + " · " + m.Groups[1].Value.Replace(" · Lv", " · Lv") : string.Empty;
        }

        private static string Section(string text, string start, string end)
        {
            int i = text.IndexOf(start + "\n", System.StringComparison.Ordinal);
            if (i < 0) return null;
            int j = text.IndexOf(end, i + start.Length, System.StringComparison.Ordinal);
            return j < 0 ? text.Substring(i) : text.Substring(i, j - i);
        }

        /// <summary>Player-facing text: no doc references, no word of the hidden Ronin.</summary>
        private static string Clean(string text)
        {
            text = Regex.Replace(text, @"\s*\((?:Lore Bible|Hostiles Plan)[^)]*\)", string.Empty);
            text = text.Replace(" — its nature stays open", string.Empty);
            var kept = new List<string>();
            foreach (string sentence in Regex.Split(text, @"(?<=[.!?])\s+"))
                if (sentence.IndexOf("Ronin", System.StringComparison.OrdinalIgnoreCase) < 0 && sentence.IndexOf("field zones", System.StringComparison.OrdinalIgnoreCase) < 0)
                    kept.Add(sentence);
            return string.Join(" ", kept).Replace("**", string.Empty).Trim();
        }

        private static string Sentence(string s)
        {
            s = s.Trim();
            if (s.Length == 0) return s;
            s = char.ToUpperInvariant(s[0]) + s.Substring(1);
            return s.EndsWith(".") ? s : s + ".";
        }

        private static string Squash(string s)
        {
            var sb = new StringBuilder();
            foreach (string word in s.Split(' ', '-'))
                if (word.Length > 0) sb.Append(char.ToUpperInvariant(word[0])).Append(word.Substring(1));
            return Regex.Replace(sb.ToString(), "[^A-Za-z0-9]", string.Empty);
        }

        // ------------------------------------------------------------------ scenes

        /// <summary>ZoneRules, SceneNetworkGuard and the travel screen, and the zone's id on the session. True when anything changed.</summary>
        private static bool AddMapPieces(Scene scene, ZoneCatalog catalog, string zoneId, string displayName)
        {
            bool changed = false;
            GameObject network = Root(scene, "NetworkManager");
            GameObject player = Root(scene, "Player");
            if (network != null)
            {
                var guard = network.GetComponent<SceneNetworkGuard>();
                if (guard == null) { guard = network.AddComponent<SceneNetworkGuard>(); changed = true; }
                guard.EditorSetOfflinePlayer(player);
                EditorUtility.SetDirty(guard);
                if (network.TryGetComponent<ZoneSessionManager>(out var session))
                {
                    var so = new SerializedObject(session);
                    if (so.FindProperty("currentZoneId").stringValue != zoneId) changed = true;
                    so.FindProperty("currentZoneId").stringValue = zoneId;
                    so.FindProperty("currentZoneDisplayName").stringValue = displayName;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            var rules = Object.FindAnyObjectByType<ZoneRules>(FindObjectsInactive.Include);
            if (rules == null)
            {
                var go = new GameObject("Zone Rules");
                GameObject gameplay = Root(scene, "--- Gameplay ---");
                if (gameplay != null) go.transform.SetParent(gameplay.transform, false);
                rules = go.AddComponent<ZoneRules>();
                changed = true;
            }
            rules.EditorSetCatalog(catalog);
            EditorUtility.SetDirty(rules);

            // Looked up by name: the UI assembly is left out while the Dedicated Server target is active.
            System.Type screenType = System.Type.GetType("KryticalNova.UI.Travel.TravelScreen, KryticalNova.UI");
            if (screenType != null)
            {
                var screen = (Component)Object.FindAnyObjectByType(screenType, FindObjectsInactive.Include);
                if (screen == null)
                {
                    screen = new GameObject("Travel Screen").AddComponent(screenType);
                    SceneManager.MoveGameObjectToScene(screen.gameObject, scene);
                    changed = true;
                }
                var screenSo = new SerializedObject(screen);
                screenSo.FindProperty("titleFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(TitleFontPath);
                screenSo.FindProperty("bodyFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(BodyFontPath);
                screenSo.ApplyModifiedPropertiesWithoutUndo();
            }

            if (changed) EditorSceneManager.MarkSceneDirty(scene);
            return changed;
        }

        /// <summary>Takes Zone01's own content out of the open scene (never saved over Zone01: it is saved as the template).</summary>
        private static void Strip(Scene scene, StringBuilder log)
        {
            string[] dropRoots = { "--- Town ---", "--- Outskirts ---", "--- Traffic ---" };
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(dropRoots, root.name) >= 0 || root.GetComponent<KryticalNova.World.TradeZone>() != null)
                {
                    Object.DestroyImmediate(root);
                    continue;
                }
                switch (root.name)
                {
                    case "--- NPCs ---":
                    case "--- Portals ---":
                        Clear(root.transform);
                        break;
                    case "--- Gameplay ---":
                        Drop(root.transform, "Combat Dummy");
                        Transform hostiles = root.transform.Find("Hostile Zones");
                        if (hostiles != null) Clear(hostiles);
                        break;
                    case "--- Environment ---":
                        Drop(root.transform, "Rocks");
                        Drop(root.transform, "Water");
                        Drop(root.transform, "Water Surface");
                        Transform terrain = root.transform.Find("Zone01 Terrain");
                        if (terrain != null) terrain.name = "Terrain";
                        // The grass needs a rule set of the map's own (its mask and lawns are Zone01's): there to set up.
                        Transform grass = root.transform.Find("GPU Grass");
                        if (grass != null) grass.gameObject.SetActive(false);
                        break;
                    case "--- Lighting & FX ---":
                        Drop(root.transform, "ProbeVolumePerSceneData");
                        Transform lighting = root.transform.Find("Lighting");
                        if (lighting != null) Drop(lighting, "Water Mist");
                        break;
                }
            }
            foreach (var surface in Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                surface.navMeshData = null;
            foreach (var anchor in Object.FindObjectsByType<ZoneMinimapAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                anchor.EditorSetData(null);
            log.AppendLine("Template: Zone01 without its town, NPCs, portals, Hostiles, trade zones, water, rocks, baked lighting and NavMesh.");
        }

        private static void MakeMap(MapPlan plan, ZoneCatalog catalog)
        {
            string folder = Path.GetDirectoryName(plan.Path).Replace('\\', '/');
            EnsureFolder(folder);
            AssetDatabase.CopyAsset(TemplatePath, plan.Path);
            Scene scene = EditorSceneManager.OpenScene(plan.Path, OpenSceneMode.Single);
            ZoneEntry e = plan.Entry;

            AddMapPieces(scene, catalog, e.sceneName, e.displayName);
            Terrain terrain = Object.FindAnyObjectByType<Terrain>();
            if (terrain != null) MakeTerrain(terrain, plan, scene.name);

            // NavMesh baked into the scene's folder, as for Zone01.
            var surface = Object.FindAnyObjectByType<NavMeshSurface>();
            if (surface != null)
            {
                surface.BuildNavMesh();
                string dataFolder = $"{folder}/{scene.name}";
                EnsureFolder(dataFolder);
                string navPath = $"{dataFolder}/NavMesh-Navigation.asset";
                AssetDatabase.DeleteAsset(navPath);
                AssetDatabase.CreateAsset(surface.navMeshData, navPath);
                EditorUtility.SetDirty(surface);
            }

            MinimapBaker.Bake();
            // Fresh ids for the scene's network objects (they are derived from the scene) are written on this save.
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void MakeTerrain(Terrain terrain, MapPlan plan, string sceneName)
        {
            string folder = $"{ZonesRoot}/{sceneName.Split('_')[0]}";
            EnsureFolder(folder);
            string path = $"{folder}/{sceneName}_TerrainData.asset";
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (data == null)
            {
                data = new TerrainData { heightmapResolution = 513, alphamapResolution = 512, baseMapResolution = 1024 };
                data.SetDetailResolution(512, 16);
                data.size = new Vector3(plan.Size, TerrainHeight, plan.Size);
                AssetDatabase.CreateAsset(data, path);
            }
            int n = data.heightmapResolution;
            var heights = new float[n, n];
            float flat = GroundHeight / TerrainHeight;
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) heights[y, x] = flat;
            data.SetHeights(0, 0, heights);

            var layers = new List<TerrainLayer>();
            foreach (string name in new[] { "TL_Grass", "TL_Dirt", "TL_Rock", "TL_TownStone" })
                layers.Add(AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{TerrainLayerFolder}/{name}.terrainlayer"));
            data.terrainLayers = layers.ToArray();
            int a = data.alphamapResolution;
            var alpha = new float[a, a, layers.Count];
            for (int y = 0; y < a; y++) for (int x = 0; x < a; x++) alpha[y, x, Mathf.Clamp(plan.Layer, 0, layers.Count - 1)] = 1f;
            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);

            terrain.terrainData = data;
            terrain.transform.position = new Vector3(-plan.Size * 0.5f, -GroundHeight, -plan.Size * 0.5f);
            if (terrain.TryGetComponent<TerrainCollider>(out var collider)) collider.terrainData = data;
            EditorUtility.SetDirty(terrain);
        }

        private static GameObject Root(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        private static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--) Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        private static void Drop(Transform parent, string child)
        {
            Transform t = parent.Find(child);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
