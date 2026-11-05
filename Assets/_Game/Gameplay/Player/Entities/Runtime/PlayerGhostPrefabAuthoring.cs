using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// In a SubScene: makes the player ghost prefab known to the world as the <see cref="PlayerGhostPrefab"/> singleton, and the Strike
    /// Dummy's as <see cref="StrikeDummyGhostPrefab"/> (M4.2b).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerGhostPrefabAuthoring : MonoBehaviour
    {
        public GameObject prefab;
        public GameObject strikeDummyPrefab;

        sealed class Baker : Baker<PlayerGhostPrefabAuthoring>
        {
            public override void Bake(PlayerGhostPrefabAuthoring a)
            {
                if (a.prefab == null) return;
                var e = GetEntity(TransformUsageFlags.None);
                AddComponent(e, new PlayerGhostPrefab { Prefab = GetEntity(a.prefab, TransformUsageFlags.Dynamic) });
                if (a.strikeDummyPrefab != null)
                    AddComponent(e, new StrikeDummyGhostPrefab { Prefab = GetEntity(a.strikeDummyPrefab, TransformUsageFlags.Dynamic) });
            }
        }
    }
}
