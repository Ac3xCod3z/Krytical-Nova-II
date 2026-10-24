using KryticalNova.Audio;
using KryticalNova.Combat;
using KryticalNova.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// The beat track (owner, 24 Oct 2026: "I have no way to truly understand when to hit on beat"): a thin strip above
    /// the Hotbar, only in a fight, where the coming beats slide in from both sides and meet the centre mark on the beat,
    /// as rhythm games show it. The centre lights gold in the Cadence window and catches a press on the beat; the first
    /// beat of a bar is a taller cyan mark (the count-in; gold is kept for the window). A press just off the beat says, quietly, EARLY or LATE, so
    /// the player knows which way to move; never a red miss. Settings ▸ Audio switches it off.
    /// </summary>
    public sealed class BeatTrack
    {
        private const int Ahead = 3;               // beats shown coming in, each side
        private const float HintSeconds = 0.7f;
        private const float HintReach = 0.25f;     // a press further than this from the beat says nothing
        private const float CatchSeconds = 0.3f;

        private readonly VisualElement layer, centre, catchMark;
        private readonly VisualElement[] left = new VisualElement[Ahead], right = new VisualElement[Ahead];
        private readonly Label hint;
        private float hintAt = -10f, caughtAt = -10f;

        public BeatTrack(VisualElement root)
        {
            layer = new VisualElement { name = "beat-track", pickingMode = PickingMode.Ignore };
            layer.AddToClassList("beat-track");
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("beat-track-line");
            layer.Add(line);
            for (int i = 0; i < Ahead; i++)
            {
                left[i] = Mark();
                right[i] = Mark();
            }
            centre = new VisualElement { pickingMode = PickingMode.Ignore };
            centre.AddToClassList("beat-track-centre");
            layer.Add(centre);
            catchMark = new VisualElement { pickingMode = PickingMode.Ignore };
            catchMark.AddToClassList("beat-track-catch");
            layer.Add(catchMark);
            hint = new Label { pickingMode = PickingMode.Ignore };
            hint.AddToClassList("beat-track-hint");
            layer.Add(hint);
            root.Add(layer);
            layer.style.display = DisplayStyle.None;
            PlayerCadence.LocalPress += OnPress;
        }

        private VisualElement Mark()
        {
            var mark = new VisualElement { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("beat-track-mark");
            layer.Add(mark);
            return mark;
        }

        /// <summary>Stops listening (the HUD was switched off).</summary>
        public void Dispose()
        {
            PlayerCadence.LocalPress -= OnPress;
            layer.RemoveFromHierarchy();
        }

        private void OnPress(double error, bool onBeat)
        {
            float now = Time.unscaledTime;
            if (onBeat) { caughtAt = now; hint.text = string.Empty; return; }
            if (!AudioPreferences.BeatHint || System.Math.Abs(error) > HintReach) return;
            hint.text = error < 0 ? "EARLY" : "LATE";
            hintAt = now;
        }

        /// <summary>Called by the HUD every frame.</summary>
        public void Tick()
        {
            float live = AudioPreferences.BeatTrack ? CombatBeat.Live01 : 0f;
            bool shown = live > 0.01f && CombatBeat.HasBeat;
            layer.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (!shown) return;
            layer.style.opacity = live;

            float phase = CombatBeat.Phase01;
            MusicTrack track = CombatBeat.Track;
            for (int i = 0; i < Ahead; i++)
            {
                // Beat k ahead (1 is the next) sits (k - phase) / Ahead of the half-width out from the centre.
                float out01 = Mathf.Clamp01((i + 1 - phase) / Ahead);
                bool bar = AudioPreferences.BeatCountIn && track != null && BeatMath.IsBarBeat(CombatBeat.Index + i + 1, track.barBeat, track.beatsPerBar);
                Place(left[i], 50f - 48f * out01, out01, bar);
                Place(right[i], 50f + 48f * out01, out01, bar);
            }

            bool inWindow = false;
            PlayerCadence cadence = LocalPlayer.Get<PlayerCadence>();
            if (cadence != null && cadence.Settings != null && CombatBeat.TryError(out double error))
                inWindow = System.Math.Abs(error) <= cadence.Settings.WindowSeconds;
            centre.EnableInClassList("beat-track-centre--window", inWindow);

            float caught = 1f - Mathf.Clamp01((Time.unscaledTime - caughtAt) / CatchSeconds);
            catchMark.style.opacity = caught;
            float size = 1f + 1.2f * (1f - caught);
            catchMark.style.scale = new Scale(new Vector3(size, size, 1f));

            float sinceHint = Time.unscaledTime - hintAt;
            hint.style.opacity = sinceHint < HintSeconds ? 1f - sinceHint / HintSeconds : 0f;
        }

        // A mark at its place along the strip: it fades in at the edge and is solid as it nears the centre.
        private static void Place(VisualElement mark, float percent, float out01, bool bar)
        {
            mark.style.left = Length.Percent(percent);
            mark.style.opacity = Mathf.Lerp(1f, 0.15f, out01);
            mark.EnableInClassList("beat-track-mark--bar", bar);
        }
    }
}
