using System.IO;
using System.Text;
using KryticalNova.Player;
using KryticalNova.World;
using KryticalNova.World.Surfaces;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Walls as a second ground (owner, 18 Oct 2026): bakes the open zone's surface graph (every building wall, rock face
    /// and steep terrain slope past the set angle) into the zone's asset and its compressed data file, points the zone's
    /// catalogue entry at it, and shows a part of it over the world with a test route from the street up a wall onto a
    /// roof. The numbers are <see cref="WallstrideSettings"/>' "Walls as a second ground".
    /// </summary>
    public static class SurfaceGraphBuilder
    {
        public const string GraphFolder = "Assets/_Game/World/Data/Assets/SurfaceGraphs";
        public const string ReportPath = "Library/KryticalNova.SurfaceGraph.txt";
        private const string SettingsPath = "Assets/_Game/Gameplay/Player/Data/Assets/WallstrideSettings.asset";
        private const string ZoneCatalogPath = "Assets/_Game/World/Data/Assets/ZoneCatalog.asset";
        private const string OverlayName = "[Surface Graph Overlay]";
        private const float RegionHeadroom = 120f;

        /// <summary>The test route in Zone01: a street spot by "Shop 1x4" and a spot on its roof.</summary>
        public static readonly Vector3 TestRouteFrom = new Vector3(40f, 0f, 30f);
        public static readonly Vector3 TestRouteTo = new Vector3(47.5f, 13f, 22.5f);

        public static string GraphPath(string sceneName) => $"{GraphFolder}/{sceneName}_SurfaceGraph.asset";
        public static string DataPath(string sceneName) => $"{GraphFolder}/{sceneName}_SurfaceGraph.bytes";

        [MenuItem("Krytical Nova/Zone/Surface Graph/Bake Zone (Open Scene)")]
        private static void BakeMenu() => Debug.Log(BakeZone());

        [MenuItem("Krytical Nova/Zone/Surface Graph/Show Around Test Route")]
        private static void ShowMenu() => Debug.Log(ShowAround(TestRouteFrom, TestRouteTo, 30f));

        [MenuItem("Krytical Nova/Zone/Surface Graph/Hide Overlay")]
        private static void HideMenu()
        {
            GameObject overlay = GameObject.Find(OverlayName);
            if (overlay != null) Object.DestroyImmediate(overlay);
        }

        public static SurfaceGraphBaker.Options OptionsFrom(WallstrideSettings s)
        {
            SurfaceGraphBaker.Options o = SurfaceGraphBaker.Options.Default;
            if (s == null) return o;
            o.Spacing = s.graphSpacing;
            o.TerrainSpacing = s.graphTerrainSpacing;
            o.OuterSpacing = s.graphOuterSpacing;
            o.StandOff = s.wallStandOff;
            o.MinAngle = s.graphMinSurfaceAngle;
            o.MaxAngle = 90f + s.maxOverhang;
            o.BodyRadius = s.graphBodyRadius;
            o.LinkDistance = s.graphLinkDistance;
            o.MaxTurn = s.graphMaxTurn;
            o.CornerTurn = s.graphCornerTurn;
            o.JoinReach = s.graphJoinReach;
            o.MinColliderHeight = s.minWallHeight;
            // Every solid thing but the players and their own parts.
            o.Layers = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Player");
            return o;
        }

        /// <summary>Where play is dense: the town (its walls and everything inside them), sampled finest.</summary>
        public static Bounds TownCore()
        {
            GameObject town = GameObject.Find(StarterTownBuilder.TownRootName);
            Transform walls = town != null ? town.transform.Find("Walls") : null;
            var bounds = new Bounds();
            bool any = false;
            foreach (Renderer r in (walls != null ? walls : town != null ? town.transform : null)?.GetComponentsInChildren<Renderer>() ?? new Renderer[0])
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        /// <summary>The open zone's whole extent: its terrain, with headroom for what stands on it.</summary>
        public static Bounds ZoneRegion()
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                Vector3 size = terrain.terrainData.size;
                var b = new Bounds(terrain.transform.position + size * 0.5f, size + Vector3.up * RegionHeadroom * 2f);
                if (!any) { bounds = b; any = true; }
                else bounds.Encapsulate(b);
            }
            if (!any)
                foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                {
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            return bounds;
        }

        /// <summary>Bakes the open zone's graph, saves it, and points the zone at it; returns the report.</summary>
        public static string BakeZone()
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var settings = AssetDatabase.LoadAssetAtPath<WallstrideSettings>(SettingsPath);
            SurfaceGraphBaker.Options o = OptionsFrom(settings);
            Bounds region = ZoneRegion();
            o.Core = TownCore();
            o.DropUnreachable = true;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            SurfaceGraphBaker.Result r = SurfaceGraphBaker.Bake(new[] { region }, o);
            watch.Stop();

            Directory.CreateDirectory(GraphFolder);
            string graphPath = GraphPath(scene), dataPath = DataPath(scene);
            var graph = AssetDatabase.LoadAssetAtPath<SurfaceGraph>(graphPath);
            if (graph == null)
            {
                graph = ScriptableObject.CreateInstance<SurfaceGraph>();
                AssetDatabase.CreateAsset(graph, graphPath);
            }
            int ground = 0, top = 0, corners = 0;
            foreach (SurfaceGraph.Join j in r.Joins) { if (j.kind == SurfaceGraph.JoinKind.Ground) ground++; else top++; }
            foreach (SurfaceGraph.EdgeKind k in r.EdgeKind) if (k == SurfaceGraph.EdgeKind.Corner) corners++;
            string note = $"Baked {System.DateTime.Now:yyyy-MM-dd HH:mm} from {scene}: region {region.center:0} size {region.size:0}; " +
                          $"spacing {o.Spacing} (terrain {o.TerrainSpacing}), stand-off {o.StandOff}, walls from {o.MinAngle}° to {o.MaxAngle}°, " +
                          $"outside the town {o.OuterSpacing}, colliders {o.MinColliderHeight} m tall and up.";
            graph.Set(r.Points, r.Normals, r.EdgeStart, r.EdgeTo, r.EdgeKind, r.Joins, note);
            byte[] bytes = graph.Encode();
            File.WriteAllBytes(dataPath, bytes);
            AssetDatabase.ImportAsset(dataPath);
            graph.SetData(AssetDatabase.LoadAssetAtPath<TextAsset>(dataPath), r.Points.Length);
            EditorUtility.SetDirty(graph);
            UseForZone(graph, scene);
            AssetDatabase.SaveAssets();

            string report = $"[Surface graph] {scene}: {r.Points.Length} nodes, {r.EdgeTo.Length} edges ({corners} round corners), " +
                            $"{ground} ground joins, {top} top joins, from {r.Colliders} colliders and {r.Terrains} terrain(s), " +
                            $"{r.Samples} steep samples (no room for the body {r.RejectedBody}, inside a solid {r.RejectedSolid}, inside a building {r.RejectedInside}, out of reach {r.Unreachable}), " +
                            $"in {watch.ElapsedMilliseconds / 1000f:0.0} s; data {bytes.Length / 1024f / 1024f:0.00} MB. Saved {graphPath}.";
            File.WriteAllText(ReportPath, report);
            return report;
        }

        /// <summary>The zone's catalogue entry points at its graph, so the game uses it there.</summary>
        private static void UseForZone(SurfaceGraph graph, string sceneName)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ZoneCatalog>(ZoneCatalogPath);
            ZoneEntry entry = catalog != null ? catalog.Find(sceneName) : null;
            if (entry == null) { Debug.LogWarning($"[Surface graph] {sceneName} is not in the zone catalogue: the game will not use this graph."); return; }
            if (entry.surfaceGraph == graph) return;
            entry.surfaceGraph = graph;
            EditorUtility.SetDirty(catalog);
        }

        /// <summary>Shows the open zone's graph within <paramref name="radius"/> of a route from one place to another, with the route.</summary>
        public static string ShowAround(Vector3 fromPoint, Vector3 toPoint, float radius)
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var graph = AssetDatabase.LoadAssetAtPath<SurfaceGraph>(GraphPath(scene));
            if (graph == null) return "[Surface graph] Bake it first.";
            var filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
            NavMesh.SamplePosition(fromPoint, out NavMeshHit from, 3f, filter);
            NavMesh.SamplePosition(toPoint, out NavMeshHit to, 3f, filter);
            SurfaceRoute.Route route = SurfaceRoute.Plan(graph, from.position, to.position, filter);

            GameObject overlay = GameObject.Find(OverlayName);
            if (overlay == null) overlay = new GameObject(OverlayName) { hideFlags = HideFlags.DontSave };
            if (!overlay.TryGetComponent(out SurfaceGraphOverlay view)) view = overlay.AddComponent<SurfaceGraphOverlay>();
            var area = new Bounds((fromPoint + toPoint) * 0.5f, new Vector3(radius * 2f, radius * 2f, radius * 2f));
            view.Show(graph, route, area);

            string report = route == null
                ? $"[Surface graph] No route from {from.position:0.0} to {to.position:0.0}."
                : $"[Surface graph] Route {route.Length:0.0} m: walk {SurfaceRoute.Length(route.Approach):0.0} m to the foot at " +
                  $"{graph.points[route.Nodes[0]]:0.0}, {route.Nodes.Length} nodes up the wall to {graph.points[route.Nodes[route.Nodes.Length - 1]]:0.0}, " +
                  $"then {SurfaceRoute.Length(route.Departure):0.0} m on the roof to {to.position:0.0}.";
            File.AppendAllText(ReportPath, "\n" + report);
            return report;
        }
    }
}
