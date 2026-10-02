using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    [ExecuteAlways]
    public class SDFAtlas : MonoBehaviour
    {
        public bool IsValid { get { return _state != null && _state.SDF != null && _state.Requests != null; } }

        public bool HasAnyLarge { get { return _state == null ? false : _state.HasAnyLarge; } }

        public int Cursor { get { return _state == null ? 0 : _state.Cursor; } }

        public ComputeBuffer Buffer { get { return _state == null ? null : _state.Buffer; } }

        public ComputeBuffer Large { get { return _state == null ? null : _state.Large; } }

        public ComputeBuffer Requests { get { return _state == null ? null : _state.Requests; } }

        public RenderTexture Voxels { get { return _state == null ? null : _state.Voxels; } }
        public ComputeBuffer Assets { get { return _state == null ? null : _state.Assets; } }
        public ComputeBuffer Bricks { get { return _state == null ? null : _state.Bricks; } }
        public RenderTexture SDF { get { return _state == null ? null : _state.SDF; } }

        [Serializable]
        public struct PrebuiltSDF
        {
            /// <summary>
            /// Target resolution of a cube enveloping the mesh. Final baked resolution can end up smaller or larger.
            /// </summary>
            [Tooltip("Target resolution of a cube enveloping the mesh. Final baked resolution can end up smaller or larger.")]
            public int Resolution;

            /// <summary>
            /// Double sided meshes are treated as hollow. By default Material.doubleSidedGI value is used. If a mesh has holes it can be helpful to treat it as double sided to avoid SDF bugs.
            /// </summary>
            [Tooltip("Double sided meshes are treated as hollow. By default Material.doubleSidedGI value is used. If a mesh has holes it can be helpful to treat it as double sided to avoid SDF bugs.")]
            public bool ForceDoubleSided;

            /// <summary>
            /// SDF bias. Positive values expand the surface, lower values contract surface.
            /// </summary>
            [Tooltip("SDF bias. Positive values expand the surface, lower values contract surface.")]
            public float Bias;

            /// <summary>
            /// Mesh to built SDF out of.
            /// </summary>
            [Tooltip("Mesh to built SDF out of.")]
            public Mesh Mesh;

            /// <summary>
            /// Material to use together with the mesh.
            /// </summary>
            [Tooltip("Material to use together with the mesh.")]
            public Material Material;

            /// <summary>
            /// Reference to a built SDF asset.
            /// </summary>
            [Tooltip("Reference to a built SDF asset.")]
            public SDFEntry Entry;
        }

        [Serializable]
        public struct BuildSettings
        {
            /// <summary>
            /// Target resolution of a cube enveloping the mesh. Final baked resolution can end up smaller or larger.
            /// </summary>
            [Tooltip("Target resolution of a cube enveloping the mesh. Final baked resolution can end up smaller or larger.")]
            public float Bias;

            /// <summary>
            /// Double sided meshes are treated as hollow. By default Material.doubleSidedGI value is used. If a mesh has holes it can be helpful to treat it as double sided to avoid SDF bugs.
            /// </summary>
            [Tooltip("Double sided meshes are treated as hollow. By default Material.doubleSidedGI value is used. If a mesh has holes it can be helpful to treat it as double sided to avoid SDF bugs.")]
            public bool ForceDoubleSided;

            /// <summary>
            /// Bounding sphere diameter of a mesh to be treated as medium sized.
            /// </summary>
            [Tooltip("Bounding sphere diameter of a mesh to be treated as medium sized.")]
            public float MediumSize;

            /// <summary>
            /// Bounding sphere diameter of a mesh to be treated as large.
            /// </summary>
            [Tooltip("Bounding sphere diameter of a mesh to be treated as large.")]
            public float LargeSize;

            /// <summary>
            /// Bounding sphere diameter of a mesh to be treated as huge.
            /// </summary>
            [Tooltip("Bounding sphere diameter of a mesh to be treated as huge.")]
            public float HugeSize;

            /// <summary>
            /// Target SDF resolution of small meshes.
            /// </summary>
            [Tooltip("Target SDF resolution of small meshes.")]
            public int SmallResolution;

            /// <summary>
            /// Target SDF resolution of medium sized meshes.
            /// </summary>
            [Tooltip("Target SDF resolution of medium sized meshes.")]
            public int MediumResolution;

            /// <summary>
            /// Target SDF resolution of large meshes.
            /// </summary>
            [Tooltip("Target SDF resolution of large meshes.")]
            public int LargeResolution;

            /// <summary>
            /// Target SDF resolution of huge meshes.
            /// </summary>
            [Tooltip("Target SDF resolution of huge meshes.")]
            public int HugeResolution;

            public static BuildSettings Make()
            {
                var settings = new BuildSettings();

                settings.MediumSize = 0.5f;
                settings.LargeSize = 2;
                settings.HugeSize = 32;

                settings.SmallResolution = 8;
                settings.MediumResolution = 16;
                settings.LargeResolution = 32;
                settings.HugeResolution = 64;

                settings.ForceDoubleSided = true;
                settings.Bias = 0.1f;

                return settings;
            }

            public void Apply(SDF sdf)
            {
                sdf.Bias = Bias;
                sdf.ForceDoubleSided = ForceDoubleSided;

                var size = sdf.Size;

                if (size < MediumSize)
                    sdf.Resolution = SmallResolution;
                else if (size < LargeSize)
                    sdf.Resolution = MediumResolution;
                else if (size < HugeSize)
                    sdf.Resolution = LargeResolution;
                else
                    sdf.Resolution = HugeResolution;
            }
        }

        [Serializable]
        public struct AutoSettings
        {
            /// <summary>
            /// When the automatic SDF generation is performed.
            /// </summary>
            [Tooltip("When the automatic SDF generation is performed.")]
            public SearchType Type;

            /// <summary>
            /// Distance from camera to SDF origin. If object is further than MaxDistance it will not be visible.
            /// </summary>
            [Tooltip("Distance from camera to SDF origin. If object is further than MaxDistance it will not be visible.")]
            public float MaxDistance;

            /// <summary>
            /// Size of the object compared to distance from camera to SDF origin. If object is smaller it will not be visible.
            /// </summary>
            [Tooltip("Size of the object compared to distance from camera to SDF origin. If object is smaller it will not be visible.")]
            public float SizeFilter;

            /// <summary>
            /// Maximum number of meshes to check every frame. It takes multiple frames to check the entire scene.
            /// </summary>
            [Tooltip("Maximum number of meshes to check every frame. It takes multiple frames to check the entire scene.")]
            public int CheckPerFrame;

            /// <summary>
            /// Settings determining resolution, bias and double-sidedness of built SDF.
            /// </summary>
            public BuildSettings Build;

            public static AutoSettings Make()
            {
                var settings = new AutoSettings();
                settings.Type = SearchType.Always;
                settings.MaxDistance = 1000;
                settings.CheckPerFrame = 100;
                settings.SizeFilter = 0.02f;

                settings.Build = BuildSettings.Make();

                return settings;
            }
        }

        [Serializable]
        public struct TreeSettings
        {
            /// <summary>
            /// When the automatic SDF generation is performed.
            /// </summary>
            [Tooltip("When the automatic SDF generation is performed.")]
            public SearchType Type;

            /// <summary>
            /// Distance from camera to SDF origin. If tree is further than MaxDistance it will not be visible.
            /// </summary>
            [Tooltip("Distance from camera to SDF origin. If tree is further than MaxDistance it will not be visible.")]
            public float MaxDistance;

            /// <summary>
            /// Maximum number of meshes to check every frame. It takes multiple frames to check the entire scene.
            /// </summary>
            [Tooltip("Maximum number of trees to check every frame. It takes multiple frames to check the entire scene.")]
            public int CheckPerFrame;

            /// <summary>
            /// Settings determining resolution, bias and double-sidedness of built SDF.
            /// </summary>
            public BuildSettings Build;

            public static TreeSettings Make()
            {
                var settings = new TreeSettings();
                settings.Type = SearchType.Always;
                settings.MaxDistance = 200;
                settings.CheckPerFrame = 1000;

                settings.Build = BuildSettings.Make();

                return settings;
            }
        }

        /// <summary>
        /// Settings for the terrain.
        /// </summary>
        public SDFTerrain Terrain = new SDFTerrain();

        /// <summary>
        /// Renderer mode to inspect various stages of the GI system.
        /// </summary>
        [Tooltip("Renderer mode to inspect various stages of the GI system.")]
        public DebugChoice Debug;

        /// <summary>
        /// Spherical mesh to use when inspecting debug visualizations.
        /// </summary>
        [Tooltip("Spherical mesh to use when inspecting debug visualizations.")]
        public Mesh DebugMesh;

        /// <summary>
        /// Settings for automatically generates SDFs.
        /// </summary>
        public AutoSettings Auto = AutoSettings.Make();

        /// <summary>
        /// Settings for SDFs generated for trees on terrain.
        /// </summary>
        public TreeSettings Trees = TreeSettings.Make();

        /// <summary>
        /// List of SDFs to be used for found meshes and trees.
        /// </summary>
        public List<PrebuiltSDF> Prebuilt = new List<PrebuiltSDF>();

        private State _state;

        private HashSet<SDF> _entries = new HashSet<SDF>();

        private ScriptableExecutor _scriptable_executor;
        private GraphicsExecutor _graphics_executor;

        private struct MeshInfo
        {
            public BuildKey Entry;
            public SDF SDF;
        }

        private Dictionary<MeshInfo, SDFEntry> _auto_builds = new Dictionary<MeshInfo, SDFEntry>();

        private struct BuildKey
        {
            public int Resolution;
            public Mesh Mesh;
            public Material Material;

            public BuildKey(Mesh mesh, Material material, int resolution)
            {
                Resolution = resolution;
                Mesh = mesh;
                Material = material;
            }
        }

        private struct PrebuiltMeshKey
        {
            public Mesh Mesh;
            public Material Material;

            public PrebuiltMeshKey(Mesh mesh, Material material)
            {
                Mesh = mesh;
                Material = material;
            }
        }

        private struct TrackedMesh
        {
            public MeshFilter Filter;
            public BuildKey Entry;
            public SDF SDF;
        }

        private Dictionary<MeshRenderer, TrackedMesh> _tracked_meshes = new Dictionary<MeshRenderer, TrackedMesh>();
        private List<MeshRenderer> _tracked_meshes_to_remove = new List<MeshRenderer>();
        private List<SDF> _entries_to_remove = new List<SDF>();

        private Dictionary<PrebuiltMeshKey, SDFEntry> _prebuilt_by_mesh = new Dictionary<PrebuiltMeshKey, SDFEntry>();
        private Dictionary<BuildKey, SDFEntry> _built = new Dictionary<BuildKey, SDFEntry>();

        [HideInInspector]
        public List<int> Selection = new List<int>();

        private class TreeWrapper
        {
            public GameObject Prefab;
            public SDFEntry Entry;
        }

        private class TreeSDF
        {
            public bool Visible;
            public GameObject Object;
        }

        private class PrototypeInstances
        {
            public Dictionary<int, TreeSDF> Map = new Dictionary<int, TreeSDF>();
            public List<GameObject> Available = new List<GameObject>();
        }

        private Dictionary<int, TreeWrapper> _tree_prototypes = new Dictionary<int, TreeWrapper>();
        private List<PrototypeInstances> _sdfs_per_tree_prototype = new List<PrototypeInstances>();
        private List<int> _indices_to_remove = new List<int>();

        private MeshRenderer[] _mesh_renderers;
        private int _mesh_renderer_iterator;

        private int _tree_iterator;

        private void Awake()
        {
            updateOverrides();

            if (DebugMesh == null)
                DebugMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        }

        public void ResetBuilt()
        {
            foreach (var build in _built.Values)
            {
                if (Application.isPlaying)
                    Destroy(build);
                else
                {
                    EditorApplication.delayCall += () =>
                    {
                        DestroyImmediate(build);
                    };
                }
            }

            _built.Clear();

            // since SDFs are invalid,reset the entire scene
            ResetAuto();
            ResetTrees();
        }

        public void ResetTrees()
        {
            _tree_prototypes.Clear();

            foreach (var list in _sdfs_per_tree_prototype)
            {
                foreach (var instance in list.Map.Values)
                {
                    if (Application.isPlaying)
                        Destroy(instance.Object);
                    else
                    {
                        EditorApplication.delayCall += () =>
                        {
                            DestroyImmediate(instance.Object);
                        };
                    }
                }

                foreach (var instance in list.Available)
                {
                    if (Application.isPlaying)
                        Destroy(instance);
                    else
                    {
                        EditorApplication.delayCall += () =>
                        {
                            DestroyImmediate(instance);
                        };
                    }
                }

                list.Map.Clear();
                list.Available.Clear();
            }

            _tree_iterator = 0;
        }

        public void Register(SDF sdf)
        {
            _entries.Add(sdf);
        }

        public void Unregister(SDF sdf)
        {
            _entries.Remove(sdf);
        }

        public void ClearSelection()
        {
            Selection.Clear();
        }

        public void AddSelection(SDF sdf)
        {
            if (_state != null)
            {
                var index = _state.GetIndex(sdf);

                if (index >= 0)
                    Selection.Add(index);
            }
        }

        private void OnDestroy()
        {
            if (_state != null)
            {
                _state.Release();
                _state = null;
            }
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginContextRendering += preRender;

            foreach (var sdf in FindObjectsOfType<SDF>())
                if (sdf.isActiveAndEnabled)
                    sdf.Register(this);

            Camera.onPreRender += preRenderGraphics;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= preRender;
        }

        public void ResetAuto()
        {
            _tracked_meshes.Clear();

            foreach (var sdf in _entries)
                if (sdf.IsAuto)
                    _entries_to_remove.Add(sdf);

            foreach (var entry in _entries_to_remove)
            {
                if (Application.isPlaying)
                    Destroy(entry);
                else
                {
                    EditorApplication.delayCall += () =>
                    {
                        DestroyImmediate(entry);
                    };
                }
            }

            _entries_to_remove.Clear();

            _mesh_renderers = null;
            _mesh_renderer_iterator = 0;
        }

        public void CheckAllAuto()
        {
        }

        private void updateOverrides()
        {
            _prebuilt_by_mesh.Clear();

            foreach (var value in Prebuilt)
                _prebuilt_by_mesh[new PrebuiltMeshKey(value.Mesh, value.Material)] = value.Entry;
        }

        private void OnValidate()
        {
            updateOverrides();

            var reset_auto = false;

            switch (Auto.Type)
            {
                case SearchType.Always: reset_auto = false; break;
                case SearchType.InGame: reset_auto = !Application.isPlaying; break;
                case SearchType.None: reset_auto = true; break;
            }

            if (reset_auto)
                ResetAuto();

            var reset_trees = false;

            switch (Trees.Type)
            {
                case SearchType.Always: reset_trees = false; break;
                case SearchType.InGame: reset_trees = !Application.isPlaying; break;
                case SearchType.None: reset_trees = true; break;
            }

            if (reset_trees)
                ResetTrees();

            foreach (var sdf in _entries)
            {
                if (sdf.IsAuto)
                {
                    sdf.MaxDistance = Auto.MaxDistance;
                    sdf.SizeFilter = Auto.SizeFilter;
                }
            }
        }

        private void Update()
        {
            switch (Auto.Type)
            {
                case SearchType.Always:
                    findMeshes();
                    break;

                case SearchType.InGame:
                    if (Application.isPlaying)
                        findMeshes();
                    else
                        ResetAuto();
                    break;

                case SearchType.None:
                    ResetAuto();
                    break;
            }

            switch (Trees.Type)
            {
                case SearchType.Always:
                    findTrees();
                    break;

                case SearchType.InGame:
                    if (Application.isPlaying)
                        findTrees();
                    else
                        ResetTrees();
                    break;

                case SearchType.None:
                    ResetTrees();
                    break;
            }
        }

        private SDFEntry getEntry(BuildKey key, float bias, bool force_double_sided)
        {
            if (_built.ContainsKey(key))
                return _built[key];

            var build = Baker.Instance.Bake(key.Mesh, key.Material, key.Resolution, bias, force_double_sided);
            _built[key] = build;

            return build;
        }

        private void findMeshes()
        {
            foreach (var key in _tracked_meshes.Keys)
                if (key == null || _tracked_meshes[key].SDF == null)
                    _tracked_meshes_to_remove.Add(key);

            foreach (var key in _tracked_meshes_to_remove)
                _tracked_meshes.Remove(key);

            _tracked_meshes_to_remove.Clear();

            //

            if (_mesh_renderers == null || _mesh_renderer_iterator >= _mesh_renderers.Length)
            {
                _mesh_renderer_iterator = 0;
                _mesh_renderers = FindObjectsOfType<MeshRenderer>();
            }

            //

            var iterator_end = _mesh_renderer_iterator + Auto.CheckPerFrame;

            if (iterator_end > _mesh_renderers.Length)
                iterator_end = _mesh_renderers.Length;

            while (_mesh_renderer_iterator < iterator_end)
            {
                var renderer = _mesh_renderers[_mesh_renderer_iterator];
                _mesh_renderer_iterator++;

                if (renderer == null)
                    continue;

                TrackedMesh tracked_mesh;

                if (_tracked_meshes.ContainsKey(renderer))
                {
                    tracked_mesh = _tracked_meshes[renderer];

                    if (!tracked_mesh.SDF.IsAuto)
                        continue;
                }
                else
                {
                    tracked_mesh = new TrackedMesh();

                    tracked_mesh.SDF = renderer.GetComponent<SDF>();

                    if (tracked_mesh.SDF == null)
                        tracked_mesh.SDF = renderer.GetComponentInParent<SDF>();

                    if (tracked_mesh.SDF == null)
                    {
                        var filter = renderer.GetComponent<MeshFilter>();

                        tracked_mesh.SDF = renderer.gameObject.AddComponent<SDF>();
                        tracked_mesh.SDF.hideFlags = HideFlags.DontSave;
                        tracked_mesh.SDF.IsAuto = true;
                        tracked_mesh.SDF.MaxDistance = Auto.MaxDistance;
                        tracked_mesh.SDF.SizeFilter = Auto.SizeFilter;
                    }

                    _tracked_meshes[renderer] = tracked_mesh;
                }

                if (tracked_mesh.Filter == null)
                    tracked_mesh.Filter = renderer.GetComponent<MeshFilter>();

                if (tracked_mesh.Filter == null)
                    continue;

                var prebuilt_key = new PrebuiltMeshKey(tracked_mesh.Filter.sharedMesh, renderer.sharedMaterial);

                if (_prebuilt_by_mesh.ContainsKey(prebuilt_key))
                    tracked_mesh.SDF.Entry = _prebuilt_by_mesh[prebuilt_key];
                {
                    Auto.Build.Apply(tracked_mesh.SDF);

                    var key = new BuildKey(prebuilt_key.Mesh, prebuilt_key.Material, tracked_mesh.SDF.Resolution);
                    tracked_mesh.SDF.Entry = getEntry(key, tracked_mesh.SDF.Bias, tracked_mesh.SDF.ForceDoubleSided);
                }
            }
        }

        private void findTrees()
        {
            var terrain_data = UnityEngine.Terrain.activeTerrain?.terrainData;

            if (terrain_data == null)
                return;

            var terrain_position = UnityEngine.Terrain.activeTerrain.transform.position;
            var prototypes = terrain_data.treePrototypes;

            for (int i = 0; i < prototypes.Length; i++)
            {
                var prototype = prototypes[i];

                if (!_tree_prototypes.ContainsKey(i))
                    _tree_prototypes.Add(i, new TreeWrapper());

                var entry = _tree_prototypes[i];

                if (entry.Prefab != prototype.prefab || entry.Entry == null)
                {
                    entry.Prefab = prototype.prefab;
                    entry.Entry = null;

                    if (i < _sdfs_per_tree_prototype.Count)
                    {
                        foreach (var value in _sdfs_per_tree_prototype[i].Map.Values)
                        {
                            if (Application.isPlaying)
                                Destroy(value.Object);
                            else
                            {
                                EditorApplication.delayCall += () =>
                                {
                                    DestroyImmediate(value.Object);
                                };
                            }
                        }

                        foreach (var value in _sdfs_per_tree_prototype[i].Available)
                        {
                            if (Application.isPlaying)
                                Destroy(value);
                            else
                            {
                                EditorApplication.delayCall += () =>
                                {
                                    DestroyImmediate(value);
                                };
                            }
                        }

                        _sdfs_per_tree_prototype[i].Map.Clear();
                        _sdfs_per_tree_prototype[i].Available.Clear();
                    }

                    var instance = Instantiate(entry.Prefab);
                    instance.hideFlags = HideFlags.HideAndDontSave;

                    var filter = instance.GetComponent<MeshFilter>();
                    var renderer = instance.GetComponent<MeshRenderer>();

                    if (filter != null && filter.sharedMesh != null && renderer != null)
                    {
                        var sdf = instance.AddComponent<SDF>();
                        sdf.hideFlags = HideFlags.HideAndDontSave;
                        sdf.IsAuto = true;
                        sdf.MaxDistance = Auto.MaxDistance;
                        sdf.SizeFilter = Auto.SizeFilter;

                        Trees.Build.Apply(sdf);

                        var prebuilt_key = new PrebuiltMeshKey(filter.sharedMesh, renderer.sharedMaterial);

                        if (_prebuilt_by_mesh.ContainsKey(prebuilt_key))
                        {
                            sdf.Entry = _prebuilt_by_mesh[prebuilt_key];
                        }
                        else
                        {
                            var key = new BuildKey(filter.sharedMesh, renderer.sharedMaterial, sdf.Resolution);

                            if (_built.ContainsKey(key))
                                sdf.Entry = _built[key];
                            else
                            {
                                sdf.Entry = Baker.Instance.Bake(sdf);
                                _built[key] = sdf.Entry;
                            }
                        }

                        entry.Entry = sdf.Entry;
                    }

                    if (Application.isPlaying)
                        Destroy(instance);
                    else
                    {
                        EditorApplication.delayCall += () =>
                        {
                            DestroyImmediate(instance);
                        };
                    }
                }
            }

            while (_sdfs_per_tree_prototype.Count < terrain_data.treePrototypes.Length)
                _sdfs_per_tree_prototype.Add(new PrototypeInstances());

            var camera_position = Vector3.zero;

            if (Application.isPlaying && Camera.main != null)
                camera_position = Camera.main.transform.position;
            else
            {
                var camera = SceneView.currentDrawingSceneView?.camera;

                if (camera != null)
                    camera_position = camera.transform.position;
                else
                {
                    camera = SceneView.lastActiveSceneView.camera;

                    if (camera != null)
                        camera_position = camera.transform.position;
                    else if (Camera.main != null)
                        camera_position = Camera.main.transform.position;
                }
            }

            var tree_instance_count = terrain_data.treeInstanceCount;
            var tree_iterator_end = _tree_iterator + Trees.CheckPerFrame;

            var spillover_end = 0;
            var is_spillover = false;
            var will_spill = false;

            if (tree_iterator_end > tree_instance_count)
            {
                will_spill = true;
                spillover_end = tree_iterator_end - tree_instance_count;
                tree_iterator_end = tree_instance_count;

                if (spillover_end > _tree_iterator)
                    spillover_end = _tree_iterator;
            }

            if (_tree_iterator >= tree_iterator_end && !will_spill)
            {
                _tree_iterator = 0;
                tree_iterator_end = spillover_end;
                is_spillover = true;
            }

            foreach (var entries in _sdfs_per_tree_prototype)
                foreach (var key in entries.Map.Keys)
                {
                    var clear = false;

                    if (key >= tree_instance_count)
                        clear = true;
                    else if (key >= _tree_iterator && key < tree_iterator_end)
                        clear = true;
                    else if (will_spill && key < spillover_end)
                        clear = true;

                    if (clear)
                        entries.Map[key].Visible = false;
                }

            while (_tree_iterator < tree_iterator_end)
            {
                var tree_index = _tree_iterator;
                _tree_iterator++;

                if (_tree_iterator >= tree_iterator_end && !is_spillover && will_spill)
                {
                    is_spillover = true;
                    _tree_iterator = 0;
                    tree_iterator_end = spillover_end;
                }

                var tree = terrain_data.GetTreeInstance(tree_index);
                var prototype = tree.prototypeIndex;

                if (prototype >= 0 && prototype < _sdfs_per_tree_prototype.Count)
                {
                    var list = _sdfs_per_tree_prototype[prototype];

                    var position = terrain_position + Vector3.Scale(tree.position, terrain_data.size);
                    var dist = Vector3.Distance(camera_position, position);

                    if (dist < Trees.MaxDistance)
                    {
                        Transform transform;

                        if (list.Map.ContainsKey(tree_index))
                        {
                            var entry = list.Map[tree_index];
                            transform = entry.Object.transform;
                            entry.Visible = true;
                        }
                        else if (list.Available.Count > 0)
                        {
                            var obj = list.Available[list.Available.Count - 1];
                            list.Available.RemoveAt(list.Available.Count - 1);

                            obj.SetActive(true);
                            transform = obj.transform;

                            var entry = new TreeSDF();
                            entry.Object = obj;
                            entry.Visible = true;

                            list.Map[tree_index] = entry;
                        }
                        else
                        {
                            var obj = new GameObject();
                            obj.hideFlags = HideFlags.HideAndDontSave;

                            var sdf = obj.AddComponent<SDF>();
                            sdf.hideFlags = HideFlags.HideAndDontSave;
                            sdf.IsAuto = true;
                            sdf.MaxDistance = Auto.MaxDistance;
                            sdf.SizeFilter = Auto.SizeFilter;

                            Trees.Build.Apply(sdf);

                            sdf.Entry = _tree_prototypes[prototype].Entry;

                            transform = obj.transform;

                            var entry = new TreeSDF();
                            entry.Object = obj;
                            entry.Visible = true;

                            list.Map[tree_index] = entry;
                        }

                        transform.position = position;
                        transform.rotation = Quaternion.AngleAxis(tree.rotation, Vector3.up);
                        transform.localScale = new Vector3(tree.widthScale, tree.heightScale, tree.widthScale);
                    }
                    else if (list.Map.ContainsKey(tree_index))
                    {
                        var instance = list.Map[tree_index];
                        list.Map.Remove(tree_index);

                        if (list.Available.Count < 100)
                        {
                            instance.Object.SetActive(false);
                            list.Available.Add(instance.Object);
                        }
                        else
                        {
                            if (Application.isPlaying)
                                Destroy(instance.Object);
                            else
                            {
                                EditorApplication.delayCall += () =>
                                {
                                    DestroyImmediate(instance.Object);
                                };
                            }
                        }
                    }
                }
            }

            foreach (var list in _sdfs_per_tree_prototype)
            {
                foreach (var entry in list.Map)
                    if (!entry.Value.Visible)
                        _indices_to_remove.Add(entry.Key);

                foreach (var tree_index in _indices_to_remove)
                {
                    var instance = list.Map[tree_index];
                    list.Map.Remove(tree_index);

                    if (list.Available.Count < 100)
                    {
                        instance.Object.SetActive(false);
                        list.Available.Add(instance.Object);
                    }
                    else
                    {
                        if (Application.isPlaying)
                            Destroy(instance.Object);
                        else
                        {
                            EditorApplication.delayCall += () =>
                            {
                                DestroyImmediate(instance.Object);
                            };
                        }
                    }
                }

                _indices_to_remove.Clear();
            }
        }

        public void ManualUpdate()
        {
            if (_graphics_executor == null)
                _graphics_executor = new GraphicsExecutor();

            runUpdate(_graphics_executor);
        }

        private void runUpdate(Executor executor)
        {
            if (_state != null && (_state.SDF == null || _state.Requests == null))
            {
                _state.Release();
                _state = null;
            }

            if (_state == null)
                _state = new State();

            _state.Update(_entries, executor);
        }

        private void preRenderGraphics(Camera camera)
        {
            ManualUpdate();
        }

        private void preRender(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (_scriptable_executor == null)
                _scriptable_executor = new ScriptableExecutor();

            _scriptable_executor.Context = context;
            runUpdate(_scriptable_executor);
        }
    }
}