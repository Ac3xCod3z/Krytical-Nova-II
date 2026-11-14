using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Mounts.Entities
{
    /// <summary>On the Mount Companion ghost prefab (made by MountCompanionAssetsBuilder): an interpolated ghost with no owner and its
    /// <see cref="CompanionView"/>. The server fills everything when it spawns one.</summary>
    [DisallowMultipleComponent]
    public sealed class MountCompanionGhostAuthoring : MonoBehaviour
    {
        sealed class Baker : Baker<MountCompanionGhostAuthoring>
        {
            public override void Bake(MountCompanionGhostAuthoring a)
            {
                Entity e = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(e, new CompanionView());
            }
        }
    }
}
