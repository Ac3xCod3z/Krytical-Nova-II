using System.Reflection;
using System.Text;
using KryticalNova.Autopilot;
using NUnit.Framework;
using Unity.Collections;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// The Autopilot preferences ride in one RPC (two FixedString512Bytes halves, so the RPC stays under the 1300 B packet). The largest
    /// preferences JSON, every field at its longest text, must fit with room to spare.
    /// </summary>
    public class AutopilotRpcSizeTests
    {
        [Test]
        public void LargestPreferences_FitTheRpcString()
        {
            var worst = new AutopilotPreferences
            {
                wholeZone = false, skipTagged = false, skipGrey = false, skipRed = false, includeBruisers = false,
                magnetOn = false, magnetStims = false, magnetMaterials = false, takeCommon = false, takeUncommon = false,
                takeRare = false, takeEpic = false,
                attackSlot = int.MinValue, areaSlot = int.MinValue, gatherCount = int.MinValue, buffSlotA = int.MinValue, buffSlotB = int.MinValue,
                huntRadius = -1.17549435E-38f, stimHealthPercent = -1.17549435E-38f, stimFluxPercent = -1.17549435E-38f,
                whenPackFull = (WhenPackFull)255, whenStimsOut = (WhenStimsOut)255, whenAttacked = (WhenAttacked)255,
            };
            int bytes = Encoding.UTF8.GetByteCount(worst.ToJson());
            Assert.LessOrEqual(bytes * 3 / 2, AutopilotRpcText.MaxBytes, $"the largest preferences are {bytes} B: keep half again as much room");
        }

        [Test]
        public void EveryPreferencesField_IsInTheMeasure()
        {
            int fields = typeof(AutopilotPreferences).GetFields(BindingFlags.Public | BindingFlags.Instance).Length;
            Assert.AreEqual(23, fields, "a field was added or removed: set it to its longest text in LargestPreferences_FitTheRpcString, then update this count");
        }

        [Test]
        public void TheHalves_JoinBackToTheSameText()
        {
            string json = new AutopilotPreferences().ToJson() + new string('x', 400) + "é" + char.ConvertFromUtf32(0x1F600);
            Assert.IsTrue(AutopilotRpcText.TrySplit(json, out FixedString512Bytes a, out FixedString512Bytes b));
            Assert.AreEqual(json, AutopilotRpcText.Join(a, b));
        }

        [Test]
        public void TooLongText_IsRefused()
        {
            Assert.IsFalse(AutopilotRpcText.TrySplit(new string('x', AutopilotRpcText.MaxBytes + 1), out _, out _));
        }
    }
}
