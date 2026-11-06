using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// A ghost players can attack that is not a player (M4.2b: the Strike Dummy; M5: Hostiles): where it stands, its hit body and its
    /// public HP (thousandths, as <see cref="PlayerVitals"/>). The server writes it from the scene object it stands for.
    /// </summary>
    public struct CombatTarget : IComponentData
    {
        [GhostField] public int3 FeetMm;
        [GhostField] public int HealthMilli;
        [GhostField] public int MaxHealthMilli;
        /// <summary>Its capsule (from the scene object's collider, scale included).</summary>
        [GhostField] public int RadiusMm;
        [GhostField] public int HeightMm;
        /// <summary><see cref="CombatTargetKinds"/>.</summary>
        [GhostField] public byte Kind;

        /// <summary>No Max HP yet counts as alive, as a player's ghost.</summary>
        public bool Alive => MaxHealthMilli == 0 || HealthMilli > 0;

        public HitProxy Proxy => RadiusMm > 0 && HeightMm > 0 ? new HitProxy { RadiusMm = RadiusMm, HeightMm = HeightMm } : HitProxy.Player;
    }

    public static class CombatTargetKinds
    {
        /// <summary>NGO's Strike Dummy: a CombatEntity with no PlayerResources and no IHostile (CombatEntity.IsStrikeDummy).</summary>
        public const byte StrikeDummy = 1;
        /// <summary>A Hostile (M5.5): its ghost also carries <c>HostileGhost</c>.</summary>
        public const byte Hostile = 2;
    }

    /// <summary>The Strike Dummy ghost prefab, known to the world as a singleton (baked beside <see cref="PlayerGhostPrefab"/>).</summary>
    public struct StrikeDummyGhostPrefab : IComponentData
    {
        public Entity Prefab;
    }
}
