using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Small helpers for driving the Synty menu prefabs from code. The prefabs carry no behaviour of their own, so
    /// our controllers find the pieces they need by name (names come from the vendor prefabs and are stable) and
    /// log clearly when one is missing, e.g. after a pack update.
    /// </summary>
    public static class MenuUi
    {
        /// <summary>Depth-first search by name, inactive objects included. Null when not found.</summary>
        public static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = Find(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        public static T Find<T>(Transform root, string name) where T : Component
        {
            var t = Find(root, name);
            return t != null ? t.GetComponentInChildren<T>(true) : null;
        }

        /// <summary>Like <see cref="Find"/>, but reports a missing piece once so a renamed vendor child is obvious.</summary>
        public static Transform Require(Transform root, string name, UnityEngine.Object context)
        {
            var t = Find(root, name);
            if (t == null) Debug.LogWarning($"[Menus] '{name}' not found under '{(root != null ? root.name : "null")}'.", context);
            return t;
        }

        /// <summary>Sets the text of the child called <paramref name="childName"/>. Returns false when it is missing.</summary>
        public static bool SetText(Transform root, string childName, string text)
        {
            var label = Find<TMP_Text>(root, childName);
            if (label == null) return false;
            label.text = text;
            return true;
        }

        /// <summary>
        /// Sets a one-line label that sits in a layout row. An empty text takes the label out of the row, so its
        /// neighbours get the room, and the text never wraps: the Synty rows are sized for short sample strings.
        /// </summary>
        public static void SetLine(Transform root, string childName, string text)
        {
            var child = Find(root, childName);
            if (child == null) return;
            bool show = !string.IsNullOrEmpty(text);
            child.gameObject.SetActive(show);
            var label = show ? child.GetComponentInChildren<TMP_Text>(true) : null;
            if (label == null) return;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.text = text;
        }

        /// <summary>Sets every label under <paramref name="root"/> (buttons carry one label per visual state).</summary>
        public static void SetAllTexts(Transform root, string text)
        {
            if (root == null) return;
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true)) label.text = text;
        }

        /// <summary>Makes any vendor element clickable (key prompts, tiles): adds an invisible hit area and a Button.</summary>
        public static Button MakeClickable(GameObject target, UnityAction onClick)
        {
            if (target == null) return null;
            if (!target.TryGetComponent<Graphic>(out var graphic))
            {
                var hit = target.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
                graphic = hit;
            }
            graphic.raycastTarget = true;
            if (!target.TryGetComponent<Button>(out var button))
            {
                button = target.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
            }
            button.onClick.RemoveAllListeners();
            if (onClick != null) button.onClick.AddListener(onClick);
            MenuTouch.On(target); // it answers the pointer like everything else that can be clicked
            return button;
        }

        /// <summary>"just now", "3 hours ago", "2 days ago": how long since a UTC tick stamp.</summary>
        public static string TimeAgo(long utcTicks)
        {
            if (utcTicks <= 0) return "never";
            var span = DateTime.UtcNow - new DateTime(utcTicks, DateTimeKind.Utc);
            if (span.TotalMinutes < 2) return "just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} minutes ago";
            if (span.TotalHours < 2) return "an hour ago";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours} hours ago";
            if (span.TotalDays < 2) return "yesterday";
            return $"{(int)span.TotalDays} days ago";
        }
    }
}
