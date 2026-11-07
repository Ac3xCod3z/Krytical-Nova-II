using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// In the player ghost subscene: makes the Hostile ghost prefab known as <see cref="HostileGhostPrefab"/>. In a file of its own name:
    /// Unity saves a scene's reference to a MonoBehaviour only through a script file named after it (7 Nov 2026: declared beside
    /// HostileGhostAuthoring, the holder came out as a missing script and no server had the prefab).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HostileGhostPrefabAuthoring : MonoBehaviour
    {
        public GameObject prefab;

        sealed class Baker : Baker<HostileGhostPrefabAuthoring>
        {
            public override void Bake(HostileGhostPrefabAuthoring a)
            {
                if (a.prefab == null) return;
                Entity e = GetEntity(TransformUsageFlags.None);
                AddComponent(e, new HostileGhostPrefab { Prefab = GetEntity(a.prefab, TransformUsageFlags.Dynamic) });
            }
        }
    }
}
