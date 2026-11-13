using KryticalNova.Core;
using KryticalNova.Crossfire;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// Crossfire on the HUD (build 17, Crossfire step 5b): the call banner "Crossfire at nightfall - Bracket IV", repeated through the
    /// Call, and a small timer under the top of the screen (war in, gates open in, time left, Crossfire over). It is drawn with the
    /// shared .panel, eyebrow, heading and timer classes, so each player sees it in their own faction's skin. It reads only
    /// <see cref="CrossfireView.Client"/> and <see cref="NetClock.Now"/>; the words are <see cref="CrossfireHudText"/>.
    /// </summary>
    public sealed class CrossfireBanner
    {
        const float RefreshSeconds = 0.25f;
        const string BigClass = "crossfire-banner--big";

        readonly VisualElement panel, fill;
        readonly Label heading, timer;
        float nextRefresh;

        public CrossfireBanner(VisualElement root)
        {
            panel = new VisualElement { name = "crossfire-banner", pickingMode = PickingMode.Ignore };
            panel.AddToClassList("crossfire-banner");
            panel.AddToClassList("panel");
            var eyebrow = new Label("CROSSFIRE") { pickingMode = PickingMode.Ignore };
            eyebrow.AddToClassList("eyebrow");
            heading = new Label { pickingMode = PickingMode.Ignore };
            heading.AddToClassList("heading");
            heading.AddToClassList("crossfire-banner-heading");
            timer = new Label { pickingMode = PickingMode.Ignore };
            timer.AddToClassList("crossfire-banner-timer");
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("duel-timer");
            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("duel-timer-fill");
            bar.Add(fill);
            panel.Add(eyebrow);
            panel.Add(heading);
            panel.Add(timer);
            panel.Add(bar);
            root.Add(panel);
            panel.style.display = DisplayStyle.None;
        }

        public void Dispose() => panel.RemoveFromHierarchy();

        public void Tick()
        {
            float now = Time.unscaledTime;
            if (now < nextRefresh) return;
            nextRefresh = now + RefreshSeconds;

            CrossfireSettings settings = CrossfireSettings.Instance;
            var progression = LocalPlayer.Get<PlayerCharacterProgression>();
            int level = progression != null ? progression.CurrentLevel : 0;
            CrossfireHudLine line = settings != null
                ? CrossfireHudText.At(CrossfireView.Client, NetClock.Now, level, settings.bannerSeconds)
                : default;

            panel.style.display = line.Show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!line.Show) return;
            panel.EnableInClassList(BigClass, line.Banner);
            heading.style.display = line.Banner ? DisplayStyle.Flex : DisplayStyle.None;
            heading.text = line.Heading;
            timer.text = line.Timer;
            fill.style.width = Length.Percent(100f * line.Left);
        }
    }
}
