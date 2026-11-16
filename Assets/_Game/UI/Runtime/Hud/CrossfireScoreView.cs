using KryticalNova.Core;
using KryticalNova.Crossfire;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// Crossfire's kill counter at the top of the screen (owner, 16 Nov 2026: a team deathmatch, a counter per side; the most kills at
    /// the War's end wins, a tie is possible). Shown while the War and its End run and the player stands in the war area; the numbers
    /// are the server's (<see cref="CrossfireView.Client"/>). Lettered like the Callout's comic words (ProgressionHUD.uss).
    /// </summary>
    public sealed class CrossfireScoreView
    {
        readonly VisualElement layer;
        readonly Label hegemony, nul, covenant;
        int shownH = -1, shownN = -1, shownC = -1;
        bool shown;

        public CrossfireScoreView(VisualElement root)
        {
            layer = new VisualElement { name = "crossfire-score", pickingMode = PickingMode.Ignore };
            layer.AddToClassList("crossfire-score");
            hegemony = Side("crossfire-score--hegemony");
            nul = Side("crossfire-score--null");
            covenant = Side("crossfire-score--covenant");
            root.Add(layer);
            Show(false);
        }

        Label Side(string side)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("crossfire-score-side");
            label.AddToClassList(side);
            layer.Add(label);
            return label;
        }

        /// <summary>The words of the counter: "HEGEMONY 12   NULL 9   COVENANT 14" (pure, for tests).</summary>
        public static string Text(string name, int kills) => name + "  " + kills;

        /// <summary>The counter is up: a war's War or End, and the player is in the war area.</summary>
        public static bool Wanted(in CrossfireState s, bool inWarArea) =>
            s.Known && inWarArea && (s.Now.Phase == CrossfirePhase.War || s.Now.Phase == CrossfirePhase.End);

        public void Tick()
        {
            CrossfireState s = CrossfireView.Client;
            bool want = Wanted(s, LocalInWarArea());
            if (want != shown) Show(want);
            if (!want) return;
            if (s.KillsHegemony != shownH) hegemony.text = Text("HEGEMONY", shownH = s.KillsHegemony);
            if (s.KillsNull != shownN) nul.text = Text("NULL", shownN = s.KillsNull);
            if (s.KillsCovenant != shownC) covenant.text = Text("COVENANT", shownC = s.KillsCovenant);
        }

        static bool LocalInWarArea()
        {
            foreach (IPlayerBody body in PlayerBodies.All)
                if (body != null && body.IsLocal && body.Transform != null) return CrossfireArea.AnyContains(body.Transform.position, false);
            return false;
        }

        void Show(bool on)
        {
            shown = on;
            layer.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) shownH = shownN = shownC = -1;
        }

        public void Dispose() => layer.RemoveFromHierarchy();
    }
}
