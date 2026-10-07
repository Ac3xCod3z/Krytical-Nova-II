using System;
using KryticalNova.CharacterCreation;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>One wedge of the emote wheel's first ring: a name, and the library groups whose motions it holds.</summary>
    [Serializable]
    public struct EmoteCategory
    {
        public string name;
        [Tooltip("Groups of the expression library (Greet, Taunt, Lean ...). Both its emotes and its stances of these groups are listed.")]
        public string[] groups;

        public EmoteCategory(string name, params string[] groups)
        {
            this.name = name; this.groups = groups;
        }
    }

    /// <summary>
    /// The emote wheel: which motions of the expression library a player can perform in the world, how they are
    /// sorted into the wheel's categories, and how often one may be started.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Social/Emote Wheel Settings", fileName = "EmoteWheelSettings")]
    public sealed class EmoteWheelSettings : ScriptableObject
    {
        [Tooltip("The motions themselves: the same library the character creator shows.")]
        public CharacterExpressionLibrary library;
        [Tooltip("Emotes shown at once in the second ring. The mouse wheel turns to the next ones.")]
        [Range(4, 12)] public int pageSize = 8;
        [Tooltip("Seconds before the same player may start another emote.")]
        [Min(0f)] public float cooldownSeconds = 1f;
        [Tooltip("The first ring, clockwise from the top. A library group that no category names cannot be performed.")]
        public EmoteCategory[] categories = DefaultCategories();

        public static EmoteCategory[] DefaultCategories() => new[]
        {
            new EmoteCategory("Greet", "Greet", "Wave", "Head Nod", "Head Shake"),
            new EmoteCategory("Cheer", "Happy", "Celebrate"),
            new EmoteCategory("Moves", "Dance", "Sporty"),
            new EmoteCategory("Taunt", "Taunt", "Aggressive", "Angry", "Reproach"),
            new EmoteCategory("Feels", "Sad", "Affection", "Plead", "Pray", "Bored", "Yawn"),
            new EmoteCategory("Gestures", "Point Hand", "Look", "Thoughtful", "Inspect", "Stretch", "Check Watch", "Play With Hair", "Weight Shift"),
            new EmoteCategory("Props", "Use Phone", "Eat", "Drink"),
            new EmoteCategory("Stances", "Lean", "Arms Folded", "Hands On Hips", "Posture", "Sway", "Pick Nose"),
        };
    }
}
