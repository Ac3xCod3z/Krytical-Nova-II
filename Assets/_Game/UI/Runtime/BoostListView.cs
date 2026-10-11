using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The running list at the side of the screen of what the player has switched on with an item and how long each
    /// has left: the XP Boosters today, and whatever is timed the same way later (time in a premium zone). One line
    /// each, "[Field Booster] 0:59:12"; nothing is drawn while nothing runs. It is text, not badges, on purpose
    /// (owner, 11 Oct 2026): these last for hours and are read at a glance, unlike a buff's seconds.
    /// Whoever has something timed adds a source; the list asks every source a few times a second.
    /// </summary>
    internal sealed class BoostListView
    {
        public struct Line
        {
            /// <summary>What is running, as the player knows it: the item's name.</summary>
            public string Name;
            public float SecondsLeft;
            /// <summary>What it does, for the tooltip.</summary>
            public string Detail;
        }

        private const float RefreshSeconds = 0.25f;

        private readonly VisualElement host;
        private readonly List<Action<List<Line>>> sources = new List<Action<List<Line>>>();
        private readonly List<Line> lines = new List<Line>();
        private readonly List<Label> labels = new List<Label>();
        private float nextRefresh;

        public BoostListView(VisualElement host) => this.host = host;

        /// <summary>Adds something that has timed boosts to show: it appends a line for each one running.</summary>
        public void AddSource(Action<List<Line>> source)
        {
            if (source != null) sources.Add(source);
        }

        /// <summary>Called every frame; redraws a few times a second, or at once when asked.</summary>
        public void Tick(bool now = false)
        {
            if (host == null || (!now && Time.unscaledTime < nextRefresh)) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            lines.Clear();
            foreach (Action<List<Line>> source in sources) source(lines);

            while (labels.Count < lines.Count)
            {
                var label = new Label { enableRichText = false };
                label.AddToClassList("boost-line");
                host.Add(label);
                labels.Add(label);
            }
            for (int i = 0; i < labels.Count; i++)
            {
                bool shown = i < lines.Count;
                labels[i].style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (!shown) continue;
                string text = $"[{lines[i].Name}] {Clock(lines[i].SecondsLeft)}";
                if (labels[i].text != text) labels[i].text = text;
                labels[i].tooltip = lines[i].Detail;
                // The last minute is told apart, so a booster does not run out unnoticed.
                labels[i].EnableInClassList("ending", lines[i].SecondsLeft <= 60f);
            }
            host.EnableInClassList("empty", lines.Count == 0);
        }

        /// <summary>Time left as a clock: "1:05:09", or "5:09" under an hour.</summary>
        public static string Clock(float seconds)
        {
            int whole = Mathf.Max(0, Mathf.CeilToInt(seconds));
            int hours = whole / 3600, minutes = whole / 60 % 60, rest = whole % 60;
            return hours > 0 ? $"{hours}:{minutes:00}:{rest:00}" : $"{minutes}:{rest:00}";
        }
    }
}
