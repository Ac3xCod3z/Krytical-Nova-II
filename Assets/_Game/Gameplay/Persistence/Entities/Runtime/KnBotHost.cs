#if (KN_TEST_LANE && KN_DEV_BOTS) || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The soak's bot host (wave 4, 12 Nov 2026): ONE headless process that holds many development-bot thin clients, so 50 bots cost one
    /// process, not 50. Started with <c>-knbots &lt;count&gt;</c> (and <c>-batchmode -nographics</c>) in the Windows test-lane bot-host build
    /// (<c>KN_TEST_LANE</c> + <c>KN_DEV_BOTS</c>; <c>KnBotHostBuild</c>). Each bot is a thin-client world made by
    /// <see cref="KnDevBots.CreateThinClient"/> (token <c>kn-devbot:&lt;n&gt;</c>, character "Knbot&lt;n&gt;", made at the maximum level with every
    /// skill by the test-lane server). Thin clients have no game logic and read no ghosts: the SERVER plays them (the soak script equips
    /// each over the test page and Autopilot fights). This host only keeps the connections alive and logs.
    /// Ramp: <c>-knbotstep</c> bots every <c>-knbotevery</c> seconds up to <c>-knbots</c>. Real ports are refused.
    /// Output: <c>&lt;out&gt;/botshost.log</c> and <c>botshost.result</c> (one line: exit, wanted, joined, dropped, seconds).
    /// Exits 0 ok, 2 bad arguments, 3 a bot not in within <see cref="Options.AdmitTimeoutSeconds"/>, 4 more than 10 % dropped.
    /// The Editor compiles the pure <see cref="Options"/> (tests); the host itself boots only in the test-lane build.
    /// </summary>
    public sealed class KnBotHost : MonoBehaviour
    {
        public const int ExitOk = 0, ExitArgs = 2, ExitNotAdmitted = 3, ExitDropped = 4;
        public const float DroppedShareLimit = 0.10f;
        public const string LogName = "botshost.log", ResultName = "botshost.result";

        /// <summary>What the command line asked for (pure, so it is easy to test).</summary>
        public sealed class Options
        {
            public int Count;
            public int First = 1;
            public string Host = "127.0.0.1";
            public ushort Port = 7799;
            public string Map = "Zone01_StarterTown";
            public string OutFolder = ".";
            public float Seconds = 300f;
            public int Step;          // 0 = all at once
            public float EverySeconds = 120f;
            public float AdmitTimeoutSeconds = 40f;
            public string Error;

            /// <summary>The bot numbers this host owns: <see cref="First"/> .. First + Count - 1.</summary>
            public int Last => First + Count - 1;

            public static string CharacterName(int number) => "Knbot" + number.ToString(CultureInfo.InvariantCulture);

            /// <summary>How many bots should be running <paramref name="secondsIn"/> seconds after the start.</summary>
            public int WantedAt(float secondsIn)
            {
                if (Step <= 0 || EverySeconds <= 0f) return Count;
                int steps = 1 + (int)Math.Floor(Math.Max(0f, secondsIn) / EverySeconds);
                return Math.Min(Count, steps * Step);
            }

            /// <summary>Null when <c>-knbots</c> is not on the line; else the options, with <see cref="Error"/> set when they cannot be used.</summary>
            public static Options Parse(string[] args)
            {
                if (args == null) return null;
                int at = -1;
                for (int i = 0; i < args.Length; i++)
                    if (string.Equals(args[i], "-knbots", StringComparison.OrdinalIgnoreCase)) at = i;
                if (at < 0) return null;
                var o = new Options();
                if (!Int(at + 1 < args.Length ? args[at + 1] : null, out o.Count) || o.Count < 1) o.Error = "-knbots needs a bot count from 1";
                for (int i = 0; i < args.Length - 1; i++)
                {
                    string value = args[i + 1];
                    switch (args[i].ToLowerInvariant())
                    {
                        case "-knbotfirst": if (!Int(value, out o.First) || o.First < 1) o.Error = "-knbotfirst needs a bot number from 1"; break;
                        case "-knbotstep": if (!Int(value, out o.Step) || o.Step < 1) o.Error = "-knbotstep needs a count from 1"; break;
                        case "-knbotevery": if (!Pos(value, out o.EverySeconds)) o.Error = "-knbotevery needs seconds above 0"; break;
                        case "-seconds": if (!Pos(value, out o.Seconds)) o.Error = "-seconds needs seconds above 0"; break;
                        case "-connect": o.Host = value; break;
                        case "-port": if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out o.Port)) o.Error = "-port is not a port"; break;
                        case "-map": o.Map = value; break;
                        case "-out": o.OutFolder = value; break;
                    }
                }
                if (o.Error == null && (long)o.First + o.Count - 1 > 999999999) o.Error = "bot numbers must stay under 10 digits";
                if (o.Error == null && Array.IndexOf(KnServerSettings.RealLanePorts, (int)o.Port) >= 0) o.Error = "port " + o.Port + " belongs to the real server";
                return o;
            }

            static bool Int(string text, out int value) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

            static bool Pos(string text, out float value) =>
                float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0f;
        }

        /// <summary>The verdict of a run, from its counts (pure: the result file and the exit code).</summary>
        public static int ExitFor(int wanted, int joined, int dropped, bool admitTimedOut)
        {
            if (admitTimedOut) return ExitNotAdmitted;
            if (wanted > 0 && dropped > wanted * DroppedShareLimit) return ExitDropped;
            return joined < wanted ? ExitNotAdmitted : ExitOk;
        }

#if !UNITY_EDITOR
        /// <summary>One bot's character, sent when its thin client goes in game.</summary>
        sealed class BotGoInGame : IKnGoInGameSource
        {
            readonly GoInGamePayload m_Payload;
            public BotGoInGame(GoInGamePayload payload) => m_Payload = payload;
            public bool TryGet(World clientWorld, out GoInGamePayload payload) { payload = m_Payload; return true; }
        }

        sealed class Bot
        {
            public int Number;
            public World World;
            public float Started;
            public bool Joined, Dropped;
        }

        Options m_Options;
        readonly List<Bot> m_Bots = new List<Bot>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Options options = Options.Parse(Environment.GetCommandLineArgs());
            if (options == null) return;
            var host = new GameObject("KnBotHost");
            DontDestroyOnLoad(host);
            host.AddComponent<KnBotHost>().m_Options = options;
        }

        IEnumerator Start()
        {
            Options o = m_Options;
            Directory.CreateDirectory(o.OutFolder);
            KnTrace.Configure(o.OutFolder, LogName);
            Application.logMessageReceivedThreaded += (text, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning) KnTrace.Write("[" + type + "] " + text);
            };
            if (o.Error != null) { Finish(ExitArgs, 0, 0, 0, 0f, "bad arguments: " + o.Error); yield break; }
            KnTrace.Write($"bot host: bots {o.First}..{o.Last} -> {o.Host}:{o.Port}, map {o.Map}, {o.Seconds:F0} s, ramp {(o.Step > 0 ? o.Step + " every " + o.EverySeconds + " s" : "all at once")}");

            if (SceneManager.GetActiveScene().name != o.Map)
            {
                AsyncOperation load = SceneManager.LoadSceneAsync(o.Map);
                if (load == null) { Finish(ExitArgs, 0, 0, 0, 0f, "no map called " + o.Map + " in this build"); yield break; }
                while (!load.isDone) yield return null;
            }
            if (!KnTestClient.TryLook(out string look, out string lookProblem)) { Finish(ExitArgs, 0, 0, 0, 0f, lookProblem); yield break; }
            if (!NetworkEndpoint.TryParse(o.Host, o.Port, out NetworkEndpoint endpoint)) { Finish(ExitArgs, 0, 0, 0, 0f, "-connect is not an IPv4 address"); yield break; }
            KnDevBots.Enabled = true;

            float start = Time.realtimeSinceStartup;
            float nextStatus = 0f;
            bool timedOut = false;
            while (Time.realtimeSinceStartup - start < o.Seconds)
            {
                float now = Time.realtimeSinceStartup;
                int wanted = o.WantedAt(now - start);
                while (m_Bots.Count < wanted)
                {
                    Bot bot = StartBot(o.First + m_Bots.Count, endpoint, look);
                    if (bot == null) { Finish(ExitArgs, wanted, Joined(), Dropped(), now - start, "could not make bot " + (o.First + m_Bots.Count)); yield break; }
                    m_Bots.Add(bot);
                }
                foreach (Bot b in m_Bots)
                {
                    bool connected = InGame(b.World);
                    if (connected && !b.Joined) { b.Joined = true; KnTrace.Write($"bot {b.Number} in after {now - b.Started:F1} s"); }
                    else if (!connected && b.Joined && !b.Dropped) { b.Dropped = true; KnTrace.Write($"bot {b.Number} DROPPED"); }
                    else if (!b.Joined && now - b.Started > o.AdmitTimeoutSeconds) timedOut = true;
                }
                if (timedOut) { Finish(ExitNotAdmitted, wanted, Joined(), Dropped(), now - start, "a bot was not in within " + o.AdmitTimeoutSeconds + " s"); yield break; }
                if (now >= nextStatus)
                {
                    KnTrace.Write($"[KnBots] wanted={wanted} made={m_Bots.Count} joined={Joined()} dropped={Dropped()} t={now - start:F0}");
                    nextStatus = now + 10f;
                }
                yield return null;
            }
            int exit = ExitFor(o.Count, Joined(), Dropped(), false);
            Finish(exit, o.Count, Joined(), Dropped(), Time.realtimeSinceStartup - start, exit == ExitOk ? "ok" : "see the log");
        }

        Bot StartBot(int number, NetworkEndpoint endpoint, string look)
        {
            World world = KnDevBots.CreateThinClient(endpoint, number, GameBuild.Version);
            if (world == null) return null;
            if (!PlayerWorldAssets.LoadGhostScene(world)) { KnTrace.Write($"bot {number}: no ghost subscene"); world.Dispose(); return null; }
            KnClientGoInGameSystem goIn = world.GetExistingSystemManaged<KnClientGoInGameSystem>();
            if (goIn == null) { KnTrace.Write($"bot {number}: the thin client has no go-in-game system"); world.Dispose(); return null; }
            goIn.Source = new BotGoInGame(new GoInGamePayload { CharacterId = string.Empty, Name = Options.CharacterName(number), RecipeText = look });
            KnTrace.Write($"bot {number} ({Options.CharacterName(number)}) joining");
            return new Bot { Number = number, World = world, Started = Time.realtimeSinceStartup };
        }

        // In = the world holds a connection with a network id that went in game, and the server sent no refusal (the client's view;
        // the soak script checks the server's player count on the test page as well).
        static bool InGame(World world)
        {
            if (world == null || !world.IsCreated) return false;
            EntityManager em = world.EntityManager;
            using (var refused = em.CreateEntityQuery(ComponentType.ReadOnly<KnJoinRefusal>()))
                if (!refused.IsEmpty) return false;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            return !q.IsEmpty;
        }

        int Joined() { int n = 0; foreach (Bot b in m_Bots) if (b.Joined) n++; return n; }
        int Dropped() { int n = 0; foreach (Bot b in m_Bots) if (b.Dropped) n++; return n; }

        void Finish(int exit, int wanted, int joined, int dropped, float seconds, string why)
        {
            string line = $"{exit} wanted={wanted} joined={joined} dropped={dropped} seconds={seconds.ToString("F0", CultureInfo.InvariantCulture)} {why}";
            KnTrace.Write("result " + line);
            try { File.WriteAllText(Path.Combine(m_Options.OutFolder, ResultName), line + "\n"); }
            catch (Exception e) { KnTrace.Write("could not write the result file: " + e.Message); }
            KnBootstrap.Stop();
            Application.Quit(exit);
        }
#endif
    }
}
#endif
