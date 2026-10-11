using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Player;
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

        /// <summary>The kit whose pieces are showing, or null while the HUD wears no faction's art.</summary>
        public static FactionHudKit ShownKit { get; private set; }
        /// <summary>
        /// Where uGUI things that follow the world are put (Hostile plates): on the pieces' canvas and under the
        /// pieces, so the Hotbar and the player's plate draw over them. A unit is a screen pixel. Null until a kit shows.
        /// </summary>
        public static RectTransform WorldLayer { get; private set; }
        /// <summary>Raised when another kit shows, or none: what was made from the old one is to be made again.</summary>
        public static event System.Action KitChanged;
        private static HudChrome active;
        private static CharacterPortraitStage portraitStage;
        private const float PortraitRetry = 0.5f;

        /// <summary>A live picture of the local player's head and shoulders, on a clear background. Null until there is a player to draw.</summary>
        public static Texture Portrait => portraitStage != null ? portraitStage.Texture : null;

        private UIDocument document;
        private Canvas canvas;
        private FactionHudKit shown;
        private readonly List<HudChromePiece> pieces = new List<HudChromePiece>();

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Showing.Clear();
            active = null;
            portraitStage = null;
            ShownKit = null;
            WorldLayer = null;
            KitChanged = null;
        }

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            active = this;
            FactionSkin.Changed += Refresh;
            PlayerInventory.LocalChanged += BindPortrait;
            Refresh();
            BindPortrait();
        }

        private void OnDisable()
        {
            FactionSkin.Changed -= Refresh;
            PlayerInventory.LocalChanged -= BindPortrait;
            if (active == this) active = null;
            Clear();
            if (portraitStage != null) portraitStage.Show(false);
        }

        private void OnDestroy()
        {
            if (portraitStage != null) Destroy(portraitStage.gameObject);
            portraitStage = null;
        }

        private void Update()
        {
            // The document's tree is rebuilt when the HUD is re-enabled: the pieces must find their elements again.
            if (shown != null && pieces.Count > 0 && !pieces[0].IsAttached) Refresh(true);
            // A player's model is dressed a little after the player appears: the picture waits for it.
            if (portraitStage != null && !portraitStage.HasSitter && Time.unscaledTime >= nextPortraitTry) BindPortrait();
        }

        private float nextPortraitTry;

        private void BindPortrait()
        {
            nextPortraitTry = Time.unscaledTime + PortraitRetry;
            PlayerInventory local = PlayerInventory.Local;
            var binder = local != null ? local.GetComponent<EquipmentVisualBinder>() : null;
            var motor = local != null ? local.GetComponent<PlayerAnimancerMotor>() : null;
            if (portraitStage == null) portraitStage = CharacterPortraitStage.Create();
            portraitStage.Bind(binder != null ? binder.Appearance : null, motor != null ? motor.IdleClip : null);
            portraitStage.Show(pieces.Count > 0);
        }

        private void Refresh() => Refresh(false);

        private void Refresh(bool again)
        {
            FactionHudKit kit = KitOf(FactionSkin.Current);
            // The pointer is the faction's own too.
            FactionCursor.Show(kit != null ? kit.cursor : null, kit != null ? kit.cursorHotspot : Vector2.zero);
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
                if (!piece.Attach(root))
                {
                    Destroy(piece.gameObject);
                    continue;
                }
                pieces.Add(piece);
                Showing.Add(piece);
            }
            Top(root).EnableInClassList(ActiveClass, pieces.Count > 0);
            if (portraitStage != null) portraitStage.Show(pieces.Count > 0);
            ShownKit = pieces.Count > 0 ? kit : null;
            KitChanged?.Invoke();
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
            if (ShownKit != null)
            {
                ShownKit = null;
                KitChanged?.Invoke();
            }
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root != null) Top(root).EnableInClassList(ActiveClass, false);
        }

        private static VisualElement Top(VisualElement root) => root.panel != null ? root.panel.visualTree : root;

        /// <summary>
        /// The kit of another faction than the one the HUD wears: what belongs to a player of that faction is drawn
        /// in that faction's art (their namecard). Null while the HUD wears no faction's art; the shown kit when
        /// that faction has none.
        /// </summary>
        public static FactionHudKit KitFor(FactionId faction)
        {
            if (ShownKit == null || active == null) return null;
            return active.KitOf(faction) ?? ShownKit;
        }

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
            var layer = new GameObject("World Layer", typeof(RectTransform)) { layer = go.layer };
            var layerRect = (RectTransform)layer.transform;
            layerRect.SetParent(go.transform, false);
            layerRect.anchorMin = Vector2.zero;
            layerRect.anchorMax = Vector2.one;
            layerRect.offsetMin = layerRect.offsetMax = Vector2.zero;
            WorldLayer = layerRect; // the first child: every piece made after it draws over it
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

        /// <summary>
        /// The same for a label of this size that stands on a point of the panel (its bottom middle), or is centred
        /// on it. A label that is not laid out yet (no size) is not covered.
        /// </summary>
        public static bool Covers(Vector2 panelPoint, float width, float height, bool centred = false)
        {
            if (Showing.Count == 0 || float.IsNaN(width) || float.IsNaN(height)) return false;
            return Covers(new Rect(panelPoint.x - width * 0.5f, panelPoint.y - (centred ? height * 0.5f : height), width, height));
        }

        /// <summary>
        /// Hides a label that follows something in the world while it is over a uGUI piece, and shows it again
        /// after. By visibility, not display, so the label keeps its size and the answer does not flicker.
        /// </summary>
        public static void HideOver(VisualElement label, Vector2 panelPoint, bool centred = false)
        {
            bool covered = Covers(panelPoint, label.resolvedStyle.width, label.resolvedStyle.height, centred);
            label.style.visibility = covered ? Visibility.Hidden : StyleKeyword.Null;
        }
    }
}
