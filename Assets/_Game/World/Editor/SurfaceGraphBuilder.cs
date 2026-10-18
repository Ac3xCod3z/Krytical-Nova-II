using System.Collections.Generic;
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
    /// Walls as a second ground, stage 1 (owner, 18 Oct 2026): bakes the surface graph of chosen regions of the open
    /// scene into an asset, shows it over the world, and finds a test route from the street up a wall onto a roof.
    /// The proof regions are one Zone01 building ("Shop 1x4", 13 m) and one mountain side at the map's east edge
    /// (4 m to 33 m, up to 80°). The numbers are <see cref="WallstrideSettings"/>' "Walls as a second ground".
    /// </summary>
    public static class SurfaceGraphBuilder
    {
        public const string GraphFolder = "Assets/_Game/World/Data/Assets/SurfaceGraphs";
        public const string ProofGraphPath = GraphFolder + "/Zone01_SurfaceGraph_Proof.asset";
        public const string ReportPath = "Library/KryticalNova.SurfaceGraph.txt";
        private const string SettingsPath = "Assets/_Game/Gameplay/Player/Data/Assets/WallstrideSettings.asset";
        private const string OverlayName = "[Surface Graph Overlay]";

        /// <summary>The proof regions: the test building and the mountain side.</summary>
        public static readonly Bounds[] ProofRegions =
        {
            new Bounds(new Vector3(46.9f, 7f, 22.5f), new Vector3(11f, 16f, 9.5f)),
            new Bounds(new Vector3(486f, 20f, 232f), new Vector3(26f, 40f, 24f)),
        };

        /// <summary>A street spot by the test building and a spot on its roof, for the test route.</summary>
        public static readonly Vector3 ProofRouteFrom = new Vector3(40f, 0f, 30f);
        public static readonly Vector3 ProofRouteTo = new Vector3(47.5f, 13f, 22.5f);

        [MenuItem("Krytical Nova/Zone/Surface Graph/Bake Proof Regions (Open Scene)")]
        private static void BakeMenu() => Debug.Log(BakeProof());

        [MenuItem("Krytical Nova/Zone/Surface Graph/Show Proof Graph and Route")]
        private static void ShowMenu() => Debug.Log(ShowProof());

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
            o.StandOff = s.wallStandOff;
            o.MinAngle = s.graphMinSurfaceAngle;
            o.MaxAngle = 90f + s.maxOverhang;
            o.BodyRadius = s.graphBodyRadius;
            o.LinkDistance = s.graphLinkDistance;
            o.MaxTurn = s.graphMaxTurn;
            o.CornerTurn = s.graphCornerTurn;
            o.JoinReach = s.graphJoinReach;
            // Every solid thing but the players and their own parts.
            o.Layers = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Player");
            return o;
        }

        /// <summary>Bakes the proof regions of the open scene into the proof asset; returns the report.</summary>
        public static string BakeProof()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WallstrideSettings>(SettingsPath);
            SurfaceGraphBaker.Options o = OptionsFrom(settings);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            SurfaceGraphBaker.Result r = SurfaceGraphBaker.Bake(ProofRegions, o);
            watch.Stop();

            Directory.CreateDirectory(GraphFolder);
            var graph = AssetDatabase.LoadAssetAtPath<SurfaceGraph>(ProofGraphPath);
            if (graph == null)
            {
                graph = ScriptableObject.CreateInstance<SurfaceGraph>();
                AssetDatabase.CreateAsset(graph, ProofGraphPath);
            }
            int ground = 0, top = 0, corners = 0;
            foreach (SurfaceGraph.Join j in r.Joins) { if (j.kind == SurfaceGraph.JoinKind.Ground) ground++; else top++; }
            foreach (SurfaceGraph.EdgeKind k in r.EdgeKind) if (k == SurfaceGraph.EdgeKind.Corner) corners++;
            var note = new StringBuilder();
            note.Append($"Baked {System.DateTime.Now:yyyy-MM-dd HH:mm} from {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}: ");
            foreach (Bounds b in ProofRegions) note.Append($"region {b.center:0.0} size {b.size:0.0}; ");
            note.Append($"spacing {o.Spacing}, stand-off {o.StandOff}, walls from {o.MinAngle}° to {o.MaxAngle}°.");
            graph.Set(r.Points, r.Normals, r.EdgeStart, r.EdgeTo, r.EdgeKind, r.Joins, note.ToString());
            EditorUtility.SetDirty(graph);
            UseForZone(graph, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            AssetDatabase.SaveAssets();

            string report = $"[Surface graph] {r.Points.Length} nodes, {r.EdgeTo.Length} edges ({corners} round corners), " +
                            $"{ground} ground joins, {top} top joins, in {watch.ElapsedMilliseconds} ms. Rays {r.Rays}, steep hits {r.Hits}, " +
                            $"no room for the body {r.RejectedBody}, inside a building {r.RejectedInside}. Saved {ProofGraphPath}.";
            File.WriteAllText(ReportPath, report);
            return report;
        }

        private const string ZoneCatalogPath = "Assets/_Game/World/Data/Assets/ZoneCatalog.asset";

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

        /// <summary>Shows the proof graph over the world with a test route from the street onto the test building's roof.</summary>
        public static string ShowProof()
        {
            var graph = AssetDatabase.LoadAssetAtPath<SurfaceGraph>(ProofGraphPath);
            if (graph == null) return "[Surface graph] Bake it first.";
            var filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas };
            NavMesh.SamplePosition(ProofRouteFrom, out NavMeshHit from, 3f, filter);
            NavMesh.SamplePosition(ProofRouteTo, out NavMeshHit to, 3f, filter);
            SurfaceRoute.Route route = SurfaceRoute.Plan(graph, from.position, to.position, filter);

            GameObject overlay = GameObject.Find(OverlayName);
            if (overlay == null) overlay = new GameObject(OverlayName) { hideFlags = HideFlags.DontSave };
            if (!overlay.TryGetComponent(out SurfaceGraphOverlay view)) view = overlay.AddComponent<SurfaceGraphOverlay>();
            view.Show(graph, route);

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
