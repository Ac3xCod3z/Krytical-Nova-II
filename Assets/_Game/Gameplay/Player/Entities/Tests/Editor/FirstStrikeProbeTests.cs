using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// Owner's hand test (5 Nov 2026): the first swing's hit and number showed before the swing. The owner's view plays a strike for each
    /// new (Step, StepStartTick) it sees (StrikePresentSystem). This probe logs every strike-1 start the owner's ghost goes through and the
    /// server's, at 100 ms, for a strike in reach and one after the server's walk in: more than one owner start for strike 1 means the
    /// swing is shown again later (the cause to prove before the fix).
    /// </summary>
    public sealed class FirstStrikeProbeTests
    {
        [Test]
        public void Probe_TheOwnersStrikeOneStarts_AgainstTheServers_InReachAndAfterTheWalkIn()
        {
            Probe("in reach", 1200);
            Probe("after the walk in", 6000);
        }

        static void Probe(string what, int targetZ)
        {
            using var rig = PlayerRig.Create(2, 100, new[] { new int3(0, 0, 0), new int3(0, 0, targetZ) });
            int target = rig.Get<GhostInstance>(rig.Server, rig.ServerGhosts[0 + 1]).ghostId;
            rig.Sources[0].Attack(target);
            var ownerStarts = new List<string>();
            string serverStart = null;
            (int, uint) last = (-1, 0);
            for (int i = 0; i < 150; i++)
            {
                rig.Tick();
                CombatState owner = rig.Get<CombatState>(rig.Clients[0], rig.GhostIn(0, 0));
                CombatState server = rig.Get<CombatState>(rig.Server, rig.ServerGhosts[0]);
                if (serverStart == null && server.Step == 1) serverStart = Tick(server.StepStartTick);
                var now = (owner.Step, owner.StepStartTick.IsValid ? owner.StepStartTick.TickIndexForValidTick : 0u);
                if (now != last && owner.Step == 1) ownerStarts.Add($"step 1 from {Tick(owner.StepStartTick)} (seen at loop {i})");
                if (now != last && owner.Step == 0 && last.Item1 > 0) ownerStarts.Add($"back to 0 at loop {i}");
                last = now;
                if (server.Step >= 2) break;
            }
            Debug.Log($"[KnSwing] {what}: server strike 1 from {serverStart ?? "never"}; the owner's strike-1 starts: " +
                      (ownerStarts.Count == 0 ? "none" : string.Join(", ", ownerStarts)));
            Assert.IsNotNull(serverStart, what + ": the server struck");
        }

        static string Tick(NetworkTick t) => t.IsValid ? "tick " + t.TickIndexForValidTick : "no tick";
    }
}
