using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// On the townsperson ghost prefab (made by TownsfolkEntitiesAssetsBuilder): an interpolated ghost with no owner and its
    /// <see cref="TownsfolkGhost"/>. The brain's components are added on the server when it is spawned, never baked: clients never think
    /// for a townsperson. No collider, no combat target: townsfolk cannot be struck.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TownsfolkGhostAuthoring : MonoBehaviour
    {
        sealed class Baker : Baker<TownsfolkGhostAuthoring>
        {
            public override void Bake(TownsfolkGhostAuthoring a)
            {
                Entity e = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(e, new TownsfolkGhost());
            }
        }
    }
}
