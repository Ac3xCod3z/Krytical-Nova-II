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
        public void TheScriptedWalk_ReadsMetres_AndRefusesAnythingElse()
        {
            var points = new System.Collections.Generic.List<Vector3>();
            Assert.IsTrue(KnPlayHere.TryPoints("0,-12;3.5,-10", points));
            Assert.AreEqual(2, points.Count);
            Assert.AreEqual(new Vector3(3.5f, 0f, -10f), points[1]);
            Assert.IsFalse(KnPlayHere.TryPoints("0,-12;3", points));
            Assert.AreEqual(0, points.Count, "a bad list leaves nothing to walk");
            Assert.IsFalse(KnPlayHere.TryPoints("", points));
        }

        [Test]
        public void TheMapToPlay_Exists()
        {
            Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(KnPlayHere.MapPath), KnPlayHere.MapPath);
        }

        [Test]
        public void TheTester_GetsTheFirstLineagesDefaultLook_FromTheShippedPlayerPrefab()
        {
            Assert.IsTrue(KnPlayHere.TryTesterLook(out string recipe, out string problem), problem);
            Assert.IsTrue(RecipeText.TryDecode(recipe, out _), "the look is a valid recipe");
        }
    }
}
