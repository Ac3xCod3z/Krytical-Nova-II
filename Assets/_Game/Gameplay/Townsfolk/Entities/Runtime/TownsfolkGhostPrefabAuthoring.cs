using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// In the player ghost subscene: makes the townsperson ghost prefab known as <see cref="TownsfolkGhostPrefab"/>. In a file of its own
    /// name (a scene saves a MonoBehaviour reference only through a script file named after it; the Hostiles' lesson of 7 Nov 2026).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TownsfolkGhostPrefabAuthoring : MonoBehaviour
    {
        public GameObject prefab;

        sealed class Baker : Baker<TownsfolkGhostPrefabAuthoring>
        {
            public override void Bake(TownsfolkGhostPrefabAuthoring a)
            {
                if (a.prefab == null) return;
                Entity e = GetEntity(TransformUsageFlags.None);
                AddComponent(e, new TownsfolkGhostPrefab { Prefab = GetEntity(a.prefab, TransformUsageFlags.Dynamic) });
            }
        }
    }
}
