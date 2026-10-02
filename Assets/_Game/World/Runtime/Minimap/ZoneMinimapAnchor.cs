using System;
using UnityEngine;

namespace KryticalNova.World.Minimap
{
    /// <summary>Marks the open zone's minimap. The HUD reads <see cref="Current"/>; zones without one hide the minimap.</summary>
    [DisallowMultipleComponent]
    public sealed class ZoneMinimapAnchor : MonoBehaviour
    {
        [SerializeField] private ZoneMinimapData data;

        public static ZoneMinimapData Current { get; private set; }
        public static event Action CurrentChanged;

        public ZoneMinimapData Data => data;

        private void OnEnable()
        {
            Current = data;
            CurrentChanged?.Invoke();
        }

        private void OnDisable()
        {
            if (Current != data) return;
            Current = null;
            CurrentChanged?.Invoke();
        }

#if UNITY_EDITOR
        public void EditorSetData(ZoneMinimapData value) => data = value;
#endif
    }
}
