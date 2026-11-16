using UnityEngine;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Marks an LODGroup the LOD builder made (EnvironmentLodBuilder), with the kind of thing it was sorted into, so
    /// running the builder again refreshes its distances from WorldLodSettings instead of adding more. A plain marker, so it
    /// lives in Core: the server's copy of the scenes keeps it and never logs a missing script (Rendering is off the server).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GeneratedLod : MonoBehaviour
    {
        public LodCategory category;
    }
}
