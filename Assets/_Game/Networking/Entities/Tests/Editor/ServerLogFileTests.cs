using NUnit.Framework;

namespace KryticalNova.Networking.Entities.Tests
{
    public class ServerLogFileTests
    {
        [Test]
        public void Mask_HidesTokensPasswordsAndLongBlobs_KeepsTheRest()
        {
            Assert.AreEqual("[GM] Ascension is in the game as GM (connection 2)", ServerLogFile.Mask("[GM] Ascension is in the game as GM (connection 2)"));
            StringAssert.DoesNotContain("hunter2", ServerLogFile.Mask("login password=hunter2 ok"));
            StringAssert.DoesNotContain("abc123", ServerLogFile.Mask("join token: abc123"));
            string blob = new string('a', 80);
            StringAssert.DoesNotContain(blob, ServerLogFile.Mask("sent " + blob));
        }
    }
}
