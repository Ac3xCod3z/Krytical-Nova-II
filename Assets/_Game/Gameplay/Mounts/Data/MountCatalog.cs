using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>Every mount in the game (the four boxes of the Mount window), looked up by box or by item.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Mounts/Mount Catalog")]
    public sealed class MountCatalog : ScriptableObject
    {
        [SerializeField] private List<MountDefinition> mounts = new List<MountDefinition>();
        [SerializeField] private MountSettings settings;

        [NonSerialized] private Dictionary<ushort, MountDefinition> byItem;

        public IReadOnlyList<MountDefinition> Mounts => mounts;
        public MountSettings Settings => settings;
        public MountTuning Tuning => settings != null ? settings.Tuning : MountTuning.Default;

        /// <summary>The mount in a box (1 to 4), or null.</summary>
        public MountDefinition Get(int mountId)
        {
            foreach (MountDefinition mount in mounts)
                if (mount != null && mount.mountId == mountId) return mount;
            return null;
        }

        /// <summary>The mount an item registers, or null when the item is not a mount.</summary>
        public MountDefinition ByItem(ushort itemId)
        {
            if (itemId == 0) return null;
            if (byItem == null)
            {
                byItem = new Dictionary<ushort, MountDefinition>(mounts.Count);
                foreach (MountDefinition mount in mounts)
                    if (mount != null && mount.itemId != 0) byItem[mount.itemId] = mount;
            }
            return byItem.TryGetValue(itemId, out MountDefinition found) ? found : null;
        }

        public void SetContents(List<MountDefinition> list, MountSettings mountSettings)
        {
            mounts = list;
            settings = mountSettings;
            byItem = null;
        }

        private void OnEnable() => byItem = null;
        private void OnValidate() => byItem = null;
    }
}
