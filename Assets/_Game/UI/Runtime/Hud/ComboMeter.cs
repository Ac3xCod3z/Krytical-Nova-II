using KryticalNova.Combat;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// The Cadence combo on the screen (owner, 23 Oct 2026), lettered like the comic words of a fight: "COMBO" and the
    /// step (×1, ×2, ×3) punch in at the right of the screen when a skill lands on the beat in its place, a bar under
    /// them runs down from right to left over the time left for the next step, and when it runs out or the chain is
    /// broken the word drops away. With Redline waiting it says so, and the third step reads REDLINE!. Each step hits
    /// harder than the one before. A miss is never shown: the word simply goes. The server decides the combo
    /// (<see cref="PlayerCadence.ServerCombo"/>); this only shows what the player's own game is told.
    /// </summary>
    public sealed class ComboMeter
    {
        private const float PunchSeconds = 0.28f;
        private const float LeaveSeconds = 0.3f;
        private static readonly string[] StepClasses = { "combo--1", "combo--2", "combo--3" };

        private readonly VisualElement layer, fill;
        private readonly Label count, tag;
        private int step;
        private float started = -10f, ends = -10f, length = 2f, punchAt = -10f, leftAt = -10f;
        private bool redline, leaving, shown;

        public ComboMeter(VisualElement root)
        {
            layer = new VisualElement { name = "combo-meter", pickingMode = PickingMode.Ignore };
            layer.AddToClassList("combo");
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("combo-line");
            var word = new Label("COMBO") { pickingMode = PickingMode.Ignore };
            word.AddToClassList("combo-word");
            count = new Label { pickingMode = PickingMode.Ignore };
            count.AddToClassList("combo-count");
            line.Add(word);
            line.Add(count);
            var timer = new VisualElement { pickingMode = PickingMode.Ignore };
            timer.AddToClassList("combo-timer");
            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("combo-timer-fill");
            timer.Add(fill);
            tag = new Label { pickingMode = PickingMode.Ignore };
            tag.AddToClassList("combo-tag");
            layer.Add(line);
            layer.Add(timer);
            layer.Add(tag);
            root.Add(layer);
            Hide();
            PlayerCadence.LocalCombo += OnCombo;
        }

        /// <summary>Stops listening (the HUD was switched off).</summary>
        public void Dispose()
        {
            PlayerCadence.LocalCombo -= OnCombo;
            layer.RemoveFromHierarchy();
        }

        private void OnCombo(int next, float seconds, bool withRedline)
        {
            float now = Time.unscaledTime;
            if (next <= 0)
            {
                if (shown && !leaving) Leave(now);
                return;
            }
            if (!AudioPreferences.ComboMeter) return;
            step = Mathf.Clamp(next, 1, CadenceCombo.Top);
            redline = withRedline;
            started = now;
            length = Mathf.Max(0.1f, seconds);
            ends = now + length;
            punchAt = now;
            leaving = false;
            shown = true;
            layer.style.display = DisplayStyle.Flex;
            layer.style.opacity = 1f;
            layer.BringToFront(); // over the HUD's other parts, never washed out under a panel
            for (int i = 0; i < StepClasses.Length; i++) layer.EnableInClassList(StepClasses[i], i == step - 1);
            bool top = step == CadenceCombo.Top;
            layer.EnableInClassList("combo--redline", redline);
            count.text = "×" + step;
            tag.text = redline ? (top ? "REDLINE!" : "REDLINE HELD") : string.Empty;
            tag.style.display = redline ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Called by the HUD every frame.</summary>
        public void Tick()
        {
            if (!shown) return;
            float now = Time.unscaledTime;
            if (!leaving && now >= ends) Leave(now);

            // The bar: full when the step lands, empty when the next must have come; it shrinks towards the left.
            float left = leaving ? 0f : Mathf.Clamp01((ends - now) / length);
            fill.style.width = Length.Percent(left * 100f);
            fill.EnableInClassList("combo-timer-fill--low", left < 0.3f);

            // The punch: every step a little bigger than the one before.
            float t = Mathf.Clamp01((now - punchAt) / PunchSeconds);
            float from = 1.5f + 0.35f * (step - 1);
            float scale = Mathf.LerpUnclamped(from, 1f, EaseOutBack(t));
            count.style.scale = new Scale(new Vector3(scale, scale, 1f));
            float shake = (1f - t) * (2f + 3f * step) * KryticalNova.Core.Accessibility.FlashScale;
            float wobble = Mathf.Sin((now - punchAt) * 70f) * shake;
            layer.style.translate = new Translate(wobble, -wobble * 0.4f);

            if (!leaving) return;
            float gone = Mathf.Clamp01((now - leftAt) / LeaveSeconds);
            layer.style.opacity = 1f - gone;
            float drop = Mathf.Lerp(1f, 0.85f, gone);
            layer.style.scale = new Scale(new Vector3(drop, drop, 1f));
            if (gone >= 1f) Hide();
        }

        private void Leave(float now)
        {
            leaving = true;
            leftAt = now;
        }

        private void Hide()
        {
            shown = leaving = false;
            step = 0;
            layer.style.display = DisplayStyle.None;
            layer.style.opacity = 0f;
            layer.style.scale = new Scale(Vector3.one);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
