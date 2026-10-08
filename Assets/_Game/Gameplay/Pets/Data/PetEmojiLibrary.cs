using System;
using UnityEngine;

namespace KryticalNova.Pets
{
    /// <summary>
    /// The emoji the animal pets speak in. Each one is a drawing of the Cartoon Emotion Pack (a face or an icon),
    /// redrawn for the comic bubble: its lines in ink, its shapes in two colours of the Colour Bible. The pack's
    /// files are only read; what the game shows are the copies listed here, baked by Krytical Nova ▸ Items ▸ 9. Bake
    /// Pet Emoji. A pet's <see cref="PetChatterSet"/> picks its emoji from these.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Pets/Pet Emoji Library")]
    public sealed class PetEmojiLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Emoji
        {
            [Tooltip("What it is called here and in the baked file: Face_Smile_01, Icon_Heart_01 ...")]
            public string name;
            [Tooltip("The pack's effect this drawing comes from (Assets/_ThirdParty/Bitnas/Prefabs/CartoonEmotionPack/Black&White). Only read.")]
            public GameObject source;
            [Tooltip("What fills its shapes (the whites of the eyes, the body of an icon).")]
            public Color fill = new Color32(0xF4, 0xEE, 0xDF, 0xFF);
            [Tooltip("Its second colour (an iris, a shine).")]
            public Color accent = new Color32(0x4F, 0xE3, 0xF0, 0xFF);
            [Tooltip("The baked copy the game shows.")]
            public Sprite sprite;
        }

        [Tooltip("The colour of every emoji's lines: the Colour Bible's ink.")]
        public Color ink = new Color32(0x14, 0x12, 0x2B, 0xFF);
        [Tooltip("Pixels along one side of a baked emoji.")]
        [Min(32)] public int size = 128;
        [Tooltip("Clear pixels left round the drawing.")]
        [Min(0)] public int padding = 6;
        public Emoji[] emoji = Array.Empty<Emoji>();

        public Emoji Find(string name)
        {
            foreach (Emoji each in emoji)
                if (each != null && each.name == name) return each;
            return null;
        }
    }
}
