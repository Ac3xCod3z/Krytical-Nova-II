using KryticalNova.Player.Entities;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// On the Hostile ghost prefab (made by HostileEntitiesAssetsBuilder, M5.5): an interpolated ghost with no owner, its
    /// <see cref="HostileGhost"/>, a <see cref="CombatTarget"/> of kind <see cref="CombatTargetKinds.Hostile"/> (the server fills the
    /// body's size and HP) and a <see cref="PositionHistory"/> ring for the players' hit test. The brain's components are added on the
    /// server when a body is linked, never baked: clients never think for a Hostile.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HostileGhostAuthoring : MonoBehaviour
    {
        sealed class Baker : Baker<HostileGhostAuthoring>
        {
            public override void Bake(HostileGhostAuthoring a)
            {
                Entity e = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(e, new HostileGhost());
                AddComponent(e, new CombatTarget { Kind = CombatTargetKinds.Hostile });
                AddComponent(e, new PositionHistory());
            }
        }
    }

    /// <summary>The Hostile ghost prefab, known to the world as a singleton (baked in the player ghost subscene).</summary>
    public struct HostileGhostPrefab : IComponentData
    {
        public Entity Prefab;
    }

    /// <summary>In the player ghost subscene: makes the Hostile ghost prefab known as <see cref="HostileGhostPrefab"/>.</summary>
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
