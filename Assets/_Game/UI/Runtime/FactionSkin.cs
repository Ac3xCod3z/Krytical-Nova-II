using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Shows the in-game UI in the local player's faction skin. Every faction uses the same layout (the Hegemony
    /// sheets, the base); the Null and Covenant skins (KryticalSkin_*.uss, listed in the HUD documents) only swap
    /// sprites, fonts and colours, and only under a faction class. This puts that class on every UI panel once the
    /// local player's identity is known. The Hegemony needs no class: it is the base.
    /// </summary>
    public static class FactionSkin
    {
        public const string NullClass = "kn-faction-null";
        public const string CovenantClass = "kn-faction-covenant";

        private static PlayerIdentity identity;

        /// <summary>The faction whose skin is showing.</summary>
        public static FactionId Current { get; private set; }

        // Enter Play Mode runs without a domain reload, so statics and static event hooks are reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialise()
        {
            Unbind();
            Current = FactionId.None;
            PlayerInventory.LocalChanged -= OnLocalPlayerChanged;
            PlayerInventory.LocalChanged += OnLocalPlayerChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnLocalPlayerChanged()
        {
            Unbind();
            var local = PlayerInventory.Local;
            identity = local != null ? local.GetComponent<PlayerIdentity>() : null;
            if (identity != null) identity.Changed += Refresh;
            Refresh();
        }

        // A newly loaded scene brings its own UI documents.
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Refresh();

        private static void Unbind()
        {
            if (identity != null) identity.Changed -= Refresh;
            identity = null;
        }

        /// <summary>Apply a skin by hand (GM tools, previews). The local player's identity sets it again when it changes.</summary>
        public static void Show(FactionId faction)
        {
            Current = faction;
            foreach (var document in Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include))
            {
                var root = document.rootVisualElement;
                if (root == null) continue;
                // The class goes on the panel's own root, above every document that shares the panel.
                var top = root.panel != null ? root.panel.visualTree : root;
                top.EnableInClassList(NullClass, faction == FactionId.Null);
                top.EnableInClassList(CovenantClass, faction == FactionId.Covenant);
            }
        }

        private static void Refresh() => Show(identity != null ? identity.Faction : FactionId.None);
    }
}
