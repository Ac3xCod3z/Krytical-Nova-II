using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Player.Entities
{
    /// <summary>In a SubScene: makes the player ghost prefab known to the world as the <see cref="PlayerGhostPrefab"/> singleton.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerGhostPrefabAuthoring : MonoBehaviour
    {
        public GameObject prefab;

        sealed class Baker : Baker<PlayerGhostPrefabAuthoring>
        {
            public override void Bake(PlayerGhostPrefabAuthoring a)
            {
                if (a.prefab == null) return;
                var e = GetEntity(TransformUsageFlags.None);
                AddComponent(e, new PlayerGhostPrefab { Prefab = GetEntity(a.prefab, TransformUsageFlags.Dynamic) });
            }
        }
    }
}
