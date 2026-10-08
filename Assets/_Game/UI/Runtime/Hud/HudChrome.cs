using System.Collections.Generic;
using KryticalNova.Factions;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// The part of the in-game interface that is drawn with the faction's own Synty HUD prefabs (uGUI): the Hotbar
    /// with its HP and Flux containers, and whatever else a <see cref="FactionHudKit"/> lists. The prefabs are views
    /// only. Everything the player can do still happens on the UI Toolkit elements, which keep their places and
    /// sizes and lose only their own look; each uGUI piece sits exactly on the element it stands for
    /// (<see cref="HudChromePiece"/>), so the layout editor, the interface size, drag and drop and click blocking
    /// work as before.
    ///
    /// The pieces live on one overlay canvas under the UI Toolkit panel, so windows, tooltips and the drag ghost
    /// draw over them.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudChrome : MonoBehaviour
    {
        /// <summary>On the panel's root while a faction's uGUI pieces are showing: the style sheets strip the stand-ins.</summary>
        public const string ActiveClass = "hud-chrome";
        /// <summary>Under the UI Toolkit panel (10), over the world.</summary>
        public const int CanvasOrder = 5;

        [Tooltip("One kit per faction: the uGUI pieces of that faction's HUD.")]
        [SerializeField] private FactionHudKit[] kits;

        private static readonly List<HudChromePiece> Showing = new List<HudChromePiece>();

        private UIDocument document;
        private Canvas canvas;
        private FactionHudKit shown;
        private readonly List<HudChromePiece> pieces = new List<HudChromePiece>();

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Showing.Clear();

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            FactionSkin.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            FactionSkin.Changed -= Refresh;
            Clear();
        }

        private void Update()
        {
            // The document's tree is rebuilt when the HUD is re-enabled: the pieces must find their elements again.
            if (shown != null && pieces.Count > 0 && !pieces[0].IsAttached) Refresh(true);
        }

        private void Refresh() => Refresh(false);

        private void Refresh(bool again)
        {
            FactionHudKit kit = KitOf(FactionSkin.Current);
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (kit == shown && !again) return;
            Clear();
            if (root == null) return;
            if (kit == null) return;

            if (canvas == null) canvas = MakeCanvas();
            shown = kit;
            foreach (HudChromePiece prefab in kit.Pieces())
            {
                if (prefab == null) continue;
                HudChromePiece piece = Instantiate(prefab, canvas.transform, false);
                piece.name = prefab.name;
                if (!piece.Attach(root, kit.unitsToPanel))
                {
                    Destroy(piece.gameObject);
                    continue;
                }
                pieces.Add(piece);
                Showing.Add(piece);
            }
            Top(root).EnableInClassList(ActiveClass, pieces.Count > 0);
        }

        private void Clear()
        {
            foreach (HudChromePiece piece in pieces)
            {
                if (piece == null) continue;
                Showing.Remove(piece);
                piece.Detach();
                Destroy(piece.gameObject);
            }
            pieces.Clear();
            shown = null;
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root != null) Top(root).EnableInClassList(ActiveClass, false);
        }

        private static VisualElement Top(VisualElement root) => root.panel != null ? root.panel.visualTree : root;

        private FactionHudKit KitOf(FactionId faction)
        {
            if (kits == null) return null;
            foreach (FactionHudKit kit in kits)
                if (kit != null && kit.faction == faction) return kit;
            return null;
        }

        private Canvas MakeCanvas()
        {
            var go = new GameObject("HUD Chrome", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(transform, false);
            var made = go.AddComponent<Canvas>();
            made.renderMode = RenderMode.ScreenSpaceOverlay;
            made.sortingOrder = CanvasOrder;
            // No scaler: a canvas unit is a screen pixel, and each piece scales itself onto its element.
            // No raycaster either: the pieces are looked at, the UI Toolkit elements over them are what is clicked.
            return made;
        }

        // ------------------------------------------------------------------ shared geometry

        /// <summary>Where an element is on screen, in pixels from the bottom-left corner. False while it has no size.</summary>
        public static bool TryScreenRect(VisualElement element, out Rect pixels)
        {
            pixels = default;
            IPanel panel = element != null ? element.panel : null;
            if (panel == null) return false;
            Rect bound = element.worldBound;
            if (float.IsNaN(bound.width) || bound.width < 1f || bound.height < 1f) return false;
            // The panel's scale follows the screen and the Interface size setting: measured, not remembered.
            Vector2 origin = RuntimePanelUtils.ScreenToPanel(panel, Vector2.zero);
            Vector2 far = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Screen.width, Screen.height));
            float across = far.x - origin.x;
            if (across < 1f) return false;
            float scale = Screen.width / across;
            pixels = new Rect((bound.x - origin.x) * scale, Screen.height - (bound.yMax - origin.y) * scale, bound.width * scale, bound.height * scale);
            return true;
        }

        /// <summary>
        /// True when a rectangle of the panel lies over a uGUI piece. Labels that follow things in the world
        /// (namecards, loot labels, combat numbers) are UI Toolkit and would draw over the pieces: they ask this.
        /// </summary>
        public static bool Covers(Rect panelRect)
        {
            foreach (HudChromePiece piece in Showing)
                if (piece != null && piece.Covers(panelRect)) return true;
            return false;
        }
    }
}
