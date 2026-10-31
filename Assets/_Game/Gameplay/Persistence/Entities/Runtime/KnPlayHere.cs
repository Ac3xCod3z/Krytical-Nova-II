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
        /// <summary>A folder: the scripted run saves a Game-view picture there every 0.1 s (proof for the owner, GUI Editor only).</summary>
        public const string CaptureVariable = "KN_PLAYHERE_CAPTURE";

        /// <summary>
        /// After the camera (Cinemachine brain 100) and the cards (200): how far the head anchor of the own body and any pet move on
        /// the screen from one frame to the next. With a follow camera without damping a smooth follow keeps them nearly still on
        /// the screen; a jump of pixels is judder. Owner, 30 Oct: everything that follows the player moves as smoothly as the body.
        /// </summary>
        /// <summary>
        /// The own body's yaw at several LateUpdate orders (owner's GUI retries 3-4: something between the put-back at -200 and the
        /// camera turned the body back); the first order where it differs from the put-back names the writer's range.
        /// </summary>
        static class YawProbes
        {
            public static readonly int[] Orders = { -150, -50, -1, 1, 50, 99, 101, 150, 250 };
            public static readonly float[] Yaw = new float[Orders.Length];
            public static void Record(int i)
            {
                GameObject body = LocalPlayer.Object;
                Yaw[i] = body != null ? body.transform.eulerAngles.y : float.NaN;
                if (i != 2 || body == null) return;                       // order -1: just before the order-0 LateUpdates
                var inventory = body.GetComponent("PlayerInventory");
                var field = inventory != null ? inventory.GetType().GetField("pullPending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) : null;
                if (field == null) return;
                if ((bool)field.GetValue(inventory)) { PullPendingFrames++; if (SuppressPull) field.SetValue(inventory, false); }
            }
            public static int PullPendingFrames;
            public static bool SuppressPull;
            public static string Line()
            {
                var sb = new System.Text.StringBuilder("yaw by LateUpdate order:");
                for (int i = 0; i < Orders.Length; i++) sb.Append(' ').Append(Orders[i]).Append('=').Append(Yaw[i].ToString("F1"));
                return sb.ToString();
            }
            public static void Attach(GameObject host)
            {
                host.AddComponent<YawProbem150>();
                host.AddComponent<YawProbem50>();
                host.AddComponent<YawProbem1>();
                host.AddComponent<YawProbe1>();
                host.AddComponent<YawProbe50>();
                host.AddComponent<YawProbe99>();
                host.AddComponent<YawProbe101>();
                host.AddComponent<YawProbe150>();
                host.AddComponent<YawProbe250>();
            }
        }
        [DefaultExecutionOrder(-150)] sealed class YawProbem150 : MonoBehaviour { void LateUpdate() => YawProbes.Record(0); }
        [DefaultExecutionOrder(-50)] sealed class YawProbem50 : MonoBehaviour { void LateUpdate() => YawProbes.Record(1); }
        [DefaultExecutionOrder(-1)] sealed class YawProbem1 : MonoBehaviour { void LateUpdate() => YawProbes.Record(2); }
        [DefaultExecutionOrder(1)] sealed class YawProbe1 : MonoBehaviour { void LateUpdate() => YawProbes.Record(3); }
        [DefaultExecutionOrder(50)] sealed class YawProbe50 : MonoBehaviour { void LateUpdate() => YawProbes.Record(4); }
        [DefaultExecutionOrder(99)] sealed class YawProbe99 : MonoBehaviour { void LateUpdate() => YawProbes.Record(5); }
        [DefaultExecutionOrder(101)] sealed class YawProbe101 : MonoBehaviour { void LateUpdate() => YawProbes.Record(6); }
        [DefaultExecutionOrder(150)] sealed class YawProbe150 : MonoBehaviour { void LateUpdate() => YawProbes.Record(7); }
        [DefaultExecutionOrder(250)] sealed class YawProbe250 : MonoBehaviour { void LateUpdate() => YawProbes.Record(8); }

        /// <summary>The enabled behaviours with a LateUpdate at exactly <paramref name="order"/> (not this file's own probes).</summary>
        static List<MonoBehaviour> LateUpdatesAt(int order)
        {
            var list = new List<MonoBehaviour>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                if (behaviour == null || !behaviour.isActiveAndEnabled || behaviour.GetType().DeclaringType == typeof(KnPlayHere)) continue;
                var type = behaviour.GetType();
                bool has = false;
                for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                    if (t.GetMethod("LateUpdate", flags | System.Reflection.BindingFlags.DeclaredOnly) != null) { has = true; break; }
                if (!has) continue;
                var script = MonoScript.FromMonoBehaviour(behaviour);
                int o = script != null ? MonoImporter.GetExecutionOrder(script) : 0;
                var attribute = (DefaultExecutionOrder)Attribute.GetCustomAttribute(type, typeof(DefaultExecutionOrder));
                if (o == 0 && attribute != null) o = attribute.order;
                if (o == order) list.Add(behaviour);
            }
            return list;
        }

        static string PathOf(Component c)
        {
            string path = c.name;
            for (var tr = c.transform.parent; tr != null; tr = tr.parent) path = tr.name + "/" + path;
            return c.GetType().FullName + " on " + path;
        }

        /// <summary>True when the body's yaw at LateUpdate order 1 differs from order -1 (something at order 0 turned it).</summary>
        static bool TurnedAtOrderZero() => Mathf.Abs(Mathf.DeltaAngle(YawProbes.Yaw[2], YawProbes.Yaw[3])) > 0.5f;

        /// <summary>
        /// Halves the order-0 LateUpdates, switching one half off for a few frames, until the one that turns the body is left
        /// (owner's GUI retries 3-4: the body slid without turning). Everything is switched back on.
        /// </summary>
        static IEnumerator FindOrderZeroWriter()
        {
            var suspects = LateUpdatesAt(0);
            for (int i = 0; i < 3; i++) yield return null;
            if (!TurnedAtOrderZero()) { Debug.Log("[KnPlayHere] bisect: nothing at order 0 turns the body now"); yield break; }
            Debug.Log($"[KnPlayHere] bisect: {suspects.Count} behaviours with a LateUpdate at order 0");
            while (suspects.Count > 1)
            {
                var half = suspects.GetRange(0, suspects.Count / 2);
                foreach (var b in half) if (b != null) b.enabled = false;
                for (int i = 0; i < 3; i++) yield return null;
                bool fixedByHalf = !TurnedAtOrderZero();
                foreach (var b in half) if (b != null) b.enabled = true;
                for (int i = 0; i < 2; i++) yield return null;
                suspects = fixedByHalf ? half : suspects.GetRange(half.Count, suspects.Count - half.Count);
            }
            Debug.Log("[KnPlayHere] " + (suspects.Count == 1 && suspects[0] != null ? "bisect: THE WRITER IS " + PathOf(suspects[0]) : "bisect: no single writer found"));

            // which part of the inventory: its pull (LateUpdate -> PullNetCells -> Changed), or being enabled at all
            GameObject own = LocalPlayer.Object;
            var inv = own != null ? own.GetComponent("PlayerInventory") : null;
            if (inv == null) yield break;
            int before = YawProbes.PullPendingFrames;
            for (int i = 0; i < 10; i++) yield return null;
            Debug.Log($"[KnPlayHere] inventory: a pull was pending on {YawProbes.PullPendingFrames - before} of the last 10 frames");
            var changed = inv.GetType().GetField("Changed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (changed?.GetValue(inv) is Delegate d)
            {
                var names = new System.Text.StringBuilder();
                foreach (var h in d.GetInvocationList())
                    names.Append("\n    ").Append(h.Method.DeclaringType?.FullName).Append('.').Append(h.Method.Name)
                         .Append(h.Target is Component c ? " on " + c.name : "");
                Debug.Log("[KnPlayHere] inventory Changed listeners:" + names);
            }
            YawProbes.SuppressPull = true;
            for (int i = 0; i < 5; i++) yield return null;
            bool turnedWithoutPull = TurnedAtOrderZero();
            YawProbes.SuppressPull = false;
            Debug.Log($"[KnPlayHere] inventory test A: pull held back -> {(turnedWithoutPull ? "still turned back (not the pull: something that needs the inventory enabled)" : "the turn STAYS (the pull's Changed chain is the writer)")}");
        }

        /// <summary>Every enabled behaviour in the scenes with a LateUpdate and an execution order in (-200, 300]: who may turn the body after the put-back.</summary>
        static string LateUpdateSuspects()
        {
            var sb = new System.Text.StringBuilder();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                if (behaviour == null || !behaviour.isActiveAndEnabled) continue;
                var type = behaviour.GetType();
                bool has = false;
                for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                    if (t.GetMethod("LateUpdate", flags | System.Reflection.BindingFlags.DeclaredOnly) != null) { has = true; break; }
                if (!has) continue;
                var script = MonoScript.FromMonoBehaviour(behaviour);
                int order = script != null ? MonoImporter.GetExecutionOrder(script) : 0;
                var attribute = (DefaultExecutionOrder)Attribute.GetCustomAttribute(type, typeof(DefaultExecutionOrder));
                if (order == 0 && attribute != null) order = attribute.order;
                if (order <= -200 || order > 300) continue;
                string path = behaviour.name;
                for (var tr = behaviour.transform.parent; tr != null; tr = tr.parent) path = tr.name + "/" + path;
                sb.Append($"\n    {order,5} {type.FullName} on {path}");
            }
            return sb.ToString();
        }

        [DefaultExecutionOrder(300)]
        sealed class ScreenWatch : MonoBehaviour
        {
            public string CaptureFolder;
            public static float WorstAnchorPx, WorstPetPx;
            public static int Frames;
            Vector3 m_LastAnchor = new Vector3(float.NaN, 0f, 0f), m_LastPet = new Vector3(float.NaN, 0f, 0f);
            float m_NextShot;
            int m_Shot;
            MonoBehaviour m_Pet;

            public static void ResetWorst() { WorstAnchorPx = 0f; WorstPetPx = 0f; Frames = 0; }

            /// <summary>The own body's yaw after the camera (this LateUpdate, order 300) and when the frame is drawn.</summary>
            public static float YawAfterCamera = float.NaN, YawAtRender = float.NaN;

            void OnEnable() => UnityEngine.Rendering.RenderPipelineManager.beginContextRendering += AtRender;
            void OnDisable() => UnityEngine.Rendering.RenderPipelineManager.beginContextRendering -= AtRender;

            static void AtRender(UnityEngine.Rendering.ScriptableRenderContext context, List<Camera> cameras)
            {
                GameObject body = LocalPlayer.Object;
                if (body != null) YawAtRender = body.transform.eulerAngles.y;
            }

            void LateUpdate()
            {
                var cam = Camera.main;
                GameObject body = LocalPlayer.Object;
                if (cam == null || body == null) return;
                YawAfterCamera = body.transform.eulerAngles.y;
                Frames++;
                Vector3 anchor = cam.WorldToScreenPoint(body.transform.position + body.transform.up * 2f);
                if (!float.IsNaN(m_LastAnchor.x)) WorstAnchorPx = Mathf.Max(WorstAnchorPx, ((Vector2)(anchor - m_LastAnchor)).magnitude);
                m_LastAnchor = anchor;
                if (m_Pet == null && Frames % 60 == 1)   // the pet's type lives in a system this one does not use: found by name
                    foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
                        if (behaviour != null && behaviour.GetType().Name == "PetFollower") { m_Pet = behaviour; break; }
                var pet = m_Pet;
                if (pet != null)
                {
                    Vector3 at = cam.WorldToScreenPoint(pet.transform.position);
                    if (!float.IsNaN(m_LastPet.x)) WorstPetPx = Mathf.Max(WorstPetPx, ((Vector2)(at - m_LastPet)).magnitude);
                    m_LastPet = at;
                }
                if (!string.IsNullOrEmpty(CaptureFolder) && Time.realtimeSinceStartup >= m_NextShot)
                {
                    m_NextShot = Time.realtimeSinceStartup + 0.1f;
                    System.IO.Directory.CreateDirectory(CaptureFolder);
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(CaptureFolder, $"f{m_Shot++:D4}_{Time.frameCount}.png"));
                }
            }
        }
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
            // Netcode for GameObjects stays off: no host on 7777, no player from the local profile, the offline avatar hidden
            // (owner's feel test 30 Oct: the scene's NGO host started too and took the clicks and the camera)
            EntityNetcodeSession.Owns = true;
            Debug.Log("[KnPlayHere] Saves go to memory only this session: nothing is written to disk. Netcode for GameObjects stays off.");
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
            host.AddComponent<ScreenWatch>().CaptureFolder = Environment.GetEnvironmentVariable(CaptureVariable);
            YawProbes.Attach(host);
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

            // the per-click lines of both worlds ([KnClick] client / server / refused why=...) go to the Console
            void OnEnable() { Application.logMessageReceived += Count; PlayerClickLog.Sink = Debug.Log; }
            void OnDisable() { Application.logMessageReceived -= Count; PlayerClickLog.Sink = null; }

            // ---- click timing and frame rate (owner's feel test 30 Oct: "slow, laggy"; measured, not guessed) ----
            const double ClickWatchSeconds = 3d;
            const float FpsEverySeconds = 5f;
            int m_SeenClicks;
            bool m_Watching;
            double m_Press;
            int m_PressFrame;
            Vector3 m_PressBody;
            ushort m_Seq, m_LastFrameSeq;
            Vector3 m_LastFrameBody;
            double m_Gather = -1, m_Predicted = -1, m_Body = -1, m_Ack = -1;
            float m_FpsSince;
            int m_FpsFrames;
            float m_WorstFrame;

            float m_LastY = float.NaN, m_WorstDy, m_IkPelvis, m_IkFoot;
            int m_LastOverwritten;

            void Update()
            {
                m_FpsFrames++;
                m_WorstFrame = Mathf.Max(m_WorstFrame, Time.unscaledDeltaTime);
                m_FpsSince += Time.unscaledDeltaTime;
                string ik = "no foot IK";
                if (TryLocalGhost(out _, out _, out GameObject body))
                {
                    float y = body.transform.position.y;
                    if (!float.IsNaN(m_LastY)) m_WorstDy = Mathf.Max(m_WorstDy, Mathf.Abs(y - m_LastY));
                    m_LastY = y;
                    var feet = body.GetComponentInChildren<FootGroundingIK>(true);
                    if (feet != null)
                    {
                        m_IkPelvis = Mathf.Max(m_IkPelvis, Mathf.Abs(feet.PelvisOffset));
                        m_IkFoot = Mathf.Max(m_IkFoot, Mathf.Max(Mathf.Abs(feet.LeftGroundDelta), Mathf.Abs(feet.RightGroundDelta)));
                        ik = $"foot IK {(feet.isActiveAndEnabled ? "on" : "OFF")}, blend {feet.Blend:F2}, biggest pelvis shift {m_IkPelvis * 1000f:F0} mm, " +
                             $"biggest foot ground step {m_IkFoot * 1000f:F0} mm";
                    }
                }
                if (m_FpsSince >= FpsEverySeconds)
                {
                    // frames on which something moved or turned the body after the follow placed it (put back before the camera)
                    int overwritten = TryLocalGhost(out _, out _, out GameObject own) && own.TryGetComponent(out PlayerPresentationState pose)
                        ? pose.Overwritten : 0;
                    Debug.Log($"[KnPlayHere] fps {m_FpsFrames / m_FpsSince:F0} over {m_FpsSince:F0} s, worst frame {m_WorstFrame * 1000f:F0} ms, " +
                              $"biggest height change of the body in one frame {m_WorstDy * 1000f:F0} mm; {ik}; " +
                              $"frames something else moved the body after the follow: {overwritten - m_LastOverwritten} frames");
                    m_LastOverwritten = overwritten;
                    m_FpsSince = 0f; m_FpsFrames = 0; m_WorstFrame = 0f; m_WorstDy = 0f; m_IkPelvis = 0f; m_IkFoot = 0f;
                }
                WatchClicks();
                LogYaw();
            }

            // ---- yaw per frame for 2 s after each ground click (owner's GUI retries 3-4, 30 Oct: the body slid without turning) ----
            const double YawLogSeconds = 2d;
            int m_YawClicks;
            double m_YawUntil;

            void LogYaw()
            {
                if (BodyClicks.GroundClicks != m_YawClicks) { m_YawClicks = BodyClicks.GroundClicks; m_YawUntil = Time.realtimeSinceStartupAsDouble + YawLogSeconds; }
                if (Time.realtimeSinceStartupAsDouble > m_YawUntil) return;
                if (!TryLocalGhost(out World client, out Entity ghost, out GameObject view)) return;
                Debug.Log("[KnYaw] " + YawLine(client, ghost, view));
            }

            /// <summary>Every yaw on the way from the ghost to the screen, in degrees.</summary>
            static string YawLine(World client, Entity ghost, GameObject view)
            {
                var em = client.EntityManager;
                var pose = em.GetComponentData<PlayerPose>(ghost);
                var lt = em.GetComponentData<Unity.Transforms.LocalTransform>(ghost);
                var move = em.GetComponentData<MoveState>(ghost);
                using var timeQuery = em.CreateEntityQuery(typeof(Unity.NetCode.NetworkTime));
                var nt = timeQuery.GetSingleton<Unity.NetCode.NetworkTime>();
                var state = view.GetComponent<PlayerPresentationState>();
                var animator = view.GetComponentInChildren<Animator>();
                return $"frame {Time.frameCount} tick {(nt.ServerTick.IsValid ? nt.ServerTick.TickIndexForValidTick : 0)}{(nt.IsPartialTick ? "p" : "")} " +
                       $"moving {((move.Flags & MoveFlags.Moving) != 0 ? 1 : 0)} order {move.OrderSeq}; ghost pose yaw {pose.YawCdeg * 0.01f:F1}, " +
                       $"ghost transform yaw {FollowMath.YawDegrees(lt.Rotation):F1}, follow input {(state != null ? state.InputYaw : float.NaN):F1}" +
                       $" ({(state != null && state.InputFromPoseAhead ? "PoseAhead" : "transform")}), follow wrote {(state != null ? state.PoseYaw : float.NaN):F1}, " +
                       $"body root {view.transform.eulerAngles.y:F1}, model {(animator != null ? animator.transform.eulerAngles.y : float.NaN):F1}" +
                       $" ({(animator != null ? animator.name : "no animator")}); last frame: at the pose check (290) {(state != null ? state.YawAtCheck : float.NaN):F1}, " +
                       $"after camera (300) {ScreenWatch.YawAfterCamera:F1}, at render {ScreenWatch.YawAtRender:F1}; motion warp {WarpState(view)}";
            }

            /// <summary>Whether a KINEMATION motion warp is playing on the body (it sets the root's rotation in its LateUpdate).</summary>
            static string WarpState(GameObject body)
            {
                foreach (var behaviour in body.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null || behaviour.GetType().Name != "MotionWarping") continue;
                    var isActive = behaviour.GetType().GetMethod("IsActive");
                    object active = isActive != null ? isActive.Invoke(behaviour, null) : null;
                    return $"{(active is bool b && b ? "ACTIVE" : "idle")} (component {(behaviour.enabled ? "on" : "off")}, on '{behaviour.name}')";
                }
                return "none";
            }

            void WatchClicks()
            {
                if (!TryLocalGhost(out World client, out Entity ghost, out GameObject view)) return;
                EntityManager em = client.EntityManager;
                if (BodyClicks.GroundClicks != m_SeenClicks)
                {
                    // this Update may run after the press and the gather of the same frame: compare with the frame before
                    m_SeenClicks = BodyClicks.GroundClicks;
                    m_Watching = true;
                    m_Press = BodyClicks.LastGroundRealtime;
                    m_PressFrame = BodyClicks.LastGroundFrame;
                    m_PressBody = m_LastFrameBody;
                    m_Seq = m_LastFrameSeq;
                    m_Gather = m_Predicted = m_Body = m_Ack = -1;
                }
                m_LastFrameSeq = em.GetComponentData<ClientPlanHeader>(ghost).Seq;
                m_LastFrameBody = view.transform.position;
                if (!m_Watching) return;
                double now = Time.realtimeSinceStartupAsDouble;
                ushort seq = em.GetComponentData<ClientPlanHeader>(ghost).Seq;
                if (m_Gather < 0 && seq != m_Seq) { m_Gather = now; m_Seq = seq; }
                if (m_Gather >= 0)
                {
                    var move = em.GetComponentData<MoveState>(ghost);
                    if (m_Predicted < 0 && move.OrderSeq == m_Seq && (move.Flags & MoveFlags.Moving) != 0) m_Predicted = now;
                    if (m_Body < 0 && Vector3.Distance(view.transform.position, m_PressBody) > 0.01f) m_Body = now;
                    if (m_Ack < 0 && em.GetComponentData<PathPlan>(ghost).Seq == m_Seq) m_Ack = now;
                }
                bool done = m_Gather >= 0 && m_Predicted >= 0 && m_Body >= 0 && m_Ack >= 0;
                if (!done && now - m_Press < ClickWatchSeconds) return;
                m_Watching = false;
                string Ms(double at) => at < 0 ? "never" : $"{(at - m_Press) * 1000d:F0} ms";
                Debug.Log($"[KnClick] seq {m_Seq}: press -> gather {Ms(m_Gather)}, first predicted move {Ms(m_Predicted)}, body moves {Ms(m_Body)}, " +
                          $"server's plan {Ms(m_Ack)} ({Time.frameCount - m_PressFrame} frames watched)");
            }

            static bool TryLocalGhost(out World client, out Entity ghost, out GameObject view)
            {
                client = KnBootstrap.ClientWorld;
                ghost = Entity.Null;
                view = null;
                if (client == null) return false;
                foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                    if (ReferenceEquals(entry.World, client) && entry.IsLocal && entry.Body != null)
                    {
                        ghost = entry.Ghost;
                        view = entry.Body;
                        return client.EntityManager.Exists(ghost) && client.EntityManager.HasComponent<ClientPlanHeader>(ghost);
                    }
                return false;
            }

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
                // the Netcode for GameObjects side must be off (owner's feel test 30 Oct: its host took the clicks and the camera)
                var ngo = Unity.Netcode.NetworkManager.Singleton;
                bool ngoUp = ngo != null && ngo.IsListening;
                var role = LocalPlayer.Object.GetComponent<KryticalNova.Core.BodyRoleTag>();
                Log($"Netcode for GameObjects {(ngoUp ? "IS RUNNING (wrong)" : "off")}; the local player is '{LocalPlayer.Object.name}' ({(role != null ? role.Kind.ToString() : "no role")})");
                if (ngoUp) Debug.LogError("[KnPlayHere] Netcode for GameObjects is running beside the entity netcode.");
                if (string.IsNullOrEmpty(Walk)) yield break; // by hand: the owner clicks

                var points = new List<Vector3>();
                if (!TryPoints(Walk, points)) { Log(WalkVariable + " does not read as x,z;x,z: " + Walk); Finish(false); yield break; }
                int arrived = 0, tries = 0;
                m_YawFrom = ServerPosition(ghost);
                foreach (bool run in new[] { false, true })
                    foreach (Vector3 flat in points)
                    {
                        tries++;
                        Vector3 p = flat;
                        p.y = Physics.Raycast(new Vector3(p.x, 500f, p.z), Vector3.down, out RaycastHit hit, 1000f) ? hit.point.y : 0f;
                        float flux = Flux(body);
                        BodyClicks.Ground(p, run);
                        ScreenWatch.ResetWorst();
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
                        if (!YawFaces(ghost, p, out string yawReport))
                        {
                            ok = false;
                            if (m_YawFailures++ == 0) yield return FindOrderZeroWriter();
                        }
                        Log(yawReport + "; " + YawProbes.Line());
                        bool smooth = ScreenWatch.WorstAnchorPx <= MaxScreenStepPx && ScreenWatch.WorstPetPx <= MaxScreenStepPx;
                        if (!smooth) { ok = false; m_JudderFailures++; }
                        Log($"followers on the screen over {ScreenWatch.Frames} frames: head anchor worst step {ScreenWatch.WorstAnchorPx:F1} px, " +
                            $"pet worst step {ScreenWatch.WorstPetPx:F1} px (limit {MaxScreenStepPx} px) -> {(smooth ? "smooth" : "JUDDER")}");
                        if (ok) arrived++;
                        string refusal = KnBootstrap.ServerWorld?.GetExistingSystemManaged<PlayerServerPlanSystem>()?.LastCostRefusal;
                        Log($"{(run ? "run" : "walk")} to {p.x:F1},{p.z:F1}: {(ok ? "arrived" : moving ? "NOT there" : "never started")} after " +
                            $"{Time.realtimeSinceStartup - clicked:F1} s; server {server.x:F2},{server.z:F2} ({off:F2} m off), body " +
                            $"{LocalPlayer.Object.transform.position.x:F2},{LocalPlayer.Object.transform.position.z:F2}; Flux {flux:F0} -> {Flux(body):F0}" +
                            (string.IsNullOrEmpty(refusal) ? "" : "; last refusal: " + refusal));
                    }
                Log($"done: {arrived} of {tries} arrived, {m_YawFailures} facing the wrong way, {m_JudderFailures} juddering, {m_Errors} error lines");
                Finish(arrived == tries && m_Errors == 0);
            }

            static void Log(string text) => Debug.Log("[KnPlayHere] " + text);

            Vector3 m_YawFrom;
            int m_YawFailures, m_JudderFailures;
            const float MaxScreenStepPx = 3f;

            /// <summary>
            /// After a walk from the last spot to <paramref name="to"/>: the server faces the way it walked, and the own body (root and
            /// animated model) faces as the server does, within 20 degrees.
            /// </summary>
            bool YawFaces(Entity serverGhost, Vector3 to, out string report)
            {
                Vector3 from = m_YawFrom;
                m_YawFrom = ServerPosition(serverGhost);
                Vector3 d = to - from;
                float way = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                World server = KnBootstrap.ServerWorld;
                float serverYaw = server.EntityManager.GetComponentData<PlayerPose>(serverGhost).YawCdeg * 0.01f;
                float root = float.NaN, model = float.NaN;
                string line = "no local body";
                if (TryLocalGhost(out World client, out Entity ghost, out GameObject view))
                {
                    root = view.transform.eulerAngles.y;
                    var animator = view.GetComponentInChildren<Animator>();
                    model = animator != null ? animator.transform.eulerAngles.y : root;
                    line = YawLine(client, ghost, view);
                }
                bool far = Flat(d) > 1f;   // a short hop says little about the way
                bool ok = !far || (Mathf.Abs(Mathf.DeltaAngle(serverYaw, way)) < 20f
                    && Mathf.Abs(Mathf.DeltaAngle(root, serverYaw)) < 20f && Mathf.Abs(Mathf.DeltaAngle(model, serverYaw)) < 20f);
                report = $"yaw after the walk: way {way:F0}, server {serverYaw:F0}, body root {root:F0}, model {model:F0} -> {(ok ? "faces the way" : "WRONG WAY")}; {line}";
                return ok;
            }

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
