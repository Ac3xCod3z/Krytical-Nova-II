#if KN_TEST_LANE && KN_DEV_BOTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using KryticalNova.Player;
using KryticalNova.Player.Entities;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The headless bot of the 2-client gate (plan section 43; only in the Linux test-lane client build: <c>KN_TEST_LANE</c> +
    /// <c>KN_DEV_BOTS</c>). Started by <c>-kntestclient &lt;n&gt;</c> with <c>-batchmode -nographics</c>: loads the map, joins the
    /// entity server with dev-bot token n (plain UDP), enters as character "Kntest&lt;n&gt;" with the database's first lineage in its
    /// default look (the test-lane server makes that character the first time), then clicks the ground points of <c>-walk</c> one after
    /// another through <see cref="BodyClicks"/> (the same path as a mouse click in view mode) and logs every click
    /// (<see cref="PlayerClickLog"/>). Exits 0 after <c>-seconds</c>, 3 when it was never let in (a refused second login), 4 when the
    /// server dropped it. Everything goes to <c>-out &lt;folder&gt;/kntestclient-&lt;label&gt;.log</c> (label = <c>-label</c>, default n) and a
    /// one-line result file; every 2 s it logs the other players it sees (<c>[KnSight]</c>).
    /// </summary>
    public sealed class KnTestClient : MonoBehaviour
    {
        public const int ExitNotAdmitted = 3;
        public const int ExitDropped = 4;

        /// <summary>What the command line asked for (pure, so it is easy to test).</summary>
        public sealed class Options
        {
            public int Number;
            public string Host = "127.0.0.1";
            public ushort Port = 7797;
            public string Map = "Zone01_StarterTown";
            public string OutFolder = ".";
            public float Seconds = 60f;
            public float StepSeconds = 4f;
            public float AdmitTimeoutSeconds = 40f;
            public readonly List<Vector3> Walk = new List<Vector3>();
            public string Error;

            /// <summary>Names this bot's log and result files (<c>-label</c>; default its number), so two bots on one account keep their own.</summary>
            public string Label;

            public string CharacterName => "Kntest" + Number.ToString(CultureInfo.InvariantCulture);
            public string FileStem => "kntestclient-" + (string.IsNullOrEmpty(Label) ? Number.ToString(CultureInfo.InvariantCulture) : Label);

            /// <summary>Null when <c>-kntestclient</c> is not on the line; else the options, with <see cref="Error"/> set when they cannot be used.</summary>
            public static Options Parse(string[] args)
            {
                if (args == null) return null;
                Options o = null;
                for (int i = 0; i < args.Length; i++)
                {
                    string flag = args[i].ToLowerInvariant();
                    string value = i + 1 < args.Length ? args[i + 1] : null;
                    switch (flag)
                    {
                        case "-kntestclient":
                            o = o ?? new Options();
                            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out o.Number) || o.Number < 1) o.Error = "-kntestclient needs a bot number from 1";
                            break;
                    }
                }
                if (o == null) return null;
                for (int i = 0; i < args.Length - 1; i++)
                {
                    string value = args[i + 1];
                    switch (args[i].ToLowerInvariant())
                    {
                        case "-connect": o.Host = value; break;
                        case "-port": if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out o.Port)) o.Error = "-port is not a port"; break;
                        case "-map": o.Map = value; break;
                        case "-label":
                            if (string.IsNullOrEmpty(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) o.Error = "-label must be a plain file name";
                            else o.Label = value;
                            break;
                        case "-out": o.OutFolder = value; break;
                        case "-seconds": o.Seconds = Float(value, o.Seconds); break;
                        case "-step": o.StepSeconds = Float(value, o.StepSeconds); break;
                        case "-walk": if (!TryWalk(value, o.Walk)) o.Error = "-walk wants x,z;x,z;... in metres"; break;
                    }
                }
                if (o.Error == null && Array.IndexOf(KnServerSettings.RealLanePorts, (int)o.Port) >= 0) o.Error = "port " + o.Port + " belongs to the real server";
                return o;
            }

            static float Float(string text, float fallback) =>
                float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v > 0f ? v : fallback;

            /// <summary>"x,z;x,z" (metres, ground plane; the height is found by the click ray on the map).</summary>
            public static bool TryWalk(string text, List<Vector3> into)
            {
                into.Clear();
                if (string.IsNullOrWhiteSpace(text)) return false;
                foreach (string pair in text.Split(';'))
                {
                    string[] xz = pair.Split(',');
                    if (xz.Length != 2
                        || !float.TryParse(xz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                        || !float.TryParse(xz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) { into.Clear(); return false; }
                    into.Add(new Vector3(x, 0f, z));
                }
                return into.Count > 0;
            }
        }

        /// <summary>The character this bot sends to go in game.</summary>
        sealed class BotGoInGame : IKnGoInGameSource
        {
            readonly GoInGamePayload m_Payload;
            public BotGoInGame(GoInGamePayload payload) => m_Payload = payload;
            public bool TryGet(World clientWorld, out GoInGamePayload payload) { payload = m_Payload; return true; }
        }

        Options m_Options;
        string m_Result = "running";
        int m_Exit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Options options = Options.Parse(Environment.GetCommandLineArgs());
            if (options == null) return;
            var host = new GameObject("KnTestClient");
            DontDestroyOnLoad(host);
            host.AddComponent<KnTestClient>().m_Options = options;
        }

        IEnumerator Start()
        {
            Options o = m_Options;
            Directory.CreateDirectory(o.OutFolder);
            KnTrace.Configure(o.OutFolder, o.FileStem + ".log");
            Application.logMessageReceivedThreaded += (text, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning) KnTrace.Write("[" + type + "] " + text);
            };
            if (o.Error != null) { Finish(2, "bad arguments: " + o.Error); yield break; }
            PlayerClickLog.Sink = KnTrace.Write;
            KnTrace.Write("bot " + o.Number + " (" + o.CharacterName + ") -> " + o.Host + ":" + o.Port + ", map " + o.Map + ", " + o.Walk.Count + " points");

            if (SceneManager.GetActiveScene().name != o.Map)
            {
                AsyncOperation load = SceneManager.LoadSceneAsync(o.Map);
                if (load == null) { Finish(2, "no map called " + o.Map + " in this build"); yield break; }
                while (!load.isDone) yield return null;
            }

            if (!TryLook(out string recipeText, out string lookProblem)) { Finish(2, lookProblem); yield break; }
            KnGoInGame.Source = new BotGoInGame(new GoInGamePayload { CharacterId = string.Empty, Name = o.CharacterName, RecipeText = recipeText });
            string join = JoinRules.PayloadText(GameBuild.Version, false, KnDevBots.TokenFor(o.Number));
            if (!KnBootstrap.StartClient(o.Host, o.Port, join, encrypted: false)) { Finish(2, "could not start the client world"); yield break; }

            // let in = this client's own body appeared (the server built its ServerBody and ghost)
            float started = Time.realtimeSinceStartup;
            while (LocalPlayer.Object == null)
            {
                if (Time.realtimeSinceStartup - started > o.AdmitTimeoutSeconds)
                {
                    Finish(ExitNotAdmitted, "not admitted within " + o.AdmitTimeoutSeconds + " s" + RefusalText());
                    yield break;
                }
                yield return null;
            }
            KnTrace.Write("admitted after " + (Time.realtimeSinceStartup - started).ToString("F1", CultureInfo.InvariantCulture) + " s");

            float until = Time.realtimeSinceStartup + o.Seconds;
            float nextSight = 0f;
            int step = 0;
            while (Time.realtimeSinceStartup < until)
            {
                if (LocalPlayer.Object == null) { Finish(ExitDropped, "the server dropped this bot" + RefusalText()); yield break; }
                if (Time.realtimeSinceStartup >= nextSight) { KnTrace.Write(SightLine()); nextSight = Time.realtimeSinceStartup + SightSeconds; }
                if (o.Walk.Count > 0)
                {
                    Vector3 p = o.Walk[step % o.Walk.Count];
                    p.y = GroundHeight(p);
                    BodyClicks.Ground(p, false);
                    KnTrace.Write("walk " + step + " -> " + p.x.ToString("F1", CultureInfo.InvariantCulture) + "," + p.z.ToString("F1", CultureInfo.InvariantCulture));
                    step++;
                }
                float stepEnd = Time.realtimeSinceStartup + o.StepSeconds;
                while (Time.realtimeSinceStartup < stepEnd)
                {
                    if (Time.realtimeSinceStartup >= nextSight) { KnTrace.Write(SightLine()); nextSight = Time.realtimeSinceStartup + SightSeconds; }
                    yield return null;
                }
            }
            Finish(0, "ok, " + step + " clicks");
        }

        // The database's first lineage in its default look: valid by construction (the server checks it again).
        static bool TryLook(out string recipeText, out string problem)
        {
            recipeText = null;
            problem = null;
            PlayerWorldAssets assets = PlayerWorldAssets.Instance;
            PlayerIdentity identity = assets != null && assets.playerPrefab != null ? assets.playerPrefab.GetComponent<PlayerIdentity>() : null;
            CharacterCreationDatabase database = identity != null ? identity.Database : null;
            if (database == null || database.Lineages.Count == 0) { problem = "no character database on the player prefab"; return false; }
            CharacterRecipe recipe = RecipeEdit.Default(database.Lineages[0], default(BodyType));
            recipeText = RecipeText.Encode(recipe);
            return true;
        }

        static float GroundHeight(Vector3 p) =>
            Physics.Raycast(new Vector3(p.x, 500f, p.z), Vector3.down, out RaycastHit hit, 1000f) ? hit.point.y : 0f;

        const float SightSeconds = 2f;

        /// <summary>
        /// "[KnSight] others=N owner=I look=V body=yes|no pos=x,z ..." for every other player's ghost in this client world: the proof by log
        /// that a bot sees the other players, their look version and a presentation body (B3; client A checks the same by eye later).
        /// </summary>
        static string SightLine()
        {
            World client = ClientWorld();
            if (client == null) return "[KnSight] no client world";
            EntityManager em = client.EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerGhostTag>(), ComponentType.ReadOnly<GhostOwner>(),
                                               ComponentType.ReadOnly<LocalTransform>());
            using var ghosts = q.ToEntityArray(Allocator.Temp);
            var line = new System.Text.StringBuilder();
            int others = 0;
            foreach (Entity e in ghosts)
            {
                if (em.HasComponent<GhostOwnerIsLocal>(e) && em.IsComponentEnabled<GhostOwnerIsLocal>(e)) continue;
                others++;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                uint look = em.HasComponent<PlayerLook>(e) ? em.GetComponentData<PlayerLook>(e).Version : 0u;
                line.Append(" owner=").Append(em.GetComponentData<GhostOwner>(e).NetworkId)
                    .Append(" look=").Append(look)
                    .Append(" body=").Append(em.HasComponent<KryticalNova.Player.Entities.Presentation.PlayerBodyLink>(e) ? "yes" : "no")
                    .Append(" pos=").Append(p.x.ToString("F1", CultureInfo.InvariantCulture)).Append(',').Append(p.z.ToString("F1", CultureInfo.InvariantCulture));
            }
            return "[KnSight] others=" + others + line;
        }

        static World ClientWorld()
        {
            foreach (World w in World.All)
                if (w.IsCreated && w.Name == KnBootstrap.ClientWorldName) return w;
            return null;
        }

        // What the server said when it turned this client away, if anything arrived.
        static string RefusalText()
        {
            World client = ClientWorld();
            if (client == null) return string.Empty;
            using var q = client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KnJoinRefusal>());
            if (q.IsEmpty) return string.Empty;
            using var refusals = q.ToComponentDataArray<KnJoinRefusal>(Allocator.Temp);
            return " (server: " + refusals[0].Detail + ")";
        }

        void Finish(int exit, string result)
        {
            m_Exit = exit;
            m_Result = result;
            KnTrace.Write("result " + exit + ": " + result);
            try { File.WriteAllText(Path.Combine(m_Options.OutFolder, m_Options.FileStem + ".result"), exit + " " + result + "\n"); }
            catch (Exception e) { KnTrace.Write("could not write the result file: " + e.Message); }
            KnBootstrap.Stop();
            Application.Quit(exit);
        }
    }
}
#endif
