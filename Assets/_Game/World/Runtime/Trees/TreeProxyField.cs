using System.Collections.Generic;
using KryticalNova.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.World
{
    /// <summary>
    /// Trees are solid only near players (Wallstride, owner 16 Oct 2026). A map's terrain holds hundreds of trees;
    /// giving each a collider for the whole map costs physics everywhere for trees nobody is near, so the terrain's
    /// own tree colliders are turned off and this field puts a pooled capsule on each tree within
    /// <see cref="WallstrideSettings.treeProxyRadius"/> of a player, marked as a tree that can be climbed
    /// (<see cref="ClimbableSurface"/>). The server keeps them round every player (climbs, jumps and deaths are decided
    /// there); a player's own game keeps them round that player only, so its clicks find the trees it can see.
    /// The radius is always nearer than where a tree turns into its far picture, so a solid tree is always drawn whole.
    ///
    /// Trees are looked up in a grid made once when the map loads; the look round each player is a few grid cells
    /// a few times a second. Nothing is allocated after that.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Terrain))]
    public sealed class TreeProxyField : MonoBehaviour
    {
        private const float CellSize = 16f;
        private const float DefaultRadius = 35f, DefaultRefresh = 0.25f, DefaultMinHeight = 3f, DefaultPerchShare = 0.6f;
        private const int DefaultCap = 512;

        private struct TreeShape
        {
            public Vector3 Foot;
            public float Radius, Height;
        }

        private Terrain terrain;
        private TreeShape[] trees = System.Array.Empty<TreeShape>();
        private readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        private readonly Dictionary<int, ClimbableSurface> live = new Dictionary<int, ClimbableSurface>();
        private readonly Stack<ClimbableSurface> pool = new Stack<ClimbableSurface>();
        private readonly HashSet<int> wanted = new HashSet<int>();
        private readonly List<int> leaving = new List<int>();
        private Transform holder;
        private float nextRefresh;

        /// <summary>How many tree colliders stand right now (for the performance readout).</summary>
        public int LiveCount => live.Count;
        /// <summary>How many trees of the terrain can be climbed at all.</summary>
        public int TreeCount => trees.Length;

        // ------------------------------------------------------------------ every map, without touching its scene

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (Terrain each in Terrain.activeTerrains)
            {
                if (each == null || each.gameObject.scene != scene || each.terrainData == null || each.terrainData.treeInstanceCount == 0) continue;
                if (each.GetComponent<TreeProxyField>() == null) each.gameObject.AddComponent<TreeProxyField>();
            }
            MarkPlacedTrees(scene);
        }

        /// <summary>
        /// Trees placed as objects (the town's) already have a capsule of their own: they are marked as trees, so they
        /// are climbed to a place to stand in them rather than read as a wall with no top.
        /// </summary>
        private static void MarkPlacedTrees(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (CapsuleCollider capsule in root.GetComponentsInChildren<CapsuleCollider>(true))
                {
                    if (capsule.isTrigger || capsule.direction != 1 || capsule.GetComponentInParent<ClimbableSurface>() != null) continue;
                    if (!LooksLikeTree(capsule.gameObject.name) && !LooksLikeTree(capsule.transform.root.name)) continue;
                    Vector3 scale = capsule.transform.lossyScale;
                    float height = capsule.height * scale.y;
                    var marker = capsule.gameObject.AddComponent<ClimbableSurface>();
                    marker.kind = ClimbableKind.Tree;
                    marker.trunkRadius = Mathf.Max(0.1f, capsule.radius * Mathf.Max(scale.x, scale.z));
                    marker.height = height;
                }
        }

        private static bool LooksLikeTree(string objectName) =>
            objectName.Contains("_Tree") && !objectName.Contains("Stump") && !objectName.Contains("Log");

        // ------------------------------------------------------------------ the field

        private void Awake()
        {
            terrain = GetComponent<Terrain>();
            // The terrain's own tree colliders stand for every tree in the map; the proxies take their place.
            if (TryGetComponent(out TerrainCollider terrainCollider)) terrainCollider.enableTreeColliders = false;
            holder = new GameObject("Tree Proxies").transform;
            holder.SetParent(transform, false);
            Build();
        }

        private void Build()
        {
            TerrainData data = terrain.terrainData;
            TreePrototype[] prototypes = data.treePrototypes;
            var shapes = new (bool ok, float radius, float height)[prototypes.Length];
            for (int i = 0; i < prototypes.Length; i++)
            {
                GameObject prefab = prototypes[i].prefab;
                CapsuleCollider capsule = prefab != null ? prefab.GetComponentInChildren<CapsuleCollider>(true) : null;
                if (capsule == null) continue;
                Vector3 scale = capsule.transform.lossyScale;
                shapes[i] = (true, capsule.radius * Mathf.Max(scale.x, scale.z), capsule.height * scale.y);
            }

            float minHeight = Settings != null ? Settings.minTreeHeight : DefaultMinHeight;
            TreeInstance[] instances = data.treeInstances;
            var kept = new List<TreeShape>(instances.Length);
            Vector3 origin = terrain.GetPosition(), size = data.size;
            foreach (TreeInstance instance in instances)
            {
                if (instance.prototypeIndex < 0 || instance.prototypeIndex >= shapes.Length) continue;
                var shape = shapes[instance.prototypeIndex];
                float height = shape.height * instance.heightScale;
                if (!shape.ok || height < minHeight) continue; // bushes and ferns stay soft
                Vector3 foot = origin + Vector3.Scale(instance.position, size);
                int index = kept.Count;
                kept.Add(new TreeShape { Foot = foot, Radius = shape.radius * instance.widthScale, Height = height });
                long key = Key(foot);
                if (!grid.TryGetValue(key, out List<int> cell)) grid[key] = cell = new List<int>(8);
                cell.Add(index);
            }
            trees = kept.ToArray();
        }

        private void OnDisable()
        {
            foreach (ClimbableSurface proxy in live.Values) Release(proxy);
            live.Clear();
        }

        private void Update()
        {
            if (Time.time < nextRefresh || trees.Length == 0) return;
            WallstrideSettings settings = Settings;
            nextRefresh = Time.time + (settings != null ? settings.treeProxyRefresh : DefaultRefresh);
            float radius = settings != null ? settings.treeProxyRadius : DefaultRadius;
            int cap = settings != null ? settings.treeProxyCap : DefaultCap;

            wanted.Clear();
            IReadOnlyList<PlayerWallRunMotor> players = PlayerWallRunMotor.Active;
            for (int p = 0; p < players.Count; p++)
            {
                PlayerWallRunMotor player = players[p];
                if (player == null || !player.WantsTreeProxies || player.gameObject.scene != gameObject.scene) continue;
                Gather(player.transform.position, radius, cap);
            }

            leaving.Clear();
            foreach (int index in live.Keys) if (!wanted.Contains(index)) leaving.Add(index);
            for (int i = 0; i < leaving.Count; i++)
            {
                Release(live[leaving[i]]);
                live.Remove(leaving[i]);
            }
            foreach (int index in wanted)
                if (!live.ContainsKey(index)) live[index] = Place(index, settings);
        }

        private void Gather(Vector3 around, float radius, int cap)
        {
            int reach = Mathf.CeilToInt(radius / CellSize);
            int cx = Mathf.FloorToInt(around.x / CellSize), cz = Mathf.FloorToInt(around.z / CellSize);
            float squared = radius * radius;
            for (int x = cx - reach; x <= cx + reach; x++)
                for (int z = cz - reach; z <= cz + reach; z++)
                {
                    if (!grid.TryGetValue(Pack(x, z), out List<int> cell)) continue;
                    for (int i = 0; i < cell.Count; i++)
                    {
                        if (wanted.Count >= cap) return;
                        Vector3 offset = trees[cell[i]].Foot - around;
                        offset.y = 0f;
                        if (offset.sqrMagnitude <= squared) wanted.Add(cell[i]);
                    }
                }
        }

        private ClimbableSurface Place(int index, WallstrideSettings settings)
        {
            ClimbableSurface proxy = pool.Count > 0 ? pool.Pop() : Make();
            TreeShape tree = trees[index];
            Transform t = proxy.transform;
            t.position = tree.Foot;
            var capsule = proxy.GetComponent<CapsuleCollider>();
            capsule.radius = tree.Radius;
            capsule.height = tree.Height;
            capsule.center = new Vector3(0f, tree.Height * 0.5f, 0f);
            proxy.kind = ClimbableKind.Tree;
            proxy.trunkRadius = tree.Radius;
            proxy.height = tree.Height;
            proxy.perchHeight = tree.Height * (settings != null ? settings.perchHeightShare : DefaultPerchShare);
            proxy.gameObject.SetActive(true);
            return proxy;
        }

        private ClimbableSurface Make()
        {
            var go = new GameObject("Tree Proxy");
            go.transform.SetParent(holder, false);
            go.layer = 0; // Default: what bodies, jumps and the camera's clicks already meet
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            go.isStatic = false;
            return go.AddComponent<ClimbableSurface>();
        }

        private void Release(ClimbableSurface proxy)
        {
            if (proxy == null) return;
            proxy.gameObject.SetActive(false);
            pool.Push(proxy);
        }

        private static WallstrideSettings Settings
        {
            get
            {
                IReadOnlyList<PlayerWallRunMotor> players = PlayerWallRunMotor.Active;
                for (int i = 0; i < players.Count; i++)
                    if (players[i] != null && players[i].Settings != null) return players[i].Settings;
                return null;
            }
        }

        private static long Key(Vector3 position) => Pack(Mathf.FloorToInt(position.x / CellSize), Mathf.FloorToInt(position.z / CellSize));
        private static long Pack(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}
