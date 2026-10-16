using System.Collections.Generic;
using KryticalNova.Core;

namespace KryticalNova.UI
{
    /// <summary>One toast: what kind of news, who it is from (may be empty) and what it says.</summary>
    public struct Toast
    {
        public AnnouncementKind Kind;
        public string Sender;
        public string Text;
        /// <summary>The names the news is about (a player, a piece of gear), "|" between them: lit in the line. May be null.</summary>
        public string Highlight;
        /// <summary>The rarity of the item the news is about (an <c>ItemRarity</c> as a byte); 0 when there is none.</summary>
        public byte Rarity;
    }

    /// <summary>
    /// Toasts in the order they arrived, shown one at a time: the next one waits until the one on screen has had
    /// its time and has faded out, so two never overlap. When more wait than the settings allow, the oldest one
    /// waiting gives way (what is on screen is never cut short). Time is passed in, so this is the same in a test.
    /// </summary>
    public sealed class ToastQueue
    {
        private readonly Queue<Toast> waiting = new Queue<Toast>(8);
        private float shownAt;
        private float freeAt;

        /// <summary>The toast on screen; only meaningful while <see cref="Showing"/>.</summary>
        public Toast Current { get; private set; }
        public bool Showing { get; private set; }
        public int Waiting => waiting.Count;

        /// <summary>Adds a toast. Returns false when an older waiting toast had to be dropped to make room.</summary>
        public bool Enqueue(in Toast toast, int maxWaiting)
        {
            bool kept = true;
            while (waiting.Count > 0 && waiting.Count >= (maxWaiting < 1 ? 1 : maxWaiting))
            {
                waiting.Dequeue();
                kept = false;
            }
            waiting.Enqueue(toast);
            return kept;
        }

        /// <summary>
        /// Moves time on. Returns true when what is on screen changed: a toast came up (<see cref="Showing"/> is
        /// true and <see cref="Current"/> is it) or the one showing went away.
        /// </summary>
        public bool Advance(float now, float showSeconds, float fadeSeconds)
        {
            if (Showing)
            {
                if (now < shownAt + showSeconds) return false;
                Showing = false;
                freeAt = now + fadeSeconds;
                return true;
            }
            if (waiting.Count == 0 || now < freeAt) return false;
            Current = waiting.Dequeue();
            Showing = true;
            shownAt = now;
            return true;
        }

        public void Clear()
        {
            waiting.Clear();
            Showing = false;
            freeAt = 0f;
        }
    }
}
