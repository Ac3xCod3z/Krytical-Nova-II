using System.Collections.Generic;
using System.Text;

namespace KryticalNova.Networking
{
    /// <summary>
    /// server-settings.json is read by two programs that know different keys: Netcode for GameObjects' <see cref="ServerLaunchOptions"/> (its
    /// fields) and the entity server (its own keys: <c>netcode</c>, <c>plainTransport</c>, ...). A rewrite from the fields alone drops
    /// every key that is not a field, so the entity test server forgot <c>plainTransport</c> after the control page made its password and
    /// would not start (DOTS builds, 8 Nov 2026). Every write of the file goes through <see cref="Merge"/>: the new text, plus every
    /// top-level key of the old text that the new text does not have, copied as it was.
    /// </summary>
    public static class SettingsKeepKeys
    {
        /// <summary>
        /// <paramref name="newJson"/> (an object) with the top-level keys of <paramref name="oldJson"/> that it lacks added at the end, their
        /// values untouched. Either text that is not an object is returned as <paramref name="newJson"/>.
        /// </summary>
        public static string Merge(string oldJson, string newJson)
        {
            if (string.IsNullOrWhiteSpace(oldJson) || string.IsNullOrWhiteSpace(newJson)) return newJson;
            var oldKeys = TopLevel(oldJson);
            var newKeys = TopLevel(newJson);
            if (oldKeys == null || newKeys == null) return newJson;
            var have = new HashSet<string>();
            foreach (var pair in newKeys) have.Add(pair.Key);
            var extra = new StringBuilder();
            foreach (var pair in oldKeys)
                if (!have.Contains(pair.Key)) extra.Append(",\n    ").Append(pair.Key).Append(": ").Append(pair.Value);
            if (extra.Length == 0) return newJson;
            int close = newJson.LastIndexOf('}');
            if (close < 0) return newJson;
            string head = newJson.Substring(0, close).TrimEnd();
            if (head.EndsWith("{")) extra.Remove(0, 1); // an empty object: no comma before the first key
            return head + extra + "\n}";
        }

        // The top-level keys of a JSON object as (quoted key text, raw value text), in order; null when the text is not an object.
        static List<KeyValuePair<string, string>> TopLevel(string json)
        {
            int i = 0;
            Skip(json, ref i);
            if (i >= json.Length || json[i] != '{') return null;
            i++;
            var result = new List<KeyValuePair<string, string>>();
            while (true)
            {
                Skip(json, ref i);
                if (i >= json.Length) return null;
                if (json[i] == '}') return result;
                if (json[i] == ',') { i++; continue; }
                if (json[i] != '"') return null;
                int keyStart = i;
                if (!EndOfString(json, ref i)) return null;
                string key = json.Substring(keyStart, i - keyStart);
                Skip(json, ref i);
                if (i >= json.Length || json[i] != ':') return null;
                i++;
                Skip(json, ref i);
                int valueStart = i;
                if (!EndOfValue(json, ref i)) return null;
                result.Add(new KeyValuePair<string, string>(key, json.Substring(valueStart, i - valueStart).TrimEnd()));
            }
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        // i on the opening quote; leaves i after the closing one.
        static bool EndOfString(string s, ref int i)
        {
            i++;
            while (i < s.Length)
            {
                if (s[i] == '\\') { i += 2; continue; }
                if (s[i] == '"') { i++; return true; }
                i++;
            }
            return false;
        }

        // i on the first character of a value; leaves i after it (a string, an object or array with its nesting, or a bare token).
        static bool EndOfValue(string s, ref int i)
        {
            if (i >= s.Length) return false;
            if (s[i] == '"') return EndOfString(s, ref i);
            if (s[i] == '{' || s[i] == '[')
            {
                int depth = 0;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == '"') { if (!EndOfString(s, ref i)) return false; continue; }
                    if (c == '{' || c == '[') depth++;
                    else if (c == '}' || c == ']') { depth--; if (depth == 0) { i++; return true; } }
                    i++;
                }
                return false;
            }
            while (i < s.Length && s[i] != ',' && s[i] != '}' && !char.IsWhiteSpace(s[i])) i++;
            return true;
        }
    }
}
