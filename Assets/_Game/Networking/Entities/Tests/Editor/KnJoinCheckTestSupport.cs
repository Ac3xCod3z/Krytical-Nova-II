using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;
using KryticalNova.Networking;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>An <see cref="IKnAccountProof"/> the test scripts: answers by number of polls so far for the connection.</summary>
    public sealed class ScriptedProof : IKnAccountProof
    {
        public Func<string, int, KnProof> Rule = (token, polls) => KnProof.Proven("a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1");
        public readonly List<string> Tokens = new List<string>();
        public int Forgotten;
        private readonly Dictionary<Entity, int> m_Polls = new Dictionary<Entity, int>();

        public KnProof Prove(Entity connection, string token)
        {
            m_Polls.TryGetValue(connection, out int polls);
            m_Polls[connection] = ++polls;
            if (polls == 1) Tokens.Add(token);
            return Rule(token, polls);
        }

        public void Forget(Entity connection) => Forgotten++;
    }

    public sealed class ScriptedTickets : IKnTravelTicketCheck
    {
        public Func<string, string, int, KnJoinVerdict> Rule = (account, ticket, polls) => KnJoinVerdict.Accept;
        public readonly List<string> Seen = new List<string>();
        private readonly Dictionary<Entity, int> m_Polls = new Dictionary<Entity, int>();

        public KnJoinVerdict Check(Entity connection, string account, string ticket)
        {
            m_Polls.TryGetValue(connection, out int polls);
            m_Polls[connection] = ++polls;
            if (polls == 1) Seen.Add(account + "/" + ticket);
            return Rule(account, ticket, polls);
        }

        public void Forget(Entity connection) { }
    }

    public sealed class FakeBans : IBanLookup
    {
        public readonly Dictionary<string, string> Banned = new Dictionary<string, string>();

        public bool TryGetBan(string account, DateTime utcNow, out string refusal) => Banned.TryGetValue(account ?? string.Empty, out refusal);
    }

    public static class KnJoinCheckTestSupport
    {
        public const string Build = "0.1.0 build 7";
        public const string AccountA = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";

        /// <summary>A key for a connection entity in a pure test (no world).</summary>
        public static Entity Conn(int n) => new Entity { Index = n, Version = 1 };

        public static string Payload(string build = Build, bool editor = false, string token = "tok", string oldAccount = "", string request = "", string ticket = "") =>
            JoinRules.PayloadText(build, editor, token, oldAccount, request, ticket);

        public static KnJoinCheck Check(IKnAccountProof proof, IKnTravelTicketCheck tickets = null, int online = 0, int max = 100,
            bool requireProof = true, bool devBots = false, string build = Build)
        {
            var options = new KnJoinCheckOptions { ServerBuild = build, MaxPlayers = max, RequireProof = requireProof, AllowDevBots = devBots };
            return new KnJoinCheck(options, proof, tickets, () => online);
        }

        /// <summary>Puts the real join check on the test server, with scripted proof and tickets.</summary>
        public static KnJoinCheck Install(KnTestWorld tw, IKnAccountProof proof, IKnTravelTicketCheck tickets = null, int max = 100,
            bool requireProof = true, bool devBots = false)
        {
            var assembler = tw.Server.GetExistingSystemManaged<KnApprovalAssembler>();
            var options = new KnJoinCheckOptions { ServerBuild = Build, MaxPlayers = max, RequireProof = requireProof, AllowDevBots = devBots };
            var check = new KnJoinCheck(options, proof, tickets, () => assembler.ApprovedCount);
            assembler.JoinCheck = check;
            return check;
        }

        /// <summary>Connects client <paramref name="index"/> and waits for its refusal and the close; checks the reason.</summary>
        public static void AssertRefused(KnTestWorld tw, World client, KnRefuseReason reason)
        {
            int used = tw.TickUntil(() => KnTestWorld.Count<KnJoinRefusal>(client) == 1 && KnTestWorld.Count<NetworkStreamConnection>(client) == 0, 500);
            Assert.GreaterOrEqual(used, 0, "no refusal arrived and closed the connection. Logs: " + string.Join(" | ", tw.Interesting));
            Assert.AreEqual((byte)reason, KnTestWorld.Singleton<KnJoinRefusal>(client).Reason, "wrong refusal reason");
            Assert.IsFalse(KnTestWorld.HasNetworkId(client), "a refused client must not get a NetworkId");
        }

        public static void AssertJoined(KnTestWorld tw, World client)
        {
            int used = tw.TickUntil(() => KnTestWorld.HasNetworkId(client), 500);
            Assert.GreaterOrEqual(used, 0, "client was not approved. Logs: " + string.Join(" | ", tw.Interesting));
        }
    }
}
