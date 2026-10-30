using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// The click-start log of the 2-client gate (plan sections 33 and 43): each click is written once by the owner and once by the
    /// server under the same seq, and in the test world (0 ms) the two start ticks differ by at most one (the measured values are logged).
    /// </summary>
    public class PlayerClickLogTests
    {
        [OneTimeSetUp] public void OneTimeSetUp() => PlayerTestAssets.Ensure();
        [OneTimeTearDown] public void OneTimeTearDown() => PlayerTestAssets.Cleanup();
        [TearDown] public void TearDown() => PlayerClickLog.Sink = null;

        static readonly Regex Line = new Regex(@"^\[KnClick\] (client|server) owner=(\d+) seq=(\d+) tick=(\d+)$");

        [Test]
        public void EachClick_IsLoggedByOwnerAndServer_WithTheSameSeq_AndAtMostOneTickApart()
        {
            var lines = new List<string>();
            using var rig = PlayerRig.Create(1);
            PlayerClickLog.Sink = l => { lock (lines) lines.Add(l); };
            for (int i = 0; i < 3; i++)
            {
                rig.Sources[0].Click(new int3(6000 + 3000 * i, 0, 4000));
                rig.Tick(20);
            }
            PlayerClickLog.Sink = null;

            var client = new Dictionary<string, uint>();
            var server = new Dictionary<string, uint>();
            foreach (string l in lines)
            {
                Match m = Line.Match(l);
                Assert.IsTrue(m.Success, "a line the pairing script cannot read: " + l);
                Assert.AreNotEqual("0", m.Groups[2].Value, "the owner's NetworkId is named: " + l);
                (m.Groups[1].Value == "client" ? client : server)[m.Groups[2].Value + "/" + m.Groups[3].Value] = uint.Parse(m.Groups[4].Value);
            }
            Assert.AreEqual(3, client.Count, "one owner line per click: " + string.Join(" | ", lines));
            CollectionAssert.AreEquivalent(client.Keys, server.Keys, "the server started every click: " + string.Join(" | ", lines));
            var diffs = new List<long>();
            foreach (var kv in client) diffs.Add((long)server[kv.Key] - kv.Value);
            UnityEngine.Debug.Log("[M3A2B-CLICK] server tick minus owner tick per click: " + string.Join(", ", diffs));
            // section 33: the start may differ by one tick either way (command-stream tick attribution); more is a bug
            foreach (long diff in diffs) Assert.That(diff, Is.InRange(-1, 1), "server tick minus owner tick");
        }
    }
}
