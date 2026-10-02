using System;
using UnityEngine;

namespace GI
{
    [Serializable]
    public struct Fit
    {
        public float Bias;
        public float ValueScale;
        public Vector3Int Bricks;
        public Vector3Int Splat;
        public Bounds Bounds;
        public Bounds SplatBounds;

        public static Fit Find(Bounds mesh_bounds, int resolution, float bias)
        {
            if (resolution < Defines.BRICK_DIM)
                resolution = Defines.BRICK_DIM;

            if (resolution > 128)
                resolution = 128;

            int padding = 1;

            float voxel_size_guess_x = (mesh_bounds.max.x - mesh_bounds.min.x) / resolution;
            float voxel_size_guess_y = (mesh_bounds.max.y - mesh_bounds.min.y) / resolution;
            float voxel_size_guess_z = (mesh_bounds.max.z - mesh_bounds.min.z) / resolution;

            float voxel_size_guess = Mathf.Max(voxel_size_guess_x, voxel_size_guess_y, voxel_size_guess_z);

            float bias_guess = Mathf.Max(bias, 0) * 2 * voxel_size_guess;

            Vector3 extents = mesh_bounds.max - mesh_bounds.min;
            extents += new Vector3(bias_guess * 2, bias_guess * 2, bias_guess * 2);

            float largest_extent = Mathf.Max(extents.x, extents.y, extents.z);

            int voxel_count_x = Math.Max((int)Mathf.Ceil(padding * 2 + (resolution - padding * 2) * (extents.x / largest_extent)), Defines.BRICK_PAYLOAD);
            int voxel_count_y = Math.Max((int)Mathf.Ceil(padding * 2 + (resolution - padding * 2) * (extents.y / largest_extent)), Defines.BRICK_PAYLOAD);
            int voxel_count_z = Math.Max((int)Mathf.Ceil(padding * 2 + (resolution - padding * 2) * (extents.z / largest_extent)), Defines.BRICK_PAYLOAD);

            int brick_count_x = Math.Max(1, (voxel_count_x + Defines.BRICK_PAYLOAD - 1) / Defines.BRICK_PAYLOAD);
            int brick_count_y = Math.Max(1, (voxel_count_y + Defines.BRICK_PAYLOAD - 1) / Defines.BRICK_PAYLOAD);
            int brick_count_z = Math.Max(1, (voxel_count_z + Defines.BRICK_PAYLOAD - 1) / Defines.BRICK_PAYLOAD);

            voxel_count_x = brick_count_x * Defines.BRICK_PAYLOAD;
            voxel_count_y = brick_count_y * Defines.BRICK_PAYLOAD;
            voxel_count_z = brick_count_z * Defines.BRICK_PAYLOAD;

            float voxel_size_x = extents.x / (voxel_count_x - padding * 2);
            float voxel_size_y = extents.y / (voxel_count_y - padding * 2);
            float voxel_size_z = extents.z / (voxel_count_z - padding * 2);
            float voxel_size = Mathf.Max(voxel_size_x, voxel_size_y, voxel_size_z);

            var fit = new Fit();
            fit.Bias = bias;

            var center = mesh_bounds.min * 0.5f + mesh_bounds.max * 0.5f;

            fit.Bounds.min = new Vector3(center.x - voxel_count_x * voxel_size * 0.5f,
                                         center.y - voxel_count_y * voxel_size * 0.5f,
                                         center.z - voxel_count_z * voxel_size * 0.5f);

            fit.Bounds.max = new Vector3(center.x + voxel_count_x * voxel_size * 0.5f,
                                         center.y + voxel_count_y * voxel_size * 0.5f,
                                         center.z + voxel_count_z * voxel_size * 0.5f);

            fit.Bricks = new Vector3Int(brick_count_x, brick_count_y, brick_count_z);
            fit.Splat = new Vector3Int(voxel_count_x + 2, voxel_count_y + 2, voxel_count_z + 2);

            fit.SplatBounds = fit.Bounds;
            fit.SplatBounds.min -= new Vector3(voxel_size, voxel_size, voxel_size);
            fit.SplatBounds.max += new Vector3(voxel_size, voxel_size, voxel_size);

            fit.ValueScale = voxel_size * 16;

            return fit;
        }
    }
}