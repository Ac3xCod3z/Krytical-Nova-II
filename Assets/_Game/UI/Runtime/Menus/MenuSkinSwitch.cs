using System;
using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// A menu scene holds its screens once per faction skin, each built from that faction's own Synty menu pack, with
    /// its own controller, all switched off. This switches on the one the menus wear (<see cref="MenuSkin"/>) before
    /// anything else in the scene wakes, and changes over at once when the Menu Theme setting changes. The layout,
    /// the text and the behaviour are the same in all three: only one of them is ever alive.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class MenuSkinSwitch : MonoBehaviour
    {
        [Serializable]
        public struct Group
        {
            public FactionId faction;
            public GameObject root;
        }

        [SerializeField] private Group[] groups = Array.Empty<Group>();

        /// <summary>The faction whose screens are showing.</summary>
        public FactionId Showing { get; private set; }

        /// <summary>The live screens' root, or null.</summary>
        public GameObject ShowingRoot
        {
            get
            {
                foreach (Group group in groups)
                    if (group.faction == Showing) return group.root;
                return null;
            }
        }

        private void Awake()
        {
            Show(MenuSkin.Current);
            MenuSkin.Changed += OnChanged;
        }

        private void OnDestroy() => MenuSkin.Changed -= OnChanged;

        private void OnChanged() => Show(MenuSkin.Current);

        private void Show(FactionId faction)
        {
            bool known = false;
            foreach (Group group in groups) known |= group.faction == faction && group.root != null;
            if (!known && groups.Length > 0) faction = groups[0].faction; // a skin this scene was not built with
            if (faction == Showing) return;
            Showing = faction;
            // The old one goes before the new one comes, so two controllers never listen at once.
            foreach (Group group in groups)
                if (group.root != null && group.faction != faction) group.root.SetActive(false);
            foreach (Group group in groups)
                if (group.root != null && group.faction == faction) group.root.SetActive(true);
        }
    }
}
