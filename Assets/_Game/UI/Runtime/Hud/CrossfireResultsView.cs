using KryticalNova.Crossfire;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// Crossfire's results screen (owner, 16 Nov 2026: after the war a 30-s results screen with the scores and the payout, then each player
    /// is ported back to exactly where they joined from). Shows the last results the server sent (<see cref="CrossfireView.Results"/>)
    /// until their time runs out; lettered like the kill counter (ProgressionHUD.uss).
    /// </summary>
    public sealed class CrossfireResultsView
    {
        readonly VisualElement layer;
        readonly Label title, score, pay;
        double shownFor = -1;
        bool shown;

        public CrossfireResultsView(VisualElement root)
        {
            layer = new VisualElement { name = "crossfire-results", pickingMode = PickingMode.Ignore };
            layer.AddToClassList("crossfire-results");
            title = Line("crossfire-results-title");
            score = Line("crossfire-results-score");
            pay = Line("crossfire-results-pay");
            root.Add(layer);
            Show(false);
        }

        Label Line(string cls)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            layer.Add(label);
            return label;
        }

        /// <summary>"HEGEMONY 12   NULL 9   COVENANT 14" (pure, for tests).</summary>
        public static string ScoreText(in CrossfireResults r) =>
            CrossfireScoreView.Text("HEGEMONY", r.KillsHegemony) + "     " + CrossfireScoreView.Text("NULL", r.KillsNull) + "     "
            + CrossfireScoreView.Text("COVENANT", r.KillsCovenant);

        /// <summary>The screen is up: results were received and their time has not run out.</summary>
        public static bool Wanted(in CrossfireResults r, double realtime) => r.ShownUntil > 0 && realtime < r.ShownUntil;

        public void Tick()
        {
            CrossfireResults r = CrossfireView.Results;
            bool want = Wanted(r, Time.realtimeSinceStartupAsDouble);
            if (want != shown) Show(want);
            if (!want || shownFor.Equals(r.ShownUntil)) return;
            shownFor = r.ShownUntil;
            title.text = r.Title ?? string.Empty;
            score.text = ScoreText(r);
            pay.text = r.Pay ?? string.Empty;
            pay.style.display = string.IsNullOrEmpty(r.Pay) ? DisplayStyle.None : DisplayStyle.Flex;
            layer.EnableInClassList("crossfire-results--won", r.Result == CrossfireResultKind.Won);
            layer.EnableInClassList("crossfire-results--lost", r.Result == CrossfireResultKind.Lost);
        }

        void Show(bool on)
        {
            shown = on;
            layer.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) shownFor = -1;
        }

        public void Dispose() => layer.RemoveFromHierarchy();
    }
}
