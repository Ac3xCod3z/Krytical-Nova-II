using System;

namespace KryticalNova.Pets
{
    /// <summary>
    /// When a pet may speak and what it picks: never the same thing twice in a row, never while it has only just
    /// spoken, and not about the same moment again too soon. Plain sums, so they can be tested without a scene.
    /// </summary>
    public static class PetChatterRules
    {
        public const int TriggerCount = 7;

        /// <summary>
        /// Picks one of <paramref name="count"/> variants at random, never <paramref name="last"/> again while there
        /// is another to pick. <paramref name="roll"/> is a random number from 0 up to (not including) 1.
        /// Returns -1 when there is nothing to pick from.
        /// </summary>
        public static int Pick(int count, int last, float roll)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;
            roll = roll < 0f ? 0f : roll >= 1f ? 0.999999f : roll;
            if (last < 0 || last >= count) return (int)(roll * count);
            int pick = (int)(roll * (count - 1));   // one of the others ...
            return pick >= last ? pick + 1 : pick;  // ... stepping over the one said last
        }

        /// <summary>The more important of two moments (the one earlier in <see cref="PetChatterTrigger"/>).</summary>
        public static PetChatterTrigger MoreImportant(PetChatterTrigger a, PetChatterTrigger b) => (int)a <= (int)b ? a : b;

        /// <summary>A death always gets through; every other moment waits out the quiet after the last bubble.</summary>
        public static bool BreaksQuiet(PetChatterTrigger trigger) => trigger == PetChatterTrigger.OwnerDied;
    }

    /// <summary>
    /// One pet's memory of what it said and when: which variant it used last for each moment, when it last spoke at
    /// all and when it last reacted to each moment.
    /// </summary>
    public sealed class PetChatterVoice
    {
        private readonly int[] lastVariant = new int[PetChatterRules.TriggerCount];
        private readonly float[] lastTime = new float[PetChatterRules.TriggerCount];
        private float lastSpoke = float.NegativeInfinity;

        public PetChatterVoice() => Forget();

        /// <summary>Starts over: a different pet has come out.</summary>
        public void Forget()
        {
            for (int i = 0; i < lastVariant.Length; i++)
            {
                lastVariant[i] = -1;
                lastTime[i] = float.NegativeInfinity;
            }
            lastSpoke = float.NegativeInfinity;
        }

        public int LastVariant(PetChatterTrigger trigger) => lastVariant[(int)trigger];

        /// <summary>True when the pet may react to this moment now.</summary>
        public bool MaySpeak(PetChatterTrigger trigger, float now, PetChatterSettings settings)
        {
            if (settings == null) return false;
            if (now - lastTime[(int)trigger] < settings.RepeatSeconds(trigger)) return false;
            return PetChatterRules.BreaksQuiet(trigger) || now - lastSpoke >= settings.quietSeconds;
        }

        /// <summary>
        /// The variant the pet says for this moment, or -1 when it stays quiet (too soon, or nothing to say). A
        /// variant returned counts as said: the pet will not pick it next time, and its quiet starts now.
        /// </summary>
        public int Speak(PetChatterTrigger trigger, int variants, float now, PetChatterSettings settings, float roll)
        {
            if (variants <= 0 || !MaySpeak(trigger, now, settings)) return -1;
            int pick = PetChatterRules.Pick(variants, lastVariant[(int)trigger], roll);
            lastVariant[(int)trigger] = pick;
            lastTime[(int)trigger] = now;
            lastSpoke = now;
            return pick;
        }
    }
}
