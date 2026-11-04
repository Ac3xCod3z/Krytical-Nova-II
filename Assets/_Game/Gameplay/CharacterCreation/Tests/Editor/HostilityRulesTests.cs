using KryticalNova.Factions;
using NUnit.Framework;

namespace KryticalNova.CharacterCreation.Tests
{
    /// <summary>
    /// The hostility rule as plain values (M4): every case (1 024) against a copy of the managed rule as it was (CombatHostility.IsHostile
    /// after its self check + FactionRules.AreHostile, 3 Nov 2026), and the cases the owner's rules name.
    /// </summary>
    public sealed class HostilityRulesTests
    {
        sealed class Member : IFactionMember
        {
            public FactionId Faction { get; set; }
            public Member Duels;
            public bool IsDuelling(IFactionMember other) => ReferenceEquals(Duels, other);
        }

        // the managed rule between two players, the switch and the Scar as arguments instead of statics
        static bool Managed(IFactionMember a, IFactionMember b, bool neutralGround, bool alwaysOnPvp, bool crossFactionPvp)
        {
            if (!neutralGround && alwaysOnPvp && a != null && b != null && !ReferenceEquals(a, b)
                && a.Faction != FactionId.None && b.Faction != FactionId.None && a.Faction != b.Faction)
                return true;
            if (!neutralGround)
            {
                if (a == null || b == null || ReferenceEquals(a, b)) return false;
                if (a.IsDuelling(b) || b.IsDuelling(a)) return true;
                return crossFactionPvp && a.Faction != FactionId.None && b.Faction != FactionId.None && a.Faction != b.Faction;
            }
            if (a == null || b == null || ReferenceEquals(a, b)) return false;
            return a.IsDuelling(b) || b.IsDuelling(a);
        }

        [Test]
        public void EveryCase_EqualsTheManagedRule()
        {
            int cases = 0;
            for (int fa = 0; fa < 4; fa++)
            for (int fb = 0; fb < 4; fb++)
            for (int flags = 0; flags < 64; flags++)
            {
                bool same = (flags & 1) != 0, duel = (flags & 2) != 0, neutral = (flags & 4) != 0, scar = (flags & 8) != 0, pvp = (flags & 16) != 0, missing = (flags & 32) != 0;
                var a = new Member { Faction = (FactionId)fa };
                var b = same ? a : new Member { Faction = (FactionId)fb };
                if (duel && !same) a.Duels = b;
                IFactionMember ma = missing ? null : a, mb = b;   // a player with no faction member at all
                bool duelling = ma != null && mb != null && !ReferenceEquals(ma, mb) && (ma.IsDuelling(mb) || mb.IsDuelling(ma));
                bool got = HostilityRules.IsHostile(ma?.Faction ?? FactionId.None, mb.Faction, true, ma != null && ReferenceEquals(ma, mb), duelling, neutral, scar, pvp);
                Assert.AreEqual(Managed(ma, mb, neutral, scar, pvp), got, $"a {(FactionId)fa} b {(FactionId)fb} same {same} duel {duel} neutral {neutral} scar {scar} pvp {pvp} missing {missing}");
                cases++;
            }
            Assert.AreEqual(1024, cases);
        }

        [Test]
        public void TheOwnersRules()
        {
            const FactionId H = FactionId.Hegemony, N = FactionId.Null;
            Assert.IsTrue(HostilityRules.IsHostile(H, H, false, false, false, true, false, false), "anything that is not a player is hostile, even on neutral ground");
            Assert.IsTrue(HostilityRules.IsHostile(H, N, true, false, false, false, false, true), "other factions are hostile with PvP on (default)");
            Assert.IsFalse(HostilityRules.IsHostile(H, N, true, false, false, false, false, false), "gm.pvp off: peace");
            Assert.IsFalse(HostilityRules.IsHostile(H, H, true, false, false, false, true, true), "the same faction never");
            Assert.IsTrue(HostilityRules.IsHostile(H, H, true, false, true, false, false, false), "a duel, even in one faction");
            Assert.IsFalse(HostilityRules.IsHostile(H, N, true, false, false, true, true, true), "neutral ground wins over PvP and the Scar");
            Assert.IsTrue(HostilityRules.IsHostile(H, N, true, false, true, true, false, false), "a duel on neutral ground");
            Assert.IsTrue(HostilityRules.IsHostile(H, N, true, false, false, false, true, false), "The Scar, whatever the switch");
            Assert.IsFalse(HostilityRules.IsHostile(FactionId.None, N, true, false, false, false, true, true), "no faction, no war");
            Assert.IsFalse(HostilityRules.IsHostile(H, N, true, true, true, false, true, true), "never itself");
        }
    }
}
