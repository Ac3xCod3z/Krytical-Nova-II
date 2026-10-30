#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using KryticalNova.Player;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using World = Unity.Entities.World;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// "Play DOTS here" (M3a owner feel checkpoint, plan section 50): Play in this Editor runs an entity-netcode server world and one
    /// client world in <see cref="MapPath"/>, and the owner walks and runs with real clicks. Safe by construction:
    /// - saves go to memory only for the whole session (<see cref="MemorySaveStore"/>, set before the scene loads): nothing reaches the
    ///   save database, the save service or the local profile on disk;
    /// - the server listens on loopback only, on the test lane's <see cref="Port"/>; a real-server port is refused;
    /// - the player enters unsaved, as "<see cref="TesterName"/>" in the first lineage's default look (no account, no character id), with
    ///   Run and Jump at their top ranks so running can be felt.
    /// The menu opens the map itself and never shows a dialog: with unsaved changes in an open scene it stops with an error instead.
    /// Scripted run: with <c>KN_PLAYHERE_WALK="x,z;x,z"</c> set, the same ground clicks are made (each point walking, then each running),
    /// every arrival, position and the Flux are logged as <c>[KnPlayHere]</c> lines, and Play stops; <see cref="Batch"/> does it in a
    /// batch Editor and quits with 0 (all arrived, no error) or 1. Editor only; nothing of it is in a build.
    /// </summary>
    public static class KnPlayHere
    {
        public const ushort Port = 7797;
        public const string TesterName = "Tester";
        public const string MenuPath = "Krytical Nova/Online/Entities/Play DOTS here";
        public const string MapPath = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity";
        public const string WalkVariable = "KN_PLAYHERE_WALK";
        /// <summary>A walk that has not arrived after this long counts as failed.</summary>
        public const float ArriveTimeoutSeconds = 25f;
        /// <summary>A batch run that is still going after this long quits with 1.</summary>
        public const double BatchTimeoutSeconds = 300;

        // Set by the menu or Batch, read on entering Play. Not reset at SubsystemRegistration on purpose: they must outlive the Play start.
        static bool s_Requested, s_QuitAfter;
        static int s_ExitCode = 1;
        static double s_BatchStarted;

        /// <summary>True while this Play session is a "Play DOTS here" one.</summary>
        public static bool Active { get; private set; }

        [MenuItem(MenuPath)]
        static void PlayHere() => Begin(quitAfter: false);

        [MenuItem(MenuPath, true)]
        static bool PlayHereValid() => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>
        /// Batch: <c>Unity -batchmode -projectPath ... -executeMethod KryticalNova.Persistence.Entities.KnPlayHere.Batch</c> with
        /// <see cref="WalkVariable"/> set (no <c>-quit</c>: the run quits by itself).
        /// </summary>
        public static void Batch()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(WalkVariable)))
            {
                Debug.LogError("[KnPlayHere] " + WalkVariable + " is not set: nothing to walk.");
                EditorApplication.Exit(2);
                return;
            }
            s_BatchStarted = EditorApplication.timeSinceStartup;
            EditorApplication.update += BatchWatchdog;
            if (!Begin(quitAfter: true)) EditorApplication.Exit(2);
        }

        static bool Begin(bool quitAfter)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[KnPlayHere] Stop Play first.");
                return false;
            }
            if (SceneManager.GetActiveScene().path != MapPath)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty)
                    {
                        Debug.LogError("[KnPlayHere] Save or discard the changes in " + SceneManager.GetSceneAt(i).name + " first (no dialog is shown).");
                        return false;
                    }
                EditorSceneManager.OpenScene(MapPath, OpenSceneMode.Single);
            }
            s_Requested = true;
            s_QuitAfter = quitAfter;
            s_ExitCode = 1;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.isPlaying = true;
            return true;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Active = false;
            if (s_QuitAfter) EditorApplication.Exit(s_ExitCode);
        }

        static void BatchWatchdog()
        {
            if (EditorApplication.timeSinceStartup - s_BatchStarted < BatchTimeoutSeconds) return;
            EditorApplication.update -= BatchWatchdog;
            Debug.LogError("[KnPlayHere] the scripted run took longer than " + BatchTimeoutSeconds + " s: stopped.");
            EditorApplication.Exit(1);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeScene()
        {
            Active = s_Requested;
            s_Requested = false;
            if (!Active) return;
            // before any scene object loads or saves (ServerSave resets its store at SubsystemRegistration, which ran already)
            ServerSave.Store = new MemorySaveStore();
            Debug.Log("[KnPlayHere] Saves go to memory only this session: nothing is written to disk.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterScene()
        {
            if (!Active) return;
            if (!TryTesterLook(out string recipeText, out string problem))
            {
                Debug.LogError("[KnPlayHere] Not started: " + problem);
                StopPlay(1);
                return;
            }
            KnGoInGame.Source = new TesterGoInGame(new GoInGamePayload { CharacterId = string.Empty, Name = TesterName, RecipeText = recipeText });
            string join = JoinRules.PayloadText(GameBuild.Version, true);
            if (!KnBootstrap.StartPlayHere(Port, join)) { StopPlay(1); return; }
            Debug.Log($"[KnPlayHere] Entity-netcode server and client started on loopback {Port}. Click the ground to walk.");
            var host = new GameObject("KnPlayHere");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<Runner>().Walk = Environment.GetEnvironmentVariable(WalkVariable);
        }

        static void StopPlay(int exitCode)
        {
            s_ExitCode = exitCode;
            if (s_QuitAfter || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(WalkVariable))) EditorApplication.isPlaying = false;
        }

        /// <summary>The database's first lineage in its default look (the same way the test-lane bots enter).</summary>
        public static bool TryTesterLook(out string recipeText, out string problem)
        {
            recipeText = null;
            problem = null;
            GameObject prefab = PlayerWorldsSetup.PlayerPrefab();
            PlayerIdentity identity = prefab != null ? prefab.GetComponent<PlayerIdentity>() : null;
            CharacterCreationDatabase database = identity != null ? identity.Database : null;
            if (database == null || database.Lineages.Count == 0) { problem = "no player prefab with a character database (Build Player Ghost Assets)."; return false; }
            recipeText = RecipeText.Encode(RecipeEdit.Default(database.Lineages[0], default(BodyType)));
            return true;
        }

        /// <summary>"x,z;x,z" in metres; false when it does not read.</summary>
        public static bool TryPoints(string text, List<Vector3> into)
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

        sealed class TesterGoInGame : IKnGoInGameSource
        {
            readonly GoInGamePayload m_Payload;
            public TesterGoInGame(GoInGamePayload payload) => m_Payload = payload;
            public bool TryGet(World clientWorld, out GoInGamePayload payload) { payload = m_Payload; return true; }
        }

        /// <summary>Waits for the tester to enter, gives it the top movement ranks, and (scripted) walks then runs the points.</summary>
        sealed class Runner : MonoBehaviour
        {
            public string Walk;
            int m_Errors;

            void OnEnable() => Application.logMessageReceived += Count;
            void OnDisable() => Application.logMessageReceived -= Count;

            void Count(string text, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) m_Errors++;
            }

            IEnumerator Start()
            {
                float started = Time.realtimeSinceStartup;
                while (LocalPlayer.Object == null || !TryServer(out _, out _))
                {
                    if (Time.realtimeSinceStartup - started > 60f) { Log("the tester never entered within 60 s"); Finish(false); yield break; }
                    yield return null;
                }
                TryServer(out GameObject body, out Entity ghost);
                var skills = body.GetComponent<PlayerMovementSkills>();
                if (skills != null) skills.GmSetMaxRanks();
                Log($"the tester entered after {Time.realtimeSinceStartup - started:F1} s; Run rank {(skills != null ? skills.RunRank : 0)}, Flux {Flux(body)}");
                if (string.IsNullOrEmpty(Walk)) yield break; // by hand: the owner clicks

                var points = new List<Vector3>();
                if (!TryPoints(Walk, points)) { Log(WalkVariable + " does not read as x,z;x,z: " + Walk); Finish(false); yield break; }
                int arrived = 0, tries = 0;
                foreach (bool run in new[] { false, true })
                    foreach (Vector3 flat in points)
                    {
                        tries++;
                        Vector3 p = flat;
                        p.y = Physics.Raycast(new Vector3(p.x, 500f, p.z), Vector3.down, out RaycastHit hit, 1000f) ? hit.point.y : 0f;
                        float flux = Flux(body);
                        BodyClicks.Ground(p, run);
                        float clicked = Time.realtimeSinceStartup;
                        bool moving = false, done = false;
                        while (Time.realtimeSinceStartup - clicked < ArriveTimeoutSeconds)
                        {
                            yield return null;
                            bool now = Moving(ghost);
                            if (now) moving = true;
                            else if (moving || Time.realtimeSinceStartup - clicked > 3f) { done = moving; break; }
                        }
                        Vector3 server = ServerPosition(ghost);
                        float off = Flat(server - p);
                        bool ok = done && off < 1.5f;
                        if (ok) arrived++;
                        string refusal = KnBootstrap.ServerWorld?.GetExistingSystemManaged<PlayerServerPlanSystem>()?.LastCostRefusal;
                        Log($"{(run ? "run" : "walk")} to {p.x:F1},{p.z:F1}: {(ok ? "arrived" : moving ? "NOT there" : "never started")} after " +
                            $"{Time.realtimeSinceStartup - clicked:F1} s; server {server.x:F2},{server.z:F2} ({off:F2} m off), body " +
                            $"{LocalPlayer.Object.transform.position.x:F2},{LocalPlayer.Object.transform.position.z:F2}; Flux {flux:F0} -> {Flux(body):F0}" +
                            (string.IsNullOrEmpty(refusal) ? "" : "; last refusal: " + refusal));
                    }
                Log($"done: {arrived} of {tries} arrived, {m_Errors} error lines");
                Finish(arrived == tries && m_Errors == 0);
            }

            static void Log(string text) => Debug.Log("[KnPlayHere] " + text);

            void Finish(bool ok) => StopPlay(ok ? 0 : 1);

            static bool TryServer(out GameObject body, out Entity ghost)
            {
                body = null;
                ghost = Entity.Null;
                World server = KnBootstrap.ServerWorld;
                if (server == null) return false;
                foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                    if (ReferenceEquals(entry.World, server) && entry.Body != null) { body = entry.Body; ghost = entry.Ghost; return true; }
                return false;
            }

            static bool Moving(Entity ghost)
            {
                World server = KnBootstrap.ServerWorld;
                return server != null && server.EntityManager.Exists(ghost)
                    && (server.EntityManager.GetComponentData<MoveState>(ghost).Flags & MoveFlags.Moving) != 0;
            }

            static Vector3 ServerPosition(Entity ghost)
            {
                World server = KnBootstrap.ServerWorld;
                if (server == null || !server.EntityManager.Exists(ghost)) return Vector3.zero;
                var pose = server.EntityManager.GetComponentData<PlayerPose>(ghost);
                return new Vector3(pose.XMm, pose.YMm, pose.ZMm) * 0.001f;
            }

            static float Flux(GameObject body)
            {
                var resources = body != null ? body.GetComponent<PlayerResources>() : null;
                return resources != null ? resources.Chi : -1f;
            }

            static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
        }
    }
}
#endif
