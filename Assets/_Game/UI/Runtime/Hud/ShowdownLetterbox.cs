using System.Collections.Generic;
using KryticalNova.Combat;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// The Showdown (owner, 22 Oct 2026): a Callout played like a film. When this player's Callout starts counting
    /// down, black bars slide in from the top and the bottom of the screen and a big 3 · 2 · 1 punches in the middle;
    /// on the start "CALLOUT!" slams in and the bars keep their countdown height for the whole bout (owner, build 17:
    /// no pull-back at the start). The player status frame and the Hotbar ease away from the bars with them, on the
    /// same curve, as an offset on top of the player's saved layout that is never saved. They
    /// slide away only when the bout ends: a player won, or someone left it (forfeit, walked off, left the game). A
    /// countdown called off slides them straight away. The bars are drawn behind the rest of the HUD, so the Hotbar,
    /// the target frames and the Callout's own panels always stay on top of them; the chat and the minimap dim while
    /// they are up. The server decides the Callout (<see cref="PlayerDuel"/>); this only shows it.
    /// </summary>
    public sealed class ShowdownLetterbox
    {
        private const float CountdownShare = 0.11f;  // each bar's height during the 3 · 2 · 1, as a share of the screen
        private const float HudSlideShare = 0.025f;  // how far the status frame and Hotbar ease away from the bars, at full bar height
        private const float SlideInSeconds = 0.18f;  // smoothing of the bars (a critically damped spring)
        private const float SlideOutSeconds = 0.28f;
        private const float PunchSeconds = 0.3f;
        private const float TitleSeconds = 1.3f;
        private const string DimClass = "showdown-dim";

        private enum Phase { Idle, Countdown, Bout, Leaving }

        private readonly VisualElement root, layer, top, bottom;
        private readonly Label count, title;
        private Phase phase = Phase.Idle;
        private float share, shareSpeed, target;
        private float countdownEnds;
        private int shownNumber;
        private float countPunchAt = -10f, titleAt = -10f;
        private bool wasDuelling;
        private readonly HudSlide slide = new HudSlide();

        public ShowdownLetterbox(VisualElement root)
        {
            this.root = root;
            layer = new VisualElement { name = "showdown", pickingMode = PickingMode.Ignore };
            layer.AddToClassList("showdown");
            top = Bar("showdown-bar--top");
            bottom = Bar("showdown-bar--bottom");
            count = new Label { pickingMode = PickingMode.Ignore };
            count.AddToClassList("showdown-count");
            title = new Label("CALLOUT!") { pickingMode = PickingMode.Ignore };
            title.AddToClassList("showdown-title");
            layer.Add(top);
            layer.Add(bottom);
            layer.Add(count);
            layer.Add(title);
            // Behind everything else of the HUD: the HUD's own parts are drawn over the bars.
            root.Insert(0, layer);
            Hide();
            PlayerDuel.LocalCountdownStarted += OnCountdown;
            PlayerDuel.LocalCountdownCancelled += OnCancelled;
        }

        /// <summary>Stops listening (the HUD was switched off); the bars go at once.</summary>
        public void Dispose()
        {
            PlayerDuel.LocalCountdownStarted -= OnCountdown;
            PlayerDuel.LocalCountdownCancelled -= OnCancelled;
            layer.RemoveFromHierarchy();
            slide.Release();
            Dim(false);
        }

        private VisualElement Bar(string side)
        {
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("showdown-bar");
            bar.AddToClassList(side);
            return bar;
        }

        private void OnCountdown(int seconds, string opponent)
        {
            phase = Phase.Countdown;
            countdownEnds = Time.unscaledTime + Mathf.Max(1, seconds);
            shownNumber = -1;
            target = CountdownShare;
            layer.style.display = DisplayStyle.Flex;
            title.style.opacity = 0f;
            Dim(true);
        }

        private void OnCancelled()
        {
            if (phase == Phase.Countdown) Leave();
        }

        private void Leave()
        {
            phase = Phase.Leaving;
            target = 0f;
            count.style.opacity = 0f;
        }

        /// <summary>Called by the Callout HUD every frame.</summary>
        public void Tick()
        {
            float now = Time.unscaledTime;
            PlayerDuel mine = PlayerDuel.Local;
            bool duelling = mine != null && mine.IsDuelling;

            // The bout began (after the countdown, or at once from a GM tool): the title slams in.
            if (duelling && !wasDuelling)
            {
                phase = Phase.Bout;
                target = CountdownShare;
                share = Mathf.Max(share, CountdownShare * 1.15f); // a small punch on the bars as it starts
                layer.style.display = DisplayStyle.Flex;
                titleAt = now;
                count.style.opacity = 0f;
                Dim(true);
            }
            // The bout ended, however it ended: the bars slide away.
            else if (!duelling && wasDuelling && phase == Phase.Bout) Leave();
            wasDuelling = duelling;

            if (phase == Phase.Idle) return;

            if (phase == Phase.Countdown)
            {
                int number = Mathf.Max(1, Mathf.CeilToInt(countdownEnds - now));
                if (number != shownNumber)
                {
                    shownNumber = number;
                    count.text = number.ToString();
                    countPunchAt = now;
                }
            }
            float punch = Mathf.Clamp01((now - countPunchAt) / PunchSeconds);
            if (phase == Phase.Countdown)
            {
                count.style.opacity = 1f - Mathf.Clamp01((now - countPunchAt - 0.7f) / 0.3f);
                float scale = Mathf.Lerp(1.8f, 1f, EaseOutBack(punch));
                count.style.scale = new Scale(new Vector3(scale, scale, 1f));
            }
            float sinceTitle = now - titleAt;
            if (sinceTitle >= 0f && sinceTitle < TitleSeconds)
            {
                float t = Mathf.Clamp01(sinceTitle / PunchSeconds);
                float scale = Mathf.Lerp(2.2f, 1f, EaseOutBack(t));
                title.style.scale = new Scale(new Vector3(scale, scale, 1f));
                title.style.opacity = 1f - Mathf.Clamp01((sinceTitle - (TitleSeconds - 0.35f)) / 0.35f);
            }
            else title.style.opacity = 0f;

            share = Mathf.SmoothDamp(share, target, ref shareSpeed, phase == Phase.Leaving ? SlideOutSeconds : SlideInSeconds,
                                     Mathf.Infinity, Time.unscaledDeltaTime);
            top.style.height = Length.Percent(share * 100f);
            bottom.style.height = Length.Percent(share * 100f);
            bool editing = HudLayoutEditor.Current != null && HudLayoutEditor.Current.IsEditing;
            slide.Apply(root, HudSlide.Offset(share, CountdownShare, HudSlideShare, editing));
            if (phase == Phase.Leaving && share < 0.002f) Hide();
        }

        private void Hide()
        {
            phase = Phase.Idle;
            share = shareSpeed = target = 0f;
            top.style.height = bottom.style.height = Length.Percent(0f);
            count.style.opacity = 0f;
            title.style.opacity = 0f;
            layer.style.display = DisplayStyle.None;
            slide.Release();
            Dim(false);
        }

        // The chat and the minimap step back while the bars are up.
        private void Dim(bool on)
        {
            root.Q("chat-window")?.EnableInClassList(DimClass, on);
            root.Q(className: "minimap")?.EnableInClassList(DimClass, on);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }

    /// <summary>
    /// Eases the player status frame and the Hotbar away from the Showdown bars: a panel in the top half of the
    /// screen moves down with the top bar, one in the bottom half up with the bottom bar. It is drawn with inline
    /// margins, which the layout editor never reads or writes, so the player's saved place (left / top / translate /
    /// scale, <see cref="HudPreferences.LayoutJson"/>) is untouched; while the layout editor is open the offset is
    /// nothing, so a panel moved during a bout is saved where it really is.
    /// </summary>
    public sealed class HudSlide
    {
        public static readonly string[] Panels = { "summary", "hotbar-dock" };
        private readonly List<VisualElement> moved = new List<VisualElement>(2);

        /// <summary>The offset as a share of the screen height: follows the bars' height, nothing while editing.</summary>
        public static float Offset(float barShare, float fullBarShare, float slideShare, bool editing)
        {
            if (editing || fullBarShare <= 0f || barShare <= 0f) return 0f;
            return slideShare * Mathf.Clamp(barShare / fullBarShare, 0f, 1.2f);
        }

        /// <summary>+1 moves down (a panel above the middle), -1 up.</summary>
        public static float Direction(float centreY, float screenHeight) => centreY < screenHeight * 0.5f ? 1f : -1f;

        public void Apply(VisualElement root, float offsetShare)
        {
            if (offsetShare <= 0f) { Release(); return; }
            VisualElement tree = root.panel?.visualTree ?? root;
            float height = tree.layout.height;
            if (!(height > 1f)) return;
            foreach (string name in Panels)
            {
                VisualElement panel = tree.Q(name);
                if (panel == null) continue;
                if (!moved.Contains(panel)) moved.Add(panel);
                float px = offsetShare * height * Direction(panel.worldBound.center.y, height);
                // Down by px whether the panel is anchored by its top or its bottom.
                panel.style.marginTop = px;
                panel.style.marginBottom = -px;
            }
        }

        /// <summary>Gives every panel back its own margins: exactly the saved spot again.</summary>
        public void Release()
        {
            foreach (VisualElement panel in moved)
                panel.style.marginTop = panel.style.marginBottom = StyleKeyword.Null;
            moved.Clear();
        }
    }
}
