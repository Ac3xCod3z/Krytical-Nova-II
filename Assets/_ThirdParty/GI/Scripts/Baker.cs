using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static UnityEngine.EventSystems.EventTrigger;

namespace GI
{
    public class Baker
    {
        public static Baker Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new Baker();

                return _instance;
            }
        }

        private static Baker _instance;

        private ComputeBuffer _sdf_positive;

        private ComputeBuffer _sdf_generate1;
        private ComputeBuffer _sdf_generate2;

        private ComputeBuffer _voxel_generate1;
        private ComputeBuffer _voxel_generate2;

        private ComputeBuffer _sdf_packed;
        private ComputeBuffer _voxel_packed;

        private ComputeBuffer _voxel_splat;
        private ComputeBuffer _voxel_mark;

        private ComputeBuffer _voxel_mark1;
        private ComputeBuffer _voxel_mark2;

        private ComputeBuffer _mapping;

        private List<Kernel> _voxel_splats = new List<Kernel>();

        private uint[] _mapping_data;

        private Kernel _kclear_sdf;
        private Kernel _ksplat_sdf;
        private Kernel _kcombine_sdf;
        private Kernel _kflood_sdf;
        private Kernel _kpack_sdf;
        private Kernel _kpack_clear_sdf;

        private Kernel _kclear_voxel;
        private Kernel _ksplat_voxel;
        private Kernel _ksplat_voxel_uv;
        private Kernel _kflood_voxel;
        private Kernel _kpack_voxel;
        private Kernel _ksum_voxel;

        private Vector3[] _bounds = new Vector3[8];

        public Baker()
        {
            var d = Defines.MAX_ASSET_DIM + 16;

            _sdf_positive = new ComputeBuffer(d * d * d, 4, ComputeBufferType.Raw);

            _sdf_generate1 = new ComputeBuffer(d * d * d, 4, ComputeBufferType.Raw);
            _sdf_generate2 = new ComputeBuffer(d * d * d, 4, ComputeBufferType.Raw);

            _voxel_generate1 = new ComputeBuffer(d * d * d, 4, ComputeBufferType.Raw);
            _voxel_generate2 = new ComputeBuffer(d * d * d, 4, ComputeBufferType.Raw);

            _voxel_mark1 = new ComputeBuffer(d * d * d, 4, ComputeBufferType.Raw);
            _voxel_mark2 = new ComputeBuffer(d * d * d, 4, ComputeBufferType.Raw);

            _voxel_splat = new ComputeBuffer(d * d * d, 16, ComputeBufferType.Raw);
            _voxel_mark = new ComputeBuffer(d * d * d, 4, ComputeBufferType.Raw);

            _sdf_packed = new ComputeBuffer(d * d * d, 4, ComputeBufferType.Raw);
            _voxel_packed = new ComputeBuffer(d * d * d, 4, ComputeBufferType.Raw);

            var b = Defines.MAX_ASSET_DIM / 4;

            _mapping = new ComputeBuffer(b * b * b, 4, ComputeBufferType.Raw);
            _mapping_data = new uint[b * b * b];

            _kclear_sdf = new Kernel(Resources.Load<ComputeShader>("Shaders/Generate/SDF/Clear"), "Main");
            _ksplat_sdf = new Kernel(Resources.Load<ComputeShader>("Shaders/Generate/SDF/Splat"), "Main");
            _kcombine_sdf = new Kernel(Resources.Load<ComputeShader>("Shaders/Generate/SDF/Combine"), "Main");
            _kflood_sdf = new Kernel(Resources.Load<ComputeShader>("Shaders/Generate/SDF/Flood"), "Main");
            _kpack_sdf = new Kernel(Resources.Load<ComputeShader>("Shaders/Generate/SDF/Pack"), "Main");
            _kpack_clear_sdf = new Kernel(Resources.Load<ComputeShader>("Shaders/Generate/SDF/Pack"), "Clear");

            _kclear_voxel = new Kernel(Resources.Load<ComputeShader>("Shaders/Generate/Voxels/Clear"), "Main");
            _ksplat_voxel = new Kernel(Resources.Load<ComputeShader>("Shaders/Generate/Voxels/Splat"), "Main");
            _ksplat_voxel_uv = new Kernel(Resources.Load<ComputeShader>("Shaders/Generate/Voxels/Splat"), "MainUV");
            _kflood_voxel = new Kernel(Resources.Load<ComputeShader>("Shaders/Generate/Voxels/Flood"), "Main");
            _kpack_voxel = new Kernel(Resources.Load<ComputeShader>("Shaders/Generate/Voxels/Pack"), "Main");
            _ksum_voxel = new Kernel(Resources.Load<ComputeShader>("Shaders/Generate/Voxels/Sum"), "Main");
        }

        ~Baker()
        {
            if (_sdf_positive != null)
            {
                _sdf_positive.Release();
                _sdf_positive = null;
            }

            if (_sdf_generate1 != null)
            {
                _sdf_generate1.Release();
                _sdf_generate1 = null;
            }

            if (_sdf_generate2 != null)
            {
                _sdf_generate2.Release();
                _sdf_generate2 = null;
            }

            if (_voxel_generate1 != null)
            {
                _voxel_generate1.Release();
                _voxel_generate1 = null;
            }

            if (_voxel_generate2 != null)
            {
                _voxel_generate2.Release();
                _voxel_generate2 = null;
            }

            if (_sdf_packed != null)
            {
                _sdf_packed.Release();
                _sdf_packed = null;
            }

            if (_voxel_packed != null)
            {
                _voxel_packed.Release();
                _voxel_packed = null;
            }

            if (_voxel_splat != null)
            {
                _voxel_splat.Release();
                _voxel_splat = null;
            }

            if (_voxel_mark != null)
            {
                _voxel_mark.Release();
                _voxel_mark = null;
            }

            if (_voxel_mark1 != null)
            {
                _voxel_mark1.Release();
                _voxel_mark1 = null;
            }

            if (_voxel_mark2 != null)
            {
                _voxel_mark2.Release();
                _voxel_mark2 = null;
            }

            if (_mapping != null)
            {
                _mapping.Release();
                _mapping = null;
            }
        }

        public SDFEntry Bake(Mesh mesh, Material material, int resolution, float bias, bool force_double_sided)
        {
            var entries = new MeshEntry[]{
                new MeshEntry(
                    mesh,
                    material,
                    Matrix4x4.identity)
            };

            return Bake(entries, mesh.bounds, Vector3.one, resolution, bias, force_double_sided);
        }

        public SDFEntry Bake(SDF sdf)
        {
            sdf.UpdateEntries();
            sdf.Entry = Bake(sdf.Meshes, sdf.BaseBounds, sdf.transform.localScale, sdf.Resolution, sdf.Bias, sdf.ForceDoubleSided);

            return sdf.Entry;
        }

        
        public SDFEntry Bake(IEnumerable<MeshEntry> entries, Bounds base_bounds, Vector3 local_scale, int resolution, float bias, bool force_double_sided)
        {
            var abs_scale = new Vector3(Mathf.Abs(local_scale.x),
                                        Mathf.Abs(local_scale.y),
                                        Mathf.Abs(local_scale.z));

            var min_axis = Mathf.Min(abs_scale.x, abs_scale.y, abs_scale.z);
            var max_axis = Mathf.Max(abs_scale.x, abs_scale.y, abs_scale.z);
            var apply_scale = max_axis > (min_axis * 2);

            var sdf_entry = ScriptableObject.CreateInstance<SDFEntry>();
            sdf_entry.HasBuiltInfo = true;
            sdf_entry.BuiltResolution = resolution;
            sdf_entry.BuiltForceDoubleSided = force_double_sided;

            var scale_transform = Matrix4x4.identity;

            if (apply_scale)
            {
                scale_transform = Matrix4x4.Scale(abs_scale);

                var min = base_bounds.min;
                var max = base_bounds.max;
                _bounds[0] = new Vector3(min.x, min.y, min.z);
                _bounds[1] = new Vector3(max.x, min.y, min.z);
                _bounds[2] = new Vector3(min.x, max.y, min.z);
                _bounds[3] = new Vector3(max.x, max.y, min.z);
                _bounds[4] = new Vector3(min.x, min.y, max.z);
                _bounds[5] = new Vector3(max.x, min.y, max.z);
                _bounds[6] = new Vector3(min.x, max.y, max.z);
                _bounds[7] = new Vector3(max.x, max.y, max.z);

                var bounds = GeometryUtility.CalculateBounds(_bounds, scale_transform);
                sdf_entry.Fit = Fit.Find(bounds, resolution, bias);

                sdf_entry.Scale = local_scale;
            }
            else
                sdf_entry.Fit = Fit.Find(base_bounds, resolution, bias);

            sdf_entry.ID = System.Guid.NewGuid().ToString();

            var buffer_x = sdf_entry.Fit.Bricks.x * Defines.BRICK_DIM;
            var buffer_y = sdf_entry.Fit.Bricks.y * Defines.BRICK_DIM;
            var buffer_z = sdf_entry.Fit.Bricks.z * Defines.BRICK_DIM;

            var brick_count = sdf_entry.Fit.Bricks.x * sdf_entry.Fit.Bricks.y * sdf_entry.Fit.Bricks.z;

            var sdf_bytes = buffer_x * buffer_y * buffer_z;

            if (sdf_bytes % 4 > 0)
                sdf_bytes += 4 - sdf_bytes % 4;

            var splat_dim = sdf_entry.Fit.Splat;
            var splat_bounds = sdf_entry.Fit.SplatBounds;

            float max_bound;
            int max_resolution;

            if (splat_bounds.size.x > splat_bounds.size.y && splat_bounds.size.x > splat_bounds.size.z)
            {
                max_bound = splat_bounds.size.x;
                max_resolution = splat_dim.x;
            }
            else if (splat_bounds.size.y > splat_bounds.size.z)
            {
                max_bound = splat_bounds.size.y;
                max_resolution = splat_dim.y;
            }
            else
            {
                max_bound = splat_bounds.size.z;
                max_resolution = splat_dim.z;
            }

            var cell_size = max_bound / max_resolution;
            var splat_threshold = cell_size * 2;
            var triangle_margin = cell_size * 2;
            var empty_step = Defines.BRICK_DIM * cell_size * 0.4f;
            var brick_threshold = cell_size * 4;

            _voxel_splats.Clear(); // unfortunately during recompile and so on values here get null so nee

            var cmd = CommandBufferPool.Get("SDF Bake");
            cmd.Clear();

            // SDF

            {
                _kclear_sdf.Bind(cmd, P.PositiveRW, _sdf_positive);
                _kclear_sdf.Bind(cmd, P.BufferRW, _sdf_generate1);
                _kclear_sdf.Bind(cmd, P.MappingRW, _mapping);
                _kclear_sdf.Seti(cmd, P.BrickCount, brick_count);
                _kclear_sdf.Seti(cmd, P.Resolution, splat_dim.x, splat_dim.y, splat_dim.z);
                _kclear_sdf.Setf(cmd, P.Distance, max_bound);

                _kclear_sdf.DispatchEnoughFor(cmd, splat_dim.x, splat_dim.y, splat_dim.z);
            }

            foreach (var entry in entries)
            {
                var mesh = entry.Mesh;

                if (mesh == null)
                    continue;

                var material = entry.Material;

                if (material == null)
                    continue;

                if (mesh.GetTopology(0) != MeshTopology.Triangles)
                    continue;

                if (mesh.indexFormat != IndexFormat.UInt16)
                    continue;

                int positions = mesh.GetVertexAttributeStream(VertexAttribute.Position);

                if (positions < 0)
                    continue;

                mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;

                var indices = mesh.GetIndexBuffer();
                var triangle_count = indices.count / 3;

                var vertices = mesh.GetVertexBuffer(positions);

                _ksplat_sdf.Setf(cmd, P.DoubleSided, (material.doubleSidedGI || force_double_sided) ? 1 : 0);
                _ksplat_sdf.Setf(cmd, P.BoundOrigin, splat_bounds.min.x, splat_bounds.min.y, splat_bounds.min.z);
                _ksplat_sdf.Setf(cmd, P.BoundSize, splat_bounds.size.x, splat_bounds.size.y, splat_bounds.size.z);
                _ksplat_sdf.Setf(cmd, P.SplatThreshold, splat_threshold);
                _ksplat_sdf.Seti(cmd, P.Resolution, splat_dim.x, splat_dim.y, splat_dim.z);
                _ksplat_sdf.Bind(cmd, P.VertexBuffer, vertices);
                _ksplat_sdf.Bind(cmd, P.IndexBuffer, indices);
                _ksplat_sdf.Seti(cmd, P.TriangleCount, triangle_count);
                _ksplat_sdf.Setf(cmd, P.TriangleMargin, triangle_margin);
                _ksplat_sdf.Seti(cmd, P.VertexStride, mesh.GetVertexBufferStride(positions));
                _ksplat_sdf.Seti(cmd, P.PositionOffset, mesh.GetVertexAttributeOffset(VertexAttribute.Position));
                _ksplat_sdf.Set(cmd, P.Transform, scale_transform * entry.Transform);
                _ksplat_sdf.Bind(cmd, P.PositiveRW, _sdf_positive);
                _ksplat_sdf.Bind(cmd, P.BufferRW, _sdf_generate1);

                _ksplat_sdf.DispatchEnoughFor(cmd, triangle_count);
            }

            {
                _kcombine_sdf.Seti(cmd, P.Resolution, splat_dim.x, splat_dim.y, splat_dim.z);
                _kcombine_sdf.Setf(cmd, P.Distance, max_bound);
                _kcombine_sdf.Setf(cmd, P.SplatThreshold, splat_threshold);
                _kcombine_sdf.Bind(cmd, P.Positive, _sdf_positive);
                _kcombine_sdf.Bind(cmd, P.BufferRW, _sdf_generate1);

                _kcombine_sdf.DispatchEnoughFor(cmd, splat_dim.x, splat_dim.y, splat_dim.z);
            }

            {
                var v0 = _sdf_generate1;
                var v1 = _sdf_generate2;

                var iterations = max_resolution;

                _kflood_sdf.Setf(cmd, P.MaxDistance, max_bound - 0.001f);
                _kflood_sdf.Seti(cmd, P.Resolution, splat_dim.x, splat_dim.y, splat_dim.z);
                _kflood_sdf.Setf(cmd, P.Step, max_bound / max_resolution);

                for (int i = 0; i < iterations; i++)
                {
                    _kflood_sdf.Bind(cmd, P.Input, v0);
                    _kflood_sdf.Bind(cmd, P.BufferRW, v1);

                    _kflood_sdf.DispatchEnoughFor(cmd, splat_dim.x, splat_dim.y, splat_dim.z);

                    var t = v0;
                    v0 = v1;
                    v1 = t;
                }

                var clear_count = sdf_bytes / 4;

                _kpack_clear_sdf.Seti(cmd, P.ClearCount, clear_count);
                _kpack_clear_sdf.Bind(cmd, P.VolumeRW, _sdf_packed);

                _kpack_clear_sdf.DispatchEnoughFor(cmd, clear_count);

                _kpack_sdf.Seti(cmd, P.InputResolution, splat_dim.x, splat_dim.y, splat_dim.z);
                _kpack_sdf.Seti(cmd, P.OutputResolution, buffer_x, buffer_y, buffer_z);
                _kpack_sdf.Setf(cmd, P.ValueScale, sdf_entry.Fit.ValueScale);
                _kpack_sdf.Setf(cmd, P.EmptyStep, empty_step);
                _kpack_sdf.Bind(cmd, P.Splat, v0);
                _kpack_sdf.Bind(cmd, P.VolumeRW, _sdf_packed);

                _kpack_sdf.Setf(cmd, P.BrickThreshold, brick_threshold);
                _kpack_sdf.Seti(cmd, P.BrickCounts, sdf_entry.Fit.Bricks.x, sdf_entry.Fit.Bricks.y, sdf_entry.Fit.Bricks.z);
                _kpack_sdf.Bind(cmd, P.MappingRW, _mapping);

                _kpack_sdf.Dispatch(cmd, sdf_entry.Fit.Bricks.x, sdf_entry.Fit.Bricks.y, sdf_entry.Fit.Bricks.z);
            }

            // Voxels

            {
                _kclear_voxel.Seti(cmd, P.Resolution, splat_dim.x, splat_dim.y, splat_dim.z);
                _kclear_voxel.Bind(cmd, P.BufferRW, _voxel_splat);
                _kclear_voxel.Bind(cmd, P.MarkRW, _voxel_mark);

                _kclear_voxel.DispatchEnoughFor(cmd, splat_dim.x, splat_dim.y, splat_dim.z);
            }

            var uv_splat_index = 0;

            foreach (var entry in entries)
            {
                var mesh = entry.Mesh;

                if (mesh == null)
                    continue;

                var material = entry.Material;

                if (material == null)
                    continue;

                if (mesh.GetTopology(0) != MeshTopology.Triangles)
                    continue;

                if (mesh.indexFormat != IndexFormat.UInt16)
                    continue;

                int positions = mesh.GetVertexAttributeStream(VertexAttribute.Position);

                if (positions < 0)
                    continue;

                mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;

                var indices = mesh.GetIndexBuffer();
                var triangle_count = indices.count / 3;

                var vertices = mesh.GetVertexBuffer(positions);

                float threshold = 2 * Mathf.Max(splat_bounds.size.x / splat_dim.x, splat_bounds.size.y / splat_dim.y, splat_bounds.size.z / splat_dim.z);

                Color color = Color.white;
                Texture texture = null;
                Texture emission_texture = null;
                Color emission_color = Color.black;

                if (material.HasColor("_Color"))
                    color = material.GetColor("_Color");
                else if (material.HasColor("_MainColor"))
                    color = material.GetColor("_MainColor");

                if (material.HasTexture("_MainTexture"))
                    texture = material.GetTexture("_MainTexture");
                else if (material.HasTexture("_MainTex"))
                    texture = material.GetTexture("_MainTex");
                else if (material.HasTexture("_AlbedoMap"))
                    texture = material.GetTexture("_AlbedoMap");
                else if (material.HasTexture("_BaseMap"))
                    texture = material.GetTexture("_BaseMap");

                if (material.HasTexture("_EmissionMap"))
                    emission_texture = material.GetTexture("_EmissionMap");

                if (material.HasColor("_EmissionColor"))
                    emission_color = material.GetColor("_EmissionColor");

                int uvs = mesh.GetVertexAttributeStream(VertexAttribute.TexCoord0);

                if (uvs >= 0 && (texture != null || emission_texture != null))
                {
                    if (texture == null)
                        texture = Texture2D.whiteTexture;

                    if (emission_texture == null)
                        emission_texture = Texture2D.whiteTexture; // multiplied by color, so likely ends up being black anyway

                    var uv_buffer = mesh.GetVertexBuffer(uvs);

                    Kernel kernel;

                    if (uv_splat_index == 0)
                        kernel = _ksplat_voxel_uv;
                    else
                    {
                        var i = uv_splat_index - 1;

                        while (i >= _voxel_splats.Count)
                            _voxel_splats.Add(_ksplat_voxel_uv.Copy());

                        kernel = _voxel_splats[i];
                    }

                    kernel.Setf(cmd, P.DoubleSided, material.doubleSidedGI ? 1 : 0);
                    kernel.Setf(cmd, P.BoundOrigin, splat_bounds.min.x, splat_bounds.min.y, splat_bounds.min.z);
                    kernel.Setf(cmd, P.BoundSize, splat_bounds.size.x, splat_bounds.size.y, splat_bounds.size.z);
                    kernel.Seti(cmd, P.Resolution, splat_dim.x, splat_dim.y, splat_dim.z);
                    kernel.Bind(cmd, P.VertexBuffer, vertices);
                    kernel.Bind(cmd, P.IndexBuffer, indices);
                    kernel.Seti(cmd, P.TriangleCount, triangle_count);
                    kernel.Setf(cmd, P.TriangleMargin, triangle_margin);
                    kernel.Seti(cmd, P.VertexStride, mesh.GetVertexBufferStride(positions));
                    kernel.Seti(cmd, P.PositionOffset, mesh.GetVertexAttributeOffset(VertexAttribute.Position));
                    kernel.Setf(cmd, P.SplatThreshold, threshold);
                    kernel.Setf(cmd, P.MaterialColor, color.r, color.g, color.b);
                    kernel.BindOnce(cmd, P.MaterialTexture, texture);
                    kernel.Setf(cmd, P.EmissionColor, emission_color.r, emission_color.g, emission_color.b);
                    kernel.BindOnce(cmd, P.EmissionMap, emission_texture);
                    kernel.Bind(cmd, P.BufferRW, _voxel_splat);
                    kernel.Bind(cmd, P.MarkRW, _voxel_mark);
                    kernel.Set(cmd, P.Transform, scale_transform * entry.Transform);

                    kernel.Bind(cmd, P.UVBuffer, uv_buffer);
                    kernel.Seti(cmd, P.UVStride, mesh.GetVertexBufferStride(uvs));
                    kernel.Seti(cmd, P.UVOffset, mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0));

                    kernel.DispatchEnoughFor(cmd, triangle_count);

                    uv_splat_index++;
                }
                else
                {
                    _ksplat_voxel.Setf(cmd, P.DoubleSided, material.doubleSidedGI ? 1 : 0);
                    _ksplat_voxel.Setf(cmd, P.BoundOrigin, splat_bounds.min.x, splat_bounds.min.y, splat_bounds.min.z);
                    _ksplat_voxel.Setf(cmd, P.BoundSize, splat_bounds.size.x, splat_bounds.size.y, splat_bounds.size.z);
                    _ksplat_voxel.Seti(cmd, P.Resolution, splat_dim.x, splat_dim.y, splat_dim.z);
                    _ksplat_voxel.Bind(cmd, P.VertexBuffer, vertices);
                    _ksplat_voxel.Bind(cmd, P.IndexBuffer, indices);
                    _ksplat_voxel.Seti(cmd, P.TriangleCount, triangle_count);
                    _ksplat_voxel.Setf(cmd, P.TriangleMargin, triangle_margin);
                    _ksplat_voxel.Seti(cmd, P.VertexStride, mesh.GetVertexBufferStride(positions));
                    _ksplat_voxel.Seti(cmd, P.PositionOffset, mesh.GetVertexAttributeOffset(VertexAttribute.Position));
                    _ksplat_voxel.Setf(cmd, P.SplatThreshold, threshold);
                    _ksplat_voxel.Setf(cmd, P.MaterialColor, color.r, color.g, color.b);
                    _ksplat_voxel.Setf(cmd, P.EmissionColor, emission_color.r, emission_color.g, emission_color.b);
                    _ksplat_voxel.Bind(cmd, P.BufferRW, _voxel_splat);
                    _ksplat_voxel.Bind(cmd, P.MarkRW, _voxel_mark);
                    _ksplat_voxel.Set(cmd, P.Transform, scale_transform * entry.Transform);

                    _ksplat_voxel.DispatchEnoughFor(cmd, triangle_count);
                }
            }

            {
                _ksum_voxel.Seti(cmd, P.Resolution, splat_dim.x, splat_dim.y, splat_dim.z);
                _ksum_voxel.Bind(cmd, P.Splat, _voxel_splat);
                _ksum_voxel.Bind(cmd, P.Mark, _voxel_mark);
                _ksum_voxel.Bind(cmd, P.VolumeRW, _voxel_generate1);
                _ksum_voxel.Bind(cmd, P.MarkRW, _voxel_mark1);

                _ksum_voxel.DispatchEnoughFor(cmd, splat_dim.x, splat_dim.y, splat_dim.z);
            }

            {
                var v0 = _voxel_generate1;
                var v1 = _voxel_generate2;

                var m0 = _voxel_mark1;
                var m1 = _voxel_mark2;

                var iterations = 4;

                _kflood_voxel.Setf(cmd, P.MaxDistance, max_bound - 0.001f);
                _kflood_voxel.Seti(cmd, P.Resolution, splat_dim.x, splat_dim.y, splat_dim.z);

                for (int i = 0; i < iterations; i++)
                {
                    _kflood_voxel.Bind(cmd, P.Input, v0);
                    _kflood_voxel.Bind(cmd, P.Mark, m0);
                    _kflood_voxel.Bind(cmd, P.BufferRW, v1);
                    _kflood_voxel.Bind(cmd, P.MarkRW, m1);

                    _kflood_voxel.DispatchEnoughFor(cmd, splat_dim.x, splat_dim.y, splat_dim.z);

                    var t = v0;
                    v0 = v1;
                    v1 = t;

                    t = m0;
                    m0 = m1;
                    m1 = t;
                }

                _kpack_voxel.Seti(cmd, P.InputResolution, splat_dim.x, splat_dim.y, splat_dim.z);
                _kpack_voxel.Seti(cmd, P.OutputResolution, buffer_x, buffer_y, buffer_z);
                _kpack_voxel.Bind(cmd, P.Splat, v0);
                _kpack_voxel.Bind(cmd, P.VolumeRW, _voxel_packed);

                _kpack_voxel.DispatchEnoughFor(cmd, buffer_x, buffer_y, buffer_z);
            }

            // Done

            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            sdf_entry.SDF = new byte[sdf_bytes];
            sdf_entry.Voxels = new byte[buffer_x * buffer_y * buffer_z * 4];
            sdf_entry.Allocation = new uint[brick_count];

            _sdf_packed.GetData(sdf_entry.SDF);
            _voxel_packed.GetData(sdf_entry.Voxels);
            _mapping.GetData(_mapping_data);

            var count = 0;
            uint index = 0;

            for (int i = 0; i < brick_count; i++)
            {
                var value = Defines.INVALID_ID;

                if (_mapping_data[i] > 0)
                {
                    value = index++;
                    count++;
                }

                sdf_entry.Allocation[i] = value;
            }

            sdf_entry.AllocationCount = count;

            sdf_entry.Version = 3;

            return sdf_entry;
        }
    }
}