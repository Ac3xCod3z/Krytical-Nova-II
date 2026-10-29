using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;

namespace KryticalNova.Networking.Entities.Tests
{
    // ---- probe systems for the group order check (they only record while a test switches them on) ----
    internal static class GoInGameProbe
    {
        public static bool On;
        public static readonly List<string> Log = new List<string>();
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(GhostSimulationSystemGroup))]
    internal partial class GoInGameProbeGhostSystem : SystemBase
    {
        protected override void OnUpdate() { if (GoInGameProbe.On) GoInGameProbe.Log.Add("ghost"); }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(KnRulesGroup))]
    internal partial class GoInGameProbeRulesSystem : SystemBase
    {
        protected override void OnUpdate() { if (GoInGameProbe.On) GoInGameProbe.Log.Add("rules"); }
    }

    /// <summary>M3a-2b step 4 (pure part): the character text travels in at most three pages.</summary>
    public class GoInGamePagesTests
    {
        private static string Text(int n)
        {
            var chars = new char[n];
            for (int i = 0; i < n; i++) chars[i] = (char)('a' + (i * 7 + i / 26) % 26);
            return new string(chars);
        }

        [Test]
        public void ThreePages_RoundTripInAnyOrder()
        {
            string text = Text(GoInGamePages.MaxChars);
            var pages = new List<string>();
            Assert.IsTrue(GoInGamePages.TrySplit(text, pages));
            Assert.AreEqual(3, pages.Count);
            var assembler = new GoInGamePages.Assembler();
            Assert.AreEqual(KnRosterPages.Add.Ok, assembler.Take(2, 3, pages[2]));
            Assert.AreEqual(KnRosterPages.Add.Ok, assembler.Take(0, 3, pages[0]));
            Assert.AreEqual(KnRosterPages.Add.Ok, assembler.Take(0, 3, pages[0]), "a duplicate is ignored");
            Assert.AreEqual(KnRosterPages.Add.Complete, assembler.Take(1, 3, pages[1]));
            Assert.AreEqual(text, assembler.Text);
        }

        [Test]
        public void TooLong_NonAscii_AndEmpty()
        {
            var pages = new List<string>();
            Assert.IsFalse(GoInGamePages.TrySplit(Text(GoInGamePages.MaxChars + 1), pages));
            Assert.IsFalse(GoInGamePages.TrySplit("café", pages));
            Assert.IsTrue(GoInGamePages.TrySplit(string.Empty, pages));
            CollectionAssert.AreEqual(new[] { string.Empty }, pages);
        }

        [Test]
        public void MoreThanThreePages_OrAContradiction_IsBad()
        {
            var assembler = new GoInGamePages.Assembler();
            Assert.AreEqual(KnRosterPages.Add.Bad, assembler.Take(0, GoInGamePages.MaxPages + 1, "x"));
            Assert.IsTrue(assembler.IsBad);
            var other = new GoInGamePages.Assembler();
            other.Take(0, 2, "a");
            Assert.AreEqual(KnRosterPages.Add.Bad, other.Take(1, 3, "b"), "the page count changed");
            var big = new GoInGamePages.Assembler();
            Assert.AreEqual(KnRosterPages.Add.Bad, big.Take(0, 1, new string('x', GoInGamePages.PageChars + 1)));
        }

        [Test]
        public void TheRequest_StaysWellUnderOnePacket()
        {
            Assert.Less(Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<GoInGameRequest>() + 16, RpcSizeLintTests.MaxSerializedBytes);
        }
    }

    /// <summary>M3a-2b step 4 (worlds): client send, server admission slot, refusals.</summary>
    public class KnGoInGameTests
    {
        private sealed class OnlyFor : IKnGoInGameSource
        {
            public World Client;
            public GoInGamePayload Payload;
            public bool TryGet(World clientWorld, out GoInGamePayload payload)
            {
                payload = Payload;
                return Client != null && clientWorld == Client;
            }
        }

        private sealed class Recording : IKnAdmission
        {
            public readonly List<KnAdmissionRequest> Seen = new List<KnAdmissionRequest>();
            public string Answer;
            public bool Throw;
            public string Admit(in KnAdmissionRequest request)
            {
                Seen.Add(request);
                if (Throw) throw new InvalidOperationException("boom");
                return Answer;
            }
        }

        private static GoInGamePayload Payload(string recipe = "{\"r\":1}") =>
            new GoInGamePayload { CharacterId = "0123456789abcdef0123456789abcdef", Name = "Vesper", RecipeText = recipe };

        private static int InGameOnServer(KnTestWorld tw)
        {
            using var q = tw.Server.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            return q.CalculateEntityCount();
        }

        private static KnTestWorld TwoClients(out KnAdmissionSystem admission)
        {
            var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            tw.Connect(goInGame: false);
            tw.Tick(3);
            admission = tw.Server.GetExistingSystemManaged<KnAdmissionSystem>();
            return tw;
        }

        [Test]
        public void Admitted_OnlyTheClientThatAsked_GetsNetworkStreamInGame_AndAskingTwiceChangesNothing()
        {
            using var tw = TwoClients(out KnAdmissionSystem system);
            var fake = new Recording();
            system.Admission = fake;
            var source = new OnlyFor { Client = tw.Clients[0], Payload = Payload() };
            tw.Clients[0].GetExistingSystemManaged<KnClientGoInGameSystem>().Source = source;
            tw.Clients[1].GetExistingSystemManaged<KnClientGoInGameSystem>().Source = source;

            Assert.GreaterOrEqual(tw.TickUntil(() => InGameOnServer(tw) == 1, 300), 0, "never admitted. Logs: " + string.Join(" | ", tw.Interesting));
            Assert.AreEqual(1, fake.Seen.Count);
            KnAdmissionRequest seen = fake.Seen[0];
            Assert.AreEqual("0123456789abcdef0123456789abcdef", seen.CharacterId);
            Assert.AreEqual("Vesper", seen.Name);
            Assert.AreEqual("{\"r\":1}", seen.RecipeText);
            Assert.IsTrue(seen.Key.IsValid);
            Assert.AreEqual(KnTestWorld.NetworkIdOf(tw.Clients[0]), seen.NetworkId);

            // a second request on the same connection is ignored
            var again = tw.Clients[0].EntityManager.CreateEntity(typeof(GoInGameRequest), typeof(SendRpcCommandRequest));
            tw.Clients[0].EntityManager.SetComponentData(again, new GoInGameRequest { CharacterId = new FixedString64Bytes("x"), Name = new FixedString64Bytes("y"), PageCount = 1 });
            tw.Tick(30);
            Assert.AreEqual(1, fake.Seen.Count);
            Assert.AreEqual(1, InGameOnServer(tw));
            Assert.AreEqual(1, system.Admitted);
        }

        [Test]
        public void ARecipeOfThreePages_ArrivesWhole()
        {
            using var tw = TwoClients(out KnAdmissionSystem system);
            var fake = new Recording();
            system.Admission = fake;
            string text = new string('q', GoInGamePages.MaxChars - 7);
            tw.Clients[0].GetExistingSystemManaged<KnClientGoInGameSystem>().Source = new OnlyFor { Client = tw.Clients[0], Payload = Payload(text) };
            Assert.GreaterOrEqual(tw.TickUntil(() => fake.Seen.Count == 1, 300), 0, "never admitted. Logs: " + string.Join(" | ", tw.Interesting));
            Assert.AreEqual(text, fake.Seen[0].RecipeText);
        }

        [Test]
        public void ARefusal_ReachesThePlayerAndEndsItsConnection_TheOtherStays()
        {
            using var tw = TwoClients(out KnAdmissionSystem system);
            system.Admission = new Recording { Answer = "That character is not on this server." };
            tw.Clients[0].GetExistingSystemManaged<KnClientGoInGameSystem>().Source = new OnlyFor { Client = tw.Clients[0], Payload = Payload() };
            Assert.GreaterOrEqual(tw.TickUntil(() => KnTestWorld.Count<KnJoinRefusal>(tw.Clients[0]) == 1, 300), 0, "no refusal. Logs: " + string.Join(" | ", tw.Interesting));
            Assert.AreEqual("That character is not on this server.", KnTestWorld.Singleton<KnJoinRefusal>(tw.Clients[0]).Detail.ToString());
            Assert.GreaterOrEqual(tw.TickUntil(() => KnTestWorld.Count<NetworkId>(tw.Server) == 1, 300), 0, "connection was not closed");
            Assert.AreEqual(0, InGameOnServer(tw), "a refused player never gets snapshots");
            Assert.AreEqual(1, system.Refused);
        }

        [Test]
        public void NoAdmissionInstalled_RefusesWithAReason_NotASilentHang()
        {
            using var tw = TwoClients(out KnAdmissionSystem system);
            Assert.IsNull(system.Admission);
            tw.Clients[0].GetExistingSystemManaged<KnClientGoInGameSystem>().Source = new OnlyFor { Client = tw.Clients[0], Payload = Payload() };
            Assert.GreaterOrEqual(tw.TickUntil(() => KnTestWorld.Count<KnJoinRefusal>(tw.Clients[0]) == 1, 300), 0, "no refusal. Logs: " + string.Join(" | ", tw.Interesting));
            Assert.AreEqual(KnAdmissionSystem.NotReadyText, KnTestWorld.Singleton<KnJoinRefusal>(tw.Clients[0]).Detail.ToString());
        }

        [Test]
        public void AnAdmissionThatThrows_RefusesInsteadOfLeavingThePlayerHanging()
        {
            using var tw = TwoClients(out KnAdmissionSystem system);
            system.Admission = new Recording { Throw = true };
            tw.Clients[0].GetExistingSystemManaged<KnClientGoInGameSystem>().Source = new OnlyFor { Client = tw.Clients[0], Payload = Payload() };
            Assert.GreaterOrEqual(tw.TickUntil(() => KnTestWorld.Count<KnJoinRefusal>(tw.Clients[0]) == 1, 300), 0, "no refusal. Logs: " + string.Join(" | ", tw.Interesting));
            Assert.AreEqual(KnAdmissionSystem.FailedText, KnTestWorld.Singleton<KnJoinRefusal>(tw.Clients[0]).Detail.ToString());
        }

        [Test]
        public void ARequestFromAConnectionWhoseKeyDied_IsDropped()
        {
            using var tw = TwoClients(out KnAdmissionSystem system);
            var fake = new Recording();
            system.Admission = fake;
            var keys = KnTestQueries.ServerKeys(tw);
            int id = KnTestWorld.NetworkIdOf(tw.Clients[0]);
            ulong key = keys.Find(k => k.id == id).key;
            Assert.IsTrue(PlayerKeys.TryGetConnection(new PlayerKey(key), out ulong serial));
            PlayerKeys.Release(serial);

            tw.Clients[0].GetExistingSystemManaged<KnClientGoInGameSystem>().Source = new OnlyFor { Client = tw.Clients[0], Payload = Payload() };
            tw.Tick(40);
            Assert.AreEqual(0, fake.Seen.Count);
            Assert.AreEqual(0, InGameOnServer(tw));
        }

        [Test]
        public void AMalformedRequest_IsRefused()
        {
            using var tw = TwoClients(out KnAdmissionSystem system);
            var fake = new Recording();
            system.Admission = fake;
            var em = tw.Clients[0].EntityManager;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>()))
            {
                Entity connection = q.GetSingletonEntity();
                Entity rpc = em.CreateEntity(typeof(GoInGameRequest), typeof(SendRpcCommandRequest));
                em.SetComponentData(rpc, new GoInGameRequest { CharacterId = new FixedString64Bytes("id"), Name = new FixedString64Bytes("n"), Page = 0, PageCount = 9 });
                em.SetComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            }
            Assert.GreaterOrEqual(tw.TickUntil(() => KnTestWorld.Count<KnJoinRefusal>(tw.Clients[0]) == 1, 300), 0, "no refusal. Logs: " + string.Join(" | ", tw.Interesting));
            Assert.AreEqual(KnAdmissionSystem.MalformedText, KnTestWorld.Singleton<KnJoinRefusal>(tw.Clients[0]).Detail.ToString());
            Assert.AreEqual(0, fake.Seen.Count);
        }

        [Test]
        public void ClientWithNothingToSend_WaitsAndSendsNothing()
        {
            using var tw = TwoClients(out KnAdmissionSystem system);
            var fake = new Recording();
            system.Admission = fake;
            tw.Tick(40);
            Assert.AreEqual(0, fake.Seen.Count);
            Assert.AreEqual(0, InGameOnServer(tw));
            Assert.AreEqual(2, KnTestWorld.Count<NetworkId>(tw.Server), "waiting is not refusing");
        }

        [Test]
        public void RulesGroup_RunsAfterTheGhostSimulation()
        {
            using var tw = TwoClients(out _);
            GoInGameProbe.Log.Clear();
            GoInGameProbe.On = true;
            try { tw.Tick(6); }
            finally { GoInGameProbe.On = false; }
            Assert.Greater(GoInGameProbe.Log.Count, 0);
            for (int i = 0; i < GoInGameProbe.Log.Count; i++)
                if (GoInGameProbe.Log[i] == "rules")
                    Assert.IsTrue(i > 0 && GoInGameProbe.Log[i - 1] == "ghost", "rules ran before the ghost simulation: " + string.Join(",", GoInGameProbe.Log));
        }
    }
}
