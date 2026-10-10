using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Video;

namespace KryticalNova.UI.Guide
{
    /// <summary>What a section of a Guide topic is, which decides how its lines are drawn.</summary>
    public enum GuideSectionKind
    {
        /// <summary>Paragraphs.</summary>
        Text,
        /// <summary>Numbered steps, done in order.</summary>
        Steps,
        /// <summary>Two columns: each line is "left | right".</summary>
        Table,
        /// <summary>Something worth knowing, set apart.</summary>
        Tip,
        /// <summary>What can go wrong: a loss, a cap, something that cannot be undone.</summary>
        Warning,
        /// <summary>Where it is done: the NPC, the place or the key. Shown beside the clip.</summary>
        Where
    }

    [Serializable]
    public sealed class GuideSection
    {
        public GuideSectionKind kind;
        [Tooltip("Small heading over the section. Empty: none.")]
        public string heading;
        [Tooltip("One entry a paragraph, step or table row. Tokens: {key:Pack} {mouse:left} {cap:SHIFT} {fact:name}.")]
        [TextArea(1, 6)] public List<string> lines = new List<string>();
    }

    [Serializable]
    public sealed class GuideTopic
    {
        [Tooltip("Never shown and never changed once set: clips are named by it and the last topic read is kept by it.")]
        public string id;
        public string title;
        [Tooltip("One or two sentences: what this is and why a player would want it.")]
        [TextArea(1, 4)] public string summary;
        [Tooltip("A short silent loop of the real game (Krytical Nova > Guide > Record Clip).")]
        public VideoClip clip;
        [Tooltip("Shown until the clip plays, and instead of it when there is none.")]
        public Texture2D still;
        [Tooltip("The part of the clip's picture the page shows, as shares of the picture (x and y are its top left corner). " +
                 "The page's screen takes this part's shape, so a tall window can be shown whole. No size: the whole picture.")]
        public Rect focus;

        /// <summary>True when the page shows only a part of the clip's picture.</summary>
        public bool HasFocus => focus.width > 0.01f && focus.height > 0.01f && (focus.width < 0.999f || focus.height < 0.999f);
        /// <summary>Width over height of what the page shows of the clip (or of the still, when there is no clip).</summary>
        public float ShownAspect
        {
            get
            {
                float whole = clip != null && clip.height > 0 ? (float)clip.width / clip.height
                    : still != null && still.height > 0 ? (float)still.width / still.height : 16f / 9f;
                return HasFocus ? whole * focus.width / focus.height : whole;
            }
        }
        public List<GuideSection> sections = new List<GuideSection>();
    }

    [Serializable]
    public sealed class GuideChapter
    {
        public string title;
        public List<GuideTopic> topics = new List<GuideTopic>();
    }

    /// <summary>A number the Guide quotes, read from the settings asset that owns it so the text cannot go stale.</summary>
    [Serializable]
    public sealed class GuideFact
    {
        [Tooltip("The name the text uses: {fact:trade.slots}.")]
        public string name;
        [Tooltip("The settings asset the number lives in.")]
        public ScriptableObject source;
        [Tooltip("The field on that asset; dots go into nested settings (temper.hardCap), brackets into a list (temper.alloys[0].step).")]
        public string field;
    }

    /// <summary>
    /// Everything the Guide page of Settings says (<see cref="GuidePage"/>): chapters of topics, one topic for each
    /// system a player can use. The text is edited here, on the asset, in the game's own terms (the Glossary of
    /// _Docs/Reference/12Sky/01_Systems_Build_List.md). Keys are written as tokens so the page shows the key the
    /// player has bound, and numbers as facts read from the settings assets.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/UI/Guide Library", fileName = "GuideLibrary")]
    public sealed class GuideLibrary : ScriptableObject
    {
        public List<GuideChapter> chapters = new List<GuideChapter>();
        public List<GuideFact> facts = new List<GuideFact>();

        /// <summary>Every topic, in reading order.</summary>
        public IEnumerable<GuideTopic> Topics
        {
            get
            {
                foreach (GuideChapter chapter in chapters)
                    foreach (GuideTopic topic in chapter.topics)
                        yield return topic;
            }
        }

        public GuideTopic Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (GuideTopic topic in Topics)
                if (topic.id == id) return topic;
            return null;
        }

        /// <summary>The fact's value as the page writes it (whole numbers with thousands separated). False when it cannot be read.</summary>
        public bool TryFact(string name, out string text)
        {
            text = null;
            foreach (GuideFact fact in facts)
            {
                if (fact.name != name) continue;
                if (fact.source == null || string.IsNullOrEmpty(fact.field)) return false;
                object value = fact.source;
                foreach (string part in fact.field.Split('.'))
                {
                    // "alloys[2]" is the third entry of the list in the field "alloys".
                    int bracket = part.IndexOf('[');
                    string member = bracket < 0 ? part : part.Substring(0, bracket);
                    FieldInfo field = value.GetType().GetField(member, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    value = field?.GetValue(value);
                    if (value != null && bracket >= 0)
                    {
                        bool numbered = int.TryParse(part.Substring(bracket + 1).TrimEnd(']'), out int index);
                        value = numbered && value is System.Collections.IList list && index >= 0 && index < list.Count ? list[index] : null;
                    }
                    if (value == null) return false;
                }
                text = Format(value);
                return true;
            }
            return false;
        }

        private static string Format(object value)
        {
            switch (value)
            {
                case int or long or short or byte or uint or ulong:
                    return Convert.ToInt64(value).ToString("N0", CultureInfo.InvariantCulture);
                case float or double:
                    return Convert.ToDouble(value).ToString("#,0.##", CultureInfo.InvariantCulture);
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }
}
