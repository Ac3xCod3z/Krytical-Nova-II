using System.IO;
using KryticalNova.Core;
using NUnit.Framework;

namespace KryticalNova.Networking.Entities.Probe.Tests
{
    /// <summary>The pure parts of the M2c probe: arguments, exit codes, the result file and the scripted client. No world, no socket.</summary>
    public class KnProbeTests
    {
        // ------------------------------------------------------------------ arguments

        [Test]
        public void NoFlag_MeansNothingRuns()
        {
            KnProbeArgs a = KnProbeArgs.Parse(new[] { "game.exe", "-batchmode", "-port", "7797" });
            Assert.That(a.Requested, Is.False);
            Assert.That(a.Enabled, Is.False);
            Assert.That(a.Error, Is.Null);
            Assert.That(KnProbeArgs.Parse(null).Enabled, Is.False);
        }

        [Test]
        public void Server_FlagIsRead()
        {
            KnProbeArgs a = KnProbeArgs.Parse(new[] { "x.exe", "-knprobe", "SERVER", "-port", "7797" });
            Assert.That(a.Role, Is.EqualTo(KnProbeRole.Server));
            Assert.That(a.Requested, Is.True);
            Assert.That(a.Error, Is.Null);
        }

        [Test]
        public void Client_AllFlagsAreRead()
        {
            KnProbeArgs a = KnProbeArgs.Parse(new[]
            {
                "x.exe", "-knprobe", "client", "-knaddr", "127.0.0.1", "-knport", "7797", "-knpageport", "7798", "-knaction", "roster",
                "-knquit-after", "2.5", "-kntimeout", "30", "-knname", "A", "-knout", "C:\\runs\\one",
            });
            Assert.That(a.Error, Is.Null);
            Assert.That(a.Role, Is.EqualTo(KnProbeRole.Client));
            Assert.That(a.Address, Is.EqualTo("127.0.0.1"));
            Assert.That(a.Port, Is.EqualTo(7797));
            Assert.That(a.PagePort, Is.EqualTo(7798));
            Assert.That(a.Action, Is.EqualTo(KnProbeAction.Roster));
            Assert.That(a.QuitAfterSeconds, Is.EqualTo(2.5));
            Assert.That(a.TimeoutSeconds, Is.EqualTo(30));
            Assert.That(a.Name, Is.EqualTo("A"));
            Assert.That(a.OutDir, Is.EqualTo("C:\\runs\\one"));
        }

        [Test]
        public void Client_DefaultsAreTheProbePorts()
        {
            KnProbeArgs a = KnProbeArgs.Parse(new[] { "x.exe", "-knprobe", "client" });
            Assert.That(a.Error, Is.Null);
            Assert.That(a.Port, Is.EqualTo(7797));
            Assert.That(a.PagePort, Is.EqualTo(7798));
            Assert.That(a.Action, Is.EqualTo(KnProbeAction.Connect));
            Assert.That(a.QuitAfterSeconds, Is.EqualTo(0));
        }

        [TestCase("7777")]
        [TestCase("7778")]
        [TestCase("8090")]
        [TestCase("7801")]
        [TestCase("7802")]
        [TestCase("0")]
        public void Client_NeverAcceptsAPortThatIsNotAProbePort(string port)
        {
            KnProbeArgs a = KnProbeArgs.Parse(new[] { "x.exe", "-knprobe", "client", "-knport", port });
            Assert.That(a.Error, Does.Contain("not a probe port"));
            KnProbeArgs page = KnProbeArgs.Parse(new[] { "x.exe", "-knprobe", "client", "-knpageport", port });
            if (port != "0") Assert.That(page.Error, Does.Contain("not a probe port"));
        }

        [Test]
        public void ServerPorts_OnlyTheProbeListPasses()
        {
            Assert.That(KnProbeArgs.ServerPortProblem(7797, 7798), Is.Null);
            Assert.That(KnProbeArgs.ServerPortProblem(7797, 8097), Is.Null);
            Assert.That(KnProbeArgs.ServerPortProblem(7797, 0), Is.Null);
            Assert.That(KnProbeArgs.ServerPortProblem(7777, 7798), Is.Not.Null);
            Assert.That(KnProbeArgs.ServerPortProblem(7797, 8090), Is.Not.Null);
            Assert.That(KnProbeArgs.ServerPortProblem(7801, 7798), Is.Not.Null);
            Assert.That(KnProbeArgs.ServerPortProblem(7797, 7778), Is.Not.Null);
        }

        [TestCase("-knprobe", "both")]
        [TestCase("-knaction", "dance")]
        [TestCase("-knport", "seven")]
        [TestCase("-kntimeout", "-3")]
        public void BadValues_AreErrorsNotGuesses(string flag, string value)
        {
            KnProbeArgs a = KnProbeArgs.Parse(new[] { "x.exe", "-knprobe", "client", flag, value });
            if (flag == "-knprobe") Assert.That(a.Requested, Is.True);
            Assert.That(a.Error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void FlagWithoutValue_IsAnError()
        {
            Assert.That(KnProbeArgs.Parse(new[] { "x.exe", "-knprobe" }).Error, Is.Not.Null);
            Assert.That(KnProbeArgs.Parse(new[] { "x.exe", "-knprobe", "client", "-knaddr" }).Error, Is.Not.Null);
        }

        [Test]
        public void Account_IsAGameSaveIdAndStablePerName()
        {
            string a = KnProbeArgs.AccountFor("A");
            Assert.That(SaveKeys.IsId(a), Is.True, a);
            Assert.That(KnProbeArgs.AccountFor("A"), Is.EqualTo(a));
            Assert.That(KnProbeArgs.AccountFor("B"), Is.Not.EqualTo(a));
            Assert.That(SaveKeys.IsId(KnProbeArgs.AccountFor(null)), Is.True);
        }

        // ------------------------------------------------------------------ exit codes and the result file

        [TestCase(KnProbeExitReason.QuitAfter, 0)]
        [TestCase(KnProbeExitReason.ServerClosed, 0)]
        [TestCase(KnProbeExitReason.Stopped, 0)]
        [TestCase(KnProbeExitReason.Refused, 2)]
        [TestCase(KnProbeExitReason.Timeout, 3)]
        [TestCase(KnProbeExitReason.Kicked, 4)]
        [TestCase(KnProbeExitReason.Error, 1)]
        [TestCase(KnProbeExitReason.None, 1)]
        public void ExitCodes_AreTheContract(KnProbeExitReason reason, int code)
        {
            Assert.That(KnProbeExit.Code(reason), Is.EqualTo(code));
        }

        [Test]
        public void ExitNames_AreDistinct()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (KnProbeExitReason r in System.Enum.GetValues(typeof(KnProbeExitReason)))
                Assert.That(seen.Add(KnProbeExit.Name(r)), Is.True, r.ToString());
        }

        [Test]
        public void ResultFile_NamesAndSanitising()
        {
            Assert.That(KnProbeFiles.FileName(KnProbeRole.Server, ""), Is.EqualTo("kn-probe-server.json"));
            Assert.That(KnProbeFiles.FileName(KnProbeRole.Client, "A"), Is.EqualTo("kn-probe-client-A.json"));
            Assert.That(KnProbeFiles.FileName(KnProbeRole.Client, null), Is.EqualTo("kn-probe-client.json"));
            Assert.That(KnProbeFiles.FileName(KnProbeRole.Client, "..\\..\\x"), Is.EqualTo("kn-probe-client-______x.json"));
        }

        [Test]
        public void ResultFile_RoundTripsThroughJson()
        {
            var r = new KnProbeResult { role = "client", name = "A", connected = true, networkId = 1, rosterAsked = true, rosterOk = true, rosterAnswer = "{\"probe\":1}" };
            r.AddPlayer(18446744073709551615UL, 3);
            r.exitReason = "kicked";
            r.exitCode = 4;
            r.droppedUtcMs = 1730200000123L;
            KnProbeResult back = KnProbeResult.FromJson(r.ToJson());
            Assert.That(back.role, Is.EqualTo("client"));
            Assert.That(back.networkId, Is.EqualTo(1));
            Assert.That(back.rosterAnswer, Is.EqualTo("{\"probe\":1}"));
            Assert.That(back.playerKeys, Is.EqualTo(new[] { "18446744073709551615" }), "a PlayerKey is a 64-bit number and travels as text");
            Assert.That(back.playerNetworkIds, Is.EqualTo(new[] { 3 }));
            Assert.That(back.joins, Is.EqualTo(1));
            Assert.That(back.droppedUtcMs, Is.EqualTo(1730200000123L));
            Assert.That(back.exitCode, Is.EqualTo(4));
        }

        [Test]
        public void ResultFile_IsWrittenWholeAndOverwritten()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kn-probe-test-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var r = new KnProbeResult { role = "server", exitReason = "running" };
                Assert.That(KnProbeFiles.Write(dir, KnProbeRole.Server, "", r), Is.True);
                r.exitReason = "stopped";
                r.exitCode = 0;
                Assert.That(KnProbeFiles.Write(dir, KnProbeRole.Server, "", r), Is.True);
                string path = Path.Combine(dir, "kn-probe-server.json");
                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.Exists(path + ".tmp"), Is.False, "no half-written leftover");
                Assert.That(KnProbeResult.FromJson(File.ReadAllText(path)).exitReason, Is.EqualTo("stopped"));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        // ------------------------------------------------------------------ the scripted client

        private static KnProbeClientMachine Machine(KnProbeAction action, out KnProbeResult result, double quitAfter = 0, double timeout = 20)
        {
            var args = new KnProbeArgs { Role = KnProbeRole.Client, Action = action, QuitAfterSeconds = quitAfter, TimeoutSeconds = timeout, Name = "A" };
            result = new KnProbeResult { role = "client", name = "A" };
            long clock = 1000;
            return new KnProbeClientMachine(args, result, () => clock += 10);
        }

        [Test]
        public void Connect_Kicked_ExitsFour()
        {
            var m = Machine(KnProbeAction.Idle, out KnProbeResult r);
            m.Begin(0);
            Assert.That(m.WantsConnect, Is.True);
            m.OnConnected(2, 1);
            Assert.That(m.Phase, Is.EqualTo(KnProbePhase.Connected));
            m.OnDropped(KnDropCause.Kicked);
            Assert.That(m.IsDone, Is.True);
            Assert.That(m.ExitCode, Is.EqualTo(4));
            Assert.That(r.kicked, Is.True);
            Assert.That(r.connected, Is.True);
            Assert.That(r.networkId, Is.EqualTo(2));
            Assert.That(r.exitReason, Is.EqualTo("kicked"));
            Assert.That(r.droppedUtcMs, Is.GreaterThan(r.connectedUtcMs));
        }

        [Test]
        public void Connect_ServerStops_ExitsZeroAndIsNotAKick()
        {
            var m = Machine(KnProbeAction.Connect, out KnProbeResult r);
            m.Begin(0);
            m.OnConnected(1, 1);
            m.OnDropped(KnDropCause.ServerClosed);
            Assert.That(m.ExitCode, Is.EqualTo(0));
            Assert.That(r.kicked, Is.False);
            Assert.That(r.exitReason, Is.EqualTo("server-closed"));
        }

        [Test]
        public void Refused_ExitsTwoAndKeepsTheWords()
        {
            var m = Machine(KnProbeAction.Connect, out KnProbeResult r);
            m.Begin(0);
            m.OnRefused("WrongVersion", "different build");
            Assert.That(m.ExitCode, Is.EqualTo(2));
            Assert.That(r.refusalReason, Is.EqualTo("WrongVersion"));
            Assert.That(r.refusalDetail, Is.EqualTo("different build"));
            Assert.That(r.connected, Is.False);
        }

        [Test]
        public void NoNetworkId_InTime_ExitsThree()
        {
            var m = Machine(KnProbeAction.Connect, out KnProbeResult r, timeout: 5);
            m.Begin(10);
            m.Tick(14.9);
            Assert.That(m.IsDone, Is.False);
            m.Tick(15.1);
            Assert.That(m.IsDone, Is.True);
            Assert.That(m.ExitCode, Is.EqualTo(3));
            Assert.That(r.exitReason, Is.EqualTo("timeout"));
        }

        [Test]
        public void Roster_Answered_ThenConnects()
        {
            var m = Machine(KnProbeAction.Roster, out KnProbeResult r);
            m.Begin(0);
            Assert.That(m.Phase, Is.EqualTo(KnProbePhase.Roster));
            Assert.That(m.WantsConnect, Is.False);
            Assert.That(r.rosterAsked, Is.True);
            m.OnRosterDone(true, "{\"probe\":\"roster-ok\"}", null, "None", 1);
            Assert.That(r.rosterOk, Is.True);
            Assert.That(r.rosterAnswer, Does.Contain("roster-ok"));
            Assert.That(m.WantsConnect, Is.True);
            m.OnConnected(1, 2);
            Assert.That(m.Phase, Is.EqualTo(KnProbePhase.Connected));
        }

        [Test]
        public void Roster_Refused_ExitsTwo_NoAnswer_ExitsThree()
        {
            var refused = Machine(KnProbeAction.Roster, out KnProbeResult rr);
            refused.Begin(0);
            refused.OnRosterDone(false, null, "Banned words", "Banned", 1);
            Assert.That(refused.ExitCode, Is.EqualTo(2));
            Assert.That(rr.refusalReason, Is.EqualTo("Banned"));

            var silent = Machine(KnProbeAction.Roster, out KnProbeResult rs);
            silent.Begin(0);
            silent.OnRosterDone(false, null, "The server did not answer.", "None", 1);
            Assert.That(silent.ExitCode, Is.EqualTo(3));
            Assert.That(rs.rosterError, Does.Contain("did not answer"));
        }

        [Test]
        public void QuitAfter_EndsAConnectedClientOk()
        {
            var m = Machine(KnProbeAction.Connect, out KnProbeResult r, quitAfter: 3);
            m.Begin(0);
            m.OnConnected(1, 10);
            m.Tick(12.9);
            Assert.That(m.IsDone, Is.False);
            m.Tick(13.0);
            Assert.That(m.IsDone, Is.True);
            Assert.That(m.ExitCode, Is.EqualTo(0));
            Assert.That(r.exitReason, Is.EqualTo("quit-after"));
        }

        [Test]
        public void ConnectedWithoutQuitAfter_StaysUntilTheServerActs()
        {
            var m = Machine(KnProbeAction.Connect, out _);
            m.Begin(0);
            m.OnConnected(1, 1);
            m.Tick(100000);
            Assert.That(m.IsDone, Is.False);
        }

        [Test]
        public void TheFirstEndingWins()
        {
            var m = Machine(KnProbeAction.Connect, out KnProbeResult r);
            m.Begin(0);
            m.OnRefused("ServerFull", "full");
            m.OnConnected(1, 1);
            m.OnDropped(KnDropCause.Kicked);
            m.OnError("late");
            m.Tick(1e9);
            Assert.That(m.ExitCode, Is.EqualTo(2));
            Assert.That(r.exitReason, Is.EqualTo("refused"));
            Assert.That(r.kicked, Is.False);
        }

        [Test]
        public void Error_ExitsOneWithTheText()
        {
            var m = Machine(KnProbeAction.Connect, out KnProbeResult r);
            m.Begin(0);
            m.OnError("not a network address");
            Assert.That(m.ExitCode, Is.EqualTo(1));
            Assert.That(r.error, Does.Contain("network address"));
        }

        [Test]
        public void Drop_BeforeConnecting_IsIgnored()
        {
            var m = Machine(KnProbeAction.Connect, out _);
            m.Begin(0);
            m.OnDropped(KnDropCause.Kicked);
            Assert.That(m.IsDone, Is.False);
        }

        // ------------------------------------------------------------------ kick or stop?

        [TestCase(true, 200, KnDropCause.Kicked)]
        [TestCase(true, 503, KnDropCause.ServerClosed)]
        [TestCase(false, 0, KnDropCause.ServerClosed)]
        [TestCase(true, 404, KnDropCause.ServerClosed)]
        public void ClassifyDrop_OnlyAHealthyServerMeansKicked(bool answered, int status, KnDropCause expected)
        {
            Assert.That(KnProbeLogic.ClassifyDrop(answered, status), Is.EqualTo(expected));
        }
    }
}
