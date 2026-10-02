using System;
using UnityEngine;

namespace GI
{
    [Serializable]
    public class SDFTerrain
    {
        /// <summary>
        /// If no Terrain is specified a closest suitable terrain will be found and it's values copied over.
        /// </summary>
        [Tooltip("If no Terrain is specified a closest suitable terrain will be found and it's values copied over.")]
        public bool AutoFind = true;

        /// <summary>
        /// Terrain to copy values over from.
        /// </summary>
        [Tooltip("Terrain to copy values over from.")]
        public GameObject Terrain;

        /// <summary>
        /// Color texture of the terrain. If not set the singular value is used from the Color property.
        /// </summary>
        [Tooltip("Color texture of the terrain. If not set the singular value is used from the Color property.")]
        public Texture ColorMap;

        /// <summary>
        /// Terrain color used for the entire terrain. Ignored if ColorMap is set.
        /// </summary>
        [Tooltip("Terrain color used for the entire terrain. Ignored if ColorMap is set.")]
        public Color Color = new Color(0.224f, 0.236f, 0.1f);

        /// <summary>
        /// Heightmap of the terrain. This value controls if the terrain system is enabled.
        /// </summary>
        [Tooltip("Heightmap of the terrain. This value controls if the terrain system is enabled.")]
        public Texture HeightMap;

        /// <summary>
        /// Optional normal map, if not provided normals are calculated from the HeightMap.
        /// </summary>
        [Tooltip("Optional normal map, if not provided normals are calculated from the HeightMap.")]
        public Texture NormalMap;

        /// <summary>
        /// Bounding box for ray tracing in local space. Used to avoid having rays traveling into infinity past the terrain.
        /// </summary>
        [Tooltip("Bounding box for ray tracing in local space. Used to avoid having rays traveling into infinity past the terrain.")]
        public Bounds Bounds = new Bounds(new Vector3(0, 0, 0), new Vector3(1, 1, 1));

        /// <summary>
        /// Base corner position of the terrain, used to transform positions from world into terrain local space.
        /// </summary>
        [Tooltip("Base corner position of the terrain, used to transform positions from world into terrain local space.")]
        public Vector3 Position = new Vector3(0, 0, 0);

        /// <summary>
        /// Vector containing horizontal size of a single terrain unit. Y value controls multiplier for the height value.
        /// </summary>
        [Tooltip("Vector containing horizontal size of a single terrain unit. Y value controls multiplier for the height value.")]
        public Vector3 Scale = new Vector3(1, 1, 1);

        public bool IsEnabled => HeightMap != null || (_auto_found != null && _auto_found.IsEnabled);

        public SDFTerrain Instance => (HeightMap != null) ? this : _auto_found;

        private SDFTerrain _auto_found;

        public void AutoUpdate(Vector3 reference_position)
        {
            if (_auto_found == null)
                _auto_found = new SDFTerrain();

            _auto_found.HeightMap = null; // cancel it out

            var terrain = Terrain;

            if (AutoFind && terrain == null)
                terrain = Find(reference_position);

            var instance = Instance;

            if (HeightMap != null) instance.HeightMap = HeightMap;
            if (NormalMap != null) instance.NormalMap = NormalMap;
            if (ColorMap != null) instance.ColorMap = ColorMap;

            instance.Color = Color;
            instance.Bounds = Bounds;
            instance.Position = Position;
            instance.Scale = Scale;

            if (terrain != null)
            {
                var component = terrain.GetComponent<Terrain>();

                if (component != null)
                    instance.Copy(component);
            }
        }

        public GameObject Find(Vector3 reference_position)
        {
            var array = GameObject.FindObjectsOfType<Terrain>();

            Terrain middle = null;
            Terrain first = null;

            foreach (var terrain in array)
            {
                if (first == null)
                    first = terrain;

                var bounds = terrain.terrainData.bounds;

                if (middle == null &&
                    reference_position.x >= bounds.min.x && reference_position.x < bounds.max.x &&
                    reference_position.z >= bounds.min.z && reference_position.z < bounds.max.z)
                {
                    middle = terrain;
                }
            }

            if (middle != null)
                return middle.gameObject;
            else if (first != null)
                return first.gameObject;
            else
                return null;
        }

        public void Copy(Terrain terrain)
        {
            var data = terrain.terrainData;

            Bounds = data.bounds;
            Scale = data.heightmapScale;
            Position = terrain.transform.position;

            if (data.heightmapTexture != null)
                HeightMap = data.heightmapTexture;

            if (terrain.normalmapTexture != null)
                NormalMap = terrain.normalmapTexture;
        }
    }
}