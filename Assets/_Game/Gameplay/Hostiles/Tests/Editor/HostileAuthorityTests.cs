using System;
using KryticalNova.Core;
using NUnit.Framework;

namespace KryticalNova.Hostiles.Tests
{
    /// <summary>
    /// M5.0 seam: with no entity-netcode worlds live, the NGO rule exactly as before; with them live, no Hostile is spawned or removed in
    /// this process unless <see cref="HostileSpawner.AuthorityOverride"/> (M5.5's server bodies) says so.
    /// </summary>
    public sealed class HostileAuthorityTests
    {
        Func<bool> m_Probe;

        [SetUp] public void SetUp() => m_Probe = EntityNetcodeWorlds.LiveProbe;

        [TearDown]
        public void TearDown()
        {
            HostileSpawner.AuthorityOverride = null;
            EntityNetcodeWorlds.LiveProbe = m_Probe;
        }

        [Test]
        public void NoEntityWorlds_IsTheNgoRule_LiveWorlds_StandAside_AndAnOverrideDecides()
        {
            EntityNetcodeWorlds.LiveProbe = () => false;
            Assert.AreEqual(HostileSpawner.NgoAuthority, HostileSpawner.HasAuthority, "no entity worlds: as before");

            EntityNetcodeWorlds.LiveProbe = () => true;
            Assert.IsFalse(HostileSpawner.HasAuthority, "entity worlds live: NGO's Hostiles stand aside");

            HostileSpawner.AuthorityOverride = () => true;
            Assert.IsTrue(HostileSpawner.HasAuthority, "the override decides (M5.5's server bodies)");
        }
    }
}
