using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// A menu button that folds a footer's prompts away and springs them out again (owner, 20 Oct 2026): a click on the
    /// button or its key opens the row, and the same again folds it. Folded, the prompts' keys still work; only their
    /// labels are put away. The button stands at the right end of the row and each prompt grows out of it leftwards in turn, a little past its size and back, and the row
    /// makes room for it as it grows, so nothing beside it jumps.
    /// </summary>
    public sealed class MenuDrawer : MonoBehaviour
    {
        private const float OpenSeconds = 0.32f, CloseSeconds = 0.18f, Stagger = 0.045f;

        private sealed class Item
        {
            public RectTransform rect;
            public LayoutElement layout;
            public CanvasGroup group;
            public LayoutGroup own; // the prompt's own row of key cap and label
            public Vector2 pivot;
            public float width;
            public bool wanted = true; // shown when the row is out (Delete only with a character chosen)
        }

        private readonly List<Item> items = new List<Item>();
        private Coroutine running;

        /// <summary>True while the prompts are out.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>Raised when the row opens or folds.</summary>
        public event System.Action Changed;

        /// <summary>The prompts this button keeps, nearest first. They start folded away.</summary>
        public void Hold(IEnumerable<Transform> prompts)
        {
            // The row is laid out once with every prompt in it, so each label has its width before it is put away.
            if (transform.parent is RectTransform row) LayoutRebuilder.ForceRebuildLayoutImmediate(row);
            foreach (Transform prompt in prompts)
            {
                if (prompt == null || !(prompt is RectTransform rect)) continue;
                var item = new Item
                {
                    rect = rect,
                    layout = Need<LayoutElement>(rect.gameObject),
                    group = Need<CanvasGroup>(rect.gameObject),
                    own = rect.TryGetComponent(out LayoutGroup own) ? own : null,
                    pivot = rect.pivot,
                };
                item.layout.enabled = false;
                items.Add(item);
                rect.gameObject.SetActive(false);
            }
            IsOpen = false;
        }

        /// <summary>Whether a prompt belongs in the row now; it is put away with the rest when the row is folded.</summary>
        public void Want(Transform prompt, bool wanted)
        {
            Item item = items.Find(each => each.rect == prompt);
            if (item == null || item.wanted == wanted) return;
            item.wanted = wanted;
            if (running == null) item.rect.gameObject.SetActive(IsOpen && wanted);
        }

        // Never "GetComponent() ?? Add": in the Editor a missing component comes back as a stand-in that is not null to ??.
        private static T Need<T>(GameObject go) where T : Component => go.TryGetComponent(out T had) ? had : go.AddComponent<T>();

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (IsOpen || items.Count == 0) return;
            IsOpen = true;
            MenuMoments.Raise(MenuMoment.Open, transform);
            Changed?.Invoke();
            Run(Spring(true));
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            MenuMoments.Raise(MenuMoment.Back, transform);
            Changed?.Invoke();
            Run(Spring(false));
        }

        private void Run(IEnumerator routine)
        {
            if (running != null) StopCoroutine(running);
            running = isActiveAndEnabled ? StartCoroutine(routine) : null;
            if (running == null) Settle(IsOpen);
        }

        private IEnumerator Spring(bool opening)
        {
            if (opening)
                foreach (Item item in items)
                {
                    if (!item.wanted) continue;
                    // Its full width, as the row would give it, before it starts from nothing.
                    item.layout.enabled = false;
                    item.rect.gameObject.SetActive(true);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(item.rect);
                    item.width = LayoutUtility.GetPreferredWidth(item.rect);
                    // Its key cap and label are laid out at that full width before the prompt starts from nothing.
                    item.rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, item.width);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(item.rect);
                    item.layout.enabled = true;
                    // It grows out of the button on its right, its key cap and label laid out at full size inside it.
                    if (item.own != null) item.own.enabled = false;
                    item.rect.pivot = new Vector2(1f, item.pivot.y);
                    Show(item, 0f, 0f);
                }

            float seconds = opening ? OpenSeconds : CloseSeconds;
            float total = seconds + Stagger * (items.Count - 1);
            float start = Time.unscaledTime;
            while (true)
            {
                float elapsed = Time.unscaledTime - start;
                for (int i = 0; i < items.Count; i++)
                {
                    // Out: nearest first. Back in: farthest first.
                    int order = opening ? i : items.Count - 1 - i;
                    float t = Mathf.Clamp01((elapsed - order * Stagger) / seconds);
                    float grow = opening ? OutBack(t) : 1f - InBack(t);
                    if (items[i].wanted) Show(items[i], grow, opening ? t : 1f - t);
                }
                if (elapsed >= total) break;
                yield return null;
            }
            Settle(opening);
            running = null;
        }

        // At rest: out at full size, or folded away.
        private void Settle(bool open)
        {
            foreach (Item item in items)
            {
                item.layout.enabled = false;
                if (item.own != null) item.own.enabled = true;
                item.rect.pivot = item.pivot;
                item.rect.localScale = Vector3.one;
                item.group.alpha = 1f;
                item.rect.gameObject.SetActive(open && item.wanted);
            }
        }

        private static void Show(Item item, float grow, float fade)
        {
            // The row gives it room up to its size; the overshoot is only in how large it is drawn.
            float room = Mathf.Clamp01(grow) * item.width;
            item.layout.minWidth = room;
            item.layout.preferredWidth = room;
            item.rect.localScale = new Vector3(Mathf.Max(grow, 0f), Mathf.Max(grow, 0f), 1f);
            item.group.alpha = Mathf.Clamp01(fade * 1.5f);
        }

        // A spring's ease: past the end and back (out), or back before going (in).
        private const float Overshoot = 1.70158f;
        private static float OutBack(float t) => 1f + (Overshoot + 1f) * Mathf.Pow(t - 1f, 3f) + Overshoot * Mathf.Pow(t - 1f, 2f);
        private static float InBack(float t) => (Overshoot + 1f) * t * t * t - Overshoot * t * t;

        private void OnDisable()
        {
            if (running == null) return;
            running = null;
            Settle(IsOpen);
        }
    }
}
