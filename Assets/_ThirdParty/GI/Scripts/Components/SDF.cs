using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public struct MeshEntry
    {
        public Mesh Mesh;
        public Material Material;
        public Matrix4x4 Transform;

        public MeshEntry(Mesh mesh, Material material, Matrix4x4 transform)
        {
            Mesh = mesh;
            Material = material;
            Transform = transform;
        }
    }

    [ExecuteAlways]
    public class SDF : MonoBehaviour
    {
        /// <sumary>
        /// List of all mesh entries used for baking the SDF.
        /// </summary>
        public IEnumerable<MeshEntry> Meshes { get { return _meshes; } }

        /// <summary>
        /// Bounds of the untransformed mesh.
        /// </summary>
        public Bounds BaseBounds
        {
            get
            {
                if (!_has_base_bounds)
                    UpdateEntries();

                return _base_bounds;
            }
        }

        /// <sumary>
        /// Diameter (radius * 2) of the bounding sphere in world space.
        /// </summary>
        public float Size
        {
            get
            {
                float largest;

                if (Entry == null)
                {
                    if (!_has_base_bounds)
                        UpdateEntries();

                    var size = _base_bounds.size;

                    largest = Mathf.Max(size.x * transform.localScale.x,
                                        size.y * transform.localScale.y,
                                        size.z * transform.localScale.z);
                }
                else
                {
                    var size = Entry.Fit.Bounds.size;
                    var local_scale = transform.localScale;
                    var scale = Entry.Scale;

                    largest = Mathf.Max(size.x * local_scale.x / scale.x,
                                        size.y * local_scale.y / scale.y,
                                        size.z * local_scale.z / scale.z);
                }

                var parent = 1.0f;

                if (transform.parent != null)
                    parent = Mathf.Max(transform.parent.lossyScale.x, transform.parent.lossyScale.y, transform.parent.lossyScale.z);

                return largest * parent;
            }
        }

        /// <summary>
        /// Unique ID used when baking and saving the SDF asset.
        /// </summary>
        [HideInInspector]
        public string UniqueID;

        /// <summary>
        /// Generated SDF as a serializable object.
        /// </summary>
        [SerializeReference]
        [Tooltip("Generated SDF as a serializable object.")]
        public SDFEntry Entry;

        /// <summary>
        /// SDF bias. Positive values expand the surface, lower values contract surface.
        /// </summary>
        [Range(-0.3f, 0.3f)]
        [Tooltip("SDF bias. Positive values expand the surface, lower values contract surface.")]
        public float Bias = 0.01f;

        /// <summary>
        /// Distance from camera to SDF origin. If object is further than MaxDistance it will not be visible.
        /// </summary>
        [Range(0, 1024 * 1024 * 1024)]
        [Tooltip("Distance from camera to SDF origin. If object is further than MaxDistance it will not be visible.")]
        public float MaxDistance = 1000;

        /// <summary>
        /// Size of the object compared to distance from camera to SDF origin. If object is smaller it will not be visible.
        /// </summary>
        [Range(0, 0.1f)]
        [Tooltip("Size of the object compared to distance from camera to SDF origin. If object is smaller it will not be visible.")]
        public float SizeFilter = 0.02f;

        /// <summary>
        /// Target resolution of a cube enveloping the mesh. Final baked resolution can end up smaller or larger.
        /// </summary>
        [Range(8, 64)]
        [Tooltip("Target resolution of a cube enveloping the mesh. Final baked resolution can end up smaller or larger.")]
        public int Resolution = 32;

        /// <summary>
        /// Double sided meshes are treated as hollow. By default Material.doubleSidedGI value is used. If a mesh has holes it can be helpful to treat it as double sided to avoid SDF bugs.
        /// </summary>
        [Tooltip("Double sided meshes are treated as hollow. By default Material.doubleSidedGI value is used. If a mesh has holes it can be helpful to treat it as double sided to avoid SDF bugs.")]
        public bool ForceDoubleSided;

        /// <summary>
        /// Should the SDF be visible when the object is selected in the editor.
        /// </summary>
        [Tooltip("Should the SDF be visible when the object is selected in the editor.")]
        public bool PreviewWhenSelected = false;

        [HideInInspector]

        public bool IsAuto = false; // have to be serialized due to reloads during code changes

        [NonSerialized]
        internal int RenderId = -1;

        private List<MeshEntry> _meshes = new List<MeshEntry>();
        private Bounds _base_bounds = new Bounds();
        private Vector3[] _bounds_temp = new Vector3[8];
        private bool _has_base_bounds = false;
        private Renderer _renderer;

        private SDFAtlas _atlas;

        private void register()
        {
            if (_atlas != null)
                _atlas.Register(this);
        }

        private void unregister()
        {
            if (_atlas != null)
                _atlas.Unregister(this);
        }

        public void Register(SDFAtlas atlas)
        {
            if (_atlas == atlas)
            {
                register();
                return;
            }

            unregister();

            _atlas = atlas;
            register();
        }

        public bool Filter()
        {
            if (Entry == null)
                return false;

            Camera camera;

            if (Application.isPlaying && Camera.main != null)
                camera = Camera.main;
            else
                camera = SceneView.lastActiveSceneView.camera;

            if (camera == null)
                return false;

            var dist = Vector3.Distance(camera.transform.position, transform.position);

            if (dist > MaxDistance)
                return false;

            var ratio = Size / Mathf.Max(dist, 0.001f);

            if (ratio < SizeFilter)
                return false;

            return true;
        }

        private void OnEnable()
        {
            _atlas = FindObjectOfType<SDFAtlas>();

            if (_atlas != null)
                _atlas.Register(this);
        }

        private void OnDisable()
        {
            unregister();
            _atlas = null;
        }

        public void RemoveAuto()
        {
            if (hideFlags == HideFlags.DontSave)
                hideFlags = HideFlags.None;

            IsAuto = false;
        }

        public void UpdateEntries()
        {
            _meshes.Clear();

            _has_base_bounds = false;
            _base_bounds = new Bounds(Vector3.zero, Vector3.zero);

            var lod_group = GetComponent<LODGroup>();

            if (lod_group != null && lod_group.lodCount > 0)
            {
                foreach (var renderer in lod_group.GetLODs()[0].renderers)
                    addEntry(renderer);
            }
            else
            {
                foreach (var renderer in GetComponentsInChildren<Renderer>())
                    addEntry(renderer);
            }
        }

        private void addEntry(Renderer renderer)
        {
            var mesh_filter = renderer.GetComponent<MeshFilter>();

            if (mesh_filter == null)
            {
                Debug.LogError("SDF requires meshes");
                return;
            }

            var mesh = mesh_filter.sharedMesh;

            if (mesh == null)
            {
                Debug.LogError("SDF requires meshes");
                return;
            }

            var material = renderer.sharedMaterial;

            if (material == null)
            {
                Debug.LogError("SDF requires materials");
                return;
            }

            if (mesh.GetTopology(0) != MeshTopology.Triangles)
            {
                Debug.LogError("SDF requires triangles");
                return;
            }

            if (mesh.indexFormat != IndexFormat.UInt16)
            {
                Debug.LogError("SDF requires 16 bit indices");
                return;
            }

            int positions = mesh.GetVertexAttributeStream(VertexAttribute.Position);

            if (positions < 0)
            {
                Debug.LogError("SDF requires positions");
                return;
            }

            var entry = new MeshEntry();
            entry.Mesh = mesh;
            entry.Material = material;

            var transform = Matrix4x4.identity;

            var cursor = renderer.gameObject;

            while (cursor != gameObject)
            {
                transform = Matrix4x4.Translate(cursor.transform.localPosition) *
                            Matrix4x4.Rotate(cursor.transform.localRotation) *
                            Matrix4x4.Scale(cursor.transform.localScale) *
                            transform;

                cursor = cursor.gameObject.transform.parent.gameObject;
            }

            entry.Transform = transform;

            _meshes.Add(entry);

            var min = renderer.localBounds.min;
            var max = renderer.localBounds.max;
            _bounds_temp[0] = new Vector3(min.x, min.y, min.z);
            _bounds_temp[1] = new Vector3(max.x, min.y, min.z);
            _bounds_temp[2] = new Vector3(min.x, max.y, min.z);
            _bounds_temp[3] = new Vector3(max.x, max.y, min.z);
            _bounds_temp[4] = new Vector3(min.x, min.y, max.z);
            _bounds_temp[5] = new Vector3(max.x, min.y, max.z);
            _bounds_temp[6] = new Vector3(min.x, max.y, max.z);
            _bounds_temp[7] = new Vector3(max.x, max.y, max.z);

            var bounds = GeometryUtility.CalculateBounds(_bounds_temp, entry.Transform);

            if (_has_base_bounds)
                _base_bounds.Encapsulate(bounds);
            else
            {
                _has_base_bounds = true;
                _base_bounds = bounds;
            }
        }
    }
}