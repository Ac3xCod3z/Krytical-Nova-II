using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// On the Strike Dummy ghost prefab (made by PlayerGhostAssetsBuilder): a static, interpolated ghost with no owner, a
    /// <see cref="CombatTarget"/> and a <see cref="PositionHistory"/> ring for the server's hit test.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StrikeDummyGhostAuthoring : MonoBehaviour
    {
        sealed class Baker : Baker<StrikeDummyGhostAuthoring>
        {
            public override void Bake(StrikeDummyGhostAuthoring a)
            {
                Entity e = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(e, new CombatTarget { Kind = CombatTargetKinds.StrikeDummy, RadiusMm = 500, HeightMm = 2000 });
                AddComponent(e, new PositionHistory());
            }
        }
    }
}
