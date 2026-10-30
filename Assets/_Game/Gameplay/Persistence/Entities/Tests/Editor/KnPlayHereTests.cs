using System.Text.RegularExpressions;
using KryticalNova.Networking.Entities;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>"Play DOTS here" stays on the test lane and can make its tester (<see cref="KnPlayHere"/>).</summary>
    public class KnPlayHereTests
    {
        [Test]
        public void StartPlayHere_RefusesEveryPortOfTheRealServer_AndStartsNothing()
        {
            foreach (int port in KnServerSettings.RealLanePorts)
            {
                LogAssert.Expect(LogType.Error, new Regex("belongs to the real server"));
                Assert.IsFalse(KnBootstrap.StartPlayHere((ushort)port, "x"), port.ToString());
            }
            Assert.AreEqual(0, KnBootstrap.ServerWorlds.Count);
            Assert.AreEqual(0, KnBootstrap.ClientWorlds.Count);
        }

        [Test]
        public void ThePlayHerePort_IsTheTestLane()
        {
            Assert.AreEqual(7797, KnPlayHere.Port);
            CollectionAssert.DoesNotContain(KnServerSettings.RealLanePorts, (int)KnPlayHere.Port);
        }

        [Test]
        public void TheTester_GetsTheFirstLineagesDefaultLook_FromTheShippedPlayerPrefab()
        {
            Assert.IsTrue(KnPlayHere.TryTesterLook(out string recipe, out string problem), problem);
            Assert.IsTrue(RecipeText.TryDecode(recipe, out _), "the look is a valid recipe");
        }
    }
}
