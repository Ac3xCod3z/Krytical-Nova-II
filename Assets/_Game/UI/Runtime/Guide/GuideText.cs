using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using KryticalNova.Player;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Guide
{
    /// <summary>
    /// A line of Guide text with its tokens filled in. <c>{key:Pack}</c> is the key the player has on that action
    /// now, drawn as a key cap; <c>{mouse:left}</c> (right, wheel) the mouse with that button coloured;
    /// <c>{cap:ESC}</c> a fixed key; <c>{fact:name}</c> a number of the library's facts. <c>&lt;b&gt;</c> marks
    /// the game's own words.
    /// </summary>
    public static class GuideText
    {
        public enum TokenKind { Text, Key, Mouse, Cap, Fact }

        public readonly struct Token
        {
            public readonly TokenKind Kind;
            public readonly string Value;
            public Token(TokenKind kind, string value) { Kind = kind; Value = value; }
        }

        private static readonly Regex TokenPattern = new Regex(@"\{(key|mouse|cap|fact):([^{}]+)\}", RegexOptions.Compiled);
        private const string TightStart = ".,;:)!?";

        /// <summary>Splits <paramref name="line"/> into its text and tokens, in order.</summary>
        public static void Parse(string line, List<Token> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(line)) return;
            int at = 0;
            foreach (Match match in TokenPattern.Matches(line))
            {
                if (match.Index > at) into.Add(new Token(TokenKind.Text, line.Substring(at, match.Index - at)));
                into.Add(new Token(KindOf(match.Groups[1].Value), match.Groups[2].Value.Trim()));
                at = match.Index + match.Length;
            }
            if (at < line.Length) into.Add(new Token(TokenKind.Text, line.Substring(at)));
        }

        private static TokenKind KindOf(string word)
        {
            switch (word)
            {
                case "key": return TokenKind.Key;
                case "mouse": return TokenKind.Mouse;
                case "cap": return TokenKind.Cap;
                default: return TokenKind.Fact;
            }
        }

        /// <summary>True when the game has a rebindable key of this name (a window key, or one of the player's own).</summary>
        public static bool IsKnownKey(string name)
        {
            foreach (KeyBindings.WindowKey key in KeyBindings.WindowKeys)
                if (key.Action == name) return true;
            foreach (var key in KeyBindings.PlayerKeys)
                if (key.Action == name) return true;
            return false;
        }

        public static bool IsKnownMouse(string name) => MousePath(name) != null;

        private static string MousePath(string name)
        {
            switch (name)
            {
                case "left": return "<Mouse>/leftButton";
                case "right": return "<Mouse>/rightButton";
                case "wheel":
                case "middle": return "<Mouse>/middleButton";
                default: return null;
            }
        }

        /// <summary>The action behind a key token: a window key, or one of the player's own (the project's copy while there is no player).</summary>
        private static InputAction ActionOf(string name, InputActionAsset playerActions)
        {
            foreach (KeyBindings.WindowKey key in KeyBindings.WindowKeys)
                if (key.Action == name) return KeyBindings.Window(name);
            InputActionAsset asset = playerActions != null ? playerActions : InputSystem.actions;
            if (asset == null) return null;
            foreach (var key in KeyBindings.PlayerKeys)
                if (key.Action == name) return asset.FindActionMap(key.Map, false)?.FindAction(key.Action, false);
            return null;
        }

        /// <summary>
        /// The line as an element. A line of words alone is one label; a line with keys in it is laid out word by
        /// word, so the caps sit in the sentence and it still wraps.
        /// </summary>
        public static VisualElement Build(string line, GuideLibrary library, InputActionAsset playerActions, List<Token> scratch)
        {
            Parse(line, scratch);
            bool inline = false;
            foreach (Token token in scratch)
                if (token.Kind == TokenKind.Key || token.Kind == TokenKind.Mouse || token.Kind == TokenKind.Cap) inline = true;

            if (!inline)
            {
                var text = new StringBuilder();
                foreach (Token token in scratch) text.Append(token.Kind == TokenKind.Fact ? Fact(library, token.Value) : token.Value);
                var label = new Label(text.ToString()) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("guide-line");
                return label;
            }

            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("guide-line");
            row.AddToClassList("guide-line-row");
            bool strong = false;
            foreach (Token token in scratch)
            {
                switch (token.Kind)
                {
                    case TokenKind.Key:
                        InputAction action = ActionOf(token.Value, playerActions);
                        string path = action != null && action.bindings.Count > 0 ? action.bindings[0].effectivePath : null;
                        row.Add(KeyCaps.For(path, action != null ? KeyBindings.Label(action) : token.Value.ToUpperInvariant()));
                        break;
                    case TokenKind.Mouse:
                        row.Add(KeyCaps.For(MousePath(token.Value), token.Value));
                        break;
                    case TokenKind.Cap:
                        row.Add(KeyCaps.For(null, token.Value));
                        break;
                    default:
                        string words = token.Kind == TokenKind.Fact ? Fact(library, token.Value) : token.Value;
                        AddWords(row, words, ref strong);
                        break;
                }
            }
            return row;
        }

        private static string Fact(GuideLibrary library, string name)
            => library != null && library.TryFact(name, out string text) ? text : "?";

        private static void AddWords(VisualElement row, string text, ref bool strong)
        {
            foreach (string piece in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string word = piece;
                bool opens = word.Contains("<b>"), closes = word.Contains("</b>");
                if (opens || closes) word = word.Replace("<b>", string.Empty).Replace("</b>", string.Empty);
                if (word.Length > 0)
                {
                    var label = new Label(word) { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("guide-word");
                    if (strong || opens) label.AddToClassList("guide-strong");
                    if (TightStart.IndexOf(word[0]) >= 0) label.AddToClassList("tight");
                    row.Add(label);
                }
                if (opens) strong = true;
                if (closes) strong = false;
            }
        }
    }
}
