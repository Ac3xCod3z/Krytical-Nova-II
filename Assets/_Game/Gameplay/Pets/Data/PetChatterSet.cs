using System;
using UnityEngine;

namespace KryticalNova.Pets
{
    /// <summary>
    /// The moments a pet reacts to, most important first: when two happen together the earlier one is the one the
    /// pet reacts to.
    /// </summary>
    public enum PetChatterTrigger
    {
        OwnerDied = 0,
        RareDrop = 1,
        LevelUp = 2,
        BruiserKill = 3,
        CriticalHit = 4,
        LowHealth = 5,
        LongIdle = 6,
    }

    /// <summary>How a pet says things: in words, or with an emoji.</summary>
    public enum PetVoice
    {
        Text = 0,
        Emoji = 1,
    }

    /// <summary>
    /// What one pet says, moment by moment. A pet that speaks in words (the Drone Core) fills <c>lines</c>; an
    /// animal pet fills <c>emoji</c>. Every moment wants at least eight, so the pet does not repeat itself; it never
    /// says the same one twice in a row (<see cref="PetChatterRules"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Pets/Pet Chatter")]
    public sealed class PetChatterSet : ScriptableObject
    {
        /// <summary>How many different things a pet should have to say at each moment.</summary>
        public const int WantedVariants = 8;

        [Serializable]
        public sealed class Moment
        {
            public PetChatterTrigger trigger;
            [Tooltip("What a pet that speaks in words says. One is picked each time, never the same twice running. Keep them short: they are read in a glance.")]
            public string[] lines = Array.Empty<string>();
            [Tooltip("What a pet that speaks in emoji shows. Baked from the emotion pack by Krytical Nova ▸ Items ▸ 9. Bake Pet Emoji.")]
            public Sprite[] emoji = Array.Empty<Sprite>();
        }

        [Tooltip("Words in a bubble, or an emoji in a bubble.")]
        public PetVoice voice;
        public Moment[] moments = Array.Empty<Moment>();

        public Moment Find(PetChatterTrigger trigger)
        {
            if (moments == null) return null;
            foreach (Moment moment in moments)
                if (moment != null && moment.trigger == trigger) return moment;
            return null;
        }

        /// <summary>How many different things this pet has to say at a moment, in the voice it speaks in.</summary>
        public int Count(PetChatterTrigger trigger)
        {
            Moment moment = Find(trigger);
            if (moment == null) return 0;
            return voice == PetVoice.Text ? (moment.lines?.Length ?? 0) : (moment.emoji?.Length ?? 0);
        }
    }
}
