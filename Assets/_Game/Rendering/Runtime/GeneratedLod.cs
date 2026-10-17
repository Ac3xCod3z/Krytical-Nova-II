using UnityEngine;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Marks an LODGroup the LOD builder made (EnvironmentLodBuilder), with the kind of thing it was sorted into, so
    /// running the builder again refreshes its distances from <see cref="WorldLodSettings"/> instead of adding more.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedLod : MonoBehaviour
    {
        public LodCategory category;
    }
}
