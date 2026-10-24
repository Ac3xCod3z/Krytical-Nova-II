using System;
using UnityEngine;

namespace KryticalNova.NPC
{
    /// <summary>
    /// One thing a town NPC says when a player walks up: the words shown in the dialogue and the NPC saying them in
    /// its own voice (owner, 24 Oct 2026). A line with no clip is only shown. Every clip of one NPC is the same voice:
    /// made from one sample and checked by the npc-voice skill before it gets here.
    /// </summary>
    [Serializable]
    public struct NpcLine
    {
        [TextArea(2, 4)] public string text;
        [Tooltip("The NPC saying this line (Assets/_Game/Audio/Voices/NPC/<Npc>/). Filled by Krytical Nova > Setup > NPC Voice Lines.")]
        public AudioClip clip;
    }

    /// <summary>Which line an NPC says next: any of them at random, never the one it said last time.</summary>
    public static class NpcLinePicker
    {
        /// <summary>
        /// An index in [0, <paramref name="count"/>) that is not <paramref name="last"/> when there is more than one
        /// line; -1 when there are none. <paramref name="roll"/> returns an int in [0, max).
        /// </summary>
        public static int Next(int count, int last, Func<int, int> roll)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;
            if (last < 0 || last >= count) return roll(count);
            // Pick among the other count - 1 lines, then step over the last one.
            int pick = roll(count - 1);
            return pick >= last ? pick + 1 : pick;
        }
    }
}
