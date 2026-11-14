using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Mounts.Entities
{
    /// <summary>
    /// In the player ghost subscene: makes the Mount Companion ghost prefab known as <see cref="MountCompanionGhostPrefab"/>. In a file of
    /// its own name (Unity saves a scene's reference to a MonoBehaviour only through a script file named after it).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MountCompanionGhostPrefabAuthoring : MonoBehaviour
    {
        public GameObject prefab;

        sealed class Baker : Baker<MountCompanionGhostPrefabAuthoring>
        {
            public override void Bake(MountCompanionGhostPrefabAuthoring a)
            {
                if (a.prefab == null) return;
                Entity e = GetEntity(TransformUsageFlags.None);
                AddComponent(e, new MountCompanionGhostPrefab { Prefab = GetEntity(a.prefab, TransformUsageFlags.Dynamic) });
            }
        }
    }
}
