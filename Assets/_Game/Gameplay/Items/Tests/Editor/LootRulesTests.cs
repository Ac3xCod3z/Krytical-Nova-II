using KryticalNova.Factions;
using KryticalNova.Items;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    public sealed class LootRulesTests
    {
        private const float Range = 3f;

        [Test]
        public void InReachAndFree_MayBeTaken()
        {
            Assert.IsNull(LootRules.PickupBlockedReason(2.9f, Range, false, FactionId.None, FactionId.Hegemony));
            Assert.IsNull(LootRules.PickupBlockedReason(0f, Range, false, FactionId.Hegemony, FactionId.Hegemony));
        }

        [Test]
        public void TooFar_IsRefused()
        {
            Assert.AreEqual(LootRules.ReasonTooFar, LootRules.PickupBlockedReason(3.1f, Range, false, FactionId.None, FactionId.Hegemony));
        }

        [Test]
        public void HeldForSomeoneElse_IsRefused()
        {
            Assert.AreEqual(LootRules.ReasonReserved, LootRules.PickupBlockedReason(1f, Range, true, FactionId.Hegemony, FactionId.Hegemony));
        }

        [Test]
        public void AnotherFactionsDrop_IsRefused_UnlessTheZoneAllowsIt()
        {
            Assert.AreEqual(LootRules.ReasonOtherFaction, LootRules.PickupBlockedReason(1f, Range, false, FactionId.Null, FactionId.Hegemony));
            Assert.IsNull(LootRules.PickupBlockedReason(1f, Range, false, FactionId.Null, FactionId.Hegemony, crossFaction: true));
        }

        [Test]
        public void TheFirstDropLiesOnTheSpot_TheRestKeepTheirDistance()
        {
            const float Spacing = 0.6f;
            const int Count = 24;
            var xs = new float[Count];
            var zs = new float[Count];
            for (int i = 0; i < Count; i++) LootRules.ScatterOffset(i, Spacing, out xs[i], out zs[i]);

            Assert.AreEqual(0f, xs[0]);
            Assert.AreEqual(0f, zs[0]);
            for (int a = 0; a < Count; a++)
            {
                for (int b = a + 1; b < Count; b++)
                {
                    float dx = xs[a] - xs[b], dz = zs[a] - zs[b];
                    Assert.GreaterOrEqual(System.Math.Sqrt(dx * dx + dz * dz), Spacing * 0.99, $"drops {a} and {b} overlap");
                }
            }
            // And they stay close: two dozen things within three metres of where they fell.
            Assert.LessOrEqual(System.Math.Sqrt(xs[Count - 1] * xs[Count - 1] + zs[Count - 1] * zs[Count - 1]), 3f);
        }

        [Test]
        public void ADropNobodyOwned_IsOpenToEveryFaction()
        {
            Assert.IsNull(LootRules.PickupBlockedReason(1f, Range, false, FactionId.None, FactionId.Covenant));
            Assert.IsNull(LootRules.PickupBlockedReason(1f, Range, false, FactionId.None, FactionId.None));
        }
    }
}
