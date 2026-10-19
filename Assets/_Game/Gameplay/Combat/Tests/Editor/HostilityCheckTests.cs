using KryticalNova.Player;
using NUnit.Framework;

namespace KryticalNova.Combat.Tests
{
    /// <summary>
    /// The click-to-move player asks whether a target may be struck through <see cref="HostilityCheck"/>, which Combat
    /// fills with its own rule when the game loads (architecture stage 5). Empty, no click would ever start an attack.
    /// </summary>
    public sealed class HostilityCheckTests
    {
        [Test]
        public void CombatFillsTheHostilityRule() =>
            Assert.IsNotNull(HostilityCheck.Rule, "Combat's CombatHostility fills HostilityCheck when the game loads");

        [Test]
        public void NoTargetIsNeverHostile() => Assert.IsFalse(HostilityCheck.IsHostile(null, null));
    }
}
