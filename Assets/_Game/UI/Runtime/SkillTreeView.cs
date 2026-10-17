using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Arsenal (the Skills window) laid out as a tree (first version 11 Oct 2026; tabs and groups 15 Oct 2026):
    /// three tabs, STRIKES (attacks), WARDS (buffs, stances, guards, channels) and STRIDES (movement), each a board of
    /// tiles on tiers by the level that opens them, under headed groups across the top (AREA, SINGLE HIT, TWO-HIT and
    /// THREE-HIT for the attacks; STANCE, GUARD and SHARED for the wards; RUN and JUMP for the strides), with lines
    /// along each line of skills, and the card of the tile that is chosen read beside the board. The lines show the
    /// path by level: they are not requirements, and nothing about how a skill is learned changes here.
    /// <see cref="ProgressionHUD"/> says which skills there are, where each stands and how, and still owns the cards,
    /// the upgrade buttons and the drag to the Hotbar; this only draws the tabs, the groups and the tiles and keeps
    /// the one chosen.
    /// </summary>
    internal sealed class SkillTreeView
    {
        /// <summary>On the Skills window while it is a tree: the wide layout hangs on it.</summary>
        public const string WindowClass = "skill-tree";

        public const float LabelWidth = 44f, CellWidth = 64f, CellHeight = 84f, TileSize = 52f, HeaderHeight = 22f;
        private const string TabClass = "skill-tab", ActiveTabClass = "skill-tab--active";

        /// <summary>The tabs of the Arsenal, in order.</summary>
        public enum Tab { Strikes = 0, Wards = 1, Strides = 2 }
        public static readonly string[] TabNames = { "STRIKES", "WARDS", "STRIDES" };

        /// <summary>A headed group of columns across the top of the board.</summary>
        public struct Group
        {
            public string Label;
            /// <summary>The first column of the group and how many columns it spans.</summary>
            public float Column, Width;
        }

        public struct Node
        {
            public string Key, Name, Rank;
            public Sprite Icon;
            /// <summary>Where it stands: the column (halves allowed) and the tier.</summary>
            public float Column;
            public int Row;
            public bool Learned, Locked, TooLow, CanUpgrade;
            /// <summary>What the tile says on hover; its name when empty.</summary>
            public string Tooltip;
            /// <summary>Its card in the list beside the tree.</summary>
            public VisualElement Card;
        }

        private sealed class Parts
        {
            public VisualElement Root, Tile, Icon;
            public Label Rank, Name;
        }

        private readonly Action<VisualElement> bindDrag;
        private readonly Dictionary<string, Parts> parts = new Dictionary<string, Parts>();
        private readonly List<Node> shown = new List<Node>();
        private readonly List<(string from, string to)> edges = new List<(string, string)>();
        private readonly List<Group> groups = new List<Group>();
        private readonly Button[] tabButtons = new Button[TabNames.Length];
        private string[] tiers = Array.Empty<string>();
        private VisualElement canvas, lines;
        private Label title;
        private string signature;

        /// <summary>The key of the tile whose card is read, or null before anything is shown.</summary>
        public string Selected { get; private set; }

        /// <summary>The tab on show. It is remembered while the game runs, so the window opens where it was left.</summary>
        public Tab Current { get; private set; } = Tab.Strikes;

        /// <summary>A tile was pressed (it is chosen by then): the key, the press, and the tile, which may start a drag.</summary>
        public event Action<string, PointerDownEvent, VisualElement> Pressed;

        /// <summary>Another tab was chosen: the HUD shows that tab's skills.</summary>
        public event Action<Tab> TabChanged;

        /// <param name="bindDrag">Called for every tile made: the HUD makes it a source of drags to the Hotbar.</param>
        public SkillTreeView(Action<VisualElement> bindDrag) => this.bindDrag = bindDrag;

        /// <summary>Puts the tree into the window, before the list of cards, which becomes the column beside it.</summary>
        public void Build(VisualElement window, VisualElement cards)
        {
            window.AddToClassList(WindowClass);
            var layout = new VisualElement { name = "skill-tree-layout" };
            layout.AddToClassList("skill-tree-layout");
            window.hierarchy.Insert(window.hierarchy.IndexOf(cards), layout);

            var side = new VisualElement();
            side.AddToClassList("skill-tree-side");
            var tabs = new VisualElement { name = "skill-tabs" };
            tabs.AddToClassList("skill-tabs");
            for (int i = 0; i < TabNames.Length; i++)
            {
                Tab tab = (Tab)i;
                var button = new Button(() => SetTab(tab)) { name = "skill-tab-" + TabNames[i].ToLowerInvariant(), text = TabNames[i] };
                button.AddToClassList(TabClass);
                tabs.Add(button);
                tabButtons[i] = button;
            }
            side.Add(tabs);
            title = new Label { name = "skill-tree-title", pickingMode = PickingMode.Ignore };
            title.AddToClassList("skill-tree-title");
            title.AddToClassList("accent");
            side.Add(title);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "skill-tree", horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("skill-tree-scroll");
            canvas = new VisualElement { name = "skill-tree-canvas" };
            canvas.AddToClassList("skill-tree-canvas");
            scroll.Add(canvas);
            side.Add(scroll);
            layout.Add(side);
            layout.Add(cards);
            ApplyTab();
        }

        /// <summary>Switches to a tab. The HUD then shows that tab's skills through <see cref="Show"/>.</summary>
        public void SetTab(Tab tab)
        {
            if (tab == Current) return;
            // The card read on the old tab goes with it; the new tab chooses its own.
            foreach (Node node in shown) if (node.Card != null) node.Card.style.display = DisplayStyle.None;
            Current = tab;
            Selected = null;
            ApplyTab();
            TabChanged?.Invoke(tab);
        }

        private void ApplyTab()
        {
            for (int i = 0; i < tabButtons.Length; i++)
                tabButtons[i]?.EnableInClassList(ActiveTabClass, i == (int)Current);
        }

        /// <summary>
        /// Shows these skills under these groups, on these tiers, and the lines between them. The tiles are made again
        /// only when the skills themselves are others (another tab, another weapon class in hand); otherwise each is
        /// brought up to date where it stands.
        /// </summary>
        public void Show(string heading, IReadOnlyList<string> tierNames, IReadOnlyList<Group> columnGroups,
            IReadOnlyList<Node> nodes, IReadOnlyList<(string from, string to)> links)
        {
            if (canvas == null) return;
            if (title != null) title.text = heading;
            var keys = new System.Text.StringBuilder();
            keys.Append((int)Current).Append('|');
            foreach (string tier in tierNames) keys.Append(tier).Append(',');
            keys.Append('|');
            foreach (Group group in columnGroups) keys.Append(group.Label).Append('@').Append(group.Column).Append('+').Append(group.Width).Append(';');
            keys.Append('|');
            foreach (Node node in nodes) keys.Append(node.Key).Append('@').Append(node.Column).Append(',').Append(node.Row).Append(';');
            string now = keys.ToString();
            shown.Clear();
            shown.AddRange(nodes);
            edges.Clear();
            edges.AddRange(links);
            groups.Clear();
            groups.AddRange(columnGroups);
            tiers = new string[tierNames.Count];
            for (int i = 0; i < tiers.Length; i++) tiers[i] = tierNames[i];
            if (now != signature)
            {
                signature = now;
                Rebuild();
            }
            foreach (Node node in shown) Draw(node);
            if (Selected == null || !parts.ContainsKey(Selected)) Select(FirstChoice());
            ApplySelection();
            lines?.MarkDirtyRepaint();
        }

        // The first tile that can be learned further, or the first there is.
        private string FirstChoice()
        {
            foreach (Node node in shown) if (node.CanUpgrade) return node.Key;
            return shown.Count > 0 ? shown[0].Key : null;
        }

        private float Top => groups.Count > 0 ? HeaderHeight + 4f : 4f;

        private void Rebuild()
        {
            canvas.Clear();
            parts.Clear();
            float widest = 0f;
            foreach (Node node in shown) widest = Mathf.Max(widest, node.Column);
            foreach (Group group in groups) widest = Mathf.Max(widest, group.Column + group.Width - 1f);
            float top = Top;
            canvas.style.width = LabelWidth + (widest + 1f) * CellWidth + 6f;
            canvas.style.height = top + tiers.Length * CellHeight;

            // The groups' headings across the top, each over its own columns.
            foreach (Group group in groups)
            {
                var head = new Label(group.Label) { pickingMode = PickingMode.Ignore };
                head.AddToClassList("skill-tree-group");
                head.style.left = LabelWidth + group.Column * CellWidth;
                head.style.width = group.Width * CellWidth;
                head.style.top = 0f;
                head.style.height = HeaderHeight;
                canvas.Add(head);
            }

            for (int row = 0; row < tiers.Length; row++)
            {
                var band = new VisualElement { pickingMode = PickingMode.Ignore };
                band.AddToClassList("skill-tree-row");
                band.EnableInClassList("alternate", row % 2 == 1);
                band.style.top = top + row * CellHeight;
                band.style.height = CellHeight;
                var tier = new Label(tiers[row]) { pickingMode = PickingMode.Ignore };
                tier.AddToClassList("skill-tree-tier");
                band.Add(tier);
                canvas.Add(band);
            }

            lines = new VisualElement { pickingMode = PickingMode.Ignore };
            lines.AddToClassList("skill-tree-lines");
            lines.generateVisualContent += DrawLines;
            canvas.Add(lines);

            foreach (Node node in shown)
            {
                string key = node.Key;
                var made = new Parts { Root = new VisualElement() };
                made.Root.AddToClassList("skill-node");
                made.Root.style.left = LabelWidth + node.Column * CellWidth;
                made.Root.style.top = top + node.Row * CellHeight;
                made.Root.style.width = CellWidth;
                made.Root.style.height = CellHeight;
                made.Tile = new VisualElement { name = "skill-node-" + key };
                made.Tile.AddToClassList("skill-node-tile");
                made.Icon = new VisualElement { pickingMode = PickingMode.Ignore };
                made.Icon.AddToClassList("skill-node-icon");
                made.Tile.Add(made.Icon);
                made.Rank = new Label { pickingMode = PickingMode.Ignore };
                made.Rank.AddToClassList("skill-node-rank");
                made.Tile.Add(made.Rank);
                made.Root.Add(made.Tile);
                made.Name = new Label { pickingMode = PickingMode.Ignore };
                made.Name.AddToClassList("skill-node-name");
                made.Root.Add(made.Name);
                made.Tile.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    Select(key);
                    Pressed?.Invoke(key, evt, made.Tile);
                });
                bindDrag?.Invoke(made.Tile);
                canvas.Add(made.Root);
                parts[key] = made;
            }
        }

        private void Draw(in Node node)
        {
            if (!parts.TryGetValue(node.Key, out Parts made)) return;
            made.Icon.style.backgroundImage = node.Icon != null ? new StyleBackground(node.Icon) : StyleKeyword.None;
            made.Rank.text = node.Rank ?? string.Empty;
            made.Rank.style.display = string.IsNullOrEmpty(node.Rank) ? DisplayStyle.None : DisplayStyle.Flex;
            made.Name.text = node.Name;
            made.Tile.tooltip = string.IsNullOrEmpty(node.Tooltip) ? node.Name : node.Tooltip;
            made.Root.EnableInClassList("learned", node.Learned);
            made.Root.EnableInClassList("unlearned", !node.Learned);
            made.Root.EnableInClassList("locked", node.Locked);
            made.Root.EnableInClassList("too-low", node.TooLow && !node.Learned);
            made.Root.EnableInClassList("can-upgrade", node.CanUpgrade);
        }

        public void Select(string key)
        {
            if (key == null || key == Selected) return;
            Selected = key;
            ApplySelection();
        }

        /// <summary>Only the chosen tile's card is read beside the tree. Run again after anything else shows or hides cards.</summary>
        public void ApplySelection()
        {
            foreach (Node node in shown)
            {
                bool chosen = node.Key == Selected;
                if (parts.TryGetValue(node.Key, out Parts made)) made.Root.EnableInClassList("selected", chosen);
                if (node.Card != null) node.Card.style.display = chosen ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private Vector2 Centre(in Node node) =>
            new Vector2(LabelWidth + (node.Column + 0.5f) * CellWidth, Top + node.Row * CellHeight + 6f + TileSize * 0.5f);

        private bool TryFind(string key, out Node node)
        {
            foreach (Node each in shown)
                if (each.Key == key) { node = each; return true; }
            node = default;
            return false;
        }

        // A line along each line of skills: lit as far as it has been learned, in the colour of the heading over the tree.
        private void DrawLines(MeshGenerationContext context)
        {
            Color lit = title != null ? title.resolvedStyle.color : Color.white;
            var dim = new Color(1f, 1f, 1f, 0.16f);
            Painter2D painter = context.painter2D;
            painter.lineWidth = 3f;
            painter.lineCap = LineCap.Round;
            foreach ((string from, string to) in edges)
            {
                if (!TryFind(from, out Node a) || !TryFind(to, out Node b)) continue;
                painter.strokeColor = a.Learned && b.Learned ? lit : dim;
                painter.BeginPath();
                painter.MoveTo(Centre(a));
                painter.LineTo(Centre(b));
                painter.Stroke();
            }
        }
    }
}
