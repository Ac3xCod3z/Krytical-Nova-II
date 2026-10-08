using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// Moves what a menu moment happened on (<see cref="MMF_MenuPunch"/>): a menu element (a Transform) or an
    /// element of a UI Toolkit window. One movement per thing at a time; a new one takes over from where the thing
    /// rests, not from where the last one left it.
    ///
    /// It works in LateUpdate, after the menu packs' own animators have written their pose for the frame, and adds
    /// its movement to whatever pose it finds: an animator, a layout group or the HUD's layout editor can go on
    /// placing the thing, and when the movement ends the thing is exactly where they put it. It runs on real time,
    /// so a paused or slowed game does not hold a button down.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuPunchDriver : MonoBehaviour
    {
        private sealed class Punch
        {
            public Transform Body;
            public RectTransform Rect;
            public VisualElement Element;
            public float Elapsed, Seconds;
            public Vector2 Swell, Nudge;
            public float Turn;
            public AnimationCurve Shape;

            // A menu element: where it rests, and what was last written to it (to tell when something else wrote).
            public Vector3 RestScale, WroteScale;
            public Vector2 RestPlace, WrotePlace;
            public float RestTurn, WroteTurn;

            // An element of a window: its pose at rest, and its own inline styles to hand back afterwards.
            public Vector3 ElementScale, ElementPlace;
            public float ElementTurn;
            public StyleScale SavedScale;
            public StyleTranslate SavedPlace;
            public StyleRotate SavedTurn;
        }

        private const float ReferenceLines = 1080f;
        // A long frame (a window that takes a moment to open) must not swallow the movement it started.
        private const float LongestStep = 1f / 30f;

        private readonly List<Punch> punches = new List<Punch>();

        /// <summary>How many things are moving now.</summary>
        public int Moving => punches.Count;

        public void Play(Transform body, VisualElement element, float seconds, Vector2 swell, Vector2 nudge, float turn, AnimationCurve shape)
        {
            if (shape == null || seconds <= 0f) return;
            if (body == null && (element == null || element.panel == null)) return;

            var punch = new Punch
            {
                Body = body, Rect = body as RectTransform, Element = body == null ? element : null,
                Seconds = seconds, Swell = swell, Nudge = nudge, Turn = turn, Shape = shape,
            };
            Punch running = Find(body, punch.Element);
            if (punch.Body != null)
            {
                // A nudge is given in pixels of a 1080-line screen, which is what a window's own pixels are: on a
                // menu canvas that is so many of the canvas's units.
                Canvas canvas = punch.Rect != null ? body.GetComponentInParent<Canvas>() : null;
                if (canvas != null) punch.Nudge *= Screen.height / ReferenceLines / Mathf.Max(0.01f, canvas.rootCanvas.scaleFactor);
                if (running != null) Rest(running); // back to rest first: the new movement starts from there
                punch.RestScale = punch.WroteScale = body.localScale;
                punch.RestPlace = punch.WrotePlace = Place(punch);
                punch.RestTurn = punch.WroteTurn = body.localEulerAngles.z;
            }
            else if (running != null)
            {
                // The style is only worked out again later in the frame: what the element is at rest is what the
                // movement it takes over from found.
                punch.ElementScale = running.ElementScale;
                punch.ElementPlace = running.ElementPlace;
                punch.ElementTurn = running.ElementTurn;
                punch.SavedScale = running.SavedScale;
                punch.SavedPlace = running.SavedPlace;
                punch.SavedTurn = running.SavedTurn;
            }
            else
            {
                // UI Toolkit wants to know beforehand that an element's transform will be animated: it then keeps
                // the element's place apart from its parent's, and its children follow it. Moving one that was not
                // told leaves the renderer's books on its children stale (it asserts when they are next drawn).
                if ((punch.Element.usageHints & UsageHints.DynamicTransform) == 0) punch.Element.usageHints |= UsageHints.DynamicTransform;
                IResolvedStyle resolved = punch.Element.resolvedStyle;
                punch.ElementScale = resolved.scale.value;
                punch.ElementPlace = resolved.translate;
                punch.ElementTurn = resolved.rotate.angle.ToDegrees();
                punch.SavedScale = punch.Element.style.scale;
                punch.SavedPlace = punch.Element.style.translate;
                punch.SavedTurn = punch.Element.style.rotate;
            }
            if (running != null) punches.Remove(running);
            punches.Add(punch);
        }

        private void LateUpdate() => Step(Mathf.Min(Time.unscaledDeltaTime, LongestStep));

        /// <summary>Shows every movement where it has got to, then moves its time on by <paramref name="step"/> seconds.</summary>
        public void Step(float step)
        {
            for (int i = punches.Count - 1; i >= 0; i--)
            {
                Punch punch = punches[i];
                bool gone = punch.Element != null ? punch.Element.panel == null : punch.Body == null;
                float along = punch.Elapsed / punch.Seconds;
                punch.Elapsed += step;
                if (gone || along >= 1f)
                {
                    if (!gone) Rest(punch);
                    punches.RemoveAt(i);
                    continue;
                }
                Apply(punch, punch.Shape.Evaluate(along));
            }
        }

        private void OnDisable()
        {
            foreach (Punch punch in punches)
                if (punch.Element != null ? punch.Element.panel != null : punch.Body != null) Rest(punch);
            punches.Clear();
        }

        private Punch Find(Transform body, VisualElement element)
        {
            foreach (Punch punch in punches)
                if (body != null ? punch.Body == body : punch.Element == element) return punch;
            return null;
        }

        private static void Apply(Punch punch, float amount)
        {
            if (punch.Element != null)
            {
                VisualElement element = punch.Element;
                if (punch.Swell != Vector2.zero)
                    element.style.scale = new Scale(new Vector3(punch.ElementScale.x * (1f + punch.Swell.x * amount), punch.ElementScale.y * (1f + punch.Swell.y * amount), 1f));
                // A window counts down the screen and turns clockwise: the same amounts move it the way they move a menu element.
                if (punch.Nudge != Vector2.zero)
                    element.style.translate = new Translate(punch.ElementPlace.x + punch.Nudge.x * amount, punch.ElementPlace.y - punch.Nudge.y * amount);
                if (punch.Turn != 0f) element.style.rotate = new Rotate(punch.ElementTurn - punch.Turn * amount);
                return;
            }

            Transform body = punch.Body;
            if (punch.Swell != Vector2.zero)
            {
                // Not what was written last frame: an animator placed it again, and that is where it rests now.
                if (!Same(body.localScale, punch.WroteScale)) punch.RestScale = body.localScale;
                punch.WroteScale = new Vector3(punch.RestScale.x * (1f + punch.Swell.x * amount), punch.RestScale.y * (1f + punch.Swell.y * amount), punch.RestScale.z);
                body.localScale = punch.WroteScale;
            }
            if (punch.Nudge != Vector2.zero)
            {
                Vector2 place = Place(punch);
                if (!Same(place, punch.WrotePlace)) punch.RestPlace = place;
                punch.WrotePlace = punch.RestPlace + punch.Nudge * amount;
                SetPlace(punch, punch.WrotePlace);
            }
            if (punch.Turn != 0f)
            {
                float turn = body.localEulerAngles.z;
                if (Mathf.Abs(Mathf.DeltaAngle(turn, punch.WroteTurn)) > 0.01f) punch.RestTurn = turn;
                punch.WroteTurn = punch.RestTurn + punch.Turn * amount;
                SetTurn(body, punch.WroteTurn);
            }
        }

        /// <summary>Puts the thing back where it rests. What something else has placed since is left as that put it.</summary>
        private static void Rest(Punch punch)
        {
            if (punch.Element != null)
            {
                if (punch.Swell != Vector2.zero) punch.Element.style.scale = punch.SavedScale;
                if (punch.Nudge != Vector2.zero) punch.Element.style.translate = punch.SavedPlace;
                if (punch.Turn != 0f) punch.Element.style.rotate = punch.SavedTurn;
                return;
            }
            Transform body = punch.Body;
            if (punch.Swell != Vector2.zero && Same(body.localScale, punch.WroteScale)) body.localScale = punch.RestScale;
            if (punch.Nudge != Vector2.zero && Same(Place(punch), punch.WrotePlace)) SetPlace(punch, punch.RestPlace);
            if (punch.Turn != 0f && Mathf.Abs(Mathf.DeltaAngle(body.localEulerAngles.z, punch.WroteTurn)) <= 0.01f) SetTurn(body, punch.RestTurn);
        }

        private static Vector2 Place(Punch punch) => punch.Rect != null ? punch.Rect.anchoredPosition : (Vector2)punch.Body.localPosition;

        private static void SetPlace(Punch punch, Vector2 place)
        {
            if (punch.Rect != null) punch.Rect.anchoredPosition = place;
            else punch.Body.localPosition = new Vector3(place.x, place.y, punch.Body.localPosition.z);
        }

        private static void SetTurn(Transform body, float degrees)
        {
            Vector3 angles = body.localEulerAngles;
            body.localEulerAngles = new Vector3(angles.x, angles.y, degrees);
        }

        private static bool Same(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-8f;

        private static bool Same(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-6f;
    }
}
