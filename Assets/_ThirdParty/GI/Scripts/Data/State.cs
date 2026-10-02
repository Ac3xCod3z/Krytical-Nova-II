using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public class State
    {
        struct BrickSlice
        {
            public int Start;
            public int Count;

            public BrickSlice(int start, int count)
            {
                Start = start;
                Count = count;
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        struct Entry
        {
            [FieldOffset(0)] uint asset;
            [FieldOffset(4)] uint hash_id;
            [FieldOffset(16)] float scale_x;
            [FieldOffset(20)] float scale_y;
            [FieldOffset(24)] float scale_z;
            [FieldOffset(32)] Matrix4x4 transform;
            [FieldOffset(96)] Matrix4x4 inverse;

            public Entry(uint asset, uint hash_id, Matrix4x4 matrix, Vector3 scale)
            {
                this.asset = asset;
                this.hash_id = hash_id;
                this.scale_x = scale.x;
                this.scale_y = scale.y;
                this.scale_z = scale.z;
                this.transform = matrix;
                this.inverse = matrix.inverse;
            }
        }

        public int Cursor { get { return _index_to_render_id.Count; } }

        public bool HasAnyLarge { get { return _has_any_large; } }

        public ComputeBuffer Buffer { get { return _buffer; } }
        public ComputeBuffer Large { get { return _large; } }

        public ComputeBuffer Requests { get { return _requests; } }

        public RenderTexture Voxels { get { return _voxel_volume; } }
        public ComputeBuffer Assets { get { return _assets; } }
        public ComputeBuffer Bricks { get { return _bricks; } }
        public RenderTexture SDF { get { return _sdf_volume; } }

        public static int LARGE_LIMIT = 4096; // number of grid cells
        public static float GRID_INFLUENCE = 4.0f; // expansion of AABB when fitting into the grid

        private bool _r32f;

        private ComputeBuffer _bricks;
        private ComputeBuffer _assets;
        private ComputeBuffer _brick_pool;

        private RenderTexture _sdf_volume;
        private RenderTexture _voxel_volume;

        private Kernel _kcopy_sdf_bricks;
        private Kernel _kcopy_voxel_bricks;

        private Kernel _kreset;
        private Kernel _kasset;
        private Kernel _kbricks_forward;
        private Kernel _kbricks_backward;

        private List<ComputeBuffer> _buffers_to_release = new List<ComputeBuffer>();

        private List<int> _available_asset_indices = new List<int>();
        private List<SDFEntry> _asset_to_entry = new List<SDFEntry>();
        private List<string> _asset_to_id = new List<string>();
        private List<int> _actual_brick_counts = new List<int>();
        private List<int> _virtual_brick_counts = new List<int>();
        private List<BrickSlice> _brick_slices = new List<BrickSlice>();
        private List<uint[]> _mappings = new List<uint[]>();
        private Dictionary<string, int> _id_to_asset = new Dictionary<string, int>();
        private HashSet<string> _ids_in_use = new HashSet<string>();
        private HashSet<string> _new_ids = new HashSet<string>();
        private HashSet<int> _asset_removals = new HashSet<int>();
        private List<int> _instance_removals = new List<int>();

        private HashSet<int> _render_ids_in_use = new HashSet<int>();
        private List<int> _indices_to_remove = new List<int>();
        private List<int> _index_to_render_id = new List<int>();
        private Dictionary<int, int> _render_id_to_index = new Dictionary<int, int>();
        private List<Matrix4x4> _transforms = new List<Matrix4x4>();
        private List<Vector3> _scales = new List<Vector3>();
        private List<int> _asset_list = new List<int>();
        private List<uint> _hash_ids = new List<uint>();
        private List<int> _available_indices = new List<int>();
        private List<bool> _large_mapping = new List<bool>();

        private List<int> _index_list = new List<int>();
        private List<Entry> _value_list = new List<Entry>();

        private ComputeBuffer _buffer;
        private ComputeBuffer _edit_indices;
        private ComputeBuffer _edit_values;

        private bool _has_edits;

        private ComputeBuffer _requests;
        private ComputeBuffer _large;

        private ComputeBuffer _counter;

        private Kernel _krequest_clear;
        private Kernel _krequest;
        private Kernel _kedit;

        private bool _requests_dirty;

        private List<BrickSlice> _available_brick_slices = new List<BrickSlice>();
        private int _brick_slice_cursor;

        private int _brick_pool_cursor;

        private bool _is_initialized;

        private bool _has_any_large;

        private uint _last_hash_id = 0;

        private int _last_render_id;

        public State()
        {
            // assume Android does not handle R8_Unorm with random writes...
            if (SystemInfo.graphicsDeviceVersion.StartsWith("OpenGL ES 3."))
                _r32f = true;
            else
                _r32f = false;

            {
                _bricks = new ComputeBuffer(Defines.MAX_BRICKS, 4, ComputeBufferType.Raw);
                _brick_pool = new ComputeBuffer(Defines.MAX_BRICKS, 4, ComputeBufferType.Raw);
                _assets = new ComputeBuffer(Defines.MAX_ASSETS, Defines.ASSET_STRIDE, ComputeBufferType.Raw);
            }

            {
                var desc = new RenderTextureDescriptor();
                desc.width = Defines.ATLAS_DIM;
                desc.height = Defines.ATLAS_DIM;
                desc.volumeDepth = Defines.ATLAS_DIM;
                desc.dimension = TextureDimension.Tex3D;
                desc.msaaSamples = 1;
                desc.enableRandomWrite = true;

                if (_r32f)
                    desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat;
                else
                    desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;

                _sdf_volume = new RenderTexture(desc);
            }

            {
                var desc = new RenderTextureDescriptor();
                desc.width = Defines.ATLAS_DIM;
                desc.height = Defines.ATLAS_DIM;
                desc.volumeDepth = Defines.ATLAS_DIM;
                desc.dimension = TextureDimension.Tex3D;
                desc.msaaSamples = 1;
                desc.enableRandomWrite = true;
                desc.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;

                _voxel_volume = new RenderTexture(desc);
            }

            {
                _kcopy_sdf_bricks = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/CopyBricks"), "Main");
                _kcopy_voxel_bricks = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/CopyVoxelBricks"), "Main");

                _kreset = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/Reset"), "Main");
                _kasset = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/Asset"), "Main");

                _kbricks_forward = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/Bricks"), "Forward");
                _kbricks_backward = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/Bricks"), "Backward");
            }

            _buffer = new ComputeBuffer(Defines.SDF_MAX_INSTANCES, 4);
            _edit_indices = new ComputeBuffer(Defines.SDF_MAX_INSTANCES, 4);
            _edit_values = new ComputeBuffer(Defines.SDF_MAX_INSTANCES, 160);

            _krequest_clear = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/Request"), "Clear");
            _krequest = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/Request"), "Main");
            _kedit = new Kernel(Resources.Load<ComputeShader>("Shaders/SDF/Edit"), "Main");

            _requests = new ComputeBuffer(Defines.SDF_REQUEST_SIZE / 4, 4, ComputeBufferType.Raw);
            _large = new ComputeBuffer(Defines.SDF_LARGE_SIZE / 4, 4, ComputeBufferType.Raw);
        }

        public void Release()
        {
            if (_sdf_volume != null)
            {
                GameObject.DestroyImmediate(_sdf_volume);
                _sdf_volume = null;
            }

            if (_voxel_volume != null)
            {
                GameObject.DestroyImmediate(_voxel_volume);
                _voxel_volume = null;
            }

            if (_bricks != null)
            {
                _bricks.Release();
                _bricks = null;
            }

            if (_assets != null)
            {
                _assets.Release();
                _assets = null;
            }

            if (_brick_pool != null)
            {
                _brick_pool.Release();
                _brick_pool = null;
            }

            foreach (var buffer in _buffers_to_release)
                buffer.Release();

            _buffers_to_release.Clear();

            if (_buffer != null)
            {
                _buffer.Release();
                _buffer = null;
            }

            if (_edit_indices != null)
            {
                _edit_indices.Release();
                _edit_indices = null;
            }

            if (_edit_values != null)
            {
                _edit_values.Release();
                _edit_values = null;
            }

            if (_requests != null)
            {
                _requests.Release();
                _requests = null;
            }

            if (_large != null)
            {
                _large.Release();
                _large = null;
            }
        }

        public int GetIndex(SDF sdf)
        {
            if (sdf == null || sdf.gameObject == null)
                return -1;

            if (sdf.RenderId >= 0 && _render_id_to_index.ContainsKey(sdf.RenderId))
                return _render_id_to_index[sdf.RenderId];
            else
                return -1;
        }

        public void Update(HashSet<SDF> entries, Executor executor)
        {
            _new_ids.Clear();
            _has_any_large = false;

            //

            foreach (var sdf in entries)
            {
                if (sdf == null || sdf.gameObject == null)
                    continue;

                var entry = sdf.Entry;

                if (entry == null || entry.ID == null)
                    continue;

                if (entry.Voxels == null || entry.Voxels.Length == 0)
                    continue;

                if (entry.SDF == null || entry.SDF.Length == 0)
                    continue;

                if (entry.Version < 2)
                    continue;

                if (!sdf.Filter())
                    continue;

                //

                int asset;

                var is_dirty = false;

                if (_id_to_asset.ContainsKey(entry.ID))
                    asset = _id_to_asset[entry.ID];
                else
                {
                    is_dirty = true;

                    if (_available_asset_indices.Count == 0)
                    {
                        asset = _asset_to_entry.Count;
                        _asset_to_entry.Add(entry);
                        _asset_to_id.Add(entry.ID);
                        _brick_slices.Add(new BrickSlice());
                        _mappings.Add(null);
                        _actual_brick_counts.Add(0);
                        _virtual_brick_counts.Add(0);
                    }
                    else
                    {
                        var i = _available_asset_indices.Count - 1;
                        asset = _available_asset_indices[i];
                        _available_asset_indices.RemoveAt(i);

                        _asset_to_entry[asset] = entry;
                        _asset_to_id[asset] = entry.ID;
                        _brick_slices[asset] = new BrickSlice();
                        _mappings[asset] = null;
                        _actual_brick_counts[asset] = 0;
                        _virtual_brick_counts[asset] = 0;
                    }

                    _id_to_asset[entry.ID] = asset;
                    _new_ids.Add(entry.ID);
                }

                _ids_in_use.Add(entry.ID);

                //

                var obj = sdf.gameObject;
                var transform = obj.transform.localToWorldMatrix;
                var scale = obj.transform.lossyScale;

                if (sdf.RenderId >= 0 && _render_id_to_index.ContainsKey(sdf.RenderId))
                {
                    _render_ids_in_use.Add(sdf.RenderId);

                    var index = _render_id_to_index[sdf.RenderId];

                    if (_scales[index] != scale)
                    {
                        is_dirty = true;
                        _scales[index] = scale;
                        _requests_dirty = true;
                    }

                    if (_transforms[index] != transform)
                    {
                        is_dirty = true;
                        _transforms[index] = transform;
                    }

                    if (_asset_list[index] != asset)
                    {
                        is_dirty = true;
                        _asset_list[index] = asset;
                        _hash_ids[index] = ++_last_hash_id;
                        _requests_dirty = true;
                    }

                    if (is_dirty)
                        updateTransform(index, (uint)asset, _hash_ids[index], transform, scale);

                    if (_large_mapping[index])
                        _has_any_large = true;
                }
                else
                {
                    _last_render_id++;
                    sdf.RenderId = _last_render_id;

                    int index;

                    if (_available_indices.Count > 0)
                    {
                        var i = _available_indices.Count - 1;
                        index = _available_indices[i];
                        _available_indices.RemoveAt(i);

                        _index_to_render_id[index] = sdf.RenderId;
                        _transforms[index] = transform;
                        _scales[index] = scale;
                        _asset_list[index] = asset;
                        _large_mapping[index] = false;
                        _hash_ids[index] = ++_last_hash_id;
                    }
                    else
                    {
                        index = _index_to_render_id.Count;
                        _index_to_render_id.Add(sdf.RenderId);
                        _transforms.Add(transform);
                        _scales.Add(scale);
                        _asset_list.Add(asset);
                        _hash_ids.Add(++_last_hash_id);
                        _large_mapping.Add(false);
                    }

                    updateTransform(index, (uint)asset, _hash_ids[index], transform, scale);

                    _render_ids_in_use.Add(sdf.RenderId);
                    _render_id_to_index[sdf.RenderId] = index;

                    _requests_dirty = true;

                    if (_large_mapping[index])
                        _has_any_large = true;
                }
            }

            //

            foreach (var buffer in _buffers_to_release)
                buffer.Release();

            _buffers_to_release.Clear();

            foreach (var id in _id_to_asset.Keys)
                if (!_ids_in_use.Contains(id))
                    _asset_removals.Add(_id_to_asset[id]);

            foreach (var id in _render_id_to_index.Keys)
                if (!_render_ids_in_use.Contains(id))
                    _instance_removals.Add(_render_id_to_index[id]);

            CommandBuffer cmd = null;

            if (!_is_initialized)
            {
                ensure(ref cmd);

                _kreset.Seti(cmd, P.BrickCounts, Defines.ATLAS_DIM / Defines.BRICK_DIM, Defines.ATLAS_DIM / Defines.BRICK_DIM, Defines.ATLAS_DIM / Defines.BRICK_DIM);

                _kreset.Bind(cmd, P.PoolRW, _brick_pool);
                _kreset.DispatchEnoughFor(cmd, Defines.MAX_BRICKS, 1, 1);

                _is_initialized = true;
            }

            foreach (var asset in _asset_removals)
            {
                ensure(ref cmd);

                var actual_brick_count = _actual_brick_counts[asset];
                var virtual_brick_count = _virtual_brick_counts[asset];
                var brick_slice = _brick_slices[asset];

                var mapping = _mappings[asset];
                _mappings[asset] = null; // for GC

                var mapping_buffer = new ComputeBuffer(mapping.Length, 4);
                mapping_buffer.SetData(mapping);

                _buffers_to_release.Add(mapping_buffer);

                {
                    _brick_pool_cursor -= actual_brick_count;

                    _kbricks_backward.Seti(cmd, P.Count, virtual_brick_count);
                    _kbricks_backward.Seti(cmd, P.InputBase, brick_slice.Start);
                    _kbricks_backward.Seti(cmd, P.OutputBase, _brick_pool_cursor);
                    _kbricks_backward.Bind(cmd, P.Mapping, mapping_buffer);

                    _kbricks_backward.Bind(cmd, P.Input, _bricks);
                    _kbricks_backward.Bind(cmd, P.OutputRW, _brick_pool);

                    _kbricks_backward.DispatchEnoughFor(cmd, virtual_brick_count);
                }

                _available_brick_slices.Add(_brick_slices[asset]);
                _available_asset_indices.Add(asset);

                var id = _asset_to_id[asset];

                if (id != null)
                {
                    _id_to_asset.Remove(id);
                    _ids_in_use.Remove(id);
                }

                _asset_to_entry[asset] = null;
                _asset_to_id[asset] = null;
            }

            _asset_removals.Clear();

            foreach (var index in _instance_removals)
            {
                _index_list.Add(index);
                _value_list.Add(new Entry(Defines.INVALID_ID, Defines.INVALID_ID, Matrix4x4.identity, Vector3.zero));

                _has_edits = true;
            }

            _instance_removals.Clear();

            foreach (var id in _new_ids)
            {
                if (!_id_to_asset.ContainsKey(id))
                    continue;

                var asset = _id_to_asset[id];
                var sdf = _asset_to_entry[asset];

                var virtual_brick_count = sdf.VirtualBrickCount;
                var actual_brick_count = sdf.ActualBrickCount;
                _actual_brick_counts[asset] = actual_brick_count;
                _virtual_brick_counts[asset] = virtual_brick_count;

                var use_allocation = sdf.Version > 2;

                if (sdf.Allocation == null)
                {
                    use_allocation = false;
                    actual_brick_count = virtual_brick_count;
                }

                if (virtual_brick_count == 0)
                    continue;

                var smallest_bigger_slice = -1;
                var used_slice = -1;

                for (int i = 0; i < _available_brick_slices.Count; i++)
                {
                    var count = _available_brick_slices[i].Count;

                    if (count == virtual_brick_count)
                    {
                        used_slice = i;
                        break;
                    }
                    else if (count > virtual_brick_count)
                    {
                        if (smallest_bigger_slice < 0)
                            smallest_bigger_slice = i;
                        else if (count < _available_brick_slices[smallest_bigger_slice].Count)
                            smallest_bigger_slice = i;
                    }
                }

                if (used_slice < 0 && smallest_bigger_slice >= 0)
                    used_slice = smallest_bigger_slice;

                // brick slice refers to the mapping in asset data, not the allocated list
                var brick_slice = new BrickSlice();

                if (used_slice >= 0)
                {
                    brick_slice = _available_brick_slices[used_slice];
                    _available_brick_slices.RemoveAt(used_slice);
                }
                else
                {
                    brick_slice.Start = _brick_slice_cursor;
                    brick_slice.Count = virtual_brick_count;
                    _brick_slice_cursor += virtual_brick_count;
                }

                _brick_slices[asset] = brick_slice;

                ensure(ref cmd);

                {
                    var brick_size = new Vector3((sdf.Fit.Bounds.max.x - sdf.Fit.Bounds.min.x) / sdf.Fit.Bricks.x,
                                                 (sdf.Fit.Bounds.max.y - sdf.Fit.Bounds.min.y) / sdf.Fit.Bricks.y,
                                                 (sdf.Fit.Bounds.max.z - sdf.Fit.Bounds.min.z) / sdf.Fit.Bricks.z);
                    var empty_step = 0.4f * (brick_size.x / 3 + brick_size.y / 3 + brick_size.z / 3);

                    _kasset.Bind(cmd, P.AssetsRW, _assets);

                    _kasset.Seti(cmd, P.Asset, asset);
                    _kasset.Seti(cmd, P.BaseBrick, brick_slice.Start);
                    _kasset.Seti(cmd, P.BrickCounts, sdf.Fit.Bricks.x, sdf.Fit.Bricks.y, sdf.Fit.Bricks.z);
                    _kasset.Setf(cmd, P.EmptyStep, empty_step);
                    _kasset.Setf(cmd, P.Bias, sdf.Fit.Bias);
                    _kasset.Setf(cmd, P.ValueScale, sdf.Fit.ValueScale);
                    _kasset.Setf(cmd, P.VolumeMin, sdf.Fit.Bounds.min.x, sdf.Fit.Bounds.min.y, sdf.Fit.Bounds.min.z);
                    _kasset.Setf(cmd, P.VolumeMax, sdf.Fit.Bounds.max.x, sdf.Fit.Bounds.max.y, sdf.Fit.Bounds.max.z);

                    _kasset.DispatchEnoughFor(cmd, 1, 1, 1);
                }

                uint[] mapping;

                if (use_allocation)
                    mapping = sdf.Allocation;
                else
                {
                    mapping = new uint[virtual_brick_count];

                    for (int i = 0; i < virtual_brick_count; i++)
                        mapping[i] = (uint)i;
                }

                _mappings[asset] = mapping;

                var mapping_buffer = new ComputeBuffer(mapping.Length, 4);
                mapping_buffer.SetData(mapping);

                _buffers_to_release.Add(mapping_buffer);

                {
                    _kbricks_forward.Seti(cmd, P.Count, virtual_brick_count);
                    _kbricks_forward.Seti(cmd, P.InputBase, _brick_pool_cursor);
                    _kbricks_forward.Seti(cmd, P.OutputBase, brick_slice.Start);

                    _kbricks_forward.Bind(cmd, P.Mapping, mapping_buffer);
                    _kbricks_forward.Bind(cmd, P.Input, _brick_pool);
                    _kbricks_forward.Bind(cmd, P.OutputRW, _bricks);

                    _kbricks_forward.DispatchEnoughFor(cmd, virtual_brick_count);

                    _brick_pool_cursor += actual_brick_count;
                }

                var buffer_x = sdf.Fit.Bricks.x * Defines.BRICK_DIM;
                var buffer_y = sdf.Fit.Bricks.y * Defines.BRICK_DIM;
                var buffer_z = sdf.Fit.Bricks.z * Defines.BRICK_DIM;

                {
                    var buffer = new ComputeBuffer(sdf.SDF.Length / 4, 4);
                    buffer.SetData(sdf.SDF);

                    _buffers_to_release.Add(buffer);

                    _kcopy_sdf_bricks.Setf(cmd, P.ValueScale, sdf.Fit.ValueScale);
                    _kcopy_sdf_bricks.Seti(cmd, P.BaseBrick, brick_slice.Start);
                    _kcopy_sdf_bricks.Seti(cmd, P.BrickCounts, sdf.Fit.Bricks.x, sdf.Fit.Bricks.y, sdf.Fit.Bricks.z);
                    _kcopy_sdf_bricks.Seti(cmd, P.Resolution, buffer_x, buffer_y, buffer_z);

                    _kcopy_sdf_bricks.Bind(cmd, P.Bricks, _bricks);
                    _kcopy_sdf_bricks.Bind(cmd, P.Volume, buffer);

                    _kcopy_sdf_bricks.BindOnce(cmd, P.VolumeRW, _sdf_volume);

                    _kcopy_sdf_bricks.Dispatch(cmd, sdf.Fit.Bricks.x, sdf.Fit.Bricks.y, sdf.Fit.Bricks.z);
                }

                {
                    var buffer = new ComputeBuffer(sdf.Voxels.Length / 4, 4);
                    buffer.SetData(sdf.Voxels);

                    _buffers_to_release.Add(buffer);

                    _kcopy_voxel_bricks.Seti(cmd, P.BaseBrick, brick_slice.Start);
                    _kcopy_voxel_bricks.Seti(cmd, P.BrickCounts, sdf.Fit.Bricks.x, sdf.Fit.Bricks.y, sdf.Fit.Bricks.z);
                    _kcopy_voxel_bricks.Seti(cmd, P.Resolution, buffer_x, buffer_y, buffer_z);

                    _kcopy_voxel_bricks.Bind(cmd, P.Bricks, _bricks);
                    _kcopy_voxel_bricks.Bind(cmd, P.Volume, buffer);

                    _kcopy_voxel_bricks.BindOnce(cmd, P.VolumeRW, _voxel_volume);

                    _kcopy_voxel_bricks.Dispatch(cmd, sdf.Fit.Bricks.x, sdf.Fit.Bricks.y, sdf.Fit.Bricks.z);
                }
            }

            _new_ids.Clear();
            _ids_in_use.Clear();

            //

            for (int i = 0; i < _index_to_render_id.Count; i++)
            {
                var render_id = _index_to_render_id[i];

                if (render_id <= 0)
                    continue;

                if (!_render_ids_in_use.Contains(render_id))
                    _indices_to_remove.Add(i);
            }

            if (_indices_to_remove.Count > 0)
                _requests_dirty = true;

            foreach (var index in _indices_to_remove)
            {
                _index_to_render_id[index] = -1;

                updateTransform(index, Defines.INVALID_ID, Defines.INVALID_ID, Matrix4x4.identity, Vector3.one);
            }

            _indices_to_remove.Clear();
            _render_ids_in_use.Clear();

            if (_has_edits)
            {
                _edit_indices.SetData(_index_list);
                _edit_values.SetData(_value_list);

                ensure(ref cmd);

                _kedit.Seti(cmd, P.Count, _index_list.Count);
                _kedit.Bind(cmd, P.Edits, _edit_indices);
                _kedit.Bind(cmd, P.Values, _edit_values);
                _kedit.Bind(cmd, P.BufferRW, _buffer);
                _kedit.DispatchEnoughFor(cmd, _index_list.Count);

                _index_list.Clear();
                _value_list.Clear();

                _has_edits = false;
            }

            if (_requests_dirty)
            {
                ensure(ref cmd);

                //

                _krequest_clear.Bind(cmd, P.RequestsRW, _requests);
                _krequest_clear.Bind(cmd, P.LargeRW, _large);
                _krequest_clear.DispatchOne(cmd);

                //

                _krequest.Setf(cmd, P.Influence, GRID_INFLUENCE);
                _krequest.Seti(cmd, P.DFInstanceCount, Cursor);

                _krequest.Seti(cmd, P.Limit, LARGE_LIMIT);

                _krequest.Bind(cmd, P.DFInstances, Buffer);
                _krequest.Bind(cmd, P.SDFAssets, _assets);

                _krequest.Seti(cmd, P.Count, Cursor);

                _krequest.Bind(cmd, P.RequestsRW, _requests);
                _krequest.Bind(cmd, P.LargeRW, _large);

                _krequest.DispatchEnoughFor(cmd, Cursor);

                //

                _requests_dirty = false;
            }

            if (cmd != null)
            {
                executor.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        private void ensure(ref CommandBuffer cmd)
        {
            if (cmd != null)
                return;

            cmd = CommandBufferPool.Get("SDF Update");
            cmd.Clear();
        }

        private Vector3[] _corners = new Vector3[8];

        private void updateTransform(int index, uint asset, uint hash_id, Matrix4x4 value, Vector3 scale)
        {
            if (asset >= _asset_to_entry.Count)
                return;

            var sdf = _asset_to_entry[(int)asset];

            var scale_offset = new Vector3(scale.x / sdf.Scale.x,
                                           scale.y / sdf.Scale.y,
                                           scale.z / sdf.Scale.z);

            if (Mathf.Abs(scale.x - scale_offset.x) > 0.001f ||
                Mathf.Abs(scale.y - scale_offset.y) > 0.001f ||
                Mathf.Abs(scale.z - scale_offset.z) > 0.001f)
            {
                value = value *
                        Matrix4x4.Scale(new Vector3(scale_offset.x / scale.x,
                                                    scale_offset.y / scale.y,
                                                    scale_offset.z / scale.z));
            }

            _index_list.Add(index);
            _value_list.Add(new Entry(asset, hash_id, value, scale_offset));

            _has_edits = true;

            {
                var local_min = sdf.Fit.Bounds.min;
                var local_max = sdf.Fit.Bounds.max;

                _corners[0] = value.MultiplyPoint3x4(new Vector3(local_min.x, local_min.y, local_min.z));
                _corners[1] = value.MultiplyPoint3x4(new Vector3(local_max.x, local_min.y, local_min.z));
                _corners[2] = value.MultiplyPoint3x4(new Vector3(local_min.x, local_min.y, local_max.z));
                _corners[3] = value.MultiplyPoint3x4(new Vector3(local_max.x, local_min.y, local_max.z));
                _corners[4] = value.MultiplyPoint3x4(new Vector3(local_min.x, local_max.y, local_min.z));
                _corners[5] = value.MultiplyPoint3x4(new Vector3(local_max.x, local_max.y, local_min.z));
                _corners[6] = value.MultiplyPoint3x4(new Vector3(local_min.x, local_max.y, local_max.z));
                _corners[7] = value.MultiplyPoint3x4(new Vector3(local_max.x, local_max.y, local_max.z));

                var aabb_min = _corners[0];
                var aabb_max = _corners[0];

                for (uint corner_id = 1; corner_id < 8; corner_id++)
                {
                    aabb_min.x = Mathf.Min(_corners[corner_id].x, aabb_min.x);
                    aabb_min.y = Mathf.Min(_corners[corner_id].y, aabb_min.y);
                    aabb_min.z = Mathf.Min(_corners[corner_id].z, aabb_min.z);

                    aabb_max.x = Mathf.Max(_corners[corner_id].x, aabb_max.x);
                    aabb_max.y = Mathf.Max(_corners[corner_id].y, aabb_max.y);
                    aabb_max.z = Mathf.Max(_corners[corner_id].z, aabb_max.z);
                }

                aabb_min.x -= GRID_INFLUENCE;
                aabb_min.y -= GRID_INFLUENCE;
                aabb_min.z -= GRID_INFLUENCE;

                aabb_max.x += GRID_INFLUENCE;
                aabb_max.y += GRID_INFLUENCE;
                aabb_max.z += GRID_INFLUENCE;

                var size_x = Mathf.Ceil((aabb_max.x - aabb_min.x) / Defines.GDF_CELL_DIM) + 1;
                var size_y = Mathf.Ceil((aabb_max.y - aabb_min.y) / Defines.GDF_CELL_DIM) + 1;
                var size_z = Mathf.Ceil((aabb_max.z - aabb_min.z) / Defines.GDF_CELL_DIM) + 1;

                var count = size_x * size_y * size_z;
                var large = count >= LARGE_LIMIT;

                _large_mapping[index] = large;
            }
        }
    }
}