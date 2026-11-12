using NUnit.Framework;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>
    /// Cutover, 12 Nov 2026: build 3's entity server had no monsters (the brains were installed only with KN_DOTS_HOSTILES=1), so no
    /// zone woke and gm.hostile.spawn failed. A built server is on by default; the variable still decides when it is set.
    /// </summary>
    public class HostileSwitchTests
    {
        [Test] public void ABuiltServer_HasMonsters_WhenTheVariableIsUnset() => Assert.IsTrue(HostileEntitiesSetup.IsOn(false, null));
        [Test] public void ABuiltServer_WithZero_HasNone() => Assert.IsFalse(HostileEntitiesSetup.IsOn(false, "0"));
        [Test] public void TheEditor_StaysOff_WhenTheVariableIsUnset() => Assert.IsFalse(HostileEntitiesSetup.IsOn(true, null));
        [Test] public void TheEditor_WithOne_HasMonsters() => Assert.IsTrue(HostileEntitiesSetup.IsOn(true, "1"));
    }
}
