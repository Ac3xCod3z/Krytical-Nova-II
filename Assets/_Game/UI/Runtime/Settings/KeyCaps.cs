using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Keys drawn as key caps, the way the Synty packs show them: a cap with the key lettered on it, a wider cap for
    /// a word (SPACE, CTRL, L SHIFT), Synty's own pictures for Enter, Space, Tab, Backspace and the arrows, and the
    /// mouse with the pressed button coloured. The cap's art, lettering and colours are the faction skin's (the
    /// sheets); this only decides which cap a key gets.
    /// </summary>
    public static class KeyCaps
    {
        public const string CapClass = "key-cap";
        private const string TextClass = "key-cap-text";
        private const string PictureClass = "key-cap-picture";
        private const string MouseClass = "key-mouse";

        /// <summary>Empties <paramref name="holder"/> and puts the cap of the action's key in it.</summary>
        public static void Fill(VisualElement holder, InputAction action, string label)
        {
            holder.Clear();
            string path = action != null && action.bindings.Count > 0 ? action.bindings[0].effectivePath : null;
            holder.Add(For(path, label));
        }

        /// <summary>Empties <paramref name="holder"/> and puts one cap in it for each of <paramref name="keys"/> (binding paths, or plain words).</summary>
        public static void Fill(VisualElement holder, params string[] keys)
        {
            holder.Clear();
            foreach (string key in keys) holder.Add(For(key, key));
        }

        /// <summary>One wide cap that says something (PRESS A KEY).</summary>
        public static VisualElement Saying(string words)
        {
            VisualElement cap = Lettered(words);
            cap.AddToClassList("saying");
            return cap;
        }

        /// <summary>The cap of a key: by its binding path when it has a picture, or lettered with <paramref name="label"/>.</summary>
        public static VisualElement For(string path, string label)
        {
            string picture = PictureOf(path);
            if (picture != null)
            {
                bool mouse = picture.StartsWith("mouse");
                var holder = new VisualElement { pickingMode = PickingMode.Ignore };
                if (mouse)
                {
                    holder.AddToClassList(MouseClass);
                    holder.AddToClassList(picture);
                    return holder;
                }
                holder.AddToClassList(CapClass);
                holder.AddToClassList("pictured");
                var art = new VisualElement { pickingMode = PickingMode.Ignore };
                art.AddToClassList(PictureClass);
                art.AddToClassList("glyph-" + picture);
                holder.Add(art);
                return holder;
            }
            return Lettered(string.IsNullOrEmpty(label) ? "—" : label);
        }

        private static VisualElement Lettered(string words)
        {
            var cap = new VisualElement { pickingMode = PickingMode.Ignore };
            cap.AddToClassList(CapClass);
            if (words.Length > 1) cap.AddToClassList("wide");
            var text = new Label(words) { pickingMode = PickingMode.Ignore };
            text.AddToClassList(TextClass);
            cap.Add(text);
            return cap;
        }

        /// <summary>The picture a key has, or null: the part of its binding path after the device.</summary>
        public static string PictureOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int slash = path.LastIndexOf('/');
            string control = slash >= 0 ? path.Substring(slash + 1) : path;
            bool mouse = path.StartsWith("<Mouse>");
            switch (control)
            {
                case "enter":
                case "numpadEnter": return "enter";
                case "space": return "space";
                case "tab": return "tab";
                case "backspace": return "backspace";
                case "upArrow": return "up";
                case "downArrow": return "down";
                case "leftArrow": return "left";
                case "rightArrow": return "right";
                case "leftButton": return mouse ? "mouse-left" : null;
                case "rightButton": return mouse ? "mouse-right" : null;
                case "middleButton": return mouse ? "mouse-middle" : null;
                case "scroll": return mouse ? "mouse-middle" : null;
                default: return null;
            }
        }
    }
}
