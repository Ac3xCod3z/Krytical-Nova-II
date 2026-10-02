using UnityEngine;

namespace KryticalNova.World.Minimap
{
    /// <summary>
    /// A zone's baked top-down map (see Krytical Nova/World/Bake Minimap). The texture covers a square of
    /// <see cref="worldSize"/> metres centred on <see cref="worldCenter"/> (world XZ), +Z up / north.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Zone Minimap")]
    public sealed class ZoneMinimapData : ScriptableObject
    {
        public Texture2D map;
        public Vector2 worldCenter;
        [Min(1f)] public float worldSize = 600f;

        /// <summary>World XZ → normalised map coordinates (0,0 bottom-left .. 1,1 top-right).</summary>
        public Vector2 WorldToUv(Vector3 world) =>
            new Vector2((world.x - worldCenter.x) / worldSize + 0.5f, (world.z - worldCenter.y) / worldSize + 0.5f);
    }
}
