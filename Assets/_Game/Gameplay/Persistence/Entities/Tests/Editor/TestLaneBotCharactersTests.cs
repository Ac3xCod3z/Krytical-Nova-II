using NUnit.Framework;
using KryticalNova.CharacterCreation;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>Plan 43.1: making bot characters exists only in a test-lane build; anywhere else the id sent is the id loaded.</summary>
    public class TestLaneBotCharactersTests
    {
        [TearDown] public void TearDown() => TestLaneBotCharacters.IsBot = null;

        [Test]
        public void WithoutTheTestLane_NothingIsMade_AndTheSentIdIsKept_EvenForABot()
        {
            Assert.IsFalse(TestLaneBotCharacters.Compiled, "the Editor and every non-test build compile it out (no KN_TEST_LANE)");
            TestLaneBotCharacters.IsBot = _ => true; // even an account that says it is a bot
            string id = TestLaneBotCharacters.Ensure("0123456789abcdef0123456789abcdef", "fedcba9876543210fedcba9876543210", "Bot1",
                default(CharacterRecipe), out string refusal);
            Assert.AreEqual("fedcba9876543210fedcba9876543210", id);
            Assert.IsNull(refusal);
        }
    }
}
