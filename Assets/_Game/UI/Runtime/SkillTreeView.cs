using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Skills window laid out as a tree (first version, 11 Oct 2026): the skills of the weapon class in hand and
    /// the skills every class shares stand as tiles on tiers by the level that opens them, with lines along each
    /// line of skills, and the card of the tile that is chosen is read beside the tree. The lines show the path by
    /// level: they are not requirements, and nothing about how a skill is learned changes here.
    /// <see cref="ProgressionHUD"/> says which skills there are and how each stands, and still owns the cards, the
    /// upgrade buttons and the drag to the Hotbar; this only draws the tiles and keeps the one chosen.
    /// </summary>
    internal sealed class SkillTreeView
    {
        /// <summary>On the Skills window while it is a tree: the wide layout hangs on it.</summary>
        public const string WindowClass = "skill-tree";

        public const float LabelWidth = 44f, CellWidth = 64f, CellHeight = 84f, TileSize = 52f, Top = 4f;

        /// <summary>The tiers, from the top: the level that opens each, and the movement skills under them.</summary>
        public static readonly string[] Tiers = { "LV 1", "LV 30", "LV 60", "LV 70", "LV 80", "MOVE" };

        public struct Node
        {
            public string Key, Name, Rank;
            public Sprite Icon;
            /// <summary>Where it stands: the column (halves allowed) and the tier.</summary>
            public float Column;
            public int Row;
            public bool Learned, Locked, TooLow, CanUpgrade;
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
        private VisualElement canvas, lines;
        private Label title;
        private string signature;

        /// <summary>The key of the tile whose card is read, or null before anything is shown.</summary>
        public string Selected { get; private set; }

        /// <summary>A tile was pressed (it is chosen by then): the key, the press, and the tile, which may start a drag.</summary>
        public event Action<string, PointerDownEvent, VisualElement> Pressed;

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
        }

        /// <summary>
        /// Shows these skills, and the lines between them. The tiles are made again only when the skills themselves
        /// are others (another weapon class in hand); otherwise each is brought up to date where it stands.
        /// </summary>
        public void Show(string heading, IReadOnlyList<Node> nodes, IReadOnlyList<(string from, string to)> links)
        {
            if (canvas == null) return;
            if (title != null) title.text = heading;
            var keys = new System.Text.StringBuilder();
            foreach (Node node in nodes) keys.Append(node.Key).Append('@').Append(node.Column).Append(',').Append(node.Row).Append(';');
            string now = keys.ToString();
            shown.Clear();
            shown.AddRange(nodes);
            edges.Clear();
            edges.AddRange(links);
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

        private void Rebuild()
        {
            canvas.Clear();
            parts.Clear();
            float widest = 0f;
            foreach (Node node in shown) widest = Mathf.Max(widest, node.Column);
            canvas.style.width = LabelWidth + (widest + 1f) * CellWidth + 6f;
            canvas.style.height = Top + Tiers.Length * CellHeight;

            for (int row = 0; row < Tiers.Length; row++)
            {
                var band = new VisualElement { pickingMode = PickingMode.Ignore };
                band.AddToClassList("skill-tree-row");
                band.EnableInClassList("alternate", row % 2 == 1);
                band.style.top = Top + row * CellHeight;
                band.style.height = CellHeight;
                var tier = new Label(Tiers[row]) { pickingMode = PickingMode.Ignore };
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
                made.Root.style.top = Top + node.Row * CellHeight;
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
            made.Tile.tooltip = node.Name;
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

        private static Vector2 Centre(in Node node) =>
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
