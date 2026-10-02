using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public static class Terrains
    {
        public static void Parameters(CommandBuffer cmd, Kernel kernel, SDFTerrain terrain)
        {
            if (terrain != null)
                terrain = terrain.Instance;

            if (terrain == null)
                return;

            kernel.Setf(cmd, P.TerrainMin, terrain.Bounds.min.x, terrain.Bounds.min.y, terrain.Bounds.min.z);
            kernel.Setf(cmd, P.TerrainMax, terrain.Bounds.max.x, terrain.Bounds.max.y, terrain.Bounds.max.z);
            kernel.Setf(cmd, P.TerrainScale, terrain.Scale.x, terrain.Scale.y, terrain.Scale.z);
            kernel.Setf(cmd, P.TerrainPosition, terrain.Position.x, terrain.Position.y, terrain.Position.z);
            kernel.Setf(cmd, P.TerrainColor, terrain.Color.r, terrain.Color.g, terrain.Color.b);

            if (terrain.HeightMap != null)
            {
                kernel.Seti(cmd, P.HeightMapResolution, terrain.HeightMap.width, terrain.HeightMap.height);
                kernel.BindOnce(cmd, P.HeightMap, terrain.HeightMap);
            }

            if (terrain.NormalMap != null)
            {
                kernel.Keyword("TERRAIN_NORMALS", true);
                kernel.BindOnce(cmd, P.NormalMap, terrain.NormalMap);
            }
            else
                kernel.Keyword("TERRAIN_NORMALS", false);

            if (terrain.ColorMap != null)
            {
                kernel.Keyword("TERRAIN_COLORS", true);
                kernel.BindOnce(cmd, P.ColorMap, terrain.ColorMap);
            }
            else
                kernel.Keyword("TERRAIN_COLORS", false);
        }
    }
}