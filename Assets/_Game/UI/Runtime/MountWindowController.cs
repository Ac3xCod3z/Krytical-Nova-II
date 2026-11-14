using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Mounts;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Mount window (owner, 25 Oct 2026: "its own window, skinned with the faction skins like the other windows"):
    /// four boxes, one for each mount of the game. An owned mount shows its icon for the stage it has grown to, its
    /// stage, growth and hunger and the two stats it gives; a box not owned says so. Clicking an owned box selects it;
    /// the Ride key ([V] to start with) gets on the selected mount and off again. Its own key is [N].
    /// The window is built in code out of the parts the other windows are made of (the window frame and header, the
    /// skin's card, item tile and bar), so every faction's skin dresses it with no style of its own.
    /// It puts itself on each scene's HUD when the scene loads, so no scene needs saving for it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MountWindowController : MonoBehaviour
    {
        private const float WindowWidth = 620f, WindowRight = 24f, WindowTop = 64f, WindowMargin = 20f;
        private static readonly Color SelectedColour = new Color(1f, 0.82f, 0.35f, 1f);

        private VisualElement root, window;
        private FloatingFit windowFit;
        private Label keyLabel, footerText;
        private Button rideButton, callButton;
        private readonly Button[] stanceButtons = new Button[4];
        private readonly Box[] boxes = new Box[MountRules.Slots];
        private PlayerMounts mounts;
        private bool dirty = true;
        private float nextRefresh;

        private sealed class Box
        {
            public VisualElement card, tile, icon;
            public Label name, stage, stats, tag, glyph;
            public ProgressBar growth, hunger, hp;   // hp: the Mount Companion's (day list item 19)
        }

        public bool IsOpen => window != null && window.style.display != DisplayStyle.None;

        // ------------------------------------------------------------------ putting itself on the HUD

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach();

        private static void Attach()
        {
            var hud = FindAnyObjectByType<ProgressionHUD>(FindObjectsInactive.Include);
            if (hud != null && hud.GetComponent<MountWindowController>() == null) hud.gameObject.AddComponent<MountWindowController>();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            PlayerInventory.LocalChanged += OnLocalChanged;
        }

        private void OnDisable()
        {
            PlayerInventory.LocalChanged -= OnLocalChanged;
            Bind(null);
            window?.RemoveFromHierarchy();
            window = null;
        }

        private void OnLocalChanged() => Bind(null);

        private void Bind(PlayerMounts next)
        {
            if (mounts != null) mounts.Changed -= OnChanged;
            mounts = next;
            if (mounts != null) mounts.Changed += OnChanged;
            dirty = true;
        }

        private void OnChanged() => dirty = true;

        private void Update()
        {
            if (root == null) return;
            if (mounts == null)
            {
                PlayerMounts local = PlayerMounts.Local;
                if (local == null) return;
                Bind(local);
            }
            if (!HudTyping.IsTyping(root))
            {
                if (KeyBindings.Pressed(KeyBindings.Mounts)) SetOpen(!IsOpen);
                if (KeyBindings.Pressed(KeyBindings.Ride)) Ride();
                if (KeyBindings.Pressed(KeyBindings.Companion)) Call();
            }
            if (!IsOpen) return;
            // Hunger runs down while riding: the meters are read again now and then, not only on a change.
            if (dirty || Time.unscaledTime >= nextRefresh) Refresh();
        }

        private void Ride()
        {
            if (mounts == null) return;
            string reason = mounts.RideBlockedReason();
            if (reason != null) { ClientPresentationRegistry.Messages?.ShowMessage(reason); return; }
            mounts.RequestRide();
        }

        // the Mount Companion (day list item 19): calls the selected mount out to walk beside you, or sends it home
        private void Call()
        {
            if (mounts == null) return;
            string reason = mounts.CompanionBlockedReason();
            if (reason != null) { ClientPresentationRegistry.Messages?.ShowMessage(reason); return; }
            mounts.RequestCompanion();
        }

        public void SetOpen(bool open)
        {
            if (open && mounts == null) return;
            if (open && window == null) Build();
            if (window == null) return;
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) return;
            window.BringToFront();
            VisualElement screen = window.panel != null ? window.panel.visualTree : null;
            if (screen != null) windowFit.At(new Vector2(screen.worldBound.xMax - WindowRight - WindowWidth, screen.worldBound.yMin + WindowTop));
            dirty = true;
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            window = new VisualElement { name = "mount-window" };
            foreach (string c in new[] { "panel", "window", "blocks-world" }) window.AddToClassList(c);
            window.style.width = WindowWidth;
            window.style.display = DisplayStyle.None;
            windowFit = new FloatingFit(window, 0f, WindowMargin);

            var header = new VisualElement();
            header.AddToClassList("row");
            header.AddToClassList("kn-header");
            var title = new Label("MOUNTS");
            title.AddToClassList("heading");
            header.Add(title);
            keyLabel = new Label($"[{KeyBindings.Label(KeyBindings.Mounts)}]");
            keyLabel.AddToClassList("muted");
            keyLabel.AddToClassList("header-key");
            header.Add(keyLabel);
            var close = new Button(() => SetOpen(false)) { name = "close-mounts" };
            close.AddToClassList("close");
            var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
            glyph.AddToClassList("kn-close-glyph");
            close.Add(glyph);
            header.Add(close);
            window.Add(header);

            var body = new VisualElement { name = "mount-boxes" };
            body.AddToClassList("window-body");
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexWrap = Wrap.Wrap;
            body.style.justifyContent = Justify.SpaceBetween;
            body.style.paddingLeft = body.style.paddingRight = 10f;
            body.style.paddingTop = 8f;
            window.Add(body);
            for (int i = 0; i < MountRules.Slots; i++)
            {
                boxes[i] = MakeBox(i + 1);
                body.Add(boxes[i].card);
            }

            var footer = new VisualElement();
            footer.AddToClassList("settings-footer");
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems = Align.Center;
            footer.style.paddingLeft = footer.style.paddingRight = 12f;
            footerText = new Label { name = "mount-help" };
            footerText.AddToClassList("muted");
            footerText.style.flexGrow = 1f;
            footerText.style.flexShrink = 1f;
            footerText.style.whiteSpace = WhiteSpace.Normal;
            footerText.style.fontSize = 13f;
            footerText.style.marginRight = 10f;
            footer.Add(footerText);
            rideButton = new Button(Ride) { name = "mount-ride" };
            rideButton.AddToClassList("settings-footer-button");
            rideButton.AddToClassList("kn-button-primary");
            rideButton.style.flexShrink = 0f;
            footer.Add(rideButton);
            callButton = new Button(Call) { name = "mount-call" };
            callButton.AddToClassList("settings-footer-button");
            callButton.style.flexShrink = 0f;
            callButton.style.marginLeft = 6f;
            callButton.tooltip = "Your selected mount walks beside you instead of being ridden. It keeps giving its stats and gets hungry faster while out; at 0 % hunger it goes home.";
            footer.Add(callButton);
            window.Add(StanceRow());
            window.Add(footer);

            root.Add(window);
        }

        // one stance at a time, set here (FREE in phases 1-2, DPS and HEALER in phase 3, TANK in phase 4)
        private VisualElement StanceRow()
        {
            var row = new VisualElement { name = "mount-stances" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = row.style.paddingRight = 12f;
            row.style.marginBottom = 6f;
            var label = new Label("COMPANION STANCE") { pickingMode = PickingMode.Ignore };
            label.AddToClassList("muted");
            label.style.fontSize = 12f;
            label.style.marginRight = 8f;
            row.Add(label);
            string[] names = { "FREE", "TANK", "DPS", "HEALER" };
            string[] tips =
            {
                "FREE: no combat. It keeps the riding bonus and may come along in PvP.",
                "TANK: it taunts the Hostiles you fight so they turn on it, and takes less damage. It deals no damage. PvE only: in PvP it acts as FREE.",
                "DPS: it helps you kill what you fight, with its own damage buff. It never draws aggro and never heals. PvE only: in PvP it acts as FREE.",
                "HEALER: it heals you over time while you fight. It deals no damage and never draws aggro. PvE only: in PvP it acts as FREE.",
            };
            for (int i = 0; i < names.Length; i++)
            {
                int stance = i;
                var b = new Button(() => mounts?.RequestStance((CompanionStance)stance)) { name = "mount-stance-" + names[i].ToLowerInvariant(), text = names[i], tooltip = tips[i] };
                b.AddToClassList("settings-footer-button");
                b.style.marginRight = 4f;
                b.SetEnabled(CompanionFightRules.IsBuilt((CompanionStance)i));
                stanceButtons[i] = b;
                row.Add(b);
            }
            return row;
        }

        private Box MakeBox(int boxNumber)
        {
            var box = new Box();
            box.card = new VisualElement { name = $"mount-box-{boxNumber}" };
            box.card.AddToClassList("kn-card");
            box.card.style.width = Length.Percent(49f);
            box.card.style.marginBottom = 8f;
            box.card.style.paddingLeft = box.card.style.paddingRight = 10f;
            box.card.style.paddingTop = box.card.style.paddingBottom = 8f;
            box.card.style.flexDirection = FlexDirection.Row;
            box.card.style.borderTopWidth = box.card.style.borderBottomWidth = box.card.style.borderLeftWidth = box.card.style.borderRightWidth = 2f;
            box.card.RegisterCallback<ClickEvent>(_ => Select(boxNumber));

            box.tile = new VisualElement { pickingMode = PickingMode.Ignore };
            box.tile.AddToClassList("equip-slot");
            box.tile.style.width = box.tile.style.height = 92f;
            box.tile.style.flexShrink = 0f;
            box.tile.style.marginRight = 10f;
            box.icon = new VisualElement { pickingMode = PickingMode.Ignore };
            box.icon.AddToClassList("item-icon");
            box.icon.style.position = Position.Absolute;
            box.icon.style.left = box.icon.style.right = box.icon.style.top = box.icon.style.bottom = 4f;
            box.tile.Add(box.icon);
            box.glyph = new Label("?") { pickingMode = PickingMode.Ignore };
            box.glyph.AddToClassList("heading");
            box.glyph.style.position = Position.Absolute;
            box.glyph.style.left = box.glyph.style.right = box.glyph.style.top = box.glyph.style.bottom = 0f;
            box.glyph.style.unityTextAlign = TextAnchor.MiddleCenter;
            box.glyph.style.fontSize = 36f;
            box.tile.Add(box.glyph);
            box.card.Add(box.tile);

            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.style.flexGrow = 1f;
            text.style.flexShrink = 1f;
            var nameRow = new VisualElement { pickingMode = PickingMode.Ignore };
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.justifyContent = Justify.SpaceBetween;
            box.name = new Label { pickingMode = PickingMode.Ignore };
            box.name.AddToClassList("heading");
            box.name.style.fontSize = 15f;
            nameRow.Add(box.name);
            box.tag = new Label { pickingMode = PickingMode.Ignore };
            box.tag.AddToClassList("accent");
            box.tag.style.fontSize = 11f;
            nameRow.Add(box.tag);
            text.Add(nameRow);
            box.stage = new Label { pickingMode = PickingMode.Ignore };
            box.stage.AddToClassList("muted");
            text.Add(box.stage);
            box.growth = Bar();
            text.Add(box.growth);
            box.hunger = Bar();
            text.Add(box.hunger);
            box.hp = Bar();
            box.hp.AddToClassList("kn-bar-hp");
            text.Add(box.hp);
            box.stats = new Label { pickingMode = PickingMode.Ignore };
            box.stats.AddToClassList("muted");
            box.stats.style.whiteSpace = WhiteSpace.Normal;
            box.stats.style.fontSize = 13f;
            box.stats.style.marginTop = 4f;
            text.Add(box.stats);
            box.card.Add(text);
            return box;
        }

        private static ProgressBar Bar()
        {
            var bar = new ProgressBar { lowValue = 0f, highValue = 100f, pickingMode = PickingMode.Ignore };
            bar.AddToClassList("kn-bar");
            bar.style.height = 16f;
            bar.style.marginTop = 3f;
            return bar;
        }

        private void Select(int box)
        {
            if (mounts == null || !mounts.Owns(box) || box == mounts.SelectedId) return;
            if (mounts.IsMounted) { ClientPresentationRegistry.Messages?.ShowMessage("Get off your mount before choosing another."); return; }
            mounts.RequestSelect(box);
        }

        // ------------------------------------------------------------------ showing

        private void Refresh()
        {
            dirty = false;
            nextRefresh = Time.unscaledTime + 1f;
            if (mounts == null || mounts.Catalog == null || window == null) return;
            keyLabel.text = $"[{KeyBindings.Label(KeyBindings.Mounts)}]";
            for (int i = 0; i < MountRules.Slots; i++) Show(boxes[i], i + 1);

            string ride = KeyBindings.Label(KeyBindings.Ride);
            MountDefinition selected = mounts.Selected;
            rideButton.text = mounts.IsMounted ? $"GET OFF  [{ride}]" : $"RIDE  [{ride}]";
            rideButton.SetEnabled(mounts.IsMounted || mounts.RideBlockedReason() == null);
            string call = KeyBindings.Label(KeyBindings.Companion);
            callButton.text = mounts.CompanionOut ? $"SEND HOME  [{call}]" : $"CALL  [{call}]";
            callButton.SetEnabled(mounts.CompanionOut || mounts.CompanionBlockedReason() == null);
            for (int i = 0; i < stanceButtons.Length; i++) stanceButtons[i].EnableInClassList("kn-button-primary", (int)mounts.Stance == i);
            footerText.text = selected == null
                ? "Use a mount item from your Pack to add a mount. Click a mount you own to select it."
                : "Walk by default, run with Run selected. Feed it Mount Feed (sold at the Apothecary) from your Pack. It earns EXP from your Hostile kills while selected.";
        }

        private void Show(Box box, int number)
        {
            MountDefinition mount = mounts.Catalog.Get(number);
            bool owned = mount != null && mounts.Owns(number);
            bool selected = owned && mounts.SelectedId == number;
            box.card.EnableInClassList("selected", selected);
            Color edge = selected ? SelectedColour : new Color(0f, 0f, 0f, 0f);
            box.card.style.borderTopColor = box.card.style.borderBottomColor = box.card.style.borderLeftColor = box.card.style.borderRightColor = edge;
            box.card.style.opacity = owned ? 1f : 0.55f;

            box.name.text = mount != null ? mount.displayName.ToUpperInvariant() : $"MOUNT {number}";
            box.tag.text = selected ? (mounts.IsMounted ? "RIDING" : mounts.CompanionOut ? "OUT" : "SELECTED") : "";
            box.growth.style.display = box.hunger.style.display = box.hp.style.display = owned ? DisplayStyle.Flex : DisplayStyle.None;

            int growth = owned ? mounts.GrowthOf(number) : 0;
            int stage = MountRules.Stage(growth);
            Sprite icon = mount != null ? mount.IconFor(owned ? stage : 0) : null;
            box.icon.style.backgroundImage = icon != null ? new StyleBackground(icon) : StyleKeyword.None;
            box.icon.EnableInClassList("silhouette", !owned);
            box.icon.style.unityBackgroundImageTintColor = owned ? Color.white : new Color(0.05f, 0.05f, 0.08f, 0.85f);
            box.glyph.style.display = icon == null ? DisplayStyle.Flex : DisplayStyle.None;

            if (!owned)
            {
                box.stage.text = mount == null || !mount.IsBuilt ? "Not owned  ·  coming later" : "Not owned";
                box.stats.text = mount != null ? $"Gives {Stat(mount.firstStat)} and {Stat(mount.secondStat)}." : "";
                return;
            }
            MountState state = mounts.StateOf(number);
            box.stage.text = $"{mount.StageName(stage)}  ·  Stage {stage + 1} of {MountRules.StageCount}";
            float exact = mounts.GrowthExactOf(number);
            box.growth.value = exact;
            box.growth.title = $"Growth {exact:0.##}%";
            box.hunger.value = state.HungerPercent;
            box.hunger.title = state.IsFed ? $"Hunger {state.HungerPercent:0.#}%" : "Starving: feed it";
            int hp = mounts.CompanionHpOf(number);
            long rest = mounts.RestLeftOf(number);
            box.hp.value = hp / 10f;
            box.hp.title = rest > 0 ? $"Resting {CompanionRules.Clock(rest)}" : $"HP {hp / 10f:0.#}%";
            box.hp.tooltip = "Its HP as your companion. A Mount Stim (Apothecary) heals it; at 0 it is knocked out, goes home and rests before it can be called again (it can still be ridden).";
            box.stats.text = $"{PlayerMounts.StatLine(mount.firstStat, mount.firstMax, growth, state.IsFed)}   " +
                             $"{PlayerMounts.StatLine(mount.secondStat, mount.secondMax, growth, state.IsFed)}" + RideLine(growth, state.IsFed);
        }

        private string RideLine(int growth, bool fed)
        {
            MountSettings settings = mounts.Settings;
            if (settings == null) return "";
            float walk = MountRules.Speed(settings.walkSpeed.x, settings.walkSpeed.y, growth, fed);
            float run = MountRules.Speed(settings.runSpeed.x, settings.runSpeed.y, growth, fed);
            return $"\nWalk {walk:0.#} m/s  ·  Run {run:0.#} m/s";
        }

        private static string Stat(KryticalNova.Progression.StatType stat) => KryticalNova.Progression.StatDisplay.Name(stat);
    }
}
