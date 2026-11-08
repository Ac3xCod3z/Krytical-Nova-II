#if KN_JOIN_HERE
using System;
using System.Collections;
using System.Globalization;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using KryticalNova.Player;
using KryticalNova.Player.Entities;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The second client of the Editor's "Play DOTS here" (only in the Windows test build: <c>KN_JOIN_HERE</c>, never in a release
    /// client or server). Started by <c>-knjoinhere</c>: loads the map, joins the entity server that Play DOTS here runs on loopback
    /// (plain UDP, 127.0.0.1:<see cref="DefaultPort"/>, or <c>-knjoinport &lt;port&gt;</c>; a real-server port is refused) and enters
    /// unsaved as "<see cref="DefaultName"/>" (or <c>-knjoinname &lt;name&gt;</c>) in the first lineage's default look, with no
    /// account and no character id. Then it is a normal player view: the mouse walks it. Safe by construction: saves go to memory
    /// only (<see cref="MemorySaveStore"/>, set before the scene loads), so nothing reaches the save service, the database or the
    /// disk, and Netcode for GameObjects stays off. One <c>[KnJoinHere]</c> log line on start, on entering and on a refusal.
    /// </summary>
    public sealed class KnJoinHere : MonoBehaviour
    {
        public const string Flag = "-knjoinhere";
        public const string Host = "127.0.0.1";
        /// <summary>
        /// <c>-knjoinhost &lt;IPv4&gt;</c>: join a dedicated entity test server instead of the Editor (DOTS builds, 8 Nov 2026). With
        /// <c>-knsignin</c> the client signs in to Unity (the PC's kept session, else anonymously) and sends the token, so a release
        /// server, which lets in only proven players, takes it. The real server's ports are refused either way.
        /// </summary>
        public const string HostFlag = "-knjoinhost";
        public const string SignInFlag = "-knsignin";
        /// <summary>The port Play DOTS here listens on (<c>KnPlayHere.Port</c>, the test lane's).</summary>
        public const ushort DefaultPort = 7797;
        public const string DefaultName = "Player2"; // letters and digits only: the server refuses a name with a space (PlayerIdentity)
        public const string Map = "Zone01_StarterTown";
        const float AdmitTimeoutSeconds = 40f;

        // Low memory (owner, 6 Nov 2026): this client runs beside the Editor's Play DOTS here on one 31 GB PC, and at full look it
        // took 5.7 GB. Lowest quality level, quarter-size textures, no shadows or post effects, a small window. Nothing is written to
        // PlayerPrefs: the build shares them with the real game client.
        public const string FullMemoryFlag = "-knfullmem";
        // No quality-level switch: the project's only other level ("Mobile") is excluded on Standalone, so a Windows player has "PC" alone
        // and SetQualityLevel(0) stayed on it (findings item 23); the savings are the texture size, shadows, post effects and window below.
        const int LowTextureMipLimit = 2;
        const int LowWidth = 960, LowHeight = 540;
        /// <summary>
        /// The frame cap of the low look (findings item 11, 6 Nov 2026: uncapped, this client burned ~4.4 cores beside the owner's Editor
        /// and the Editor fell to 27 fps). vSync off, so the cap is what holds it.
        /// </summary>
        public const int LowFrameRate = 30;

        /// <summary>Before the map loads, so its textures load small.</summary>
        static void LowMemoryEarly()
        {
            QualitySettings.globalTextureMipmapLimit = LowTextureMipLimit;
            QualitySettings.shadows = ShadowQuality.Disable;
        }

        /// <summary>
        /// After a scene loaded and after the game's graphics options (which set the window from the player's own choice): every
        /// light without shadows, every post-processing volume off (found by type name, so this assembly needs no URP reference),
        /// the small window.
        /// </summary>
        static void LowMemoryScene()
        {
            foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                light.shadows = LightShadows.None;
            int volumes = 0;
            foreach (Behaviour b in FindObjectsByType<Behaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b.GetType().FullName == "UnityEngine.Rendering.Volume" && b.enabled) { b.enabled = false; volumes++; }
            if (Screen.width != LowWidth || Screen.height != LowHeight || Screen.fullScreenMode != FullScreenMode.Windowed)
                Screen.SetResolution(LowWidth, LowHeight, FullScreenMode.Windowed);
            // after the game's graphics options (GraphicsSettingsApplier.ApplyDisplay sets vSync and the player's own cap)
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = LowFrameRate;
            Debug.Log($"[KnJoinHere] Low memory: texture mip limit {QualitySettings.globalTextureMipmapLimit}, shadows off, {volumes} post volumes off, " +
                      $"{LowWidth}x{LowHeight} window, {LowFrameRate} fps cap ({FullMemoryFlag} for the full look).");
        }

        static void LowMemoryOnSceneLoaded(Scene scene, LoadSceneMode mode) => LowMemoryScene();

        /// <summary>What the command line asked for (pure, so it is easy to test).</summary>
        public sealed class Options
        {
            public ushort Port = DefaultPort;
            public string Host = KnJoinHere.Host;
            public bool SignIn;
            public string Name = DefaultName;
            /// <summary>The low-memory look (<see cref="LowMemoryEarly"/>), on unless <c>-knfullmem</c> is on the line.</summary>
            public bool LowMemory = true;
            public string Error;

            /// <summary>Null when <c>-knjoinhere</c> is not on the line; else the options, with <see cref="Error"/> set when they cannot be used.</summary>
            public static Options Parse(string[] args)
            {
                if (args == null || Array.FindIndex(args, a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase)) < 0) return null;
                var o = new Options();
                if (Array.FindIndex(args, a => string.Equals(a, FullMemoryFlag, StringComparison.OrdinalIgnoreCase)) >= 0) o.LowMemory = false;
                if (Array.FindIndex(args, a => string.Equals(a, SignInFlag, StringComparison.OrdinalIgnoreCase)) >= 0) o.SignIn = true;
                for (int i = 0; i < args.Length - 1; i++)
                {
                    string value = args[i + 1];
                    switch (args[i].ToLowerInvariant())
                    {
                        case "-knjoinport":
                            if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out o.Port) || o.Port == 0) o.Error = "-knjoinport is not a port";
                            break;
                        case HostFlag:
                            if (System.Net.IPAddress.TryParse(value, out System.Net.IPAddress ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) o.Host = ip.ToString();
                            else o.Error = HostFlag + " is not an IPv4 address";
                            break;
                        case "-knjoinname":
                            if (!string.IsNullOrWhiteSpace(value)) o.Name = value.Trim();
                            break;
                    }
                }
                if (o.Error == null && IsRealPort(o.Port)) o.Error = "port " + o.Port + " belongs to the real server";
                return o;
            }

            // the real server's game, save service and page ports, named outright as well as through the settings' list
            static bool IsRealPort(ushort port) =>
                port == 7777 || port == 7778 || port == 7801 || port == 8090 || Array.IndexOf(KnServerSettings.RealLanePorts, (int)port) >= 0;
        }

        /// <summary>The character this client sends to go in game.</summary>
        sealed class JoinHereGoInGame : IKnGoInGameSource
        {
            readonly GoInGamePayload m_Payload;
            public JoinHereGoInGame(GoInGamePayload payload) => m_Payload = payload;
            public bool TryGet(World clientWorld, out GoInGamePayload payload) { payload = m_Payload; return true; }
        }

        static Options s_Options;
        Options m_Options;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeScene()
        {
            s_Options = Options.Parse(Environment.GetCommandLineArgs());
            if (s_Options == null) return;
            // before any scene object loads or saves: memory only, and Netcode for GameObjects stays off (no host on 7777, the offline
            // avatar hidden). Also for a refusal, so the build never hosts or saves on its way out.
            ServerSave.Store = new MemorySaveStore();
            EntityNetcodeSession.Owns = true;
            if (s_Options.Error == null && s_Options.LowMemory) LowMemoryEarly();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterScene()
        {
            Options options = s_Options;
            if (options == null) return;
            if (options.Error != null)
            {
                Debug.LogError("[KnJoinHere] Refused: " + options.Error + ". Nothing started.");
                Application.Quit(2);
                return;
            }
            var host = new GameObject("KnJoinHere");
            DontDestroyOnLoad(host);
            host.AddComponent<KnJoinHere>().m_Options = options;
        }

        IEnumerator Start()
        {
            Options o = m_Options;
            // the owner clicks between this window and the Editor: the connection must not stall while this one is not in front
            Application.runInBackground = true;
            Debug.Log($"[KnJoinHere] Joining {(o.Host == Host ? "Play DOTS here" : "the entity server")} at {o.Host}:{o.Port} as \"{o.Name}\" " +
                      $"(plain UDP, {(o.SignIn ? "signed in to Unity" : "no sign-in")}, saves to memory only on this side).");

            if (SceneManager.GetActiveScene().name != Map)
            {
                AsyncOperation load = SceneManager.LoadSceneAsync(Map);
                if (load == null) { Debug.LogError("[KnJoinHere] Refused: no map called " + Map + " in this build."); yield break; }
                while (!load.isDone) yield return null;
            }
            if (o.LowMemory)
            {
                yield return null; // the game's graphics options are in force by now; the low look goes over them
                LowMemoryScene();
                SceneManager.sceneLoaded += LowMemoryOnSceneLoaded;
            }

            if (!TryLook(out string recipeText, out string lookProblem)) { Debug.LogError("[KnJoinHere] Refused: " + lookProblem); yield break; }
            KnGoInGame.Source = new JoinHereGoInGame(new GoInGamePayload { CharacterId = string.Empty, Name = o.Name, RecipeText = recipeText });
            string token = string.Empty;
            if (o.SignIn)
            {
                var signIn = UnityAccount.JoinTokenAsync();
                while (!signIn.IsCompleted) yield return null;
                token = signIn.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? signIn.Result : null;
                if (string.IsNullOrEmpty(token)) { Debug.LogError("[KnJoinHere] Refused: could not sign in to Unity (internet? Unity services?)."); yield break; }
                Debug.Log("[KnJoinHere] Signed in to Unity as player " + UnityAccount.PlayerNow + ".");
            }
            string join = JoinRules.PayloadText(GameBuild.Version, false, token);
            if (!KnBootstrap.StartClient(o.Host, o.Port, join, encrypted: false)) { Debug.LogError("[KnJoinHere] Refused: could not start the client world."); yield break; }

            // let in = this client's own body appeared (the server built its ServerBody and ghost)
            float started = Time.realtimeSinceStartup;
            string lastState = null;
            while (LocalPlayer.Object == null)
            {
                string state = ConnectionState();
                if (state != lastState)
                {
                    Debug.Log($"[KnJoinHere] {(Time.realtimeSinceStartup - started).ToString("F1", CultureInfo.InvariantCulture)} s: {state}");
                    lastState = state;
                }
                // a refusal ends the wait at once, as NGO's join does (NetworkConnectionManager.JoinChosenServer: the disconnect ends
                // the wait, its reason is read, the client shuts down and the reason is shown; DOTS builds 8 Nov 2026)
                string refused = RefusalDetail();
                if (refused != null)
                {
                    Debug.LogError($"[KnJoinHere] Refused by the server after {(Time.realtimeSinceStartup - started).ToString("F1", CultureInfo.InvariantCulture)} s: {refused}");
                    KnBootstrap.Stop();   // the game's own session-ended panel shows the reason (NetSession.EndReason)
                    yield break;
                }
                if (Time.realtimeSinceStartup - started > AdmitTimeoutSeconds)
                {
                    Debug.LogError($"[KnJoinHere] Refused: not let in within {AdmitTimeoutSeconds} s{RefusalText()}. Is the server running?");
                    KnBootstrap.Stop();
                    m_Shown = $"No answer from {o.Host}:{o.Port} within {AdmitTimeoutSeconds} s.\nIs the server running?";
                    yield break;
                }
                yield return null;
            }
            Debug.Log($"[KnJoinHere] Joined after {(Time.realtimeSinceStartup - started).ToString("F1", CultureInfo.InvariantCulture)} s. Click the ground to walk.");

            while (LocalPlayer.Object != null) yield return null;
            Debug.LogWarning("[KnJoinHere] The server let this player go" + RefusalText() + " (Play DOTS here stopped?).");
        }

        // What the player is told when the server never answered (the game's session-ended panel covers a refusal or a send-off).
        string m_Shown;

        void OnGUI()
        {
            if (m_Shown == null) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 18, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            float w = Mathf.Min(620f, Screen.width - 40f), h = 150f;
            GUI.Box(new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h), m_Shown + "\n\nClose this window to quit.", style);
        }

        // Where this client's connection is, for the join log: no world, no connection, connecting (state), or let in with a NetworkId.
        static string ConnectionState()
        {
            World client = null;
            foreach (World w in World.All)
                if (w.IsCreated && w.Name == KnBootstrap.ClientWorldName) { client = w; break; }
            if (client == null) return "no client world";
            using var connections = client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Unity.NetCode.NetworkStreamConnection>());
            if (connections.IsEmpty) return "no connection";
            using var ids = client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Unity.NetCode.NetworkId>());
            if (!ids.IsEmpty) return "approved (NetworkId " + ids.GetSingleton<Unity.NetCode.NetworkId>().Value + "), waiting for the server to make the body";
            return "connected, waiting for approval";
        }

        // The server's own words when it turned this client away; null when none arrived.
        static string RefusalDetail()
        {
            string text = RefusalText();
            return text.Length == 0 ? null : text.Substring(" (server: ".Length, text.Length - " (server: ".Length - 1);
        }

        // The database's first lineage in its default look: valid by construction (the server checks it again).
        static bool TryLook(out string recipeText, out string problem)
        {
            recipeText = null;
            problem = null;
            PlayerWorldAssets assets = PlayerWorldAssets.Instance;
            PlayerIdentity identity = assets != null && assets.playerPrefab != null ? assets.playerPrefab.GetComponent<PlayerIdentity>() : null;
            CharacterCreationDatabase database = identity != null ? identity.Database : null;
            if (database == null || database.Lineages.Count == 0) { problem = "no character database on the player prefab."; return false; }
            CharacterRecipe recipe = RecipeEdit.Default(database.Lineages[0], default(BodyType));
            recipeText = RecipeText.Encode(recipe);
            return true;
        }

        // What the server said when it turned this client away, if anything arrived.
        static string RefusalText()
        {
            World client = null;
            foreach (World w in World.All)
                if (w.IsCreated && w.Name == KnBootstrap.ClientWorldName) { client = w; break; }
            if (client == null) return string.Empty;
            using var q = client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KnJoinRefusal>());
            if (q.IsEmpty) return string.Empty;
            using var refusals = q.ToComponentDataArray<KnJoinRefusal>(Allocator.Temp);
            return " (server: " + refusals[0].Detail + ")";
        }
    }
}
#endif
