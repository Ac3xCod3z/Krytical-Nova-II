using KryticalNova.GameFeel;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Perf clean-ups round 2, chunk 15 part 2: <see cref="MountPulse"/> and <see cref="PetPulse"/> keep what a renderer's material says
    /// (looked at again when the material reference changes and at each new pulse) instead of asking it every frame. The kept answer is
    /// the old per-frame expression's, on materials with and without an emission colour, with emission on and off, and with _BaseColor,
    /// _Color or neither.
    /// </summary>
    public sealed class PulseColourCacheTests
    {
        static readonly int Emission = Shader.PropertyToID("_EmissionColor"), Base = Shader.PropertyToID("_BaseColor"), Col = Shader.PropertyToID("_Color");

        static Material Make(string shader)
        {
            Shader s = Shader.Find(shader);
            Assume.That(s != null, "shader " + shader + " is in the project");
            return new Material(s) { hideFlags = HideFlags.HideAndDontSave };
        }

        [TestCase("Universal Render Pipeline/Lit", true)]
        [TestCase("Universal Render Pipeline/Lit", false)]
        [TestCase("Universal Render Pipeline/Unlit", false)]
        [TestCase("Sprites/Default", false)]
        public void TheMountPulsesKeptAnswer_IsTheOldPerFrameOne(string shader, bool emissionOn)
        {
            Material m = Make(shader);
            try
            {
                if (m.HasProperty(Emission)) m.SetColor(Emission, new Color(0.2f, 0.5f, 0.9f, 1f));
                if (emissionOn) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
                bool oldGlows = m.HasProperty(Emission) && m.IsKeywordEnabled("_EMISSION");
                bool oldRestores = m.HasProperty(Emission);
                byte kind = MountPulse.KindOf(m, out Color emission);
                Assert.AreEqual(oldGlows, kind == 2, "glows");
                Assert.AreEqual(oldRestores, kind != 0, "restored at the end");
                if (oldRestores) Assert.AreEqual(m.GetColor(Emission), emission);
                Assert.AreEqual(0, MountPulse.KindOf(null, out _), "no material");
            }
            finally { Object.DestroyImmediate(m); }
        }

        [TestCase("Universal Render Pipeline/Lit")]
        [TestCase("Universal Render Pipeline/Unlit")]
        [TestCase("Sprites/Default")]
        public void ThePetPulsesKeptAnswer_IsTheOldPerFrameOne(string shader)
        {
            Material m = Make(shader);
            try
            {
                int oldId = m.HasProperty(Base) ? Base : m.HasProperty(Col) ? Col : 0;
                int id = PetPulse.ColourOf(m, out Color colour);
                Assert.AreEqual(oldId, id);
                if (oldId != 0) Assert.AreEqual(m.GetColor(oldId), colour);
                Assert.AreEqual(0, PetPulse.ColourOf(null, out _));
            }
            finally { Object.DestroyImmediate(m); }
        }
    }
}
