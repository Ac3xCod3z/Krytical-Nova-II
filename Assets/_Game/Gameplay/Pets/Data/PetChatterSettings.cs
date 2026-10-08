using System;
using KryticalNova.Items;
using UnityEngine;

namespace KryticalNova.Pets
{
    /// <summary>Who sees a pet's bubbles.</summary>
    public enum PetChatterAudience
    {
        /// <summary>Only the pet's owner. Nothing is sent over the network for it.</summary>
        OwnerOnly = 0,
    }

    /// <summary>
    /// How pets react to what their owner does: how often they may speak, how long a bubble stays, and what counts
    /// as low HP, a rare drop and standing idle. What they say is in each pet's <see cref="PetChatterSet"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Pets/Pet Chatter Settings")]
    public sealed class PetChatterSettings : ScriptableObject
    {
        [Header("Who sees the bubbles")]
        [Tooltip("Owner only: a pet's bubbles are for the player it belongs to. Nothing is sent for them.")]
        public PetChatterAudience audience = PetChatterAudience.OwnerOnly;

        [Header("How often")]
        [Tooltip("Seconds of quiet after any bubble before the pet speaks again. A death is the one moment that is never held back.")]
        [Min(0f)] public float quietSeconds = 8f;
        [Tooltip("Seconds before the pet reacts to the same moment again, in the order: death, rare drop, level up, Bruiser kill, critical hit, low HP, long idle.")]
        public float[] repeatSeconds = { 0f, 20f, 0f, 10f, 25f, 45f, 120f };

        [Header("The bubble")]
        [Tooltip("How long a bubble stays up, in seconds.")]
        [Min(0.5f)] public float bubbleSeconds = 2.6f;
        [Tooltip("Added to that for every character of a text line, so a longer line can be read.")]
        [Min(0f)] public float secondsPerCharacter = 0.045f;
        [Tooltip("A bubble never stays longer than this.")]
        [Min(0.5f)] public float longestBubbleSeconds = 4.5f;

        [Header("The moments")]
        [Tooltip("The pet warns when its owner's HP falls below this share of the full bar.")]
        [Range(0.05f, 0.9f)] public float lowHealthFraction = 0.25f;
        [Tooltip("It warns again only after the HP has been back above this share.")]
        [Range(0.05f, 1f)] public float lowHealthRearmFraction = 0.5f;
        [Tooltip("Seconds without moving or fighting before the pet remarks on it.")]
        [Min(5f)] public float idleSeconds = 75f;
        [Tooltip("An item picked up off the ground counts as a rare drop from this rarity up.")]
        public ItemRarity rareDropFrom = ItemRarity.Rare;

        public float RepeatSeconds(PetChatterTrigger trigger)
        {
            int index = (int)trigger;
            return repeatSeconds != null && index >= 0 && index < repeatSeconds.Length ? Mathf.Max(0f, repeatSeconds[index]) : 0f;
        }

        /// <summary>How long a bubble with this text stays up (an emoji bubble passes no text).</summary>
        public float BubbleSeconds(string text) =>
            Mathf.Min(longestBubbleSeconds, bubbleSeconds + (string.IsNullOrEmpty(text) ? 0f : text.Length * secondsPerCharacter));
    }
}
