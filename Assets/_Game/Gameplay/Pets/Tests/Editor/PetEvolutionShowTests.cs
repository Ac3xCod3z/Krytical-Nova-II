using NUnit.Framework;

namespace KryticalNova.Pets.Tests
{
    /// <summary>
    /// Owner's 2-player test, 6 Nov 2026, item 4: another player's pet evolution burst was not seen. Their ghost is interpolated, so
    /// the server's "evolved" came before the new stage and the burst landed on the old model; a view now holds it until it shows that
    /// stage (or at most EvolutionWaitSeconds).
    /// </summary>
    public class PetEvolutionShowTests
    {
        [Test]
        public void AnEvolution_PlaysOnceItsStageIsShown_OrAfterTheWait()
        {
            Assert.IsFalse(PlayerPet.EvolutionDue(2, 1, 0f), "told before the view shows stage 2: it waits");
            Assert.IsTrue(PlayerPet.EvolutionDue(2, 2, 0.1f), "the stage is shown: it plays (on the new model)");
            Assert.IsTrue(PlayerPet.EvolutionDue(2, 3, 0f), "a GM jumped past it: it plays");
            Assert.IsTrue(PlayerPet.EvolutionDue(2, 1, PlayerPet.EvolutionWaitSeconds), "never lost: it plays after the wait on what is there");
        }
    }
}
