using Unity.AI.Navigation;
using UnityEngine;

namespace KryticalNova.Navigation
{
    /// <summary>Builds missing navigation at startup; an assigned, baked NavMesh is reused.</summary>
    [DefaultExecutionOrder(StartupExecutionOrder)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshSurface))]
    public sealed class RuntimeNavMeshBuilder : MonoBehaviour
    {
        private const int StartupExecutionOrder = -1000;

        private void Start()
        {
            // All scene OnEnable calls have completed here. The early execution order
            // builds navigation before PointAndClickPlayer.Start enables its agent.
            NavMeshSurface surface = GetComponent<NavMeshSurface>();
            if (!surface.isActiveAndEnabled)
            {
                Debug.LogError("Enable the NavMeshSurface before starting the scene.", this);
                return;
            }

            if (surface.navMeshData == null)
                surface.BuildNavMesh();

            if (surface.navMeshData == null)
                Debug.LogError("Navigation could not be built. Check the surface's layer mask and geometry settings.", this);
        }
    }
}
