using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Tests;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// M7.4 GM lines on the entity netcode, by NGO's <c>PlayerGm</c> rules: a player who is not GM is refused and nothing runs; a player whose
    /// account the owner bound to a GM is made GM and told so; their lines run on the server for their connection and only they get the
    /// answer, a long answer in one piece; taking the GM off tells them and the next line is refused.
    /// </summary>
    public class GmLineTests
    {
        readonly List<(ulong id, string line)> m_Ran = new List<(ulong, string)>();
        Func<ulong, string, string> m_OldRunner;

        [SetUp]
        public void SetUp()
        {
            m_Ran.Clear();
            m_OldRunner = PlayerGm.CommandRunner;
            PlayerGm.CommandRunner = (id, line) =>
            {
                m_Ran.Add((id, line));
                return line == "gm.help" ? new string('x', 1200) + " – end" : "ran " + line;
            };
            GmRoster.SetAll(Array.Empty<GmEntry>());
        }

        [TearDown]
        public void TearDown()
        {
            PlayerGm.CommandRunner = m_OldRunner;
            GmRoster.SetAll(Array.Empty<GmEntry>());
        }

        static ConnectionPlayerKey ServerKeyOf(KnTestWorld tw, World client)
        {
            int networkId = KnTestWorld.NetworkIdOf(client);
            var em = tw.Server.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<ConnectionPlayerKey>());
            using var ids = query.ToComponentDataArray<NetworkId>(Allocator.Temp);
            using var keys = query.ToComponentDataArray<ConnectionPlayerKey>(Allocator.Temp);
            for (int i = 0; i < ids.Length; i++) if (ids[i].Value == networkId) return keys[i];
            Assert.Fail("the server has no key for that client");
            return default;
        }

        [Test]
        public void OnlyABoundGm_RunsLines_ForTheirOwnConnection_AndOnlyTheyGetTheAnswer()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            tw.Connect();
            var a = tw.Clients[0].GetExistingSystemManaged<GmLineClientSystem>();
            var b = tw.Clients[1].GetExistingSystemManaged<GmLineClientSystem>();
            var server = tw.Server.GetExistingSystemManaged<GmLineServerSystem>();
            Assert.IsNotNull(a, "the client world has the GM link");
            Assert.IsNotNull(server, "the server world has the GM lines");
            tw.Tick(5);

            a.GmLink.Send("gm.time 0");
            tw.TickUntil(() => a.Replies > 0, 120);
            Assert.AreEqual("You are not a GM on this server.", a.LastReply, "not GM: refused");
            Assert.AreEqual(0, m_Ran.Count, "and nothing ran");
            Assert.IsFalse(a.GmLink.IsGm);

            // the owner binds A's account to the GM "Ace" on the control page
            ConnectionPlayerKey keyA = ServerKeyOf(tw, tw.Clients[0]);
            ServerAccounts.Remember(keyA.PlayerKey, "acct-ace", string.Empty);
            try
            {
                GmRoster.SetAll(new[] { new GmEntry { name = "Ace", account = "acct-ace" } });
                Assert.IsTrue(GmRoster.SignInBound(keyA.Serial, out string name), "A's account is bound");
                Assert.AreEqual("Ace", name);
                int r0 = a.Replies;
                tw.TickUntil(() => a.GmLink.IsGm && a.Replies > r0, 120);
                Assert.IsTrue(a.GmLink.IsGm, "A is told it is GM");
                Assert.AreEqual("Ace", a.GmLink.GmName);
                StringAssert.StartsWith("You are GM (Ace) on this server.", a.LastReply);
                Assert.IsFalse(b.GmLink.IsGm, "B is not");

                int b0 = b.Replies;
                r0 = a.Replies;
                a.GmLink.Send("  gm.time 0  ");
                tw.TickUntil(() => a.Replies > r0, 120);
                Assert.AreEqual(1, m_Ran.Count, "the line ran once");
                Assert.AreEqual((keyA.Serial, "gm.time 0"), m_Ran[0], "for A's connection, trimmed");
                Assert.AreEqual("ran gm.time 0", a.LastReply, "A gets the answer");

                r0 = a.Replies;
                a.GmLink.Send("gm.help");
                tw.TickUntil(() => a.Replies > r0, 120);
                Assert.AreEqual(new string('x', 1200) + " – end", a.LastReply, "a long answer arrives whole, in one reply");
                Assert.AreEqual(r0 + 1, a.Replies);
                tw.Tick(10);
                Assert.AreEqual(b0, b.Replies, "B got none of A's answers");

                // the owner takes the GM off
                r0 = a.Replies;
                GmRoster.SetAll(Array.Empty<GmEntry>());
                tw.TickUntil(() => !a.GmLink.IsGm && a.Replies > r0, 120);
                Assert.IsFalse(a.GmLink.IsGm, "A is told GM rights ended");
                Assert.AreEqual("Your GM rights were removed on this server.", a.LastReply);
                r0 = a.Replies;
                a.GmLink.Send("gm.time 0");
                tw.TickUntil(() => a.Replies > r0, 120);
                Assert.AreEqual("You are not a GM on this server.", a.LastReply);
                Assert.AreEqual(2, m_Ran.Count, "nothing more ran");
                Assert.AreEqual(0, server.OverBudget, "within the account call limits");
            }
            finally
            {
                ServerAccounts.Forget(keyA.PlayerKey);
            }
        }

        static byte TagOf(World world, int networkId)
        {
            var em = world.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerGhostTag>(), ComponentType.ReadOnly<GhostOwner>(), ComponentType.ReadOnly<PlayerGmTag>());
            using var owners = query.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            using var tags = query.ToComponentDataArray<PlayerGmTag>(Allocator.Temp);
            for (int i = 0; i < owners.Length; i++) if (owners[i].NetworkId == networkId) return tags[i].On;
            return 255;   // no such ghost here
        }

        [Test]
        public void EveryoneSeesTheGmTag_OfABoundGm_AndItGoesWhenTheGmIsTakenOff()
        {
            using var rig = PlayerRig.Create(2);
            int idA = KnTestWorld.NetworkIdOf(rig.Clients[0]), idB = KnTestWorld.NetworkIdOf(rig.Clients[1]);
            rig.Tw.Tick(5);
            Assert.AreEqual(0, TagOf(rig.Clients[1], idA), "nobody is GM yet");

            ConnectionPlayerKey keyA = ServerKeyOf(rig.Tw, rig.Clients[0]);
            ServerAccounts.Remember(keyA.PlayerKey, "acct-ace", string.Empty);
            try
            {
                GmRoster.SetAll(new[] { new GmEntry { name = "Ace", account = "acct-ace" } });
                GmRoster.SignInBound(keyA.Serial, out _);
                rig.Tw.TickUntil(() => TagOf(rig.Clients[1], idA) == 1 && TagOf(rig.Clients[0], idA) == 1, 120);
                Assert.AreEqual(1, TagOf(rig.Server, idA), "the server's ghost of A says GM");
                Assert.AreEqual(1, TagOf(rig.Clients[1], idA), "B sees A's tag");
                Assert.AreEqual(1, TagOf(rig.Clients[0], idA), "A sees its own");
                Assert.AreEqual(0, TagOf(rig.Clients[0], idB), "B is no GM");
                Assert.AreEqual(1, rig.Clients[1].GetExistingSystemManaged<GmLineClientSystem>().TagsShown);

                GmRoster.SetAll(Array.Empty<GmEntry>());
                rig.Tw.TickUntil(() => TagOf(rig.Clients[1], idA) == 0, 120);
                Assert.AreEqual(0, TagOf(rig.Clients[1], idA), "the tag goes with the GM rights");
            }
            finally
            {
                ServerAccounts.Forget(keyA.PlayerKey);
            }
        }

        [Test]
        public void ReplyParts_NeverSplitACharacter_AndJoinBackToTheText()
        {
            string text = new string('a', 499) + "–" + new string('b', 600) + "😀";
            List<string> parts = GmLineServerSystem.Parts(text, GmLineServerSystem.PartBytes);
            Assert.AreEqual(text, string.Concat(parts));
            foreach (string part in parts)
                Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(part), GmLineServerSystem.PartBytes);
            Assert.AreEqual(new string('a', 499), parts[0], "the 3-byte dash does not fit after 499 bytes: next part");
        }
    }
}
