using System.Security.Cryptography;
using CTFX2.Creator.Objects;
using UnityEngine;

namespace PSD.Creator.Objects
{
    [CreateAssetMenu(fileName = "Randomization", menuName = "CTFX2/Randomization", order = 1)]
    public class RandomizationObject : ScriptableObject
    {
        public BackgroundObject[] backgrounds;
        public HalftoneObject[] halftones;
        public TextObject[] texts;
        public ImpactObject[] impacts;
        public EffectsObject[] effects;
        public SmokeObject[] smokes;
        public DebrisObject[] debris;

        public T Get<T>(T[] array) where T : ComponentObject
        {
            if (array == null || array.Length == 0) return null;
            return array[RandomNumberGenerator.GetInt32(array.Length)];
        }
    }
}
