using System;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;

namespace KryticalNova.Social
{
    /// <summary>One motion a player can pick on the emote wheel.</summary>
    public readonly struct EmoteEntry
    {
        /// <summary>What the motion is called on the wire and in a save: see <see cref="EmoteCatalog.Key"/>.</summary>
        public readonly string Key;
        public readonly string Label;
        /// <summary>A stance: held until the character does something else. Otherwise it plays once.</summary>
        public readonly bool Stance;

        public EmoteEntry(string key, string label, bool stance)
        {
            Key = key; Label = label; Stance = stance;
        }
    }

    /// <summary>
    /// Reads the emote wheel's contents out of the expression library. A motion is named by its kind, group and
    /// name, never by its place in the library: a re-bake reorders the library, and every machine must still mean
    /// the same motion by the same key.
    /// </summary>
    public static class EmoteCatalog
    {
        private const char Separator = '/';
        private const string EmoteKind = "E", StanceKind = "S";

        /// <summary>The stable name of a motion: "E/Greet/Bow" for an emote, "S/Lean/Back Base" for a stance.</summary>
        public static string Key(bool stance, string group, string name) =>
            (stance ? StanceKind : EmoteKind) + Separator + group + Separator + name;

        /// <summary>
        /// What the wheel calls a motion. The library names many by a single word that only means something beside
        /// its group ("Large" under Head Nod): those are shown with the group in front, unless the category they
        /// are listed under already says it ("Bow" under GREET, not "Greet Bow").
        /// </summary>
        public static string Label(string group, string name, string category = null)
        {
            if (string.IsNullOrEmpty(name)) return group ?? string.Empty;
            if (string.IsNullOrEmpty(group) || name.IndexOf(' ') >= 0) return name;
            if (string.Equals(name, group, StringComparison.OrdinalIgnoreCase) || string.Equals(category, group, StringComparison.OrdinalIgnoreCase)) return name;
            return group + " " + name;
        }

        /// <summary>Pages needed to show <paramref name="count"/> motions, at least one.</summary>
        public static int PageCount(int count, int pageSize) =>
            pageSize <= 0 ? 1 : Math.Max(1, (count + pageSize - 1) / pageSize);

        /// <summary>Finds the motion a key names. False for a key no motion has (an older build's, or a made-up one).</summary>
        public static bool TryResolve(CharacterExpressionLibrary library, string key, out CharacterExpressionLibrary.Motion motion, out bool stance)
        {
            motion = default;
            stance = false;
            if (library == null || string.IsNullOrEmpty(key)) return false;
            int first = key.IndexOf(Separator);
            int second = first < 0 ? -1 : key.IndexOf(Separator, first + 1);
            if (second < 0) return false;
            string kind = key.Substring(0, first), group = key.Substring(first + 1, second - first - 1), name = key.Substring(second + 1);
            if (kind != EmoteKind && kind != StanceKind) return false;
            stance = kind == StanceKind;
            IReadOnlyList<CharacterExpressionLibrary.Motion> motions = stance ? library.Idles : library.Emotes;
            for (int i = 0; i < motions.Count; i++)
            {
                if (motions[i].name != name || motions[i].group != group) continue;
                motion = motions[i];
                return true;
            }
            return false;
        }

        /// <summary>True when a motion may be performed at all: some category of the wheel lists its group.</summary>
        public static bool IsOffered(EmoteWheelSettings settings, string group)
        {
            if (settings?.categories == null) return false;
            foreach (EmoteCategory category in settings.categories)
            {
                if (category.groups == null) continue;
                foreach (string listed in category.groups)
                    if (listed == group) return true;
            }
            return false;
        }

        /// <summary>
        /// The wheel for one body type: for each category, its motions in the order of the category's groups, emotes
        /// before stances within a group. Only motions performed for that body are listed.
        /// </summary>
        public static void Collect(EmoteWheelSettings settings, BodyType body, List<List<EmoteEntry>> perCategory)
        {
            perCategory.Clear();
            if (settings == null || settings.categories == null) return;
            foreach (EmoteCategory category in settings.categories)
            {
                var entries = new List<EmoteEntry>();
                perCategory.Add(entries);
                if (settings.library == null || category.groups == null) continue;
                foreach (string group in category.groups)
                {
                    Add(settings.library.Emotes, group, category.name, body, false, entries);
                    Add(settings.library.Idles, group, category.name, body, true, entries);
                }
            }
        }

        private static void Add(IReadOnlyList<CharacterExpressionLibrary.Motion> motions, string group, string category, BodyType body, bool stance, List<EmoteEntry> into)
        {
            for (int i = 0; i < motions.Count; i++)
            {
                CharacterExpressionLibrary.Motion motion = motions[i];
                if (motion.group != group || !motion.Has(body)) continue;
                into.Add(new EmoteEntry(Key(stance, motion.group, motion.name), Label(motion.group, motion.name, category), stance));
            }
        }
    }
}
