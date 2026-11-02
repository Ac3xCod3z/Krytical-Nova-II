#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Items;
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

        /// <summary>
        /// The own body, held from the moment the tester entered (run 3e, 31 Oct: the bisect switched PlayerInventory off, whose
        /// OnDisable clears LocalPlayer, and every probe then lost the body). Before entering it is LocalPlayer's.
        /// </summary>
        static GameObject s_OwnBody;
        static GameObject OwnBody => s_OwnBody != null ? s_OwnBody : KryticalNova.Core.LocalPlayer.Object;
        /// <summary>Scripted jumps (M3b, plan 53): <c>KN_PLAYHERE_JUMP="x,z;x,z"</c>, after any walk, each logged and judged.</summary>
        public const string JumpVariable = "KN_PLAYHERE_JUMP";

        /// <summary>A scripted run (walk or jump) was asked for: it quits by itself.</summary>
        static bool Scripted => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(WalkVariable))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(JumpVariable))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KN_PLAYHERE_VAULT"))    // the scripted vaults (M3c)
            || Environment.GetEnvironmentVariable("KN_PLAYHERE_WATER") == "1"                   // the scripted Waterstride (M3c)
            || Environment.GetEnvironmentVariable("KN_PLAYHERE_WALL") == "1"                    // the scripted wall walk (M3c)
            || Environment.GetEnvironmentVariable("KN_PLAYHERE_CLIMB") == "1"                   // the scripted straight climb (M3c part 2)
            || Environment.GetEnvironmentVariable("KN_PLAYHERE_TREE") == "1"                    // the scripted tree, perch and drop (M3c part 3)
            || Environment.GetEnvironmentVariable("KN_PLAYHERE_CATCH") == "1"                   // the scripted jump that catches a wall (M3c part 4)
            || Environment.GetEnvironmentVariable("KN_PLAYHERE_AUTO") == "1"                    // the scripted self-starting run (M3c part 5)
            || Environment.GetEnvironmentVariable("KN_PLAYHERE_LEDGE") == "1"                   // the scripted route jumps (M3c part 5b)
            || Environment.GetEnvironmentVariable("KN_PLAYHERE_FIXES") == "1"                   // the owner's feel-test fixes 1-3 (2 Nov)
            || Environment.GetEnvironmentVariable("KN_PLAYHERE_ACROSS") == "1"                  // across the water when quicker (item 4)
            || Environment.GetEnvironmentVariable("KN_PLAYHERE_ITEMS") == "1";                  // items used by the server (item 5)
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
            /// <summary>The body's place at each order too (GUI run 2, 31 Oct: the camera trailed the body by one frame; who moved it when).</summary>
            public static readonly Vector3[] Pos = new Vector3[Orders.Length];
            /// <summary>The body's place at the last order of the previous frame (did it move this frame).</summary>
            public static Vector3 LastFramePos;
            /// <summary>Counted in the frame itself (run 3i: the coroutine judge read 0 of 20 while every frame was put back).</summary>
            public static int MovingFrames, PutBackFrames;
            static Vector3 s_PrevPos0;
            /// <summary>The frame of the last <see cref="Record"/> at order -150 (the follow runs before every LateUpdate, or not).</summary>
            public static int FrameAtFirst = -1;
            public static void Record(int i)
            {
                GameObject body = OwnBody;
                Yaw[i] = body != null ? body.transform.eulerAngles.y : float.NaN;
                Pos[i] = body != null ? body.transform.position : Vector3.zero;
                if (i == 0) FrameAtFirst = Time.frameCount;
                if (i == Orders.Length - 1)
                {
                    LastFramePos = Pos[i];
                    // moved this frame (order -150 against the last frame's) and put back at order 0 (order -1 against order 1)
                    if (Vector3.Distance(Pos[0], s_PrevPos0) > 0.005f) { MovingFrames++; if (Vector3.Distance(Pos[2], Pos[3]) > 0.005f) PutBackFrames++; }
                    s_PrevPos0 = Pos[0];
                }
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
        /// GUI run 3b (31 Oct): an order-0 LateUpdate puts the own body back on LAST frame's place (it is on the follow's pose at order
        /// -1 and on the old one at order 1), so the camera (order 100) trails it by a frame. Halves the order-0 LateUpdates, one half
        /// off at a time, judging only frames where the body moved, until the writer is left. Runs beside the walks; all back on after.
        /// </summary>
        static IEnumerator FindOrderZeroMover()
        {
            // Run 3c (31 Oct): switching ProgressionHUD back on threw (SkillTreeView.Build) and the retries had no end. Now every
            // toggle is guarded: a behaviour that throws is put back on, left out of the search and listed; at most 3 tries
            // without a verdict; it stops by itself.
            var suspects = LateUpdatesAt(0);
            var skipped = new List<string>();
            Debug.Log($"[KnPlayHere] place bisect: {suspects.Count} behaviours with a LateUpdate at order 0");
            var baseVerdict = new bool[2];
            yield return JudgeMovedBack(baseVerdict);
            Debug.Log($"[KnPlayHere] place bisect: control, all on: {Verdict(baseVerdict)} ({LastJudge})");
            if (!baseVerdict[1]) { Debug.Log("[KnPlayHere] place bisect: no put-back with all on: nothing to search"); yield break; }
            int rounds = 0, noVerdict = 0;
            while (suspects.Count > 1 && rounds++ < 12)
            {
                var half = suspects.GetRange(0, suspects.Count / 2);
                var off = new List<MonoBehaviour>();
                foreach (var b in half) if (TrySwitch(b, false, skipped)) off.Add(b);
                RestoreLocalPlayer();   // PlayerInventory.OnDisable clears LocalPlayer; the writer may read it
                var verdict = new bool[2];
                yield return JudgeMovedBack(verdict);
                foreach (var b in off) TrySwitch(b, true, skipped);
                RestoreLocalPlayer();
                suspects.RemoveAll(b => b == null || skipped.Contains(PathOf(b)));
                half.RemoveAll(b => b == null || skipped.Contains(PathOf(b)));
                if (!verdict[0])
                {
                    if (++noVerdict >= 3) { Debug.Log("[KnPlayHere] place bisect: GAVE UP, the body did not move for 3 tries"); break; }
                    Debug.Log($"[KnPlayHere] place bisect: the body stopped moving before a verdict; try {noVerdict} of 3");
                    rounds--;
                    continue;
                }
                var rest = suspects.FindAll(b => !half.Contains(b));
                suspects = !verdict[1] ? half : rest;
                Debug.Log($"[KnPlayHere] place bisect: round {rounds}: {(verdict[1] ? "still put back" : "NOT put back")} with {half.Count} off ({LastJudge}) -> {suspects.Count} left");
                if (suspects.Count <= 16) Debug.Log("[KnPlayHere] place bisect: left: " + string.Join(", ", suspects.ConvertAll(b => PathOf(b))));
                yield return null;
            }
            if (suspects.Count == 1 && suspects[0] != null && TrySwitch(suspects[0], false, skipped))
            {
                RestoreLocalPlayer();
                var alone = new bool[2];
                yield return JudgeMovedBack(alone);
                TrySwitch(suspects[0], true, skipped);
                RestoreLocalPlayer();
                Debug.Log($"[KnPlayHere] place bisect: proof, only {PathOf(suspects[0])} off: {Verdict(alone)} ({LastJudge})");
            }
            Debug.Log("[KnPlayHere] " + (suspects.Count == 1 && suspects[0] != null ? "place bisect: THE WRITER IS " + PathOf(suspects[0])
                : $"place bisect: no single writer found ({suspects.Count} left)") +
                (skipped.Count > 0 ? "; left out because switching them threw: " + string.Join(", ", skipped) : ""));
        }

        static int s_LoggedExceptions;
        static void CountExceptions(string text, string stack, LogType type) { if (type == LogType.Exception) s_LoggedExceptions++; }

        /// <summary>
        /// Switches a behaviour on or off; when that throws (Unity logs an exception from OnEnable/OnDisable instead of passing it to the
        /// caller, so both are watched), it is put back on and listed (false).
        /// </summary>
        static bool TrySwitch(MonoBehaviour b, bool on, List<string> skipped)
        {
            if (b == null) return false;
            int before = s_LoggedExceptions;
            string why = null;
            Application.logMessageReceived += CountExceptions;
            try { b.enabled = on; }
            catch (Exception e) { why = e.GetType().Name + ": " + e.Message; }
            finally { Application.logMessageReceived -= CountExceptions; }
            if (why == null && s_LoggedExceptions == before) return true;
            string name = PathOf(b);
            if (!skipped.Contains(name)) skipped.Add(name);
            Debug.Log($"[KnPlayHere] place bisect: switching {name} {(on ? "on" : "off")} threw {why ?? "(logged)"}; left out of the search");
            try { b.enabled = true; } catch (Exception) { }
            return false;
        }

        /// <summary>
        /// Run 3g (31 Oct): controls instead of a bisect (3f's verdict, WeaponHitbox, was biased: after PlayerInventory was switched off
        /// once, no round saw the put-back again). Nothing is switched off except as named; each is judged on walking frames.
        /// </summary>
        static IEnumerator PlaceControls()
        {
            var verdict = new bool[2];
            int pullBefore = YawProbes.PullPendingFrames, frameBefore = Time.frameCount;
            yield return JudgeMovedBack(verdict);
            Debug.Log($"[KnPlayHere] place control A, all on: {Verdict(verdict)}; the inventory pull was pending on " +
                      $"{YawProbes.PullPendingFrames - pullBefore} of {Time.frameCount - frameBefore} frames");

            GameObject own = OwnBody;
            var inv = own != null ? own.GetComponent("PlayerInventory") : null;
            var changed = inv != null ? inv.GetType().GetField("Changed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) : null;
            if (changed?.GetValue(inv) is Delegate d)
            {
                var names = new System.Text.StringBuilder();
                foreach (var h in d.GetInvocationList())
                    names.Append(" | ").Append(h.Method.DeclaringType?.FullName).Append('.').Append(h.Method.Name).Append(h.Target is Component c ? " on " + c.name : "");
                Debug.Log("[KnPlayHere] place control: inventory Changed listeners" + names);
            }

            verdict = new bool[2];
            YawProbes.SuppressPull = true;
            yield return JudgeMovedBack(verdict);
            YawProbes.SuppressPull = false;
            Debug.Log($"[KnPlayHere] place control B, inventory pull held back: {Verdict(verdict)}");

            var hitbox = own != null ? own.GetComponentInChildren(System.Type.GetType("KryticalNova.Combat.WeaponHitbox, KryticalNova.Combat") ?? typeof(MonoBehaviour), false) as MonoBehaviour : null;
            verdict = new bool[2];
            if (hitbox != null && hitbox.GetType().Name == "WeaponHitbox") hitbox.enabled = false;
            yield return JudgeMovedBack(verdict);
            if (hitbox != null && hitbox.GetType().Name == "WeaponHitbox") hitbox.enabled = true;
            Debug.Log($"[KnPlayHere] place control C, only WeaponHitbox off ({(hitbox != null ? hitbox.GetType().Name : "none found")}): {Verdict(verdict)}");

            verdict = new bool[2];
            yield return JudgeMovedBack(verdict);
            Debug.Log($"[KnPlayHere] place control D, all on again: {Verdict(verdict)}");
        }

        /// <summary>Puts the own body back as LocalPlayer when switching something off cleared it.</summary>
        static void RestoreLocalPlayer()
        {
            if (s_OwnBody != null && KryticalNova.Core.LocalPlayer.Object != s_OwnBody)
            {
                KryticalNova.Core.LocalPlayer.Set(s_OwnBody);
                Debug.Log("[KnPlayHere] place bisect: LocalPlayer was cleared (PlayerInventory.OnDisable); set back to the own body");
            }
        }

        /// <summary>
        /// Run 3j (31 Oct): the put-back is on EVERY moving frame (run 3i). All order-0 LateUpdates off together (is it one of them at
        /// all?), then each alone for 6 moving frames; counted at order 250 in the frame itself. Ones that throw when switched are
        /// skipped and listed; LocalPlayer is set back after each switch.
        /// </summary>
        static IEnumerator SingleSwitchTest()
        {
            var suspects = LateUpdatesAt(0);
            var skipped = new List<string>();
            Debug.Log($"[KnPlayHere] single test: {suspects.Count} behaviours with a LateUpdate at order 0");
            yield return Count(6, "all on");
            var off = new List<MonoBehaviour>();
            foreach (var b in suspects) if (TrySwitch(b, false, skipped)) off.Add(b);
            RestoreLocalPlayer();
            yield return Count(6, $"ALL {off.Count} order-0 LateUpdates off");
            foreach (var b in off) TrySwitch(b, true, skipped);
            RestoreLocalPlayer();
            yield return Count(4, "all on again");
            foreach (var b in suspects)
            {
                if (b == null || skipped.Contains(PathOf(b))) continue;
                if (!TrySwitch(b, false, skipped)) continue;
                RestoreLocalPlayer();
                yield return Count(6, "only " + PathOf(b) + " off");
                TrySwitch(b, true, skipped);
                RestoreLocalPlayer();
                for (int i = 0; i < 2; i++) yield return null;
            }
            Debug.Log("[KnPlayHere] single test: done" + (skipped.Count > 0 ? "; skipped (threw when switched): " + string.Join(", ", skipped) : ""));
        }

        /// <summary>Waits for <paramref name="moving"/> moving frames (at most 600 frames) and logs how many were put back.</summary>
        static IEnumerator Count(int moving, string what)
        {
            int m0 = YawProbes.MovingFrames, p0 = YawProbes.PutBackFrames;
            for (int i = 0; i < 600 && YawProbes.MovingFrames - m0 < moving; i++) yield return null;
            int m = YawProbes.MovingFrames - m0, pb = YawProbes.PutBackFrames - p0;
            Debug.Log($"[KnPlayHere] single test: {what}: {pb} of {m} moving frames put back{(m > 0 && pb == 0 ? "  <== NO PUT-BACK" : "")}");
        }

        static string Verdict(bool[] v) => !v[0] ? "NO VERDICT (the body did not move)" : v[1] ? "PUT BACK (the camera trails)" : "not put back";

        /// <summary>Waits for up to 4 frames where the body moved; verdict[0] = some moved, verdict[1] = put back at order 0 on any.</summary>
        static IEnumerator JudgeMovedBack(bool[] verdict)
        {
            // moved = the follow's place (order -150) changed from the frame before; NOT order -150 against order 250 of the same frame,
            // which are equal whenever the writer is off (run 3d: every try with the writer's half off read as "not moving")
            int seen = 0, putBack = 0;
            Vector3 before = YawProbes.Pos[0];
            // run 3g: the put-back comes on about a quarter of the walking frames, in short runs; 4 frames often missed it all
            for (int i = 0; i < 900 && seen < JudgeFrames; i++)
            {
                yield return null;
                Vector3 now = YawProbes.Pos[0];
                bool moved = Vector3.Distance(now, before) > 0.005f;
                before = now;
                if (!moved) continue;
                seen++;
                if (Vector3.Distance(YawProbes.Pos[2], YawProbes.Pos[3]) > 0.005f) { verdict[1] = true; putBack++; }
            }
            verdict[0] = seen >= JudgeFrames / 2;
            LastJudge = $"{putBack} of {seen} moving frames put back";
        }

        const int JudgeFrames = 20;
        static string LastJudge = "";

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
            GameObject own = OwnBody;
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
        /// <summary>
        /// The first frames DRAWN after the own body appeared (owner via Master, 31 Oct): a player may click at once and sees whatever is
        /// drawn, so the camera must already be on the body on the first of them. Logged at render time, with the camera's place on the
        /// last frame drawn before the body existed, so a snap after a drawn off-body frame shows as a big "moved".
        /// </summary>
        sealed class SpawnCameraWatch : MonoBehaviour
        {
            public const int Frames = 10;
            int m_Drawn = -1, m_LastFrame = -1;
            Vector3 m_LastCam;
            bool m_Has;

            void OnEnable() => UnityEngine.Rendering.RenderPipelineManager.beginContextRendering += AtRender;
            void OnDisable() => UnityEngine.Rendering.RenderPipelineManager.beginContextRendering -= AtRender;

            void AtRender(UnityEngine.Rendering.ScriptableRenderContext context, List<Camera> cameras)
            {
                if (m_Drawn >= Frames - 1 || Time.frameCount == m_LastFrame) return;   // the Scene view renders too: once a frame
                m_LastFrame = Time.frameCount;
                Camera cam = Camera.main;
                if (cam == null) return;
                Vector3 at = cam.transform.position;
                GameObject body = OwnBody;
                if (body == null) { m_LastCam = at; m_Has = true; return; }   // the world drawn before the player entered
                m_Drawn++;
                float toAnchor = Vector3.Distance(at, body.transform.position + Vector3.up * 2f);
                float moved = m_Has ? Vector3.Distance(at, m_LastCam) : 0f;
                m_LastCam = at;
                m_Has = true;
                Debug.Log($"[KnSpawnCam] drawn frame {m_Drawn} with the body (frame {Time.frameCount}, dt {Time.deltaTime * 1000f:F0} ms): " +
                          $"camera to head anchor {toAnchor:F2} m, camera moved {moved:F2} m since the last drawn frame; body at " +
                          $"{body.transform.position.x:F2},{body.transform.position.y:F2},{body.transform.position.z:F2}");
            }
        }

        /// <summary>
        /// Run 3k (31 Oct): the truth at render time, after every LateUpdate whatever their order. The LateUpdate probes are nested
        /// classes, and Unity keeps execution order per script file, so their DefaultExecutionOrder may not hold (run 3j's count at
        /// "250" saw no put-back while run 3i's trace at "300" saw one every frame). Here, once per drawn frame: how much the camera's
        /// offset from the body changed (0 = the camera follows exactly; a one-frame lag changes it by the body's own move).
        /// </summary>
        sealed class RenderWatch : MonoBehaviour
        {
            public static int Frames, Lagging;
            public static float WorstOffsetChangeMm;
            int m_LastFrame = -1, m_Lines;
            Vector3 m_LastOffset, m_LastBody;
            bool m_Has;

            void OnEnable() => UnityEngine.Rendering.RenderPipelineManager.beginContextRendering += AtRender;
            void OnDisable() => UnityEngine.Rendering.RenderPipelineManager.beginContextRendering -= AtRender;

            public static void Reset() { Frames = 0; Lagging = 0; WorstOffsetChangeMm = 0f; }

            void AtRender(UnityEngine.Rendering.ScriptableRenderContext context, List<Camera> cameras)
            {
                if (Time.frameCount == m_LastFrame) return;
                m_LastFrame = Time.frameCount;
                Camera cam = Camera.main;
                GameObject body = OwnBody;
                if (cam == null || body == null) return;
                Vector3 offset = cam.transform.position - body.transform.position;
                float bodyMoved = m_Has ? Vector3.Distance(body.transform.position, m_LastBody) : 0f;
                float change = m_Has ? Vector3.Distance(offset, m_LastOffset) : 0f;
                m_LastOffset = offset; m_LastBody = body.transform.position; m_Has = true;
                if (bodyMoved < 0.005f) return;
                Frames++;
                WorstOffsetChangeMm = Mathf.Max(WorstOffsetChangeMm, change * 1000f);
                if (change > 0.005f) Lagging++;
                if (s_RenderTrace && m_Lines++ < 300)
                    Debug.Log($"[KnRender] frame {Time.frameCount} dt {Time.deltaTime * 1000f:F1} ms body moved {bodyMoved * 1000f:F0} mm " +
                              $"camera offset changed {change * 1000f:F0} mm{(change > 0.005f ? " LAG" : "")}");
            }

            static readonly bool s_RenderTrace = Environment.GetEnvironmentVariable("KN_PLAYHERE_RENDER_TRACE") == "1";
        }

        sealed class ScreenWatch : MonoBehaviour
        {
            public string CaptureFolder;
            // Steps are judged with LAST frame's projection and this frame's view: a field-of-view change (the Run Start kick, the
            // same GameFeel in the NGO game) zooms the whole picture and is the designed look, not judder. The zoom's own worst is
            // reported beside it.
            public static float WorstAnchorPx, WorstPetPx, WorstZoomPx;
            public static int Frames;
            Vector3 m_LastAnchor = new Vector3(float.NaN, 0f, 0f), m_LastPet = new Vector3(float.NaN, 0f, 0f);
            float m_NextShot;
            int m_Shot;
            MonoBehaviour m_Pet;
            Vector3 m_LastBody, m_LastCam;
            Quaternion m_LastCamRot = Quaternion.identity;
            Matrix4x4 m_LastProjection;
            int m_LastGc;
            int m_CamLines, m_JumpLines;

            public static void ResetWorst() { WorstAnchorPx = 0f; WorstPetPx = 0f; WorstZoomPx = 0f; Frames = 0; }

            /// <summary>Where <paramref name="world"/> lands on the screen through <paramref name="projection"/> and the camera's view now.</summary>
            static Vector3 OnScreen(Camera cam, Matrix4x4 projection, Vector3 world)
            {
                Vector4 clip = projection * (cam.worldToCameraMatrix * new Vector4(world.x, world.y, world.z, 1f));
                if (Mathf.Abs(clip.w) < 1e-6f) return new Vector3(float.NaN, 0f, 0f);
                return new Vector3((clip.x / clip.w * 0.5f + 0.5f) * cam.pixelWidth, (clip.y / clip.w * 0.5f + 0.5f) * cam.pixelHeight, clip.w);
            }

            /// <summary>The own body's yaw after the camera (this LateUpdate, order 300) and when the frame is drawn.</summary>
            public static float YawAfterCamera = float.NaN, YawAtRender = float.NaN;

            void OnEnable() => UnityEngine.Rendering.RenderPipelineManager.beginContextRendering += AtRender;
            void OnDisable() => UnityEngine.Rendering.RenderPipelineManager.beginContextRendering -= AtRender;

            static string Fmt(Vector3 v) => $"{v.x:F2},{v.z:F2}";

            static string PosLine()
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < YawProbes.Orders.Length; i++) sb.Append(YawProbes.Orders[i]).Append(' ').Append(Fmt(YawProbes.Pos[i])).Append(i + 1 < YawProbes.Orders.Length ? ", " : "");
                return sb.ToString();
            }

            static bool s_ListedLateScripts;

            /// <summary>Once: every enabled script on the own body (and its children) with a LateUpdate, and its execution order.</summary>
            static string LateScriptsOnce()
            {
                if (s_ListedLateScripts || OwnBody == null) return "";
                s_ListedLateScripts = true;
                var sb = new System.Text.StringBuilder("; LateUpdate scripts on the own body:");
                const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                                                          | System.Reflection.BindingFlags.NonPublic;
                foreach (var behaviour in OwnBody.GetComponentsInChildren<MonoBehaviour>(false))
                {
                    if (behaviour == null || !behaviour.isActiveAndEnabled) continue;
                    var type = behaviour.GetType();
                    if (type.GetMethod("LateUpdate", any) == null) continue;
                    var order = (DefaultExecutionOrder)Attribute.GetCustomAttribute(type, typeof(DefaultExecutionOrder));
                    sb.Append(' ').Append(type.Name).Append('(').Append(order != null ? order.order : 0).Append(") on '").Append(behaviour.name).Append('\'');
                }
                return sb.ToString();
            }

            /// <summary>Where the follow put the own body this frame (before any LateUpdate), from its state.</summary>
            static string FollowPose()
            {
                GameObject body = OwnBody;
                var state = body != null ? body.GetComponent<PlayerPresentationState>() : null;
                return state != null && state.HasPose ? Fmt(state.PosePosition) : "none";
            }

            /// <summary>The Cinemachine brain's live camera and whether it is blending: who placed the camera this frame.</summary>
            static string CameraBrainLine(Camera cam)
            {
                var brain = cam.GetComponent("CinemachineBrain");   // Cinemachine is not a reference of this assembly: read by name
                if (brain == null) return "none";
                var type = brain.GetType();
                var live = type.GetProperty("ActiveVirtualCamera")?.GetValue(brain) as Component;
                return $"live {(live != null ? live.name : "none")} blending {type.GetProperty("IsBlending")?.GetValue(brain)}";
            }

            static void AtRender(UnityEngine.Rendering.ScriptableRenderContext context, List<Camera> cameras)
            {
                GameObject body = OwnBody;
                if (body != null) YawAtRender = body.transform.eulerAngles.y;
            }

            static readonly bool s_TickTrace = Environment.GetEnvironmentVariable("KN_PLAYHERE_TICK_TRACE") == "1";
            int m_TraceLines;
            uint m_LastTraceTick;
            Vector3 m_LastTracePos;

            /// <summary>
            /// Run 3i (31 Oct, Master's lead): the put-back comes on the long frames; per walking frame, which frames: dt, the client's
            /// tick (p = partial), how many whole ticks it moved, the put-back at order 0 and the body's move.
            /// </summary>
            void TraceTick(GameObject body)
            {
                if (!s_TickTrace || m_TraceLines >= 500) return;
                Vector3 now = YawProbes.Pos[0];
                float moved = Vector3.Distance(now, m_LastTracePos);
                m_LastTracePos = now;
                if (moved < 0.005f) return;
                World client = KnBootstrap.ClientWorld;
                string tick = "?";
                uint index = 0;
                if (client != null && client.IsCreated)
                {
                    using var q = client.EntityManager.CreateEntityQuery(typeof(Unity.NetCode.NetworkTime));
                    if (q.CalculateEntityCount() == 1)
                    {
                        var nt = q.GetSingleton<Unity.NetCode.NetworkTime>();
                        if (nt.ServerTick.IsValid)
                        {
                            index = nt.ServerTick.TickIndexForValidTick;
                            tick = index + (nt.IsPartialTick ? "p" : "") + " frac " + nt.ServerTickFraction.ToString("F2");
                        }
                    }
                }
                int advanced = m_LastTraceTick == 0 ? 0 : (int)(index - m_LastTraceTick);
                m_LastTraceTick = index;
                float putBack = Vector3.Distance(YawProbes.Pos[2], YawProbes.Pos[3]) * 1000f;
                m_TraceLines++;
                Debug.Log($"[KnTick] frame {Time.frameCount} dt {Time.deltaTime * 1000f:F1} ms tick {tick} (+{advanced}) moved {moved * 1000f:F0} mm " +
                          $"put back {putBack:F0} mm{(putBack > 5f ? " PUT-BACK" : "")}");
            }

            void LateUpdate()
            {
                var cam = Camera.main;
                GameObject body = OwnBody;
                if (cam == null || body == null) return;
                TraceTick(body);
                YawAfterCamera = body.transform.eulerAngles.y;
                Frames++;
                Vector3 anchorWorld = body.transform.position + body.transform.up * 2f;
                Vector3 anchor = OnScreen(cam, cam.projectionMatrix, anchorWorld);
                bool first = float.IsNaN(m_LastAnchor.x);
                float zoomStep = first ? 0f : ((Vector2)(anchor - m_LastAnchor)).magnitude;
                float step = first ? 0f : ((Vector2)(OnScreen(cam, m_LastProjection, anchorWorld) - m_LastAnchor)).magnitude;
                if (!first) { WorstAnchorPx = Mathf.Max(WorstAnchorPx, step); WorstZoomPx = Mathf.Max(WorstZoomPx, zoomStep); }
                // fast moves, frame by frame: body travel, camera travel, camera distance (the deoccluder pulls it in), screen step
                Vector3 bodyPos = body.transform.position, camPos = cam.transform.position;
                if (!float.IsNaN(m_LastAnchor.x) && m_CamLines < 600)
                {
                    float bodyMove = Vector3.Distance(bodyPos, m_LastBody), camMove = Vector3.Distance(camPos, m_LastCam);
                    if (bodyMove / Mathf.Max(1e-4f, Time.deltaTime) > 10f)
                    {
                        m_CamLines++;
                        Vector3 gap = (camPos - m_LastCam) - (bodyPos - m_LastBody);   // camera travel minus body travel, as a vector
                        float turned = Quaternion.Angle(cam.transform.rotation, m_LastCamRot);
                        Debug.Log($"[KnCam] frame {Time.frameCount} dt {Time.deltaTime * 1000f:F1} ms body moved {bodyMove * 1000f:F0} mm camera moved {camMove * 1000f:F0} mm " +
                                  $"travel gap {gap.magnitude * 1000f:F1} mm camera turned {turned:F3} deg GC {System.GC.CollectionCount(0) - m_LastGc} " +
                                  $"fov {cam.fieldOfView:F2} aspect {cam.aspect:F3} pixels {cam.pixelWidth}x{cam.pixelHeight} scaled {cam.scaledPixelWidth} " +
                                  $"res scale {ScalableBufferManager.widthScaleFactor:F3} " +
                                  $"camera to anchor {Vector3.Distance(camPos, anchorWorld):F3} m anchor screen step {step:F1} px y {bodyPos.y:F3}");
                    }
                }
                if (!first && step > Runner.MaxScreenStepPx && m_JumpLines++ < 40)
                    Debug.Log($"[KnJump] frame {Time.frameCount} dt {Time.deltaTime * 1000f:F1} ms anchor step {step:F1} px (with zoom {zoomStep:F1}) " +
                              $"body moved {Vector3.Distance(bodyPos, m_LastBody) * 1000f:F0} mm camera moved {Vector3.Distance(camPos, m_LastCam) * 1000f:F0} mm " +
                              $"camera turned {Quaternion.Angle(cam.transform.rotation, m_LastCamRot):F3} deg fov {cam.fieldOfView:F2} " +
                              $"camera to anchor {Vector3.Distance(camPos, anchorWorld):F3} m camera {camPos} body {bodyPos} brain {CameraBrainLine(cam)}; " +
                              $"body by LateUpdate order: {PosLine()} (frame {YawProbes.FrameAtFirst}); follow wrote {FollowPose()}{LateScriptsOnce()}");
                m_LastBody = bodyPos; m_LastCam = camPos; m_LastCamRot = cam.transform.rotation; m_LastGc = System.GC.CollectionCount(0);
                m_LastAnchor = anchor;
                if (m_Pet == null && Frames % 60 == 1)   // the pet's type lives in a system this one does not use: found by name
                    foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
                        if (behaviour != null && behaviour.GetType().Name == "PetFollower") { m_Pet = behaviour; break; }
                var pet = m_Pet;
                if (pet != null)
                {
                    Vector3 at = OnScreen(cam, cam.projectionMatrix, pet.transform.position);
                    if (!float.IsNaN(m_LastPet.x))
                        WorstPetPx = Mathf.Max(WorstPetPx, ((Vector2)(OnScreen(cam, m_LastProjection, pet.transform.position) - m_LastPet)).magnitude);
                    m_LastPet = at;
                }
                m_LastProjection = cam.projectionMatrix;
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

        /// <summary>The GUI Editor opened with <c>-executeMethod</c> straight into "Play DOTS here" by hand (the owner's feel checkpoint).</summary>
        public static void ByHand() => Begin(quitAfter: false);

        /// <summary>The feel checkpoint's level: 60 opens every traversal (owner, 2 Nov: no Wallstride switch; a roof's ground click backflips with it on).</summary>
        const int FeelLevelOpen = 60;

        static void SetFeelLevel(int level)
        {
            if (!Runner.TryServer(out GameObject body, out _)) return;
            var progression = body.GetComponent<PlayerCharacterProgression>();
            if (progression == null) return;
            progression.SetLevel(level);
            Debug.Log($"[KnPlayHere] level {level}: Wallstride and Waterstride {(level >= FeelLevelOpen ? "on" : "off")}");
        }

        /// <summary>
        /// Batch: <c>Unity -batchmode -projectPath ... -executeMethod KryticalNova.Persistence.Entities.KnPlayHere.Batch</c> with
        /// <see cref="WalkVariable"/> set (no <c>-quit</c>: the run quits by itself).
        /// </summary>
        public static void Batch()
        {
            if (!Scripted)
            {
                Debug.LogError("[KnPlayHere] " + WalkVariable + " and " + JumpVariable + " are not set: nothing to walk or jump.");
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
            // one camera change per proof run: KN_PLAYHERE_SPAWNCAM_FIX=0 measures without the spawn-camera fix
            s_OwnBody = null;
            PlayerWorldsSetup.TakeCameraWhenBodyMade = Environment.GetEnvironmentVariable("KN_PLAYHERE_SPAWNCAM_FIX") != "0";
            Debug.Log("[KnPlayHere] spawn-camera fix " + (PlayerWorldsSetup.TakeCameraWhenBodyMade ? "on" : "OFF"));
            if (!KnBootstrap.StartPlayHere(Port, join)) { StopPlay(1); return; }
            Debug.Log($"[KnPlayHere] Entity-netcode server and client started on loopback {Port}. Click the ground to walk.");
            var host = new GameObject("KnPlayHere");
            UnityEngine.Object.DontDestroyOnLoad(host);
            if (Scripted)
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);   // scripted run: logging must not cause the spikes it measures
            var runner = host.AddComponent<Runner>();
            runner.Walk = Environment.GetEnvironmentVariable(WalkVariable);
            runner.Jump = Environment.GetEnvironmentVariable(JumpVariable);
            host.AddComponent<ScreenWatch>().CaptureFolder = Environment.GetEnvironmentVariable(CaptureVariable);
            YawProbes.Attach(host);
            host.AddComponent<SpawnCameraWatch>();
            host.AddComponent<RenderWatch>();
        }

        static void StopPlay(int exitCode)
        {
            s_ExitCode = exitCode;
            if (s_QuitAfter || Scripted) EditorApplication.isPlaying = false;
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
            public string Jump;
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

            /// <summary>The scripted run stops itself after this (stops Play, reports what it has), before the batch watchdog's 300 s.</summary>
            public const float HardCapSeconds = 270f;
            float m_RunStarted;

            bool OverTheCap()
            {
                if (Time.realtimeSinceStartup - m_RunStarted < HardCapSeconds) return false;
                Log($"HARD CAP: the scripted run passed {HardCapSeconds:F0} s; stopping with what it has");
                return true;
            }

            IEnumerator Start()
            {
                m_RunStarted = Time.realtimeSinceStartup;
                float started = Time.realtimeSinceStartup;
                while (OwnBody == null || !TryServer(out _, out _))
                {
                    if (Time.realtimeSinceStartup - started > 60f) { Log("the tester never entered within 60 s"); Finish(false); yield break; }
                    yield return null;
                }
                TryServer(out GameObject body, out Entity ghost);
                s_OwnBody = KryticalNova.Core.LocalPlayer.Object;
                var skills = body.GetComponent<PlayerMovementSkills>();
                if (skills != null) skills.GmSetMaxRanks();
                Log($"the tester entered after {Time.realtimeSinceStartup - started:F1} s; Run rank {(skills != null ? skills.RunRank : 0)}, Flux {Flux(body)}");
                // the Netcode for GameObjects side must be off (owner's feel test 30 Oct: its host took the clicks and the camera)
                var ngo = Unity.Netcode.NetworkManager.Singleton;
                bool ngoUp = ngo != null && ngo.IsListening;
                var role = OwnBody.GetComponent<KryticalNova.Core.BodyRoleTag>();
                Log($"Netcode for GameObjects {(ngoUp ? "IS RUNNING (wrong)" : "off")}; the local player is '{OwnBody.name}' ({(role != null ? role.Kind.ToString() : "no role")})");
                if (ngoUp) Debug.LogError("[KnPlayHere] Netcode for GameObjects is running beside the entity netcode.");
                Log("scripted parts: " + ScriptedParts());
                if (HasScriptedTries)
                {
                    // The camera's first move onto the body that just entered is the spawn, not a click (GUI run 1, 31 Oct: 3.9 m in
                    // one 333 ms warm-up frame with the body still, judged as the first click's step). Let it settle, log it apart.
                    Camera cam = Camera.main;
                    Vector3 camAtEntry = cam != null ? cam.transform.position : Vector3.zero;
                    float settleStart = Time.realtimeSinceStartup;
                    yield return new WaitForSeconds(2f);
                    Log($"the camera settled on the body before the first click: moved {(cam != null ? Vector3.Distance(camAtEntry, cam.transform.position) : 0f):F2} m " +
                        $"in the {Time.realtimeSinceStartup - settleStart:F1} s after entering");
                    // A player clicks once the loading screen has opened, not in the Editor's warm-up (GUI run clicktick-1, 31 Oct: a press
                    // in 779 ms frames reached the server 11 ticks late): wait for the join's door (WorldEntryDoor: body, camera, steady
                    // frames, or its cap), as the loading screen does.
                    float waitStart = Time.realtimeSinceStartup;
                    while (KryticalNova.Networking.JoinFlow.Joining && Time.realtimeSinceStartup - waitStart < 60f) yield return null;
                    var doorSystem = KnBootstrap.ClientWorld?.GetExistingSystemManaged<WorldEntryDoorSystem>();
                    WorldEntryDoor door = doorSystem != null ? doorSystem.Door : null;
                    Log(door == null ? "the join's door: NO door installed (the first click does not wait)"
                        : $"the join's door {(door.IsOpen ? "opened by " + door.OpenedBy : "NEVER opened")} after {door.WaitedSeconds:F1} s with the body and the camera " +
                          $"ready (worst frame meanwhile {door.WorstFrameMs:F0} ms); the click waited {Time.realtimeSinceStartup - waitStart:F1} s more for it");
                }
                if (!HasScriptedTries)
                {
                    // the owner's feel checkpoint (M3c, 2 Nov): every traversal open by hand
                    SetFeelLevel(FeelLevelOpen);
                    Log("no scripted tries: the owner clicks by hand");
                    yield break;
                }

                var points = new List<Vector3>();
                if (!string.IsNullOrEmpty(Walk) && !TryPoints(Walk, points)) { Log(WalkVariable + " does not read as x,z;x,z: " + Walk); Finish(false); yield break; }
                var jumpPoints = new List<Vector3>();
                if (!string.IsNullOrEmpty(Jump) && !TryPoints(Jump, jumpPoints)) { Log(JumpVariable + " does not read as x,z;x,z: " + Jump); Finish(false); yield break; }
                int arrived = 0, tries = 0;
                m_YawFrom = ServerPosition(ghost);
                foreach (bool run in new[] { false, true })
                    foreach (Vector3 flat in points)
                    {
                        if (OverTheCap()) { Finish(false); yield break; }
                        tries++;
                        Vector3 p = flat;
                        p.y = Physics.Raycast(new Vector3(p.x, 500f, p.z), Vector3.down, out RaycastHit hit, 1000f) ? hit.point.y : 0f;
                        float flux = Flux(body);
                        BodyClicks.Ground(p, run);
                        ScreenWatch.ResetWorst();
                        RenderWatch.Reset();
                        if (!m_PlaceBisectStarted && Environment.GetEnvironmentVariable("KN_PLAYHERE_PLACE_BISECT") == "1")
                        {
                            m_PlaceBisectStarted = true;
                            StartCoroutine(FindOrderZeroMover());
                        }
                        if (!m_PlaceBisectStarted && Environment.GetEnvironmentVariable("KN_PLAYHERE_SINGLE_TEST") == "1")
                        {
                            m_PlaceBisectStarted = true;
                            StartCoroutine(SingleSwitchTest());
                        }
                        if (!m_PlaceBisectStarted && Environment.GetEnvironmentVariable("KN_PLAYHERE_PLACE_CONTROL") == "1")
                        {
                            m_PlaceBisectStarted = true;
                            StartCoroutine(PlaceControls());
                        }
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
                        // arrived: the walk ended with the server on the point; a short move can start and end between two looks
                        bool ok = (done || !moving) && off < 1.5f;
                        if (!YawFaces(ghost, p, out string yawReport))
                        {
                            if (m_YawFailures++ == 0) yield return FindOrderZeroWriter();
                        }
                        Log(yawReport + "; " + YawProbes.Line());
                        // run 3k (31 Oct): the LateUpdate screen step was a measuring artifact (its watcher's order did not hold); the
                        // verdict is the render-time one: the camera keeps its offset from the body (within MaxOffsetChangeMm)
                        bool smooth = RenderWatch.WorstOffsetChangeMm <= MaxOffsetChangeMm;
                        if (!smooth) m_JudderFailures++;
                        Log($"at render time over {RenderWatch.Frames} moving frames: camera offset from the body changed on {RenderWatch.Lagging} " +
                            $"(worst {RenderWatch.WorstOffsetChangeMm:F0} mm; 0 = the camera follows the body exactly)");
                        Log($"followers on the screen over {ScreenWatch.Frames} frames: head anchor worst step {ScreenWatch.WorstAnchorPx:F1} px " +
                            $"({ScreenWatch.WorstZoomPx:F1} px with the field-of-view kick), pet worst step {ScreenWatch.WorstPetPx:F1} px " +
                            $"(limit {MaxScreenStepPx} px) -> {(smooth ? "smooth" : "JUDDER")}");
                        if (ok) arrived++;
                        string refusal = KnBootstrap.ServerWorld?.GetExistingSystemManaged<PlayerServerPlanSystem>()?.LastCostRefusal;
                        Log($"{(run ? "run" : "walk")} to {p.x:F1},{p.z:F1}: {(ok ? "arrived" : moving ? "NOT there" : off < 1.5f ? "arrived" : "never started")} after " +
                            $"{Time.realtimeSinceStartup - clicked:F1} s; server {server.x:F2},{server.z:F2} ({off:F2} m off), body " +
                            $"{OwnBody.transform.position.x:F2},{OwnBody.transform.position.z:F2}; Flux {flux:F0} -> {Flux(body):F0}" +
                            (string.IsNullOrEmpty(refusal) ? "" : "; last refusal: " + refusal));
                    }
                int landed = 0;
                foreach (Vector3 flat in jumpPoints)
                {
                    if (OverTheCap()) { Finish(false); yield break; }
                    var outcome = new JumpOutcome();
                    yield return JumpTo(flat, body, ghost, outcome);
                    if (outcome.Ok) landed++;
                }
                // a low rank: the owner's client and the server refuse the same jump with the same words (the clean case is the jumps above)
                var lowRank = new JumpOutcome { Ok = true };
                if (jumpPoints.Count > 0) yield return LowRankRefusals(body, ghost, skills, lowRank);
                var vaults = new VaultTally();
                if (VaultCount > 0) yield return Vaults(ghost, vaults);
                var waters = new VaultTally();
                if (WaterRun) yield return Waters(body, ghost, waters);
                var walls = new VaultTally();
                if (WallRun) yield return Walls(body, ghost, walls);
                if (ClimbRun) yield return Climbs(body, ghost, walls);
                // the catch before the trees: the tree part ends at a terrain tree far from the spawn's walls
                if (CatchRun) yield return Catches(body, ghost, walls);
                if (AutoRun) yield return Autos(body, ghost, walls);
                if (LedgeRun) yield return Ledges(body, ghost, walls);
                if (TreeRun) yield return Climbs(body, ghost, walls, tree: true);
                if (FixesRun) yield return Fixes(body, ghost, walls);
                if (AcrossRun) yield return Acrosses(body, ghost, waters);
                if (ItemsRun) yield return Items(body, ghost, walls);
                Log($"done: {arrived} of {tries} arrived, {landed} of {jumpPoints.Count} jumps passed, low-rank refusals {(lowRank.Ok ? "match" : "DIFFER")}, " +
                    $"{vaults.Passed} of {vaults.Tried} vaults passed, {waters.Passed} of {waters.Tried} Waterstride tries passed, " +
                    $"{walls.Passed} of {walls.Tried} wall walk tries passed, " +
                    $"{m_YawFailures} facing the wrong way, {m_JudderFailures} juddering, {m_Errors} error lines");
                Finish(arrived == tries && landed == jumpPoints.Count && lowRank.Ok && vaults.Passed == vaults.Tried && waters.Passed == waters.Tried
                    && walls.Passed == walls.Tried
                    && m_YawFailures == 0 && m_JudderFailures == 0 && m_Errors == 0);
            }

            /// <summary>
            /// The ONE list of the run's scripted parts (1 Nov: gui-wall-1 stood still because the early exit after the join's door knew only
            /// the walk, the jumps and the vaults, so a run of Waterstride or the wall walk alone never started). Every gate reads this; a new
            /// scripted part is added here, and to <see cref="Scripted"/>, which reads the same variables before the runner exists.
            /// </summary>
            bool HasScriptedTries => !string.IsNullOrEmpty(Walk) || !string.IsNullOrEmpty(Jump) || VaultCount > 0 || WaterRun || WallRun || ClimbRun || TreeRun || CatchRun || AutoRun || LedgeRun || FixesRun || AcrossRun || ItemsRun;

            /// <summary>Which scripted parts are on, for the log's first lines (a part that should run and is not listed shows at once).</summary>
            string ScriptedParts()
            {
                var on = new List<string>();
                if (!string.IsNullOrEmpty(Walk)) on.Add("walk");
                if (!string.IsNullOrEmpty(Jump)) on.Add("jumps");
                if (VaultCount > 0) on.Add($"vaults {VaultCount}");
                if (WaterRun) on.Add("Waterstride");
                if (WallRun) on.Add("wall walk");
                if (ClimbRun) on.Add("straight climb");
                if (TreeRun) on.Add("tree, perch and drop");
                if (CatchRun) on.Add("jumps that catch a wall");
                if (AutoRun) on.Add("the self-starting run");
                if (LedgeRun) on.Add("the route's jumps");
                if (FixesRun) on.Add("the feel-test fixes 1-3");
                if (AcrossRun) on.Add("across the water when quicker");
                if (ItemsRun) on.Add("items used by the server");
                return on.Count > 0 ? string.Join(", ", on) : "none (by hand)";
            }

            sealed class JumpOutcome { public bool Ok; }

            sealed class VaultTally { public int Passed, Tried; }

            sealed class PlanterApproach { public bool Ready; public Vector3 Goal; public string Why; }

            /// <summary>The scripted vault clicks run (env KN_PLAYHERE_VAULT_RUN=1; owner, 31 Oct: look for the slide at running speed).</summary>
            static bool VaultRun => Environment.GetEnvironmentVariable("KN_PLAYHERE_VAULT_RUN") == "1";

            /// <summary>How many of the scripted vaults go over the spawn planters (env KN_PLAYHERE_VAULT_PLANTERS).</summary>
            static int PlanterVaults => int.TryParse(Environment.GetEnvironmentVariable("KN_PLAYHERE_VAULT_PLANTERS"), out int n) ? n : 0;

            /// <summary>
            /// Lines up a vault over the <paramref name="index"/>-th nearest spawn planter: walks to 3.5 m in front of it (from where the
            /// body stands), then checks with the server's own planners that a click 6 m past it vaults.
            /// </summary>
            IEnumerator ApproachPlanter(Entity ghost, int index, PlanterApproach result, bool shortClick = false)
            {
                World server = KnBootstrap.ServerWorld;
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                Vector3 from = ServerPosition(ghost);
                var hedges = new List<Transform>();
                foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (t.name.StartsWith("SM_Env_Hedge", StringComparison.Ordinal)) hedges.Add(t);
                hedges.Sort((a, b) => Flat(a.position - from).CompareTo(Flat(b.position - from)));
                if (index >= hedges.Count) { result.Why = "no planter left"; yield break; }
                Vector3 middle = hedges[index].GetComponentInChildren<Renderer>() is Renderer r ? r.bounds.center : hedges[index].position;
                Vector3 way = middle - from;
                way.y = 0f;
                way = way.sqrMagnitude > 0.01f ? way.normalized : Vector3.forward;
                // the short click (Master, 1 Nov): from about 5 m in front, 1.4 m behind the planter's far edge (its bounds along the way)
                Vector3 extents = hedges[index].GetComponentInChildren<Renderer>() is Renderer rr ? rr.bounds.extents : Vector3.one * 0.5f;
                float halfDepth = Mathf.Abs(extents.x * way.x) + Mathf.Abs(extents.z * way.z);
                float front = shortClick ? 5f : 3.5f, beyond = shortClick ? halfDepth + 1.4f : 6f;
                if (!UnityEngine.AI.NavMesh.SamplePosition(middle - way * front, out UnityEngine.AI.NavMeshHit before, 1f, UnityEngine.AI.NavMesh.AllAreas)
                    || !UnityEngine.AI.NavMesh.SamplePosition(middle + way * beyond, out UnityEngine.AI.NavMeshHit past, 1f, UnityEngine.AI.NavMesh.AllAreas))
                { result.Why = "no NavMesh in front of or past the planter"; yield break; }
                BodyClicks.Ground(before.position, false);
                float clicked = Time.realtimeSinceStartup;
                bool moving = false;
                while (Time.realtimeSinceStartup - clicked < 10f)
                {
                    yield return null;
                    bool now = Moving(ghost);
                    if (now) moving = true;
                    else if (moving || Time.realtimeSinceStartup - clicked > 2f) break;
                }
                yield return new WaitForSeconds(0.4f);
                PlayerPose pose = server.EntityManager.GetComponentData<PlayerPose>(ghost);
                MoveStats stats = server.EntityManager.GetComponentData<MoveStats>(ghost);
                var goalMm = new Unity.Mathematics.int3(Mathf.RoundToInt(past.position.x * 1000f), Mathf.RoundToInt(past.position.y * 1000f), Mathf.RoundToInt(past.position.z * 1000f));
                var corners = new Unity.Collections.NativeList<Unity.Mathematics.int3>(PlayerLimits.MaxCorners, Unity.Collections.Allocator.Temp);
                bool vault = VaultPath.Plan(plans.Planner, plans.VaultPlanner, ghost, pose.Mm, goalMm, VaultRun ? stats.RunMmps : stats.WalkMmps, corners, out VaultPlan v)
                    && v.LegLengthMm > 0;
                corners.Dispose();
                string reason = plans.VaultPlanner is BodyVaultPlanner bp ? bp.LastDecision : "?";
                result.Ready = vault;
                result.Goal = past.position;
                string click = shortClick ? "click 1.4 m behind its far edge" : "click 6 m past";
                result.Why = vault ? $"over spawn planter {index + 1} at {middle.x:F1},{middle.z:F1} (from {front:F1} m in front, {click})"
                    : $"planter {index + 1} at {middle.x:F1},{middle.z:F1}: the server's planner says \"{reason}\" from {front:F1} m in front ({click})";
            }

            /// <summary>How many vaults with the click well past the obstacle (env KN_PLAYHERE_VAULT); one short click behind it follows.</summary>
            static int VaultCount => int.TryParse(Environment.GetEnvironmentVariable("KN_PLAYHERE_VAULT"), out int n) ? n : 0;

            /// <summary>
            /// The scripted vaults (M3c; owner, 31 Oct: watch for the body sliding after the landing): for each, a vault the server's own
            /// planners find on a ring round the body, the click at least 4 m past the landing (the last one only just behind it); logged
            /// with the wall clock so the video can be cut. Pass: planned, owner and server on the same leg, the clip played, the walk ends
            /// on the click, and the drawn body stops with the walk (no glide over 2 cm once the server stopped).
            /// </summary>
            IEnumerator Vaults(Entity ghost, VaultTally tally)
            {
                World server = KnBootstrap.ServerWorld;
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                if (plans == null || plans.Planner == null || plans.VaultPlanner == null) { Log("vault: FAIL; the server has no path or vault planner"); tally.Tried++; yield break; }
                bool hasClient = TryLocalGhost(out World clientWorld, out Entity clientGhost, out _);
                var state = OwnBody.GetComponent<PlayerPresentationState>();
                var anim = OwnBody.GetComponentInChildren<PlayerAnimancerMotor>();
                // the instrument on a known case first: 2 m over the open ground at the spawn reads "straight line is clear"
                if (plans.VaultPlanner is BodyVaultPlanner probe)
                {
                    PlayerPose at0 = server.EntityManager.GetComponentData<PlayerPose>(ghost);
                    var near = new Unity.Mathematics.int3(at0.XMm + 2000, at0.YMm, at0.ZMm);
                    var scratch = new Unity.Collections.NativeList<Unity.Mathematics.int3>(PlayerLimits.MaxCorners, Unity.Collections.Allocator.Temp);
                    VaultPath.Plan(plans.Planner, probe, ghost, at0.Mm, near, 5000, scratch, out _);
                    scratch.Dispose();
                    Log($"vault planner check (2 m over the open ground at the spawn, expected \"straight line is clear\"): \"{probe.LastDecision ?? "(no answer)"}\"");
                    // the planters right at the spawn (owner, 31 Oct: "we built the vaulting for the original game right where he spawns")
                    var crossing = new Unity.Collections.NativeList<Unity.Mathematics.int3>(PlayerLimits.MaxCorners, Unity.Collections.Allocator.Temp);
                    Vector3 spawn = new Vector3(at0.XMm, at0.YMm, at0.ZMm) * 0.001f;
                    foreach (Vector3 goal in HedgeGoals(spawn, 3))
                    {
                        var goalMm = new Unity.Mathematics.int3(Mathf.RoundToInt(goal.x * 1000f), Mathf.RoundToInt(goal.y * 1000f), Mathf.RoundToInt(goal.z * 1000f));
                        bool way = VaultPath.Plan(plans.Planner, probe, ghost, at0.Mm, goalMm, 5000, crossing, out VaultPlan v);
                        Log($"vault planner on a planter: walk to {goal.x:F1},{goal.z:F1} across it ({Flat(goal - spawn):F1} m): " +
                            $"{(v.LegLengthMm > 0 ? $"VAULT (leg {v.LegLengthMm} mm, rise {v.RaiseMm} mm)" : "no vault")}, way {(way ? "found" : "NONE")}; planner: \"{probe.LastDecision ?? "(not asked)"}\"");
                    }
                    crossing.Dispose();
                }
                int total = VaultCount + 1;
                for (int n = 0; n < total; n++)
                {
                    if (OverTheCap()) yield break;
                    bool shortClick = n == total - 1;
                    tally.Tried++;
                    Vector3 goal;
                    string found;
                    if (shortClick && PlanterVaults > 0)
                    {
                        // the short click from planter 1 directly, never the finder (Master, 1 Nov: the finder missed it twice)
                        var approach = new PlanterApproach();
                        yield return ApproachPlanter(ghost, 0, approach, shortClick: true);
                        if (!approach.Ready) { Log($"vault {n + 1}: NOT TESTED; {approach.Why}"); continue; }
                        goal = approach.Goal;
                        found = approach.Why;
                    }
                    else if (n < PlanterVaults && !shortClick)
                    {
                        // over a spawn planter (owner, 31 Oct): walk to 3.5 m in front of it first, then click 6 m past it
                        var approach = new PlanterApproach();
                        yield return ApproachPlanter(ghost, n, approach);
                        if (!approach.Ready) { Log($"vault {n + 1}: FAIL; {approach.Why}"); continue; }
                        goal = approach.Goal;
                        found = approach.Why;
                    }
                    else if (!FindVault(plans, ghost, shortClick, out goal, out found))
                    { Log($"vault {n + 1}: FAIL; {found}"); continue; }
                    int vaults0 = plans.Vaults, clip0 = state != null ? state.VaultClipFrame : 0;
                    string at = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    Vector3 bodyStart = OwnBody.transform.position;
                    BodyClicks.Ground(goal, VaultRun);
                    float clicked = Time.realtimeSinceStartup, stoppedAt = -1f, glide = 0f, topBody = float.MinValue;
                    Vector3 bodyAtStop = default;
                    bool moving = false, wasInVault = false;
                    string afterLanding = null;
                    while (Time.realtimeSinceStartup - clicked < 12f)
                    {
                        yield return new WaitForEndOfFrame();
                        bool now = Moving(ghost);
                        if (now) moving = true;
                        Vector3 b = OwnBody.transform.position;
                        topBody = Mathf.Max(topBody, b.y);
                        if (state != null)
                        {
                            if (wasInVault && !state.InVault && afterLanding == null && anim != null) afterLanding = anim.CurrentState.ToString();
                            wasInVault |= state.InVault;
                        }
                        if (moving && !now)
                        {
                            if (stoppedAt < 0f) { stoppedAt = Time.realtimeSinceStartup; bodyAtStop = b; }
                            else
                            {
                                glide = Mathf.Max(glide, Flat(b - bodyAtStop));
                                if (Time.realtimeSinceStartup - stoppedAt > 0.6f) break;
                            }
                        }
                    }
                    VaultPlan v = server.EntityManager.GetComponentData<VaultPlan>(ghost);
                    VaultPlan own = hasClient && clientWorld.IsCreated ? clientWorld.EntityManager.GetComponentData<ClientVaultPlan>(clientGhost).Plan : default;
                    bool planned = plans.Vaults == vaults0 + 1;
                    bool sameLeg = own.Seq == v.Seq && own.LegStartMm == v.LegStartMm && own.LegLengthMm == v.LegLengthMm;
                    bool clip = state != null && state.VaultClipFrame != clip0;
                    float off = Flat(ServerPosition(ghost) - goal);
                    bool ok = planned && sameLeg && clip && wasInVault && off < 1.5f && glide < 0.02f;
                    if (ok) tally.Passed++;
                    Log($"vault {n + 1} ({(shortClick ? "short click just behind" : "click past")}) clicked at {at}: {(ok ? "PASS" : "FAIL")}; {found}; " +
                        $"server {(planned ? "planned the vault" : "planned NO vault")}, owner {(sameLeg ? "on the same leg" : "on ANOTHER leg")} (start {v.LegStartMm} mm, " +
                        $"{v.LegLengthMm} mm, air {v.AirMicros / 1000} ms, rise {v.RaiseMm} mm, style {v.Style}); clip {(clip ? "played" : "NOT played")} " +
                        $"(frame {(state != null ? state.VaultClipFrame : 0)}); body rose {(topBody - bodyStart.y) * 100f:F0} cm; after the landing: {afterLanding ?? "never landed"}; " +
                        $"ended {off:F2} m from the click; glide after the stop {glide * 100f:F1} cm (limit 2)");
                    yield return new WaitForSeconds(0.8f);
                }
            }

            /// <summary>The scripted Waterstride run (env KN_PLAYHERE_WATER=1; M3c, `m3c-waterstride-design.md` section 4).</summary>
            static bool WaterRun => Environment.GetEnvironmentVariable("KN_PLAYHERE_WATER") == "1";

            /// <summary>Zone01's river north of the spawn (its water map: z 193-219 m, a bridge at x -6..6): the south and north banks used.</summary>
            static readonly Vector3 SouthBank = new Vector3(20f, 0f, 188f), NorthBank = new Vector3(30f, 0f, 228f);

            /// <summary>
            /// The scripted Waterstride (M3c): level 60 and the top ranks for the tester, put on the river's south bank, then six tries, each
            /// logged with the wall clock for the video and judged: a run out over the water to a click on it (stands there), a water click
            /// past it, a land click across the river past the shore (ashore and on), a run out again with the Flux run down (carried back
            /// to the shore), a water click without Run from land (not Waterstride's: NGO walks it), and a jump onto the water (stands).
            /// The instrument first: the server's planner on a known refusal (level 1) and a known run (level 60) before any click.
            /// </summary>
            IEnumerator Waters(GameObject body, Entity ghost, VaultTally tally)
            {
                World server = KnBootstrap.ServerWorld;
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                var fluxSystem = server.GetExistingSystemManaged<WaterstrideFluxSystem>();
                var landing = server.GetExistingSystemManaged<WaterstrideLandingSystem>();
                var planner = plans != null ? plans.WaterPlanner as BodyWaterPlanner : null;
                if (planner == null || fluxSystem == null || landing == null) { Log("water: FAIL; the server has no Waterstride planner or systems"); tally.Tried++; yield break; }
                var em = server.EntityManager;
                var progression = body.GetComponent<PlayerCharacterProgression>();
                var resources = body.GetComponent<PlayerResources>();
                float level = 0f;
                float offset = body.GetComponent<UnityEngine.AI.NavMeshAgent>() is UnityEngine.AI.NavMeshAgent ag ? ag.baseOffset : 0f;

                // to the south bank (a teleport, as gm.travel does)
                if (!UnityEngine.AI.NavMesh.SamplePosition(SouthBank, out UnityEngine.AI.NavMeshHit south, 6f, UnityEngine.AI.NavMesh.AllAreas)
                    || !UnityEngine.AI.NavMesh.SamplePosition(NorthBank, out UnityEngine.AI.NavMeshHit north, 6f, UnityEngine.AI.NavMesh.AllAreas))
                { Log("water: FAIL; no NavMesh on the river's banks"); tally.Tried++; yield break; }
                Teleport(em, ghost, south.position + Vector3.up * offset);
                yield return new WaitForSeconds(1.5f);

                // the instrument on a known refusal and a known run (no click): level 1 is not Waterstride's, level 60 runs out
                Vector3 water1 = new Vector3(south.position.x, -1.2f, 206f);
                string Probe()
                {
                    var corners = new Unity.Collections.NativeList<Unity.Mathematics.int3>(PlayerLimits.MaxCorners, Unity.Collections.Allocator.Temp);
                    PlayerPose pose = em.GetComponentData<PlayerPose>(ghost);
                    var answer = planner.Plan(ghost, pose.Mm, Mm(water1), true, false, false, corners, out _, out _);
                    corners.Dispose();
                    return answer + " (\"" + planner.LastDecision + "\")";
                }
                if (progression != null) level = progression.CurrentLevel;
                Log($"water planner check at level {level:F0} (expected NotWater, the gate): {Probe()}");
                if (progression != null && progression.CurrentLevel < 60) progression.SetLevel(60);
                yield return new WaitForSeconds(1f);   // the level reaches the ghost (MoveRanks) and the owner
                Log($"water planner check at level {(progression != null ? progression.CurrentLevel : 0)} (expected Planned, out over the water): {Probe()}");

                // the six tries
                yield return WaterTry(body, ghost, tally, "run out over the water", water1, run: true, jump: false, expectWater: true, expectRun: true);
                yield return WaterTry(body, ghost, tally, "water to water, clicked past", new Vector3(south.position.x + 14f, -1.2f, 213f), true, false, true, true);
                yield return WaterTry(body, ghost, tally, "ashore across the river, clicked past the shore", north.position, true, false, false, true);
                // a run out from the north bank, then the Flux run down: carried back to the shore
                int strand0 = fluxSystem.Strandings;
                yield return WaterTry(body, ghost, tally, "run out from the north bank", new Vector3(north.position.x - 4f, -1.2f, 210f), true, false, true, true);
                if (resources != null && resources.Chi > 2f) resources.TrySpendChi(resources.Chi - 2f);
                float drained = Time.realtimeSinceStartup;
                while (fluxSystem.Strandings == strand0 && Time.realtimeSinceStartup - drained < 6f) yield return null;
                float strandedAt = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - strandedAt < 12f && (Moving(ghost) || em.GetComponentData<MoveState>(ghost).Water != 0)) yield return null;
                tally.Tried++;
                bool back = fluxSystem.Strandings == strand0 + 1 && em.GetComponentData<MoveState>(ghost).Water == 0
                    && UnityEngine.AI.NavMesh.SamplePosition(ServerPosition(ghost) - Vector3.up * offset, out _, 0.5f, UnityEngine.AI.NavMesh.AllAreas);
                if (back) tally.Passed++;
                Log($"water: out of Flux at {DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}: {(back ? "PASS" : "FAIL")}; stranded " +
                    $"{fluxSystem.Strandings - strand0}x, back ashore at {ServerPosition(ghost).x:F1},{ServerPosition(ghost).z:F1} after {Time.realtimeSinceStartup - strandedAt:F1} s; Flux {Flux(body):F0}");
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                yield return new WaitForSeconds(0.8f);
                yield return WaterTry(body, ghost, tally, "a water click without Run (not Waterstride's: walked as NGO)", new Vector3(north.position.x + 6f, -1.2f, 212f), false, false, false, false);
                Vector3 jumpSpot;
                string jumpWhy = JumpOntoWaterSpot(ghost, out jumpSpot);
                if (jumpWhy != null) { tally.Tried++; Log($"water: a jump onto the water: NOT TESTED; {jumpWhy}"); }
                else yield return WaterTry(body, ghost, tally, "a jump onto the water (stands)", jumpSpot, false, true, true, false);
                Log($"water: Flux paid {fluxSystem.Paid} points in all, {landing.Stood} jump(s) stood on the water, {landing.FellIn} fell in");
            }

            /// <summary>
            /// One scripted Waterstride click, logged and judged: the server's answer (a Waterstride run or not), the owner on the same leg,
            /// where it ended (within 0.3 m of the click), on the water or not as expected, and no glide over 2 cm after the stop.
            /// </summary>
            IEnumerator WaterTry(GameObject body, Entity ghost, VaultTally tally, string what, Vector3 click, bool run, bool jump, bool expectWater, bool expectRun)
            {
                if (OverTheCap()) yield break;
                World server = KnBootstrap.ServerWorld;
                var em = server.EntityManager;
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                var landing = server.GetExistingSystemManaged<WaterstrideLandingSystem>();
                bool hasClient = TryLocalGhost(out World clientWorld, out Entity clientGhost, out _);
                tally.Tried++;
                int runs0 = plans.WaterRuns, stood0 = landing.Stood;
                float flux0 = Flux(body);
                string at = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                if (jump) BodyClicks.Jump(click);
                else BodyClicks.Ground(click, run);
                float clicked = Time.realtimeSinceStartup, stoppedAt = -1f, glide = 0f;
                Vector3 bodyAtStop = default;
                bool moving = false;
                while (Time.realtimeSinceStartup - clicked < 15f)
                {
                    yield return null;   // not WaitForEndOfFrame: it never resumes in a batch Editor
                    bool now = Moving(ghost) || (jump && Time.realtimeSinceStartup - clicked < 2f);
                    if (now) moving = true;
                    else if (!moving && Time.realtimeSinceStartup - clicked > 3f) break;   // never started (refused, or no way)
                    Vector3 b = OwnBody.transform.position;
                    if (moving && !now)
                    {
                        if (stoppedAt < 0f) { stoppedAt = Time.realtimeSinceStartup; bodyAtStop = b; }
                        else
                        {
                            glide = Mathf.Max(glide, Flat(b - bodyAtStop));
                            if (Time.realtimeSinceStartup - stoppedAt > 0.8f) break;
                        }
                    }
                }
                var move = em.GetComponentData<MoveState>(ghost);
                WaterPlan leg = em.GetComponentData<WaterPlan>(ghost);
                WaterPlan own = hasClient && clientWorld.IsCreated ? clientWorld.EntityManager.GetComponentData<ClientWaterPlan>(clientGhost).Plan : default;
                byte ownWater = hasClient && clientWorld.IsCreated ? clientWorld.EntityManager.GetComponentData<MoveState>(clientGhost).Water : (byte)255;
                bool planned = plans.WaterRuns == runs0 + 1;
                bool sameLeg = !expectRun || (own.Seq == leg.Seq && own.LegStartMm == leg.LegStartMm);
                Vector3 end = ServerPosition(ghost);
                float off = Flat(end - click);
                bool wet = move.Water != 0;
                bool ok = (jump ? landing.Stood == stood0 + 1 : planned == expectRun) && sameLeg && wet == expectWater && ownWater == move.Water
                    && glide < 0.02f && (!expectRun && !jump || off < (expectWater ? 0.75f : 0.3f));   // on the water: the clicked cell's centre (NGO)
                if (ok) tally.Passed++;
                Log($"water: {what} clicked at {at}: {(ok ? "PASS" : "FAIL")}; server {(planned ? "planned a Waterstride run" : "planned no Waterstride run")} " +
                    $"(\"{(plans.WaterPlanner as BodyWaterPlanner)?.LastDecision}\"{(string.IsNullOrEmpty(plans.LastWaterRefusal) ? "" : "; last refusal: " + plans.LastWaterRefusal)}), " +
                    $"owner {(sameLeg ? "on the same leg" : "on ANOTHER leg")} (start {leg.LegStartMm} mm, speed {leg.SpeedMmps} mm/s); on the water: server {move.Water}, owner {ownWater} " +
                    $"(expected {(expectWater ? 1 : 0)}); ended {off:F2} m from the click (limit {(expectWater ? "0.75, a cell's centre" : "0.3")}) at {end.x:F1},{end.y:F2},{end.z:F1}; glide after the stop {glide * 100f:F1} cm (limit 2); " +
                    $"Flux {flux0:F0} -> {Flux(body):F0}");
                yield return new WaitForSeconds(0.8f);
            }

            /// <summary>Across the water when quicker (env KN_PLAYHERE_ACROSS=1; owner change, feel test 2 Nov item 4).</summary>
            static bool AcrossRun => Environment.GetEnvironmentVariable("KN_PLAYHERE_ACROSS") == "1";

            /// <summary>Zone01's bridge over the river (x -6..6): its south and north ends, where the walk over it is as quick as across.</summary>
            static readonly Vector3 BridgeSouth = new Vector3(0f, 0f, 187f), BridgeNorth = new Vector3(0f, 0f, 225f);

            /// <summary>
            /// Across the water (item 4, owner change: the quickest of walk, vault and Waterstride across, Run on or off): from the river's
            /// south bank a click on the north bank runs across (Run on, then walking), the bridge's own crossing is walked (as quick). The
            /// instrument first: the server's planner asked the same clicks before any click, with its reason.
            /// </summary>
            IEnumerator Acrosses(GameObject body, Entity ghost, VaultTally tally)
            {
                World server = KnBootstrap.ServerWorld;
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                var planner = plans != null ? plans.WaterPlanner as BodyWaterPlanner : null;
                var skills = OwnBody.GetComponent<PlayerMovementSkills>();
                if (planner == null || skills == null) { Log("across: FAIL; the server has no Waterstride planner or the body no skills"); tally.Tried++; yield break; }
                var em = server.EntityManager;
                var progression = body.GetComponent<PlayerCharacterProgression>();
                var resources = body.GetComponent<PlayerResources>();
                float offset = body.GetComponent<UnityEngine.AI.NavMeshAgent>() is UnityEngine.AI.NavMeshAgent ag ? ag.baseOffset : 0f;
                if (progression != null && progression.CurrentLevel < 60) progression.SetLevel(60);
                if (!UnityEngine.AI.NavMesh.SamplePosition(SouthBank, out UnityEngine.AI.NavMeshHit south, 6f, UnityEngine.AI.NavMesh.AllAreas)
                    || !UnityEngine.AI.NavMesh.SamplePosition(NorthBank, out UnityEngine.AI.NavMeshHit north, 6f, UnityEngine.AI.NavMesh.AllAreas))
                { Log("across: FAIL; no NavMesh on the river's banks"); tally.Tried++; yield break; }
                bool bridge = UnityEngine.AI.NavMesh.SamplePosition(BridgeSouth, out UnityEngine.AI.NavMeshHit bridgeSouth, 6f, UnityEngine.AI.NavMesh.AllAreas)
                    & UnityEngine.AI.NavMesh.SamplePosition(BridgeNorth, out UnityEngine.AI.NavMeshHit bridgeNorth, 6f, UnityEngine.AI.NavMesh.AllAreas);
                void Run(bool on) { if (on) { if (!skills.IsRunning) skills.ToggleSelection(MovementSkillId.Run); } else skills.SelectWalk(); }
                string Probe(Vector3 to, bool run)
                {
                    var corners = new Unity.Collections.NativeList<Unity.Mathematics.int3>(PlayerLimits.MaxCorners, Unity.Collections.Allocator.Temp);
                    PlayerPose pose = em.GetComponentData<PlayerPose>(ghost);
                    var answer = planner.Plan(ghost, pose.Mm, Mm(to), run, false, false, corners, out _, out _);
                    corners.Dispose();
                    return answer + " (\"" + planner.LastDecision + "\")";
                }

                Teleport(em, ghost, south.position + Vector3.up * offset);
                yield return new WaitForSeconds(1.5f);
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                Log($"across planner check, south bank {south.position} to north bank {north.position}, Run on (expected Planned, across): {Probe(north.position, true)}");
                Log($"across planner check, the same, Run off (expected Planned, across walking): {Probe(north.position, false)}");
                Run(true);
                yield return WaterTry(body, ghost, tally, "across the river, Run on", north.position, true, false, false, true);

                Teleport(em, ghost, south.position + Vector3.up * offset);
                yield return new WaitForSeconds(1.5f);
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                Run(false);
                yield return WaterTry(body, ghost, tally, "across the river, Run off (walking)", north.position, false, false, false, true);

                if (bridge)
                {
                    Teleport(em, ghost, bridgeSouth.position + Vector3.up * offset);
                    yield return new WaitForSeconds(1.5f);
                    if (resources != null) resources.RestoreChi(resources.MaxChi);
                    Log($"across planner check, the bridge {bridgeSouth.position} to {bridgeNorth.position}, Run on (expected NotWater, the bridge is as quick): {Probe(bridgeNorth.position, true)}");
                    Run(true);
                    yield return WaterTry(body, ghost, tally, "over the bridge (as quick: walked, not across)", bridgeNorth.position, true, false, false, false);
                }
                else { tally.Tried++; Log("across: the bridge control: NOT TESTED; no NavMesh at its ends"); }
                skills.SelectWalk();
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                yield return FenceRow(body, ghost, tally);
            }

            /// <summary>
            /// Vaults in a row (owner, 2 Nov, item 4): two spawn planters in line, found by the server's own planners (a vault from in front
            /// of the first, and another from its landing; the place is logged, so a GUI run repeats a place the batch run proved), then one
            /// click past the second: both vaulted (the second planned at the first one's landing), ending on the click.
            /// </summary>
            IEnumerator FenceRow(GameObject body, Entity ghost, VaultTally tally)
            {
                World server = KnBootstrap.ServerWorld;
                var em = server.EntityManager;
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                MoveStats stats = em.GetComponentData<MoveStats>(ghost);
                float offset = body.GetComponent<UnityEngine.AI.NavMeshAgent>() is UnityEngine.AI.NavMeshAgent ag ? ag.baseOffset : 0f;
                var hedges = new List<Transform>();
                foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (t.name.StartsWith("SM_Env_Hedge", StringComparison.Ordinal)) hedges.Add(t);
                hedges.Sort((a, b) => string.CompareOrdinal(a.name + a.position, b.name + b.position));   // the same order every run
                Vector3 Middle(Transform t) => t.GetComponentInChildren<Renderer>() is Renderer r ? r.bounds.center : t.position;
                var corners = new Unity.Collections.NativeList<Unity.Mathematics.int3>(PlayerLimits.MaxCorners, Unity.Collections.Allocator.Temp);
                bool Vault(Vector3 from, Vector3 to, out Vector3 landing)
                {
                    landing = from;
                    if (!VaultPath.Plan(plans.Planner, plans.VaultPlanner, ghost, Mm(from + Vector3.up * offset), Mm(to + Vector3.up * offset), stats.RunMmps, corners, out VaultPlan v)
                        || v.LegLengthMm <= 0) return false;
                    int along = 0;
                    for (int i = 1; i < corners.Length; i++)
                    {
                        along += PlayerPath.DistanceMm(corners[i - 1], corners[i]);
                        if (along > v.LegStartMm + 1) { landing = new Vector3(corners[i].x, corners[i].y, corners[i].z) * 0.001f - Vector3.up * offset; return true; }
                    }
                    return false;
                }
                Vector3 front = default, past = default;
                string found = null;
                int asked = 0;
                for (int i = 0; i < hedges.Count && found == null && asked < 400; i++)
                for (int j = 0; j < hedges.Count && found == null && asked < 400; j++)
                {
                    if (i == j) continue;
                    Vector3 a = Middle(hedges[i]), b = Middle(hedges[j]), way = b - a;
                    way.y = 0f;
                    if (way.magnitude < 4f || way.magnitude > 14f) continue;
                    way.Normalize();
                    asked++;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(a - way * 3.5f, out UnityEngine.AI.NavMeshHit f, 1f, UnityEngine.AI.NavMesh.AllAreas)
                        || !UnityEngine.AI.NavMesh.SamplePosition(b + way * 3f, out UnityEngine.AI.NavMeshHit p, 1f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                    if (!Vault(f.position, p.position, out Vector3 landed) || !Vault(landed, p.position, out _)) continue;
                    front = f.position; past = p.position;
                    found = $"{hedges[i].name} at {a} then {hedges[j].name} at {b}";
                }
                corners.Dispose();
                tally.Tried++;
                if (found == null) { Log($"across: vaults in a row: NOT TESTED; no two planters in line found ({asked} pairs asked)"); yield break; }
                Log($"across: vaults in a row: the place: {found}; from {front} to {past} ({asked} pairs asked)");
                var skills = OwnBody.GetComponent<PlayerMovementSkills>();
                if (skills != null && !skills.IsRunning) skills.ToggleSelection(MovementSkillId.Run);
                Teleport(em, ghost, front + Vector3.up * offset);
                yield return new WaitForSeconds(1.5f);
                int vaults0 = plans.Vaults, chained0 = plans.ChainedVaults;
                string at = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                BodyClicks.Ground(past, true);
                float clicked = Time.realtimeSinceStartup;
                bool moving = false;
                while (Time.realtimeSinceStartup - clicked < 12f)
                {
                    yield return null;
                    bool now = Moving(ghost);
                    if (now) moving = true;
                    else if (moving || Time.realtimeSinceStartup - clicked > 3f) break;
                }
                float off = Flat(ServerPosition(ghost) - Vector3.up * offset - past);
                bool ok = plans.Vaults - vaults0 == 2 && plans.ChainedVaults - chained0 == 1 && off < 0.3f;
                if (ok) tally.Passed++;
                Log($"across: vaults in a row clicked at {at}: {(ok ? "PASS" : "FAIL")}; {plans.Vaults - vaults0} vault(s) planned, " +
                    $"{plans.ChainedVaults - chained0} from a landing; ended {off:F2} m from the click");
                skills?.SelectWalk();
            }

            /// <summary>The scripted straight climb run (env KN_PLAYHERE_CLIMB=1; M3c part 2, `m3c-wallstride-design.md` section 4).</summary>
            static bool ClimbRun => Environment.GetEnvironmentVariable("KN_PLAYHERE_CLIMB") == "1";

            /// <summary>The scripted tree run (env KN_PLAYHERE_TREE=1; M3c part 3: the climb ends perched, the drop by a click below, by a Jump click, too far).</summary>
            static bool TreeRun => Environment.GetEnvironmentVariable("KN_PLAYHERE_TREE") == "1";

            /// <summary>
            /// The scripted straight climb (part 2): level 60 and the top ranks, a face near the spawn that NGO's climb takes and the graph does
            /// not cover, then: the pop check proved in this run (standing still and a plain run at full speed are clean, the teleport is a
            /// fault), a climb with Run from 6-8 m out (onto the top, 12 Flux, the foot height as the body passes 45 degrees in the plant), and
            /// the same climb with too little Flux (NGO: stopped at the foot).
            /// </summary>
            IEnumerator Climbs(GameObject body, Entity ghost, VaultTally tally, bool tree = false)
            {
                World server = KnBootstrap.ServerWorld;
                var em = server.EntityManager;
                var grip = server.GetExistingSystemManaged<WallGripSystem>();
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                var motor = body.GetComponent<PlayerWallRunMotor>();
                var progression = body.GetComponent<PlayerCharacterProgression>();
                var resources = body.GetComponent<PlayerResources>();
                if (grip == null || motor == null || plans == null) { Log("climb: FAIL; no Grip system, wall motor or plan system"); tally.Tried++; yield break; }
                float offset = body.GetComponent<UnityEngine.AI.NavMeshAgent>() is UnityEngine.AI.NavMeshAgent ag ? ag.baseOffset : 0f;
                if (progression != null && progression.CurrentLevel < 60) progression.SetLevel(60);
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                yield return new WaitForSeconds(1f);
                var ask = new PlayerWallRunMotor.GhostWallAsk { Open = true, Alive = true, Run = true, Flux = 999f, HasFluxForOne = true,
                    Up = Vector3.up, Forward = Vector3.forward, GroundSpeed = em.GetComponentData<MoveStats>(ghost).RunMmps * 0.001f };
                if (tree)
                {
                    // the instrument first (Master, 1 Nov): placed trees marked as trees and terrain tree colliders live near the bodies
                    yield return new WaitForSeconds(0.6f);
                    Log($"tree: trees marked {TreeProxyField.PlacedTreesMarked}, proxies live {TreeProxyField.LiveTotal}; Zone01's placed tree " +
                        $"SM_Env_Tree_01 at 12.6,-13.1 reads {motor.ClimbKindAt(new Vector3(12.6f, 1.2f, -13.1f), Vector3.back)} (expected Tree); " +
                        $"{TreeProxyField.AnchorReport()}");
                }
                // a tree by the ray aimed at Zone01's placed tree (gui-catch-1: the 45-degree rays found it in gui-tree-2..4, then not at all)
                string why = tree ? motor.FindTestTreeClimb(PlacedTree, ask, out Vector3 stand, out Vector3 point, out Vector3 normal)
                    : motor.FindTestClimb(ServerPosition(ghost) - Vector3.up * offset, ask, out stand, out point, out normal, false);
                if (why != null) { Log("climb: NOT TESTED; " + why); tally.Tried++; yield break; }
                Log($"climb: the test face at {point.x:F1},{point.y:F2},{point.z:F1} facing {normal.x:F2},{normal.z:F2} (kind {motor.ClimbKindAt(point, normal)}); " +
                    $"the start {stand.x:F1},{stand.z:F1}");

                // the pop check proved in this run (owner pick a): standing still, the teleport to the start, a plain run at full speed
                m_Pops.Reset();
                float watch = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - watch < 1f) { yield return new WaitForEndOfFrame(); PopFrame(ghost); }
                Log($"climb: pop check on a known clean case (standing 1 s): {m_Pops.Pops} pop(s) (expected 0) {m_Pops.Worst}");
                m_Pops.Reset();
                PopFrame(ghost);
                Teleport(em, ghost, stand + Vector3.up * offset);
                watch = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - watch < 1.5f) { yield return new WaitForEndOfFrame(); PopFrame(ghost); }
                Log($"climb: pop check on a known fault (the teleport to the start): {m_Pops.Pops} pop(s) (expected 1 or more): {m_Pops.Worst}");
                Vector3 side = Vector3.Cross(Vector3.up, normal).normalized;
                if (UnityEngine.AI.NavMesh.SamplePosition(stand + side * 15f, out UnityEngine.AI.NavMeshHit street, 3f, UnityEngine.AI.NavMesh.AllAreas)
                    || UnityEngine.AI.NavMesh.SamplePosition(stand - side * 15f, out street, 3f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    m_Pops.Reset();
                    BodyClicks.Ground(street.position, true);
                    float ran = Time.realtimeSinceStartup;
                    int top = 0;
                    bool started = false;
                    while (Time.realtimeSinceStartup - ran < 6f)
                    {
                        yield return new WaitForEndOfFrame();
                        PopFrame(ghost);
                        top = Mathf.Max(top, em.GetComponentData<MoveState>(ghost).SpeedMmps);
                        if (Moving(ghost)) started = true;
                        else if (started) break;
                    }
                    Log($"climb: pop check on a known clean case (a plain run of {Flat(street.position - stand):F1} m at {top} mm/s): {m_Pops.Pops} pop(s) (expected 0) {m_Pops.Worst}");
                    yield return new WaitForSeconds(0.5f);
                    BodyClicks.Ground(stand, true);
                    yield return new WaitForSeconds(2f);
                }
                else Log("climb: pop check on a plain run: NOT TESTED (no NavMesh 15 m to the side)");

                if (tree)
                {
                    // part 3: up the tree to the perch, then down by a click below (Run), up again and down by a Jump click (NGO: the same drop),
                    // up again and a click too far (refused in NGO's words, still perched)
                    Vector3 below = stand + (point - stand).normalized * 0f;   // the start, 6-8 m from the trunk
                    yield return TreeTry(body, ghost, tally, "up the tree with Run (ends perched, crouched)", point, normal, stand, offset, false, false, TreeEnd.Perched);
                    yield return TreeTry(body, ghost, tally, "down by a click on the ground below", below, Vector3.up, stand, offset, false, false, TreeEnd.Down);
                    yield return TreeTry(body, ghost, tally, "up the tree again (Run)", point, normal, stand, offset, false, false, TreeEnd.Perched);
                    yield return TreeTry(body, ghost, tally, "down by a Jump click (NGO: the same drop)", below, Vector3.up, stand, offset, true, false, TreeEnd.Down);
                    yield return TreeTry(body, ghost, tally, "up the tree again (Run)", point, normal, stand, offset, false, false, TreeEnd.Perched);
                    if (FarGround(point, stand, out Vector3 far, out string farWhy))
                        yield return TreeTry(body, ghost, tally, "a click 30 m off (NGO: too far, still perched)", far, Vector3.up, stand, offset, false, true, TreeEnd.Perched);
                    else { tally.Tried++; Log("tree: a click 30 m off: NOT TESTED; " + farWhy); }
                    yield return TreeTry(body, ghost, tally, "down by a click below to finish", below, Vector3.up, stand, offset, false, false, TreeEnd.Down);

                    // a terrain tree (its collider is a proxy the field puts near the bodies; the nearest is ~180 m from the start in Zone01): the
                    // instrument at the bodies' new place first, then up, too far and down. At least 6 m tall: gui-tree-3's nearest (4.5 m) is one
                    // NGO refuses ("There is no room at the top.")
                    // the planned terrain tree, out in the open (owner, 1 Nov; proved in edit mode: NGO's Decide accepts it)
                    if (!TreeProxyField.NearestTree(TestTerrainTree, 6f, out Vector3 trunk, out float trunkHeight))
                    {
                        tally.Tried++;
                        Log($"tree: terrain tree: NOT TESTED; no terrain tree in a field ({TreeProxyField.AnchorReport()})");
                        yield break;
                    }
                    Vector3 terrainStand = default;
                    bool placed = false;
                    for (int k = 0; k < 16 && !placed; k++)
                    {
                        Vector3 dir = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
                        if (UnityEngine.AI.NavMesh.SamplePosition(trunk + dir * 7f, out UnityEngine.AI.NavMeshHit near7, 2f, UnityEngine.AI.NavMesh.AllAreas)
                            && Flat(near7.position - trunk) >= 5f)
                        { terrainStand = near7.position; placed = true; }
                    }
                    if (!placed)
                    {
                        tally.Tried++;
                        Log($"tree: terrain tree at {trunk.x:F1},{trunk.z:F1} ({Flat(trunk - stand):F0} m away): NOT TESTED; no NavMesh 5-9 m from its trunk");
                        yield break;
                    }
                    int liveBefore = TreeProxyField.LiveTotal;
                    Teleport(em, ghost, terrainStand + Vector3.up * offset);
                    yield return new WaitForSeconds(1.5f);   // the field refreshes four times a second
                    Log($"tree: terrain tree {trunkHeight:F1} m tall at {trunk.x:F1},{trunk.z:F1} ({Flat(trunk - stand):F0} m from the placed tree's start); " +
                        $"proxies live {liveBefore} at the start, {TreeProxyField.LiveTotal} here (expected more than 0); {TreeProxyField.AnchorReport()}");
                    string terrainWhy = motor.FindTestTreeClimb(trunk, ask, out Vector3 tStand, out Vector3 tPoint, out Vector3 tNormal);
                    if (terrainWhy != null)
                    {
                        tally.Tried++;
                        Log("tree: terrain tree: NOT TESTED; " + terrainWhy);
                        // the instrument (1 Nov): in edit mode NGO's Decide accepts this tree; in Play it said "There is no room at the top.": what
                        // overlaps the perch here, in Play (NGO's BodyBlocked: a body capsule at the perch, the tree's own proxy left out)
                        ClimbableSurface proxy = null;
                        foreach (Collider c in Physics.OverlapSphere(trunk + Vector3.up, 1.5f, ~0, QueryTriggerInteraction.Ignore))
                            if (c.GetComponentInParent<ClimbableSurface>() is ClimbableSurface m && m.kind == ClimbableKind.Tree) { proxy = m; break; }
                        if (proxy == null) { Log("tree: at the terrain tree's perch: NO tree proxy found by the trunk"); yield break; }
                        Vector3 perchSide = terrainStand - trunk;
                        perchSide.y = 0f;
                        Vector3 perch = proxy.PerchFor(perchSide.sqrMagnitude > 1e-4f ? perchSide.normalized : Vector3.forward, 0.6f, 0.5f);
                        var hits = new List<string>();
                        foreach (Collider c in Physics.OverlapCapsule(perch + Vector3.up * 0.6f, perch + Vector3.up * 1.5f, 0.45f, ~0, QueryTriggerInteraction.Ignore))
                            hits.Add($"{c.name} ({c.GetType().Name}, layer {LayerMask.LayerToName(c.gameObject.layer)}{(c.GetComponent<ClimbableSurface>() == proxy ? ", the tree's own proxy" : "")})");
                        Log($"tree: at the terrain tree's perch {perch.x:F1},{perch.y:F2},{perch.z:F1} in Play: {(hits.Count == 0 ? "nothing" : string.Join("; ", hits))}");
                        yield break;
                    }
                    Log($"tree: the terrain tree's face at {tPoint.x:F1},{tPoint.y:F2},{tPoint.z:F1} (kind {motor.ClimbKindAt(tPoint, tNormal)}), the start {tStand.x:F1},{tStand.z:F1}");
                    Teleport(em, ghost, tStand + Vector3.up * offset);
                    yield return new WaitForSeconds(1.5f);
                    yield return TreeTry(body, ghost, tally, "a terrain tree: up with Run (ends perched)", tPoint, tNormal, tStand, offset, false, false, TreeEnd.Perched);
                    if (FarGround(tPoint, tStand, out Vector3 tFar, out string tFarWhy))
                        yield return TreeTry(body, ghost, tally, "a terrain tree: a click 30 m off (NGO: too far, still perched)", tFar, Vector3.up, tStand, offset, false, true, TreeEnd.Perched);
                    else { tally.Tried++; Log("tree: a terrain tree: a click 30 m off: NOT TESTED; " + tFarWhy); }
                    yield return TreeTry(body, ghost, tally, "a terrain tree: down by a click below", tStand, Vector3.up, tStand, offset, false, false, TreeEnd.Down);
                    yield break;
                }

                // the climb, then the same with too little Flux
                yield return ClimbTry(body, ghost, tally, "a straight climb with Run, onto the top", point, normal, stand, offset, true);
                Teleport(em, ghost, stand + Vector3.up * offset);
                yield return new WaitForSeconds(1.5f);
                if (resources != null && resources.Chi > 5f) resources.TrySpendChi(resources.Chi - 5f);
                yield return new WaitForSeconds(0.5f);
                yield return ClimbTry(body, ghost, tally, "the same climb with 5 Flux (NGO: refused at the click)", point, normal, stand, offset, false);
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                Log($"climb: {grip.ClimbsPaid} climb(s) paid, {grip.ClimbsRefused} stopped for want of Flux, {plans.WallWalks} wall clicks planned");
            }

            enum TreeEnd { Perched, Down }

            /// <summary>
            /// Ground for the too-far click: on the NavMesh 30-34 m from the trunk, at least 27 m off it flat (NGO's drop reaches 25 m), looked for
            /// all round the tree, away from the start first. gui-tree-2 skipped the try silently: the one way it looked had no NavMesh.
            /// </summary>
            static bool FarGround(Vector3 trunk, Vector3 stand, out Vector3 far, out string why)
            {
                far = default;
                Vector3 away = stand - trunk;
                away.y = 0f;
                away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
                for (int k = 0; k < 16; k++)
                {
                    // 0, +22.5, -22.5, +45 ... degrees from straight away from the start
                    float deg = (k + 1) / 2 * 22.5f * (k % 2 == 0 ? 1f : -1f);
                    Vector3 dir = Quaternion.Euler(0f, deg, 0f) * away;
                    for (float r = 30f; r <= 34f; r += 2f)
                        if (UnityEngine.AI.NavMesh.SamplePosition(trunk + dir * r, out UnityEngine.AI.NavMeshHit hit, 3f, UnityEngine.AI.NavMesh.AllAreas)
                            && Flat(hit.position - trunk) >= 27f)
                        {
                            far = hit.position;
                            why = null;
                            return true;
                        }
                }
                why = $"no NavMesh 30-34 m from the trunk at {trunk.x:F1},{trunk.z:F1} in 16 directions";
                return false;
            }

            /// <summary>
            /// One scripted tree click (part 3), judged: planned (or refused, when <paramref name="refused"/>), where it ended (perched: Wall 2,
            /// NGO's Perched phase, upright, above the ground; down: Wall 0 on the ground within 1 m of the click), owner with the server, pops.
            /// </summary>
            IEnumerator TreeTry(GameObject body, Entity ghost, VaultTally tally, string what, Vector3 click, Vector3 normal, Vector3 stand, float offset,
                bool jump, bool refused, TreeEnd expect)
            {
                if (OverTheCap()) yield break;
                World server = KnBootstrap.ServerWorld;
                var em = server.EntityManager;
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                bool hasClient = TryLocalGhost(out World clientWorld, out Entity clientGhost, out _);
                tally.Tried++;
                int walks0 = plans.WallWalks;
                string at = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                BodyClicks.Wall(click, normal, !jump, jump);
                float clicked = Time.realtimeSinceStartup, stoppedAt = -1f, glide = 0f;
                Vector3 bodyAtStop = default;
                bool moving = false;
                m_Pops.Reset();
                while (Time.realtimeSinceStartup - clicked < 15f)
                {
                    yield return new WaitForEndOfFrame();
                    bool now = Moving(ghost);
                    if (now) moving = true;
                    else if (!moving && Time.realtimeSinceStartup - clicked > 3f) break;
                    Vector3 b = OwnBody.transform.position;
                    PopFrame(ghost);
                    if (moving && !now)
                    {
                        if (stoppedAt < 0f) { stoppedAt = Time.realtimeSinceStartup; bodyAtStop = b; }
                        else { glide = Mathf.Max(glide, (b - bodyAtStop).magnitude); if (Time.realtimeSinceStartup - stoppedAt > 1.2f) break; }
                    }
                }
                var move = em.GetComponentData<MoveState>(ghost);
                var tilt = em.GetComponentData<PoseTilt>(ghost);
                Vector3 end = ServerPosition(ghost) - Vector3.up * offset;
                PlayerPose ownPose = hasClient && clientWorld.IsCreated ? clientWorld.EntityManager.GetComponentData<PlayerPose>(clientGhost) : default;
                byte ownWall = hasClient && clientWorld.IsCreated ? clientWorld.EntityManager.GetComponentData<MoveState>(clientGhost).Wall : (byte)255;
                float apart = (new Vector3(ownPose.XMm, ownPose.YMm, ownPose.ZMm) * 0.001f - ServerPosition(ghost)).magnitude;
                bool planned = plans.WallWalks == walks0 + 1;
                bool perched = move.Wall == 2 && tilt.Phase == WallPhase.Perched && tilt.UpY > 990 && end.y > stand.y + 1f;
                bool down = move.Wall == 0 && !tilt.Tilted && Flat(end - click) < 1f && Mathf.Abs(end.y - click.y) < 0.6f;
                bool ok = (refused ? !planned : planned) && (expect == TreeEnd.Perched ? perched : down) && ownWall == move.Wall && apart < 0.05f
                    && glide < 0.02f && m_Pops.Pops == 0;
                if (ok) tally.Passed++;
                Log($"tree: {what} clicked at {at}: {(ok ? "PASS" : "FAIL")}; server {(planned ? "planned it" : "planned NOTHING")} " +
                    $"(\"{(plans.WallPlanner as BodyWallPlanner)?.LastDecision}\"{(string.IsNullOrEmpty(plans.LastWallRefusal) ? "" : "; last refusal: " + plans.LastWallRefusal)}); " +
                    $"Wall server {move.Wall} owner {ownWall}; phase {tilt.Phase}, up {tilt.UpX},{tilt.UpY},{tilt.UpZ}; ended {end.y - stand.y:F2} m above the start, " +
                    $"{Flat(end - click):F2} m from the click at {end.x:F1},{end.y:F2},{end.z:F1} (expected {expect}); owner {apart * 100f:F1} cm from the server; " +
                    $"glide {glide * 100f:F1} cm; pops {m_Pops.Pops}{(m_Pops.Pops > 0 ? " (worst: " + m_Pops.Worst + ")" : "")}");
                yield return new WaitForSeconds(1f);
            }

            /// <summary>One scripted straight climb, judged: planned as a climb, paid (or stopped at the foot), where it ended, upright, the pops.</summary>
            IEnumerator ClimbTry(GameObject body, Entity ghost, VaultTally tally, string what, Vector3 click, Vector3 normal, Vector3 stand, float offset, bool expectClimb)
            {
                if (OverTheCap()) yield break;
                World server = KnBootstrap.ServerWorld;
                var em = server.EntityManager;
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                var grip = server.GetExistingSystemManaged<WallGripSystem>();
                bool hasClient = TryLocalGhost(out World clientWorld, out Entity clientGhost, out _);
                tally.Tried++;
                int walks0 = plans.WallWalks, paid0 = grip.ClimbsPaid, refused0 = grip.ClimbsRefused;
                float flux0 = Flux(body);
                string at = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                BodyClicks.Wall(click, normal, true, false);
                float clicked = Time.realtimeSinceStartup, stoppedAt = -1f, glide = 0f;
                Vector3 bodyAtStop = default;
                bool moving = false;
                string plant45 = null;
                m_Pops.Reset();
                while (Time.realtimeSinceStartup - clicked < 15f)
                {
                    yield return new WaitForEndOfFrame();
                    bool now = Moving(ghost);
                    if (now) moving = true;
                    else if (!moving && Time.realtimeSinceStartup - clicked > 3f) break;
                    Vector3 b = OwnBody.transform.position;
                    PopFrame(ghost);
                    if (plant45 == null && Vector3.Angle(OwnBody.transform.up, Vector3.up) >= 45f && em.GetComponentData<PoseTilt>(ghost).Phase == WallPhase.Plant)
                    {
                        Vector3 f = b - OwnBody.transform.up * offset;
                        plant45 = $"feet {f.y - stand.y:F2} m above the ground and {Vector3.Dot(f - click, normal):F2} m out from the face";
                    }
                    if (moving && !now)
                    {
                        if (stoppedAt < 0f) { stoppedAt = Time.realtimeSinceStartup; bodyAtStop = b; }
                        else { glide = Mathf.Max(glide, (b - bodyAtStop).magnitude); if (Time.realtimeSinceStartup - stoppedAt > 1f) break; }
                    }
                }
                Vector3 end = ServerPosition(ghost) - Vector3.up * offset;
                var tilt = em.GetComponentData<PoseTilt>(ghost);
                PlayerPose ownPose = hasClient && clientWorld.IsCreated ? clientWorld.EntityManager.GetComponentData<PlayerPose>(clientGhost) : default;
                float apart = (new Vector3(ownPose.XMm, ownPose.YMm, ownPose.ZMm) * 0.001f - ServerPosition(ghost)).magnitude;
                bool planned = plans.WallWalks == walks0 + 1;
                bool up = end.y > stand.y + 1.5f;
                bool ok = (expectClimb ? planned && grip.ClimbsPaid == paid0 + 1 && up : !planned && !up)   // too little Flux: NGO refuses at the click
                    && !tilt.Tilted && em.GetComponentData<MoveState>(ghost).Wall == 0 && apart < 0.05f && glide < 0.02f && m_Pops.Pops == 0;
                if (ok) tally.Passed++;
                Log($"climb: {what} clicked at {at}: {(ok ? "PASS" : "FAIL")}; server {(planned ? "planned it" : "planned NOTHING")} " +
                    $"(\"{(plans.WallPlanner as BodyWallPlanner)?.LastDecision}\"{(string.IsNullOrEmpty(plans.LastWallRefusal) ? "" : "; last refusal: " + plans.LastWallRefusal)}); " +
                    $"paid {grip.ClimbsPaid - paid0}, stopped for Flux {grip.ClimbsRefused - refused0}; ended {end.y - stand.y:F2} m above the start at {end.x:F1},{end.y:F2},{end.z:F1} " +
                    $"(expected {(expectClimb ? "on the top" : "refused, at the start")}); upright {!tilt.Tilted}; owner {apart * 100f:F1} cm from the server; glide {glide * 100f:F1} cm; " +
                    $"pops {m_Pops.Pops}{(m_Pops.Pops > 0 ? " (worst: " + m_Pops.Worst + ")" : "")}; at 45 degrees in the plant: {plant45 ?? "no plant"}; Flux {flux0:F0} -> {Flux(body):F0}");
                yield return new WaitForSeconds(0.8f);
            }

            /// <summary>The scripted jump that catches a wall (env KN_PLAYHERE_CATCH=1; M3c part 4, `m3c-wallstride-design.md` section 5).</summary>
            static bool CatchRun => Environment.GetEnvironmentVariable("KN_PLAYHERE_CATCH") == "1";

            /// <summary>
            /// The scripted jumps at a wall (part 4, with the owner's change 5.3: caught where the arc meets the wall): a face near the spawn the
            /// graph does not cover (NGO's Decide), the pop check proved in the run, then three Jump clicks at it, at different heights and the last
            /// from farther back, each judged (a catch planned, handed off where it meets the face, on to the top, owner with the server, no pop)
            /// and the three catch heights side by side (expected to rise with the click).
            /// </summary>
            IEnumerator Catches(GameObject body, Entity ghost, VaultTally tally)
            {
                World server = KnBootstrap.ServerWorld;
                var em = server.EntityManager;
                var jumps = server.GetExistingSystemManaged<PlayerJumpServerSystem>();
                var grip = server.GetExistingSystemManaged<WallGripSystem>();
                var motor = body.GetComponent<PlayerWallRunMotor>();
                var progression = body.GetComponent<PlayerCharacterProgression>();
                var resources = body.GetComponent<PlayerResources>();
                if (jumps == null || jumps.WallPlanner == null || grip == null || motor == null)
                { Log("catch: FAIL; no jump system with a wall planner, Grip system or wall motor"); tally.Tried++; yield break; }
                float offset = body.GetComponent<UnityEngine.AI.NavMeshAgent>() is UnityEngine.AI.NavMeshAgent ag ? ag.baseOffset : 0f;
                if (progression != null && progression.CurrentLevel < 60) progression.SetLevel(60);
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                yield return new WaitForSeconds(1f);
                var ask = new PlayerWallRunMotor.GhostWallAsk { Open = true, Alive = true, Run = true, Jump = true, Flux = 999f, HasFluxForOne = true,
                    Up = Vector3.up, Forward = Vector3.forward, GroundSpeed = em.GetComponentData<MoveStats>(ghost).RunMmps * 0.001f };
                // Zone01's walls near the spawn are all the surface graph's (the wall walk's): the catch is for surfaces it does not cover, so the
                // placed tree (gui-catch-1, 1 Nov: no wall face fits; NGO catches a tree and ends perched), found by the ray aimed at its trunk
                string why = motor.FindTestTreeClimb(PlacedTree, ask, out Vector3 stand, out Vector3 point, out Vector3 normal);
                if (why != null) { Log("catch: NOT TESTED; " + why); tally.Tried++; yield break; }
                Log($"catch: the test tree's face at {point.x:F1},{point.y:F2},{point.z:F1} facing {normal.x:F2},{normal.z:F2}; the start {stand.x:F1},{stand.z:F1}");

                // the pop check proved in this run: standing still is clean, the teleport to the start is a known pop
                m_Pops.Reset();
                float watch = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - watch < 1f) { yield return new WaitForEndOfFrame(); PopFrame(ghost); }
                Log($"catch: pop check on a known clean case (standing 1 s): {m_Pops.Pops} pop(s) (expected 0) {m_Pops.Worst}");
                m_Pops.Reset();
                PopFrame(ghost);
                Teleport(em, ghost, stand + Vector3.up * offset);
                watch = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - watch < 1.5f) { yield return new WaitForEndOfFrame(); PopFrame(ghost); }
                Log($"catch: pop check on a known fault (the teleport to the start): {m_Pops.Pops} pop(s) (expected 1 or more): {m_Pops.Worst}");

                // three jumps at the face: low, middle, high; the last from 3 m farther out
                Vector3 outward = new Vector3(normal.x, 0f, normal.z).normalized;
                Vector3 farther = UnityEngine.AI.NavMesh.SamplePosition(stand + outward * 3f, out UnityEngine.AI.NavMeshHit back, 1.5f, UnityEngine.AI.NavMesh.AllAreas)
                    ? back.position : stand;
                var heights = new List<string>();
                var tries = new (string what, float up, Vector3 from)[]
                {
                    ("low, at 1.8 m up the face", 1.8f, stand),
                    ("middle, at 2.8 m up the face", 2.8f, stand),
                    ($"high, at 3.8 m up the face, {Flat(farther - point):F1} m out", 3.8f, farther),
                };
                foreach (var (what, upBy, from) in tries)
                {
                    Teleport(em, ghost, from + Vector3.up * offset);
                    yield return new WaitForSeconds(1.5f);
                    if (resources != null) resources.RestoreChi(resources.MaxChi);
                    yield return new WaitForSeconds(0.5f);
                    var result = new CatchResult();
                    yield return CatchTry(body, ghost, tally, what, new Vector3(point.x, from.y + upBy, point.z), normal, from, offset, result);
                    heights.Add(result.Height);
                }
                Log($"catch: caught at {string.Join(", ", heights)} above the take-off (expected rising with the click; owner, 1 Nov: where the arc meets the wall)");

                // a jump onto a wall of the zone's graph (owner, 1 Nov, design 5.4): the real jump, grabbing where clicked within the Jump rank's
                // reach, above it as high as the jump gets under the click, then the wall walk up to it; three heights: below, at, above the reach
                // the planned test wall (owner, 1 Nov: test places are planned and proved in edit mode before the run, never searched for)
                string wallWhy = motor.FindTestWall(TestGraphWall, out PlayerWallRunMotor.TestWall wall);
                if (wallWhy != null) { Log("catch: a graph wall: NOT TESTED; " + wallWhy); tally.Tried++; yield break; }
                float reach = body.GetComponent<PlayerJumpMotor>() is PlayerJumpMotor jm ? jm.GhostJumpReach : 5f;
                Vector3 ground = wall.Front;
                Log($"catch: the graph wall's foot at {wall.Foot.x:F1},{wall.Foot.y:F2},{wall.Foot.z:F1} facing {wall.Normal.x:F2},{wall.Normal.z:F2}; Jump reaches {reach:F1} m");
                var grabs = new List<string>();
                // three heights on the wall, each grabbed where clicked (owner pick B a, 1 Nov: at Wallstride's gate, Jump rank 20, the jump
                // reaches 67.5 m in Play, so every graph-wall click in Zone01 is within reach)
                foreach (var (what, upBy) in new (string, float)[]
                {
                    ("a graph wall, low", 2.5f),
                    ("a graph wall, middle", 5f),
                    ("a graph wall, high", 8f),
                })
                {
                    Vector3 click = wall.Lower + Vector3.up * (wall.Foot.y + upBy - wall.Lower.y);
                    Teleport(em, ghost, ground + Vector3.up * offset);
                    yield return new WaitForSeconds(1.5f);
                    if (resources != null) resources.RestoreChi(resources.MaxChi);
                    yield return new WaitForSeconds(0.5f);
                    var result = new CatchResult();
                    yield return CatchTry(body, ghost, tally, $"{what}, at {upBy:F1} m up the face", click, wall.Normal, ground, offset, result, onWall: true);
                    grabs.Add(result.Height);
                }
                Log($"catch: graph wall grabbed at {string.Join(", ", grabs)} above the take-off (expected: where clicked, 2.5, 5 and 8 m)");
            }

            sealed class CatchResult { public string Height = "none"; }

            /// <summary>The scripted self-starting run (env KN_PLAYHERE_AUTO=1; M3c part 5, `m3c-wallstride-design.md` section 6).</summary>
            static bool AutoRun => Environment.GetEnvironmentVariable("KN_PLAYHERE_AUTO") == "1";

            /// <summary>
            /// Part 5's straight climb by itself (A3), planned before the run (owner rule, 1 Nov) and proved in edit mode by
            /// Zone01WallPropProbe.FindPart5ClimbFaces (PlanAutoForGhost: UpClimb): a face the graph does not cover at -25.0,1.25,-50.5 facing -z,
            /// started from 6.5 m out, its walkable top 8.9 m up (none by the spawn: its walls are all the graph's).
            /// </summary>
            static readonly Vector3 AutoClimbStand = new Vector3(-25.0f, 0.05f, -57.0f), AutoClimbTop = new Vector3(-24.3f, 9.00f, -49.4f);

            /// <summary>
            /// Part 5 (NGO TryPlanRoute: plain ground clicks Wallstride takes by itself), at the places proved in edit mode: the test graph wall's
            /// roof and the street in front of it, and a straight-climb face by the spawn. The pop check proved first; then each try judged: what
            /// the server made of the click (over a wall, up a face, or the plain walk) against what was expected, on to the place clicked, owner
            /// with the server, no glide, no pop. Last, the core case unchanged: a click directly on the wall.
            /// </summary>
            IEnumerator Autos(GameObject body, Entity ghost, VaultTally tally)
            {
                World server = KnBootstrap.ServerWorld;
                var em = server.EntityManager;
                var motor = body.GetComponent<PlayerWallRunMotor>();
                var progression = body.GetComponent<PlayerCharacterProgression>();
                var resources = body.GetComponent<PlayerResources>();
                if (motor == null) { Log("auto: FAIL; no wall motor"); tally.Tried++; yield break; }
                float offset = body.GetComponent<UnityEngine.AI.NavMeshAgent>() is UnityEngine.AI.NavMeshAgent ag ? ag.baseOffset : 0f;
                if (progression != null && progression.CurrentLevel < 60) progression.SetLevel(60);
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                yield return new WaitForSeconds(1f);
                string wallWhy = motor.FindTestWall(TestGraphWall, out PlayerWallRunMotor.TestWall wall);
                if (wallWhy != null) { Log("auto: the graph wall: NOT TESTED; " + wallWhy); tally.Tried++; yield break; }
                Log($"auto: the graph wall's foot {wall.Foot.x:F1},{wall.Foot.y:F2},{wall.Foot.z:F1}, roof {wall.Top.x:F1},{wall.Top.y:F2},{wall.Top.z:F1}, street {wall.FrontFar.x:F1},{wall.FrontFar.z:F1}");

                m_Pops.Reset();
                float watch = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - watch < 1f) { yield return new WaitForEndOfFrame(); PopFrame(ghost); }
                Log($"auto: pop check on a known clean case (standing 1 s): {m_Pops.Pops} pop(s) (expected 0) {m_Pops.Worst}");
                m_Pops.Reset();
                PopFrame(ghost);
                Teleport(em, ghost, wall.FrontFar + Vector3.up * offset);
                watch = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - watch < 1.5f) { yield return new WaitForEndOfFrame(); PopFrame(ghost); }
                Log($"auto: pop check on a known fault (the teleport to the street): {m_Pops.Pops} pop(s) (expected 1 or more): {m_Pops.Worst}");

                // the expected ways are the edit-mode probe's (ThePart5TestPlaces_StandInZone01, design 6.2): this roof has no walk up or down;
                // None = NGO's plain walk (not Wallstride's)
                yield return AutoTry(body, ghost, tally, "street to the roof, running", wall.FrontFar, wall.Top, true, PlayerWallRunMotor.AutoWay.OverWall, offset);
                yield return AutoTry(body, ghost, tally, "roof to the street, walking (no walk down)", wall.Top, wall.FrontFar, false, PlayerWallRunMotor.AutoWay.OverWall, offset);
                yield return AutoTry(body, ghost, tally, "street to the roof, walking (no walk up)", wall.FrontFar, wall.Top, false, PlayerWallRunMotor.AutoWay.OverWall, offset);
                yield return AutoTry(body, ghost, tally, "street to street, running (control)", wall.FrontFar, wall.Front, true, PlayerWallRunMotor.AutoWay.None, offset);

                // up a face by itself, then on over the roof to the place (the owner's change): the top, and 2 m on from it when the roof is there
                Vector3 inward = new Vector3(AutoClimbTop.x - AutoClimbStand.x, 0f, AutoClimbTop.z - AutoClimbStand.z).normalized;
                Vector3 past = UnityEngine.AI.NavMesh.SamplePosition(AutoClimbTop + inward * 2f, out UnityEngine.AI.NavMeshHit on, 0.3f, UnityEngine.AI.NavMesh.AllAreas)
                    && Mathf.Abs(on.position.y - AutoClimbTop.y) < 0.3f ? on.position : AutoClimbTop;
                Log($"auto: the straight-climb face's start {AutoClimbStand.x:F1},{AutoClimbStand.z:F1}, its top {AutoClimbTop.x:F1},{AutoClimbTop.y:F2},{AutoClimbTop.z:F1}, the place {past.x:F1},{past.y:F2},{past.z:F1}");
                yield return AutoTry(body, ghost, tally, "up the face, running, and on to a place past its top (owner change)", AutoClimbStand, past, true,
                    PlayerWallRunMotor.AutoWay.UpClimb, offset);
                yield return AutoTry(body, ghost, tally, "the same, walking (NGO: never by itself)", AutoClimbStand, past, false, PlayerWallRunMotor.AutoWay.None, offset);

                // the core case, unchanged: a click directly on the wall with Run (owner, 1 Nov: wall battles)
                Teleport(em, ghost, wall.Front + Vector3.up * offset);
                yield return new WaitForSeconds(1.5f);
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                int walks0 = plans.WallWalks;
                tally.Tried++;
                BodyClicks.Wall(wall.Onto, wall.Normal, true, false);
                float clicked = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - clicked < 8f && (Time.realtimeSinceStartup - clicked < 1f || Moving(ghost))) yield return null;
                bool onIt = em.GetComponentData<MoveState>(ghost).Wall == 1 && plans.WallWalks == walks0 + 1;
                if (onIt) tally.Passed++;
                Log($"auto: a click directly on the wall (the core case): {(onIt ? "PASS" : "FAIL")}; wall walks {plans.WallWalks - walks0}, on the wall {em.GetComponentData<MoveState>(ghost).Wall}");
            }

            /// <summary>Items used by the server on its body (env KN_PLAYHERE_ITEMS=1; owner, 2 Nov, feel test item 5).</summary>
            static bool ItemsRun => Environment.GetEnvironmentVariable("KN_PLAYHERE_ITEMS") == "1";

            /// <summary>
            /// Item 5 (owner pick a): the owner's own body asks, the server uses the item on its body, and the owner sees the server's
            /// numbers. An HP stim with half the HP gone (healed on the server, the owner's HP and Pack count follow); the Flux Flask with
            /// the Flux run down (it pours 45 % of Max Flux over 8 s on the server, the owner's Flux follows, the bottle keeps its
            /// drink count); then a save to the session's memory store holds the Pack exactly as the server has it (what a relog loads).
            /// </summary>
            IEnumerator Items(GameObject body, Entity ghost, VaultTally tally)
            {
                var inventory = body.GetComponent<PlayerInventory>();
                var resources = body.GetComponent<PlayerResources>();
                var own = OwnBody != null ? OwnBody.GetComponent<PlayerInventory>() : null;
                var ownResources = OwnBody != null ? OwnBody.GetComponent<PlayerResources>() : null;
                if (inventory == null || resources == null || own == null || ownResources == null)
                { tally.Tried++; Log("items: FAIL; a body has no inventory or resources"); yield break; }
                const ushort Stim = 9001, Flask = 9012;
                inventory.ServerGive(Stim, 3);
                if (inventory.CountItem(Flask) == 0) inventory.ServerGive(Flask, 1);
                yield return new WaitForSeconds(1.2f);   // the Pack's cells reach the owner

                // an HP stim, asked by the owner's own body
                tally.Tried++;
                resources.ApplyDamage(resources.MaxHealth * 0.5f);
                yield return new WaitForSeconds(0.6f);
                float serverHp0 = resources.Health, ownHp0 = ownResources.Health;
                int serverCount0 = inventory.CountItem(Stim), ownCount0 = own.CountItem(Stim);
                string at = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                own.UseItem(Stim);
                yield return new WaitForSeconds(1.5f);
                bool stimOk = resources.Health > serverHp0 && Mathf.Abs(ownResources.Health - resources.Health) < 1f
                    && inventory.CountItem(Stim) == serverCount0 - 1 && own.CountItem(Stim) == ownCount0 - 1;
                if (stimOk) tally.Passed++;
                Log($"items: an HP stim asked by the owner at {at}: {(stimOk ? "PASS" : "FAIL")}; server HP {serverHp0:F0} -> {resources.Health:F0}, owner's HP " +
                    $"{ownHp0:F0} -> {ownResources.Health:F0}; stims: server {serverCount0} -> {inventory.CountItem(Stim)}, owner {ownCount0} -> {own.CountItem(Stim)}");

                // the Flux Flask, with the Flux run down
                tally.Tried++;
                if (resources.Chi > resources.MaxChi * 0.2f) resources.TrySpendChi(resources.Chi - resources.MaxChi * 0.2f);
                yield return new WaitForSeconds(1.2f);   // past the shared use cooldown, and the low Flux reaches the owner
                float serverFlux0 = resources.Chi, ownFlux0 = ownResources.Chi;
                at = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                own.UseItem(Flask);
                yield return new WaitForSeconds(9f);
                ulong bottle = 0, ownBottle = 0;
                for (int i = 0; i < PlayerInventory.Capacity; i++)
                {
                    if (inventory.Get(i).itemId == Flask) bottle = inventory.Get(i).data;
                    if (own.Get(i).itemId == Flask) ownBottle = own.Get(i).data;
                }
                bool flaskOk = resources.Chi - serverFlux0 >= resources.MaxChi * 0.4f && Mathf.Abs(ownResources.Chi - resources.Chi) < 1f
                    && (bottle & 0xFF) == 1 && ownBottle == bottle;
                if (flaskOk) tally.Passed++;
                Log($"items: the Flux Flask asked by the owner at {at}: {(flaskOk ? "PASS" : "FAIL")}; server Flux {serverFlux0:F0} -> {resources.Chi:F0} of " +
                    $"{resources.MaxChi:F0}, owner's Flux {ownFlux0:F0} -> {ownResources.Chi:F0}; the bottle's drinks used: server {bottle & 0xFF}, owner {ownBottle & 0xFF}");

                // the tester enters without an account (this run's safety: nothing is saved), so its relog is not run here: the PlayMode
                // test B3 saves a Pack on the real prefab, loads it into a fresh server body and saves it back byte for byte
                Log($"items: the Pack after this run: {inventory.CountItem(Stim)} stims, the Flask's drinks used {bottle & 0xFF} (a relog of a saved " +
                    "character keeps them: PlayMode B3; this tester has no account, so nothing is saved here)");
                resources.RestoreAll();
            }

            /// <summary>The owner's feel-test fixes 1-3 (env KN_PLAYHERE_FIXES=1; `m3c-wallstride-design.md` section 9).</summary>
            static bool FixesRun => Environment.GetEnvironmentVariable("KN_PLAYHERE_FIXES") == "1";

            /// <summary>
            /// The real click path of the owner's body (the ray, the water, the jump branch: <see cref="PointAndClickPlayer.ClickWorld"/>), as a
            /// mouse press at that point's place on the player's own camera. Returns what happened, for the log: the game's message, if any.
            /// </summary>
            static string PressAt(GameObject body, Vector3 world)
            {
                var player = body.GetComponent<PointAndClickPlayer>();
                if (player == null) return "no click player";
                Camera cam = player.GameplayCamera != null ? player.GameplayCamera : Camera.main;
                if (cam == null) return "no camera";
                Vector3 screen = cam.WorldToScreenPoint(world);
                var at = new Vector2(screen.x, screen.y);
                if (screen.z <= 0f || !cam.pixelRect.Contains(at)) return $"off screen ({screen.x:F0},{screen.y:F0},{screen.z:F1} in {cam.pixelRect})";
                string said = null;
                Action<string> hear = m => said = m;
                player.CommandMessage += hear;
                player.ClickWorld(at);
                player.CommandMessage -= hear;
                return said != null ? "the game said: " + said : $"pressed at {at.x:F0},{at.y:F0}";
            }

            /// <summary>
            /// The owner's feel-test fixes 1-3 (2 Nov), on the river south bank (the Waterstride part's places): 1) the map's travel with Run
            /// on runs (and walks with Run off, the control); 2) a jump made on the water, clicked through the real ray, lands on the spot
            /// of the surface clicked (the lake bed under it is logged: where the old aim would have landed); 3) a new click while
            /// waterstriding: the body never steps away from the new place first.
            /// </summary>
            IEnumerator Fixes(GameObject body, Entity ghost, VaultTally tally)
            {
                World server = KnBootstrap.ServerWorld;
                var em = server.EntityManager;
                var progression = body.GetComponent<PlayerCharacterProgression>();
                var resources = body.GetComponent<PlayerResources>();
                var skills = OwnBody.GetComponent<PlayerMovementSkills>();
                var view = OwnBody.GetComponent<PointAndClickPlayer>();
                float offset = body.GetComponent<UnityEngine.AI.NavMeshAgent>() is UnityEngine.AI.NavMeshAgent ag ? ag.baseOffset : 0f;
                if (skills == null || view == null) { Log("fixes: FAIL; the owner's body has no skills or click player"); tally.Tried++; yield break; }
                if (progression != null && progression.CurrentLevel < 60) progression.SetLevel(60);
                if (!UnityEngine.AI.NavMesh.SamplePosition(SouthBank, out UnityEngine.AI.NavMeshHit south, 6f, UnityEngine.AI.NavMesh.AllAreas))
                { Log("fixes: FAIL; no NavMesh on the south bank"); tally.Tried++; yield break; }
                Teleport(em, ghost, south.position + Vector3.up * offset);
                yield return new WaitForSeconds(1.5f);
                void Select(MovementSkillId id) { if (id == MovementSkillId.Run ? !skills.IsRunning : !skills.IsJumpSelected) skills.ToggleSelection(id); }

                // 1) the map's travel: Run on runs, Run off walks (NGO's ExecuteLocalMove / ServerMoveTo)
                Vector3 inland = south.position + new Vector3(0f, 0f, -24f);
                if (!UnityEngine.AI.NavMesh.SamplePosition(inland, out UnityEngine.AI.NavMeshHit inlandHit, 6f, UnityEngine.AI.NavMesh.AllAreas))
                { Log("fixes: FAIL; no NavMesh inland of the south bank"); tally.Tried++; yield break; }
                MoveStats stats = em.GetComponentData<MoveStats>(ghost);
                foreach (bool run in new[] { true, false })
                {
                    tally.Tried++;
                    if (resources != null) resources.RestoreChi(resources.MaxChi);
                    if (run) Select(MovementSkillId.Run); else skills.SelectWalk();
                    bool sent = view.TravelTo(run ? inlandHit.position : south.position, "the test place");
                    Vector3 to = view.TravelDestination;   // the goal TravelTo resolved on the NavMesh
                    float t0 = Time.realtimeSinceStartup;
                    int fastest = 0;
                    while (Time.realtimeSinceStartup - t0 < 12f)
                    {
                        yield return null;
                        var m = em.GetComponentData<MoveState>(ghost);
                        if ((m.Flags & MoveFlags.Moving) != 0) fastest = Math.Max(fastest, m.SpeedMmps);
                        else if (Time.realtimeSinceStartup - t0 > 1f) break;
                    }
                    int want = run ? stats.RunMmps : stats.WalkMmps;
                    float off = Flat(ServerPosition(ghost) - Vector3.up * offset - to);
                    bool ok = sent && fastest == want && off < 0.5f;
                    if (ok) tally.Passed++;
                    Log($"fixes: the map's travel with Run {(run ? "on" : "off")}: {(ok ? "PASS" : "FAIL")}; sent {sent}, fastest {fastest * 0.001f:F1} m/s " +
                        $"(walk {stats.WalkMmps * 0.001f:F1}, run {stats.RunMmps * 0.001f:F1}), ended {off:F2} m from the place");
                }

                // 2) a jump made on the water lands on the surface spot clicked
                tally.Tried++;
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                Select(MovementSkillId.Run);
                Vector3 water1 = new Vector3(south.position.x, -1.2f, 206f);
                BodyClicks.Ground(water1, true);
                float w0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - w0 < 10f && (Moving(ghost) || Time.realtimeSinceStartup - w0 < 1f)) yield return null;
                bool onWater = em.GetComponentData<MoveState>(ghost).Water != 0;
                Vector3 feet = ServerPosition(ghost) - Vector3.up * offset;
                Vector3 target = new Vector3(feet.x + 6f, feet.y, feet.z + 4f);
                // the click's ray as the game camera casts it (behind and above the body, about 50 degrees down) through the surface spot:
                // the hit under it is the lake bed; the view's own aim (TryWaterUnderRay) turns it back into the spot on the surface
                Vector3 back = (feet - target); back.y = 0f; back = back.normalized;
                var ray = new Ray(target + back * 8f + Vector3.up * 9.5f, (target - (target + back * 8f + Vector3.up * 9.5f)).normalized);
                float bedOff = -1f;
                Vector3 aim = target;
                string jumpPress = "no hit under the click";
                var water = OwnBody.GetComponent<PlayerWaterstrideMotor>();
                // the click's own layers (PCP.TryPickPointerHit; batch-fixes-2 run 1 used the default layers and hit at the spot itself)
                if (Physics.Raycast(ray, out RaycastHit bed, 500f, view.RaycastLayers.value, QueryTriggerInteraction.Ignore))
                {
                    bedOff = Flat(bed.point - target);
                    Log($"fixes: the click's ray hit {bed.collider.name} (layer {LayerMask.LayerToName(bed.collider.gameObject.layer)}) at y {bed.point.y:F2}; the surface spot is at y {target.y:F2}");
                    jumpPress = water != null && water.TryWaterUnderRay(ray, bed, out aim) ? $"aimed at the surface {Flat(aim - target):F2} m from the spot" : "the view found no water on the ray (aims at the bed)";
                    if (water == null || !water.TryWaterUnderRay(ray, bed, out aim)) aim = bed.point;
                }
                Select(MovementSkillId.Jump);
                BodyClicks.Jump(aim);
                float j0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - j0 < 4f) yield return null;
                Vector3 landed = ServerPosition(ghost) - Vector3.up * offset;
                float jumpOff = Flat(landed - target);
                bool jumpOk = onWater && jumpOff < 0.35f;
                if (jumpOk) tally.Passed++;
                Log($"fixes: a jump made on the water: {(jumpOk ? "PASS" : "FAIL")}; started on the water {onWater}; landed {jumpOff:F2} m from the surface " +
                    $"spot clicked; the lake bed under that click is {bedOff:F2} m past it (where the old aim landed); the press: {jumpPress}");

                // 3) a new click while waterstriding: the body never steps away from the new place first
                tally.Tried++;
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                // walking (5 m/s), so the body is still on its way when the new click comes (batch-fixes-2 run 1: running, it had
                // arrived 9 m off before 0.7 s); Waterstride across is used walking too (owner, item 4)
                skills.SelectWalk();
                Vector3 here = ServerPosition(ghost) - Vector3.up * offset;
                string runPress = "sent";
                BodyClicks.Ground(new Vector3(here.x + 12f, here.y, here.z + 1f), false);
                float r0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - r0 < 0.7f) yield return null;
                bool movingOnWater = Moving(ghost) && em.GetComponentData<MoveState>(ghost).Water != 0;
                Vector3 at = ServerPosition(ghost) - Vector3.up * offset;
                Vector3 second = new Vector3(at.x - 1f, at.y, at.z + 8f);
                float start = Flat(at - second), worstBack = 0f, best = start;
                string turnPress = "sent";
                BodyClicks.Ground(second, false);
                float s0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - s0 < 1f)
                {
                    yield return null;   // not WaitForEndOfFrame: it never resumes in a batch Editor (2+3 are proved in batch)
                    float d = Flat(OwnBody.transform.position - second);
                    worstBack = Mathf.Max(worstBack, d - best);
                    best = Mathf.Min(best, d);
                }
                bool reOk = movingOnWater && worstBack < 0.05f && best < start - 1f;
                if (reOk) tally.Passed++;
                Log($"fixes: a new click while waterstriding: {(reOk ? "PASS" : "FAIL")}; moving on the water {movingOnWater}; the drawn body stepped " +
                    $"away from the new place by at most {worstBack:F2} m and got {start - best:F1} m nearer in 1 s; the presses: {runPress} / {turnPress}");
                skills.SelectWalk();
                if (resources != null) resources.RestoreChi(resources.MaxChi);
            }

            /// <summary>The scripted route jumps (env KN_PLAYHERE_LEDGE=1; M3c part 5b, `m3c-wallstride-design.md` section 7).</summary>
            static bool LedgeRun => Environment.GetEnvironmentVariable("KN_PLAYHERE_LEDGE") == "1";

            /// <summary>
            /// Part 5b's places, planned before the run (owner rule, 1 Nov) and proved in edit mode: with Wallstride locked, the test graph wall's
            /// roof, which has no walk down (Zone01WallPropProbe.ThePart5TestPlaces_StandInZone01), to the street in front; and the pop check's
            /// teleport to the foot of a drop between roofs (HowAWalkCrossesZone01sRoofGaps). gui-ledge-1 also crossed a 2 m gap at 51 -> 59, 88.5
            /// and the 2.4 m drop from -89, 11.5 (owner, 2 Nov: those keep the normal jump, not run again).
            /// </summary>
            static readonly Vector3 DropLinkBelow = new Vector3(-81.0f, 21.82f, 11.5f);

            /// <summary>
            /// The route's jumps (part 5b): each try a plain click whose walk must jump (a gap, a drop-down link, or with Wallstride locked the
            /// edge of a roof with no walk down), judged: taken by the server as the walk's jumps, the LedgeJump phase shown, the Jump's Flux paid,
            /// on to the place clicked, owner with the server, no glide, no pop. The owner's own body is shown masculine and feminine in turn (its
            /// view only; owner, 1 Nov: one masculine and one feminine try in the GIFs).
            /// </summary>
            IEnumerator Ledges(GameObject body, Entity ghost, VaultTally tally)
            {
                World server = KnBootstrap.ServerWorld;
                var em = server.EntityManager;
                var motor = body.GetComponent<PlayerWallRunMotor>();
                var progression = body.GetComponent<PlayerCharacterProgression>();
                if (motor == null) { Log("ledge: FAIL; no wall motor"); tally.Tried++; yield break; }
                float offset = body.GetComponent<UnityEngine.AI.NavMeshAgent>() is UnityEngine.AI.NavMeshAgent ag ? ag.baseOffset : 0f;
                // owner, 2 Nov (design 9 item 6): with Wallstride ON a ground click from a roof backflips off its edge, a wall click goes onto the wall
                if (progression != null && progression.CurrentLevel < 60) progression.SetLevel(60);
                yield return new WaitForSeconds(1f);
                m_Pops.Reset();
                float watch = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - watch < 1f) { yield return new WaitForEndOfFrame(); PopFrame(ghost); }
                Log($"ledge: pop check on a known clean case (standing 1 s): {m_Pops.Pops} pop(s) (expected 0) {m_Pops.Worst}");
                m_Pops.Reset();
                PopFrame(ghost);
                Teleport(em, ghost, DropLinkBelow + Vector3.up * offset);
                watch = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - watch < 1.5f) { yield return new WaitForEndOfFrame(); PopFrame(ghost); }
                Log($"ledge: pop check on a known fault (the teleport to the roof): {m_Pops.Pops} pop(s) (expected 1 or more): {m_Pops.Worst}");

                // owner, 2 Nov: the backflip is only off a roof's edge; the gaps keep the normal jump (proved in gui-ledge-1, not run again)
                string wallWhy = motor.FindTestWall(TestGraphWall, out PlayerWallRunMotor.TestWall wall);
                if (wallWhy != null) { Log("ledge: the roof with no walk down: NOT TESTED; " + wallWhy); tally.Tried++; yield break; }
                yield return LedgeTry(body, ghost, tally, "off a roof with no walk down to the street, Wallstride on, running, masculine", wall.Top, wall.FrontFar, BodyType.Masculine, offset, run: true);
                yield return LedgeTry(body, ghost, tally, "off the same roof, walking, feminine", wall.Top, wall.FrontFar, BodyType.Feminine, offset, run: false);
                // the wall click from the roof still goes onto the wall (Wallstride), never the drop
                if (OverTheCap()) yield break;
                Teleport(em, ghost, wall.Top + Vector3.up * offset);
                yield return new WaitForSeconds(1.5f);
                if (body.GetComponent<PlayerResources>() is PlayerResources res) res.RestoreChi(res.MaxChi);
                tally.Tried++;
                BodyClicks.Wall(wall.Onto, wall.Normal, true, false);
                float w0 = Time.realtimeSinceStartup;
                bool onWall = false, routeJump = false;
                while (Time.realtimeSinceStartup - w0 < 5f)
                {
                    yield return null;
                    onWall |= em.GetComponentData<MoveState>(ghost).Wall != 0;
                    routeJump |= WallPhase.IsRouteJump(em.GetComponentData<PoseTilt>(ghost).Phase);
                }
                bool wallOk = onWall && !routeJump;
                if (wallOk) tally.Passed++;
                Log($"ledge: a wall click from the same roof: {(wallOk ? "PASS" : "FAIL")}; onto the wall {onWall}, a route jump {routeJump}");
            }

            /// <summary>One plain click whose walk must jump, the owner's body shown as <paramref name="look"/>, judged.</summary>
            IEnumerator LedgeTry(GameObject body, Entity ghost, VaultTally tally, string what, Vector3 from, Vector3 to, BodyType look, float offset, bool run = false)
            {
                if (OverTheCap()) yield break;
                World server = KnBootstrap.ServerWorld;
                var em = server.EntityManager;
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                var grip = server.GetExistingSystemManaged<WallGripSystem>();
                var resources = body.GetComponent<PlayerResources>();
                // the owner's own body in the look asked for (its view only; the clip follows the body type every machine knows)
                PlayerIdentity view = OwnBody != null ? OwnBody.GetComponent<PlayerIdentity>() : null;
                if (view != null && view.State.bodyType != look)
                {
                    CharacterNetState state = view.State;
                    state.bodyType = look;
                    view.ApplyView(state);
                }
                Teleport(em, ghost, from + Vector3.up * offset);
                yield return new WaitForSeconds(1.5f);
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                yield return new WaitForSeconds(0.5f);
                bool hasClient = TryLocalGhost(out World clientWorld, out Entity clientGhost, out _);
                tally.Tried++;
                int autos0 = plans.AutoWalls, paid0 = grip != null ? grip.LedgeJumpsPaid : 0;
                float flux0 = Flux(body);
                string at = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                BodyClicks.Ground(to, run);
                float clicked = Time.realtimeSinceStartup, stoppedAt = -1f, glide = 0f;
                Vector3 bodyAtStop = default;
                bool sawJump = false, sawFall = false, sawLand = false;
                // design 9 item 7: the drawn body is down on the ground when the jump's last phase gives way to the walk
                var shown = OwnBody != null ? OwnBody.GetComponent<PlayerPresentationState>() : null;
                byte lastShown = 0;
                float walkedAbove = -1f;
                m_Pops.Reset();
                while (Time.realtimeSinceStartup - clicked < 15f)
                {
                    yield return new WaitForEndOfFrame();
                    PopFrame(ghost);
                    byte serverPhase = em.GetComponentData<PoseTilt>(ghost).Phase;
                    sawJump |= WallPhase.IsRouteJump(serverPhase);
                    sawFall |= serverPhase == WallPhase.RouteFall;
                    sawLand |= serverPhase == WallPhase.RouteLand;
                    Vector3 b = OwnBody.transform.position;
                    if (shown != null)
                    {
                        if (lastShown != 0 && shown.ShownWallPhase == 0 && walkedAbove < 0f
                            && UnityEngine.AI.NavMesh.SamplePosition(b, out UnityEngine.AI.NavMeshHit under, 3f, UnityEngine.AI.NavMesh.AllAreas))
                            walkedAbove = b.y - offset - under.position.y;
                        lastShown = shown.ShownWallPhase;
                    }
                    if (Time.realtimeSinceStartup - clicked > 1f && !Moving(ghost))
                    {
                        if (stoppedAt < 0f) { stoppedAt = Time.realtimeSinceStartup; bodyAtStop = b; }
                        else { glide = Mathf.Max(glide, (b - bodyAtStop).magnitude); if (Time.realtimeSinceStartup - stoppedAt > 1f) break; }
                    }
                }
                Vector3 end = ServerPosition(ghost) - Vector3.up * offset;
                PlayerPose ownPose = hasClient && clientWorld.IsCreated ? clientWorld.EntityManager.GetComponentData<PlayerPose>(clientGhost) : default;
                float apart = (new Vector3(ownPose.XMm, ownPose.YMm, ownPose.ZMm) * 0.001f - ServerPosition(ghost)).magnitude;
                bool took = plans.AutoWalls == autos0 + 1;
                int paid = grip != null ? grip.LedgeJumpsPaid - paid0 : 0;
                float miss = Vector3.Distance(end, to);
                bool ok = took && sawJump && sawFall && sawLand && walkedAbove >= -0.1f && walkedAbove < 0.1f
                    && paid >= 1 && miss < 0.6f && apart < 0.05f && glide < 0.02f && m_Pops.Pops == 0;
                if (ok) tally.Passed++;
                Log($"ledge: {what} clicked at {at}: {(ok ? "PASS" : "FAIL")}; the server {(took ? "took it as the walk's jumps" : "made it a plain walk")} " +
                    $"(\"{(plans.WallPlanner as BodyWallPlanner)?.LastDecision}\"); the jump shown {sawJump}, Jump Flux paid {paid}; the owner's body " +
                    $"{(view != null ? view.State.bodyType.ToString() : "?")}; ended {miss:F2} m from the click at {end.x:F1},{end.y:F2},{end.z:F1}; " +
                    $"owner {apart * 100f:F1} cm from the server; glide {glide * 100f:F1} cm; pops {m_Pops.Pops}" +
                    $"{(m_Pops.Pops > 0 ? " (worst: " + m_Pops.Worst + ")" : "")}; Flux {flux0:F0} -> {Flux(body):F0}; " +
                    $"fall {sawFall}, landing {sawLand}, the drawn body {walkedAbove:F2} m above the ground when the walk took over");
                yield return new WaitForSeconds(0.8f);
            }

            /// <summary>One plain ground click from <paramref name="from"/> to <paramref name="to"/>, judged against the way expected.</summary>
            IEnumerator AutoTry(GameObject body, Entity ghost, VaultTally tally, string what, Vector3 from, Vector3 to, bool run,
                PlayerWallRunMotor.AutoWay expected, float offset)
            {
                if (OverTheCap()) yield break;
                World server = KnBootstrap.ServerWorld;
                var em = server.EntityManager;
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                var grip = server.GetExistingSystemManaged<WallGripSystem>();
                var resources = body.GetComponent<PlayerResources>();
                Teleport(em, ghost, from + Vector3.up * offset);
                yield return new WaitForSeconds(1.5f);
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                yield return new WaitForSeconds(0.5f);
                bool hasClient = TryLocalGhost(out World clientWorld, out Entity clientGhost, out _);
                tally.Tried++;
                int autos0 = plans.AutoWalls, paid0 = grip != null ? grip.ClimbsPaid : 0;
                float flux0 = Flux(body);
                string at = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                BodyClicks.Ground(to, run);
                float clicked = Time.realtimeSinceStartup, stoppedAt = -1f, glide = 0f;
                Vector3 bodyAtStop = default;
                bool sawWall = false;
                m_Pops.Reset();
                while (Time.realtimeSinceStartup - clicked < 20f)
                {
                    yield return new WaitForEndOfFrame();
                    PopFrame(ghost);
                    sawWall |= em.GetComponentData<MoveState>(ghost).Wall != 0;
                    Vector3 b = OwnBody.transform.position;
                    if (Time.realtimeSinceStartup - clicked > 1f && !Moving(ghost))
                    {
                        if (stoppedAt < 0f) { stoppedAt = Time.realtimeSinceStartup; bodyAtStop = b; }
                        else { glide = Mathf.Max(glide, (b - bodyAtStop).magnitude); if (Time.realtimeSinceStartup - stoppedAt > 1f) break; }
                    }
                }
                Vector3 end = ServerPosition(ghost) - Vector3.up * offset;
                PlayerPose ownPose = hasClient && clientWorld.IsCreated ? clientWorld.EntityManager.GetComponentData<PlayerPose>(clientGhost) : default;
                float apart = (new Vector3(ownPose.XMm, ownPose.YMm, ownPose.ZMm) * 0.001f - ServerPosition(ghost)).magnitude;
                bool took = plans.AutoWalls == autos0 + 1;
                int climbs = grip != null ? grip.ClimbsPaid - paid0 : 0;
                bool wayOk = expected == PlayerWallRunMotor.AutoWay.None ? !took
                    : expected == PlayerWallRunMotor.AutoWay.OverWall ? took && sawWall : took && climbs == 1;
                float miss = Vector3.Distance(end, to);
                // the plain walk ends where NavMesh lets it (a walk may not reach a roof): only Wallstride's tries must end on the place
                bool there = expected == PlayerWallRunMotor.AutoWay.None || miss < 0.6f;
                bool ok = wayOk && there && apart < 0.05f && glide < 0.02f && m_Pops.Pops == 0;
                if (ok) tally.Passed++;
                Log($"auto: {what} clicked at {at}: {(ok ? "PASS" : "FAIL")}; expected {expected}, the server {(took ? "took it as Wallstride's" : "made it a plain walk")} " +
                    $"(\"{(plans.WallPlanner as BodyWallPlanner)?.LastDecision}\"); on a wall {sawWall}, climbs paid {climbs}; ended {miss:F2} m from the click at " +
                    $"{end.x:F1},{end.y:F2},{end.z:F1}; owner {apart * 100f:F1} cm from the server; glide {glide * 100f:F1} cm; pops {m_Pops.Pops}" +
                    $"{(m_Pops.Pops > 0 ? " (worst: " + m_Pops.Worst + ")" : "")}; Flux {flux0:F0} -> {Flux(body):F0}");
                yield return new WaitForSeconds(0.8f);
            }

            /// <summary>Zone01's placed tree near the spawn (SM_Env_Tree_01, its trunk; the tree tries and the catch use it).</summary>
            static readonly Vector3 PlacedTree = new Vector3(12.6f, 0f, -13.1f);

            /// <summary>
            /// The test places planned before the run (owner, 1 Nov), proved in edit mode by Zone01WallPropProbe.ThePlannedTestPlaces_StandInZone01:
            /// in front of the wall walk's test graph wall (the face at z -45.3, facing +z), and an 8 m terrain tree truly in the open (nothing but the
            /// ground within 4 m; Zone01WallPropProbe.FindTerrainTreesInTheOpen; the first pick, 186.7,-82.1, stood against Ruin 26's rubble).
            /// </summary>
            static readonly Vector3 TestGraphWall = new Vector3(-10.1f, 0f, -40f), TestTerrainTree = new Vector3(31.85f, 1.06f, -215.76f);

            /// <summary>One scripted Jump click at a wall (part 4), judged: planned as a catch, handed off where the jump met the face, on to the top.</summary>
            IEnumerator CatchTry(GameObject body, Entity ghost, VaultTally tally, string what, Vector3 click, Vector3 normal, Vector3 from, float offset, CatchResult result,
                bool onWall = false)
            {
                if (OverTheCap()) yield break;
                World server = KnBootstrap.ServerWorld;
                var em = server.EntityManager;
                var jumps = server.GetExistingSystemManaged<PlayerJumpServerSystem>();
                var grip = server.GetExistingSystemManaged<WallGripSystem>();
                bool hasClient = TryLocalGhost(out World clientWorld, out Entity clientGhost, out _);
                tally.Tried++;
                int catches0 = jumps.Catches, paid0 = grip.ClimbsPaid;
                float flux0 = Flux(body);
                string at = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                BodyClicks.Wall(click, normal, false, true);
                float clicked = Time.realtimeSinceStartup, stoppedAt = -1f, glide = 0f;
                Vector3 bodyAtStop = default;
                float caughtAt = float.NaN;
                bool handed = false;
                m_Pops.Reset();
                while (Time.realtimeSinceStartup - clicked < 12f)
                {
                    yield return new WaitForEndOfFrame();
                    var plan = em.GetComponentData<MotionPlan>(ghost);
                    var move = em.GetComponentData<MoveState>(ghost);
                    if (!handed && JumpFlight.IsCatch(in plan) && move.OrderSeq == plan.PlanSeq)
                    {
                        handed = true;
                        caughtAt = ServerPosition(ghost).y - offset - from.y;
                    }
                    PopFrame(ghost);
                    Vector3 b = OwnBody.transform.position;
                    bool busy = Moving(ghost) || (plan.Kind == MotionKind.Jump && !handed);
                    if (!busy && Time.realtimeSinceStartup - clicked > 3f && !handed) break;   // never caught
                    if (handed && !Moving(ghost))
                    {
                        if (stoppedAt < 0f) { stoppedAt = Time.realtimeSinceStartup; bodyAtStop = b; }
                        else { glide = Mathf.Max(glide, (b - bodyAtStop).magnitude); if (Time.realtimeSinceStartup - stoppedAt > 1f) break; }
                    }
                }
                Vector3 end = ServerPosition(ghost) - Vector3.up * offset;
                PlayerPose ownPose = hasClient && clientWorld.IsCreated ? clientWorld.EntityManager.GetComponentData<PlayerPose>(clientGhost) : default;
                float apart = (new Vector3(ownPose.XMm, ownPose.YMm, ownPose.ZMm) * 0.001f - ServerPosition(ghost)).magnitude;
                bool planned = jumps.Catches == catches0 + 1;
                bool up = end.y > from.y + 1.5f;
                // onto a graph wall: no climb price (the wall walk's Grip instead), it ends on the wall by the place clicked
                bool there = onWall ? em.GetComponentData<MoveState>(ghost).Wall == 1 && Vector3.Distance(end, click) < 1.5f : grip.ClimbsPaid == paid0 + 1 && up;
                bool ok = planned && handed && there && apart < 0.05f && glide < 0.02f && m_Pops.Pops == 0;
                if (ok) tally.Passed++;
                result.Height = handed ? $"{caughtAt:F2} m" : "none";
                Log($"catch: {what} clicked at {at}: {(ok ? "PASS" : "FAIL")}; server {(planned ? "planned a catch" : "planned NO catch")} " +
                    $"(\"{(jumps.WallPlanner as BodyWallPlanner)?.LastDecision}\"{(string.IsNullOrEmpty(jumps.LastRefusal) ? "" : "; last jump refusal: " + jumps.LastRefusal)}); " +
                    $"caught {(handed ? caughtAt.ToString("F2", CultureInfo.InvariantCulture) + " m above the take-off" : "NEVER")} (click {click.y - from.y:F2} m up); " +
                    $"climb paid {grip.ClimbsPaid - paid0}; ended {end.y - from.y:F2} m up at {end.x:F1},{end.y:F2},{end.z:F1} (expected {(onWall ? "on the wall by the click, " + Vector3.Distance(end, click).ToString("F2", CultureInfo.InvariantCulture) + " m from it" : "on the top or perched")}); " +
                    $"owner {apart * 100f:F1} cm from the server; glide {glide * 100f:F1} cm; pops {m_Pops.Pops}{(m_Pops.Pops > 0 ? " (worst: " + m_Pops.Worst + ")" : "")}; " +
                    $"Flux {flux0:F0} -> {Flux(body):F0}");
                yield return new WaitForSeconds(0.8f);
            }

            /// <summary>The scripted wall walk run (env KN_PLAYHERE_WALL=1; M3c, `m3c-wallstride-design.md` section 3, step 5).</summary>
            static bool WallRun => Environment.GetEnvironmentVariable("KN_PLAYHERE_WALL") == "1";

            /// <summary>
            /// The scripted wall walk (M3c): level 60 and the top ranks for the tester, a wall near the spawn found in Zone01's own surface
            /// graph, the tester put 6 m in front of it, then nine tries, each logged with the wall clock for the video and judged: onto the
            /// wall with Run, along it, over its edge onto the roof, from the roof down onto the face (the way down), off it to the ground, a
            /// Jump onto the wall, a Jump off it, out of Grip (pushed off), and a plain click past the wall's foot while on it. The instrument
            /// first: the server's planner on a known refusal (level 1: the gate) and a known plan (level 60) before any click.
            /// </summary>
            IEnumerator Walls(GameObject body, Entity ghost, VaultTally tally)
            {
                World server = KnBootstrap.ServerWorld;
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                var grip = server.GetExistingSystemManaged<WallGripSystem>();
                var planner = plans != null ? plans.WallPlanner as BodyWallPlanner : null;
                var motor = body.GetComponent<PlayerWallRunMotor>();
                if (planner == null || grip == null || motor == null) { Log("wall: FAIL; the server has no wall walk planner, Grip system or wall motor"); tally.Tried++; yield break; }
                var em = server.EntityManager;
                var progression = body.GetComponent<PlayerCharacterProgression>();
                var resources = body.GetComponent<PlayerResources>();
                float offset = body.GetComponent<UnityEngine.AI.NavMeshAgent>() is UnityEngine.AI.NavMeshAgent ag ? ag.baseOffset : 0f;

                string why = motor.FindTestWall(ServerPosition(ghost) - Vector3.up * offset, out PlayerWallRunMotor.TestWall wall);
                if (why != null) { Log("wall: NOT TESTED; " + why); tally.Tried++; yield break; }
                Log($"wall: the test wall's foot at {wall.Foot.x:F1},{wall.Foot.y:F2},{wall.Foot.z:F1}, facing {wall.Normal.x:F2},{wall.Normal.z:F2}; " +
                    $"the roof at {wall.Top.x:F1},{wall.Top.y:F2},{wall.Top.z:F1}");
                // the pop check (owner, 1 Nov: run at running speed, look for any slide or glitch) proved first: standing still is clean,
                // the teleport 9 m out in front of the wall (where the running tries start) is a known pop
                m_Pops.Reset();
                float watch = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - watch < 1f) { yield return new WaitForEndOfFrame(); PopFrame(ghost); }
                Log($"wall: pop check on a known clean case (standing 1 s): {m_Pops.Pops} pop(s) (expected 0) {m_Pops.Worst}");
                m_Pops.Reset();
                PopFrame(ghost);
                Teleport(em, ghost, wall.FrontFar + Vector3.up * offset);
                watch = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - watch < 1.5f) { yield return new WaitForEndOfFrame(); PopFrame(ghost); }
                Log($"wall: pop check on a known fault (the teleport 9 m out): {m_Pops.Pops} pop(s) (expected 1 or more): {m_Pops.Worst}");

                // the prop check (owner, 1 Nov: no clipping; gui-wall-2's "along" ended inside an air-conditioner box) proved first on the
                // node it ended at (a known fault: the box is in the body there) and the node "onto" goes to (known clean)
                Log($"wall: prop check on a known fault (the air-conditioner stretch, {wall.Along.x:F1},{wall.Along.y:F2},{wall.Along.z:F1}): " +
                    $"{(PropAt(wall.Along, wall.Normal, body) ? "a prop in the body" : "NO prop found")}; on a known clean face (onto): " +
                    $"{(PropAt(wall.Onto, wall.Normal, body) ? "a PROP in the body" : "clear")}");

                // the instrument on a known refusal and a known plan (no click)
                string Probe()
                {
                    var corners = new Unity.Collections.NativeList<Unity.Mathematics.int3>(PlayerLimits.MaxWallCorners, Unity.Collections.Allocator.Temp);
                    var legs = new Unity.Collections.NativeList<WallCorner>(PlayerLimits.MaxWallCorners, Unity.Collections.Allocator.Temp);
                    PlayerPose pose = em.GetComponentData<PlayerPose>(ghost);
                    var answer = planner.Plan(ghost, pose.Mm, pose.YawCdeg, default, Mm(wall.Onto), Permille(wall.Normal), true, false, false, corners, legs, out _, out _);
                    int n = corners.Length;
                    corners.Dispose();
                    legs.Dispose();
                    return answer + " (\"" + planner.LastDecision + "\", " + n + " corners)";
                }
                Log($"wall planner check at level {(progression != null ? progression.CurrentLevel : 0)} (expected Refused, the gate): {Probe()}");
                if (progression != null && progression.CurrentLevel < 60) progression.SetLevel(60);
                yield return new WaitForSeconds(1f);   // the level reaches the ghost (MoveRanks) and the owner
                Log($"wall planner check at level {(progression != null ? progression.CurrentLevel : 0)} (expected Planned, onto the wall): {Probe()}");
                if (resources != null) resources.RestoreChi(resources.MaxChi);

                // the pop check on a known clean case at full speed: a plain run along the street (Run, level 60), no wall
                Vector3 street = wall.FrontFar + Vector3.Cross(Vector3.up, wall.Normal).normalized * 15f;
                if (UnityEngine.AI.NavMesh.SamplePosition(street, out UnityEngine.AI.NavMeshHit streetHit, 3f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    m_Pops.Reset();
                    BodyClicks.Ground(streetHit.position, true);
                    float ran = Time.realtimeSinceStartup;
                    int top = 0;
                    bool started = false;
                    while (Time.realtimeSinceStartup - ran < 6f)
                    {
                        yield return new WaitForEndOfFrame();
                        PopFrame(ghost);
                        top = Mathf.Max(top, em.GetComponentData<MoveState>(ghost).SpeedMmps);
                        if (Moving(ghost)) started = true;
                        else if (started) break;
                    }
                    Log($"wall: pop check on a known clean case (a plain run of {Flat(streetHit.position - wall.FrontFar):F1} m along the street at {top} mm/s): " +
                        $"{m_Pops.Pops} pop(s) (expected 0) {m_Pops.Worst}");
                    yield return new WaitForSeconds(0.5f);
                    BodyClicks.Ground(wall.FrontFar, true);   // back to the start, 9 m out
                    yield return new WaitForSeconds(2f);
                }
                else Log("wall: pop check on a plain run: NOT TESTED (no NavMesh 15 m along the street)");

                Vector3 up = Vector3.up;
                yield return WallTry(body, ghost, tally, "onto the wall from the ground (Run)", wall.Onto, wall.Normal, true, false, true, offset);
                yield return WallTry(body, ghost, tally, "along the wall", wall.Along, wall.Normal, true, false, true, offset);
                yield return WallTry(body, ghost, tally, "over its edge onto the roof", wall.Top, up, true, false, false, offset);
                yield return WallTry(body, ghost, tally, "from the roof down onto the face (the way down)", wall.Lower, wall.Normal, true, false, true, offset);
                yield return WallTry(body, ghost, tally, "off it to the ground in front", wall.Front, up, true, false, false, offset);
                yield return WallTry(body, ghost, tally, "a Jump onto the wall", wall.Onto, wall.Normal, false, true, true, offset);
                yield return WallTry(body, ghost, tally, "a Jump off it to the ground", wall.FrontFar, up, false, true, false, offset);
                // onto it again, then the Flux run down: pushed off to the ground in front (NGO's only forced way off)
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                yield return WallTry(body, ghost, tally, "onto the wall again (Run)", wall.Lower, wall.Normal, true, false, true, offset);
                int push0 = grip.PushOffs;
                if (resources != null && resources.Chi > 10f) resources.TrySpendChi(resources.Chi - 10f);
                float drained = Time.realtimeSinceStartup;
                while (grip.PushOffs == push0 && Time.realtimeSinceStartup - drained < 6f) yield return null;
                float pushedAt = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - pushedAt < 8f && (Moving(ghost) || em.GetComponentData<MoveState>(ghost).Wall != 0)) yield return null;
                tally.Tried++;
                bool down = grip.PushOffs == push0 + 1 && em.GetComponentData<MoveState>(ghost).Wall == 0
                    && UnityEngine.AI.NavMesh.SamplePosition(ServerPosition(ghost) - Vector3.up * offset, out _, 0.5f, UnityEngine.AI.NavMesh.AllAreas);
                if (down) tally.Passed++;
                Log($"wall: out of Grip at {DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}: {(down ? "PASS" : "FAIL")}; pushed off " +
                    $"{grip.PushOffs - push0}x, down at {ServerPosition(ghost).x:F1},{ServerPosition(ghost).y:F2},{ServerPosition(ghost).z:F1} after {Time.realtimeSinceStartup - pushedAt:F1} s; Flux {Flux(body):F0}");
                if (resources != null) resources.RestoreChi(resources.MaxChi);
                yield return new WaitForSeconds(0.8f);
                // on the wall, a plain click past its foot (not a wall click): NGO takes every click on a wall as the wall's (down, then on)
                yield return WallTry(body, ghost, tally, "onto the wall (Run) for the plain click", wall.Onto, wall.Normal, true, false, true, offset);
                yield return WallTry(body, ghost, tally, "a plain click past the wall's foot, from the wall", wall.FrontFar, up, true, false, false, offset, plain: true);
                Log($"wall: Grip paid {grip.Paid} points in all, {grip.PushOffs} push-off(s), {plans.WallWalks} wall walks planned");
            }

            /// <summary>
            /// One scripted wall click, logged and judged: the server planned it as the wall's (or refused it, said why), the owner on the same
            /// plan and within 5 cm of the server, the body where clicked (within 0.8 m: the nearest node), on the wall or not as expected,
            /// tilted onto the face on it and upright off it, and no glide over 2 cm after the stop.
            /// </summary>
            IEnumerator WallTry(GameObject body, Entity ghost, VaultTally tally, string what, Vector3 click, Vector3 normal, bool run, bool jump,
                bool expectWall, float offset, bool plain = false)
            {
                if (OverTheCap()) yield break;
                World server = KnBootstrap.ServerWorld;
                var em = server.EntityManager;
                var plans = server.GetExistingSystemManaged<PlayerServerPlanSystem>();
                bool hasClient = TryLocalGhost(out World clientWorld, out Entity clientGhost, out _);
                tally.Tried++;
                int walks0 = plans.WallWalks;
                float flux0 = Flux(body);
                string at = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                if (plain) BodyClicks.Ground(click, run);
                else BodyClicks.Wall(click, normal, run, jump);
                float clicked = Time.realtimeSinceStartup, stoppedAt = -1f, glide = 0f;
                Vector3 bodyAtStop = default;
                bool moving = false;
                int topSpeed = 0, inProp = 0;
                string plant45 = null;
                float groundY = UnityEngine.AI.NavMesh.SamplePosition(ServerPosition(ghost) - Vector3.up * offset, out UnityEngine.AI.NavMeshHit g0, 2f, UnityEngine.AI.NavMesh.AllAreas)
                    ? g0.position.y : ServerPosition(ghost).y - offset;
                m_Pops.Reset();
                while (Time.realtimeSinceStartup - clicked < 20f)
                {
                    yield return new WaitForEndOfFrame();
                    bool now = Moving(ghost);
                    if (now) moving = true;
                    else if (!moving && Time.realtimeSinceStartup - clicked > 3f) break;   // never started (refused, or no way)
                    Vector3 b = OwnBody.transform.position;
                    float speed = ServerSpeed(ghost);
                    topSpeed = Mathf.Max(topSpeed, Mathf.RoundToInt(speed * 1000f));
                    PopFrame(ghost);
                    // the plant (owner, 1 Nov): how high the feet are as the drawn body passes 45 degrees onto the face
                    if (plant45 == null && Vector3.Angle(OwnBody.transform.up, Vector3.up) >= 45f && em.GetComponentData<PoseTilt>(ghost).Phase == WallPhase.Plant)
                    {
                        Vector3 f = b - OwnBody.transform.up * offset;
                        plant45 = $"feet {f.y - groundY:F2} m above the ground and {Vector3.Dot(f - click, normal):F2} m out from the face";
                    }
                    // on the wall: the drawn body never inside a prop on the face
                    var shownTilt = em.GetComponentData<PoseTilt>(ghost);
                    if (shownTilt.Tilted && em.GetComponentData<MoveState>(ghost).Wall != 0
                        && PropAt(b - (Vector3)shownTilt.Up * offset, shownTilt.Up, body)) inProp++;
                    if (moving && !now)
                    {
                        if (stoppedAt < 0f) { stoppedAt = Time.realtimeSinceStartup; bodyAtStop = b; }
                        else
                        {
                            glide = Mathf.Max(glide, (b - bodyAtStop).magnitude);
                            if (Time.realtimeSinceStartup - stoppedAt > 1f) break;
                        }
                    }
                }
                var move = em.GetComponentData<MoveState>(ghost);
                var tilt = em.GetComponentData<PoseTilt>(ghost);
                var wall = em.GetComponentData<WallPlan>(ghost);
                var own = hasClient && clientWorld.IsCreated ? clientWorld.EntityManager.GetComponentData<ClientWallPlan>(clientGhost).Plan : default;
                byte ownWall = hasClient && clientWorld.IsCreated ? clientWorld.EntityManager.GetComponentData<MoveState>(clientGhost).Wall : (byte)255;
                PlayerPose ownPose = hasClient && clientWorld.IsCreated ? clientWorld.EntityManager.GetComponentData<PlayerPose>(clientGhost) : default;
                bool planned = plans.WallWalks == walks0 + 1;
                Vector3 root = ServerPosition(ghost);
                Vector3 feet = root - (tilt.Tilted ? (Vector3)tilt.Up : Vector3.up) * offset;
                float off = (feet - click).magnitude;
                float apart = (new Vector3(ownPose.XMm, ownPose.YMm, ownPose.ZMm) * 0.001f - root).magnitude;
                bool onWall = move.Wall != 0;
                bool tiltRight = onWall ? tilt.Tilted && Vector3.Dot(tilt.Up, normal) > 0.8f : !tilt.Tilted;
                bool ok = planned && onWall == expectWall && ownWall == move.Wall && own.Seq != 0 && apart < 0.05f && tiltRight
                    && glide < 0.02f && off < (expectWall ? 1.5f : 1.0f) && inProp == 0 && m_Pops.Pops == 0;   // owner pick a, 1 Nov: pops judged against the drawn path
                if (ok) tally.Passed++;
                string refusal = plans.LastWallRefusal;
                Log($"wall: {what} clicked at {at}: {(ok ? "PASS" : "FAIL")}; server {(planned ? "planned the wall walk" : "planned NO wall walk")} " +
                    $"(\"{(plans.WallPlanner as BodyWallPlanner)?.LastDecision}\"{(string.IsNullOrEmpty(refusal) ? "" : "; last refusal: " + refusal)}), plan seq {wall.Seq} " +
                    $"(owner's own {own.Seq}), on the wall: server {move.Wall}, owner {ownWall} (expected {(expectWall ? 1 : 0)}); tilt up {tilt.UpX},{tilt.UpY},{tilt.UpZ} " +
                    $"phase {tilt.Phase} ({(tiltRight ? "right" : "WRONG")}); ended {off:F2} m from the click at {feet.x:F1},{feet.y:F2},{feet.z:F1}; owner {apart * 100f:F1} cm " +
                    $"from the server; glide after the stop {glide * 100f:F1} cm (limit 2); speed on the wall {wall.SpeedMmps} mm/s, top speed {topSpeed} mm/s " +
                    $"({(run ? "Run" : jump ? "Jump" : "walk")}); pops {m_Pops.Pops} (limit 0{(m_Pops.Pops > 0 ? "; worst: " + m_Pops.Worst : "")}); frames inside a prop {inProp} (limit 0); at 45 degrees in the plant: {plant45 ?? "no plant"}; Flux {flux0:F0} -> {Flux(body):F0}");
                yield return new WaitForSeconds(0.8f);
            }

            /// <summary>
            /// A slide or glitch of the drawn body (owner, 1 Nov; pick a: judged against the drawn body's own path, the point the follow aims
            /// it at each frame). A pop is a frame where the body moves more than its target did (+ 20 cm: a follow glitch), or the target itself
            /// jumps farther than the run speed allows over the frame (x 1.5, at least 1 m: a path glitch, as a teleport). Proved in the same run
            /// on a plain run at full speed (clean) and the teleport (fault) before the tries.
            /// </summary>
            sealed class PopWatch
            {
                Vector3 m_PrevBody, m_PrevTarget;
                bool m_Has;
                public int Pops;
                public float WorstM;
                public string Worst = "";

                public void Reset() { m_Has = false; Pops = 0; WorstM = 0f; Worst = ""; }

                /// <summary>
                /// Called at the end of a frame (<c>WaitForEndOfFrame</c>), after the entity simulation has placed the body, so the move is
                /// judged by the time of the frame that made it. A plain <c>yield return null</c> resumes BEFORE the simulation and pairs last
                /// frame's move with this frame's time: gui-pop-1 (1 Nov) read a 61 ms frame's run as "the path jumped 1.69 m in 33 ms".
                /// </summary>
                public void Frame(Vector3 body, Vector3 target, float runMps, float dt)
                {
                    if (m_Has)
                    {
                        float bodyStep = (body - m_PrevBody).magnitude, targetStep = (target - m_PrevTarget).magnitude;
                        float follow = bodyStep - targetStep - 0.2f;
                        float path = targetStep - Mathf.Max(1f, runMps * dt * 1.5f);
                        float over = Mathf.Max(follow, path);
                        if (over > 0f)
                        {
                            Pops++;
                            if (over > WorstM) { WorstM = over; Worst = follow >= path ? $"the body jumped {bodyStep:F2} m while its path moved {targetStep:F2} m" : $"the path jumped {targetStep:F2} m in {dt * 1000f:F0} ms"; }
                        }
                    }
                    m_PrevBody = body;
                    m_PrevTarget = target;
                    m_Has = true;
                }
            }

            /// <summary>The drawn body and the point the follow aimed it at this frame, and the run speed (m/s).</summary>
            void PopFrame(Entity ghost)
            {
                var state = OwnBody.GetComponent<PlayerPresentationState>();
                Vector3 b = OwnBody.transform.position;
                var em = KnBootstrap.ServerWorld.EntityManager;
                float run = em.GetComponentData<MoveStats>(ghost).RunMmps * 0.001f;
                m_Pops.Frame(b, state != null && state.HasPose ? state.TargetPosition : b, Mathf.Max(run, ServerSpeed(ghost)), Time.unscaledDeltaTime);
            }

            readonly PopWatch m_Pops = new PopWatch();

            /// <summary>A prop on the face in a body standing at <paramref name="feet"/> (the wall walk's own test), the face found in along the normal.</summary>
            static bool PropAt(Vector3 feet, Vector3 normal, GameObject body)
            {
                Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
                Collider face = Physics.Raycast(feet + n * 0.4f, -n, out RaycastHit hit, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.collider : null;
                return WallWalkLayout.PropOnFace(feet, n, face, body.transform.root, Physics.DefaultRaycastLayers & ~(1 << body.layer));
            }

            /// <summary>The server's walk speed for the ghost now (a leg's own speed on a timed leg), m/s.</summary>
            static float ServerSpeed(Entity ghost)
            {
                var em = KnBootstrap.ServerWorld.EntityManager;
                var m = em.GetComponentData<MoveState>(ghost);
                return (m.Flags & MoveFlags.Moving) != 0 ? m.SpeedMmps * 0.001f : 0f;
            }

            static Unity.Mathematics.int3 Permille(Vector3 v) =>
                new Unity.Mathematics.int3(Mathf.RoundToInt(v.x * 1000f), Mathf.RoundToInt(v.y * 1000f), Mathf.RoundToInt(v.z * 1000f));

            /// <summary>
            /// A point on the river the server's own jump planner takes from where the body stands (rings of 6-20 m, every 30 degrees); null
            /// when one is found, else why not (the planner's answers counted).
            /// </summary>
            static string JumpOntoWaterSpot(Entity ghost, out Vector3 spot)
            {
                spot = default;
                World server = KnBootstrap.ServerWorld;
                var jumps = server.GetExistingSystemManaged<PlayerJumpServerSystem>();
                if (jumps == null || jumps.Planner == null) return "the server has no jump planner";
                PlayerPose pose = server.EntityManager.GetComponentData<PlayerPose>(ghost);
                Vector3 from = new Vector3(pose.XMm, pose.YMm, pose.ZMm) * 0.001f;
                var why = new Dictionary<string, int>();
                for (float radius = 6f; radius <= 20f; radius += 2f)
                for (int deg = 0; deg < 360; deg += 30)
                {
                    float a = deg * Mathf.Deg2Rad;
                    Vector3 p = new Vector3(from.x + Mathf.Sin(a) * radius, -1.2f, from.z + Mathf.Cos(a) * radius);
                    if (jumps.Planner.TryPlan(ghost, pose.Mm, Mm(p), out _, out _, out string reason)
                        && WaterstrideRulesProbe(p)) { spot = p; return null; }
                    string key = reason ?? "landing not on the water";
                    why[key] = why.TryGetValue(key, out int c) ? c + 1 : 1;
                }
                var parts = new List<string>();
                foreach (var kv in why) parts.Add($"{kv.Value}x \"{kv.Key}\"");
                return "no spot on the water the jump planner takes; why not: " + string.Join(", ", parts);
            }

            /// <summary>The point is on Zone01's runnable water (its map), so a jump there comes down on the water.</summary>
            static bool WaterstrideRulesProbe(Vector3 p) => PlayerWaterstrideMotor.TryWaterLandingAt(p, out _);

            static void Teleport(EntityManager em, Entity ghost, Vector3 root)
            {
                PlayerPose pose = em.GetComponentData<PlayerPose>(ghost);
                em.SetComponentData(ghost, new TeleportRequest { XMm = Mathf.RoundToInt(root.x * 1000f), YMm = Mathf.RoundToInt(root.y * 1000f), ZMm = Mathf.RoundToInt(root.z * 1000f), YawCdeg = pose.YawCdeg });
                em.SetComponentEnabled<TeleportRequest>(ghost, true);
            }

            static Unity.Mathematics.int3 Mm(Vector3 m) =>
                new Unity.Mathematics.int3(Mathf.RoundToInt(m.x * 1000f), Mathf.RoundToInt(m.y * 1000f), Mathf.RoundToInt(m.z * 1000f));

            /// <summary>
            /// Points straight across the planters (the hedges, `SM_Env_Hedge*`) nearest to <paramref name="from"/>: for each, 4, 6 and 8 m past
            /// its middle on the line from the body, on the NavMesh. The vaulting of the NGO game was built over these at the spawn.
            /// </summary>
            static List<Vector3> HedgeGoals(Vector3 from, int hedges)
            {
                var found = new List<Transform>();
                foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (t.name.StartsWith("SM_Env_Hedge", StringComparison.Ordinal)) found.Add(t);
                found.Sort((a, b) => Flat(a.position - from).CompareTo(Flat(b.position - from)));
                var goals = new List<Vector3>();
                for (int i = 0; i < found.Count && i < hedges; i++)
                {
                    Vector3 middle = found[i].GetComponentInChildren<Renderer>() is Renderer r ? r.bounds.center : found[i].position;
                    Vector3 way = middle - from;
                    way.y = 0f;
                    if (way.sqrMagnitude < 0.01f) continue;
                    way.Normalize();
                    foreach (float past in new[] { 4f, 6f, 8f })
                    {
                        Vector3 p = middle + way * past;
                        if (Physics.Raycast(new Vector3(p.x, from.y + 50f, p.z), Vector3.down, out RaycastHit hit, 100f)
                            && UnityEngine.AI.NavMesh.SamplePosition(hit.point, out UnityEngine.AI.NavMeshHit nav, 0.5f, UnityEngine.AI.NavMesh.AllAreas))
                            goals.Add(nav.position);
                    }
                }
                return goals;
            }

            /// <summary>A click the server's own planners vault on the way to: on a ring round the body, the walk on from the landing at least 4 m (or 0.3-1.5 m for the short click).</summary>
            static bool FindVault(PlayerServerPlanSystem plans, Entity ghost, bool shortClick, out Vector3 goal, out string found)
            {
                goal = default;
                World server = KnBootstrap.ServerWorld;
                PlayerPose pose = server.EntityManager.GetComponentData<PlayerPose>(ghost);
                MoveStats stats = server.EntityManager.GetComponentData<MoveStats>(ghost);
                Vector3 from = new Vector3(pose.XMm, pose.YMm, pose.ZMm) * 0.001f;
                var corners = new Unity.Collections.NativeList<Unity.Mathematics.int3>(PlayerLimits.MaxCorners, Unity.Collections.Allocator.Temp);
                // why each candidate got no vault (the planner's own words), so "no obstacle near" and "the planner refuses" read apart
                var reasons = new Dictionary<string, int>();
                void Count(string why) => reasons[why] = reasons.TryGetValue(why, out int c) ? c + 1 : 1;
                var bodyPlanner = plans.VaultPlanner as BodyVaultPlanner;
                try
                {
                    // the planters near the body first (straight across each), then rings
                    foreach (Vector3 across in HedgeGoals(from, 6))
                    {
                        var goalMm = new Unity.Mathematics.int3(Mathf.RoundToInt(across.x * 1000f), Mathf.RoundToInt(across.y * 1000f), Mathf.RoundToInt(across.z * 1000f));
                        if (!VaultPath.Plan(plans.Planner, plans.VaultPlanner, ghost, pose.Mm, goalMm, VaultRun ? stats.RunMmps : stats.WalkMmps, corners, out VaultPlan hv) || hv.LegLengthMm <= 0)
                        { Count("planter: " + (bodyPlanner != null && bodyPlanner.LastDecision != null ? bodyPlanner.LastDecision : "no way or no vault")); continue; }
                        int left = PlayerPath.LengthMm(pose.Mm, corners.AsArray(), corners.Length) - hv.LegStartMm - hv.LegLengthMm;
                        if (shortClick ? left < 300 || left > 1500 : left < 4000) { Count("planter: vault, but the walk on from the landing too " + (shortClick ? "long or short" : "short")); continue; }
                        goal = across;
                        found = $"a planter {Flat(across - from):F1} m out, the walk on from the landing {left / 1000f:F1} m";
                        return true;
                    }
                    foreach (float radius in new[] { 5f, 8f, 12f, 16f, 22f, 30f, 40f })
                        for (int i = 0; i < 48; i++)
                        {
                            float a = i * 7.5f * Mathf.Deg2Rad;
                            Vector3 p = from + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius;
                            if (!Physics.Raycast(new Vector3(p.x, from.y + 50f, p.z), Vector3.down, out RaycastHit hit, 100f)) { Count("no ground under the point"); continue; }
                            if (!UnityEngine.AI.NavMesh.SamplePosition(hit.point, out UnityEngine.AI.NavMeshHit nav, 0.5f, UnityEngine.AI.NavMesh.AllAreas)) { Count("point off the NavMesh"); continue; }
                            var goalMm = new Unity.Mathematics.int3(Mathf.RoundToInt(nav.position.x * 1000f), Mathf.RoundToInt(nav.position.y * 1000f), Mathf.RoundToInt(nav.position.z * 1000f));
                            if (!VaultPath.Plan(plans.Planner, plans.VaultPlanner, ghost, pose.Mm, goalMm, VaultRun ? stats.RunMmps : stats.WalkMmps, corners, out VaultPlan v) || v.LegLengthMm <= 0)
                            { Count(bodyPlanner != null && bodyPlanner.LastDecision != null ? bodyPlanner.LastDecision : "no way or no vault"); continue; }
                            int after = PlayerPath.LengthMm(pose.Mm, corners.AsArray(), corners.Length) - v.LegStartMm - v.LegLengthMm;
                            if (shortClick ? after < 300 || after > 1500 : after < 4000) { Count("vault, but the walk on from the landing too " + (shortClick ? "long or short" : "short")); continue; }
                            goal = nav.position;
                            found = $"obstacle {radius:F0} m out at {i * 7.5f:F1} deg, the walk on from the landing {after / 1000f:F1} m";
                            return true;
                        }
                    var parts = new List<string>();
                    foreach (var r in reasons) parts.Add($"{r.Value}x \"{r.Key}\"");
                    found = "no vaultable obstacle on rings of 5-40 m round the body; why not: " + string.Join(", ", parts);
                    return false;
                }
                finally { corners.Dispose(); }
            }

            /// <summary>
            /// The ranks reach the owner (MoveRanks) and a jump the rank does not allow is refused by the owner's own check and by the
            /// server with the same words: rank 0 ("Learn Jump first."), then rank 1 at a spot past its distance. Top ranks again after.
            /// </summary>
            IEnumerator LowRankRefusals(GameObject body, Entity ghost, PlayerMovementSkills skills, JumpOutcome outcome)
            {
                var serverJumps = KnBootstrap.ServerWorld.GetExistingSystemManaged<PlayerJumpServerSystem>();
                var ownSkills = OwnBody.GetComponent<PlayerMovementSkills>();
                var click = OwnBody.GetComponent<PointAndClickPlayer>();
                if (skills == null || ownSkills == null || click == null || serverJumps == null)
                { Log("low rank: FAIL; no movement skills, click or server jump system to test with"); outcome.Ok = false; yield break; }
                int run = skills.RunRank, top = skills.JumpRank;
                MovementSkillData jumpData = skills.GetDefinition(MovementSkillId.Jump);
                float rank1Distance = jumpData != null && jumpData.TryGetRank(1, out MovementSkillRank r1) ? r1.jumpDistance : 20f;
                float topCost = jumpData != null && jumpData.TryGetRank(top, out MovementSkillRank rt) ? rt.chiCost : 5f;
                var serverFlux = body.GetComponent<PlayerResources>();
                var ownFlux = OwnBody.GetComponent<PlayerResources>();
                Vector3 from = ServerPosition(ghost);
                bool hasClient = TryLocalGhost(out World clientWorld, out Entity clientGhost, out _);
                // rank 0 ("Learn Jump first."), rank 1 past its distance, then the top rank without the Flux for one jump
                foreach (int rank in new[] { 0, 1, -1 })
                {
                    bool fluxCase = rank < 0;
                    float set = Time.realtimeSinceStartup;
                    bool mirrored;
                    if (fluxCase)
                    {
                        if (serverFlux == null || ownFlux == null) { Log("low Flux: FAIL; no resources"); outcome.Ok = false; continue; }
                        skills.GmSetMaxRanks();
                        while (ownSkills.JumpRank != top && Time.realtimeSinceStartup - set < 3f) yield return null;
                        serverFlux.TrySpendChi(Mathf.Max(0f, serverFlux.Chi - (topCost - 1f)));   // one Flux short of a jump
                        set = Time.realtimeSinceStartup;
                        while (ownFlux.Chi >= topCost && Time.realtimeSinceStartup - set < 3f) yield return null;
                        mirrored = ownFlux.Chi < topCost && ownSkills.JumpRank == top;
                    }
                    else
                    {
                        skills.RestoreSave(JsonUtility.ToJson(new MovementSkillState { runRank = (byte)run, jumpRank = (byte)rank }));
                        while (ownSkills.JumpRank != rank && Time.realtimeSinceStartup - set < 3f) yield return null;
                        mirrored = ownSkills.JumpRank == rank;
                    }
                    // rank 0 and Flux: a spot 2 m off; rank 1: a spot 1.5x its distance away (past the limit), on the ground where one is found
                    Vector3 p = from + new Vector3(2f, 0f, 0f);
                    if (rank == 1)
                        foreach (Vector3 way in new[] { Vector3.right, Vector3.forward, Vector3.left, Vector3.back })
                        {
                            Vector3 q = from + way * (rank1Distance * 1.5f);
                            if (Physics.Raycast(new Vector3(q.x, 500f, q.z), Vector3.down, out RaycastHit h, 1000f)
                                && UnityEngine.AI.NavMesh.SamplePosition(h.point, out _, 0.3f, UnityEngine.AI.NavMesh.AllAreas)) { p = h.point; break; }
                        }
                    if (rank != 1 && Physics.Raycast(new Vector3(p.x, 500f, p.z), Vector3.down, out RaycastHit g, 1000f)) p = g.point;
                    string clientWhy = click.ViewJumpRefusal(p);
                    int refused0 = serverJumps.Refused, planned0 = serverJumps.Planned;
                    ushort clientSeq0 = hasClient ? clientWorld.EntityManager.GetComponentData<ClientMotionPlan>(clientGhost).Plan.PlanSeq : (ushort)0;
                    float bodyY0 = OwnBody.transform.position.y, bodyRise = 0f;
                    BodyClicks.Jump(p);   // sent anyway (a real click stops at the owner's check): the server must refuse it on its own
                    float sent = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - sent < 0.8f)
                    {
                        yield return new WaitForEndOfFrame();
                        bodyRise = Mathf.Max(bodyRise, OwnBody.transform.position.y - bodyY0);
                    }
                    string serverWhy = serverJumps.Refused > refused0 ? serverJumps.LastRefusal : null;
                    // no visible start: the owner made no plan of its own for it and the drawn body never left the ground
                    bool predicted = hasClient && clientWorld.IsCreated
                        && clientWorld.EntityManager.GetComponentData<ClientMotionPlan>(clientGhost).Plan.PlanSeq != clientSeq0;
                    bool ok = mirrored && clientWhy != null && serverWhy != null && clientWhy == serverWhy && serverJumps.Planned == planned0
                        && !predicted && bodyRise < 0.02f;
                    if (!ok) outcome.Ok = false;
                    string label = fluxCase ? $"low Flux (server {serverFlux.Chi:F0}, owner {ownFlux.Chi:F0}, a jump costs {topCost:F0})" : $"low rank {rank}";
                    Log($"{label}: {(ok ? "PASS" : "FAIL")}; the server's numbers {(mirrored ? "arrived" : "NEVER arrived")} at the owner after " +
                        $"{Time.realtimeSinceStartup - set:F2} s (owner Jump rank {ownSkills.JumpRank}); jump to {p.x:F1},{p.z:F1} " +
                        $"({Flat(p - from):F1} m): client \"{clientWhy ?? "allowed"}\", server \"{serverWhy ?? (serverJumps.Planned > planned0 ? "PLANNED it" : "no answer")}\"; " +
                        $"owner {(predicted ? "PREDICTED it" : "made no plan")}, drawn body rose {bodyRise * 100f:F1} cm (limit 2)");
                }
                skills.GmSetMaxRanks();
                if (serverFlux != null) serverFlux.RestoreChi(serverFlux.MaxChi);
                float back = Time.realtimeSinceStartup;
                while ((ownSkills.JumpRank != skills.JumpRank || (ownFlux != null && serverFlux != null && ownFlux.Chi < serverFlux.Chi - 0.01f))
                       && Time.realtimeSinceStartup - back < 3f) yield return null;
                Log($"top ranks and Flux again: server Jump {skills.JumpRank} Flux {Flux(body):F0}, owner Jump {ownSkills.JumpRank} Flux {(ownFlux != null ? ownFlux.Chi : -1f):F0}");
            }

            /// <summary>
            /// One scripted jump (plan 53): press to lift on the own body, the server's apex and landing against its plan and the click,
            /// Flux taken once, the root never moved off the follow's pose by another writer, and the head anchor's worst step.
            /// </summary>
            IEnumerator JumpTo(Vector3 flat, GameObject body, Entity ghost, JumpOutcome outcome)
            {
                World serverWorld = KnBootstrap.ServerWorld;
                var planner = serverWorld.GetExistingSystemManaged<PlayerJumpServerSystem>();
                var pay = serverWorld.GetExistingSystemManaged<PlayerJumpPaySystem>();
                Vector3 p = flat;
                p.y = Physics.Raycast(new Vector3(p.x, 500f, p.z), Vector3.down, out RaycastHit hit, 1000f) ? hit.point.y : 0f;
                Vector3 from = ServerPosition(ghost);
                float flux = Flux(body);
                int planned0 = planner.Planned, refused0 = planner.Refused, paid0 = pay.Paid;
                Transform own = OwnBody.transform;
                var state = OwnBody.GetComponent<PlayerPresentationState>();
                var motor = OwnBody.GetComponentInChildren<PlayerAnimancerMotor>();
                int clipLines = 0;
                Vector3 bodyStart = own.position;
                float lift = -1f, bodyTop = float.MinValue, serverTop = float.MinValue, worstRoot = 0f, worstArc = 0f;
                // the press as the real click makes it: the owner's own rules first (PointAndClickPlayer view mode), then the click
                var click = OwnBody.GetComponent<PointAndClickPlayer>();
                string clientWhy = click != null ? click.ViewJumpRefusal(p) : "no PointAndClickPlayer on the own body";
                if (clientWhy != null)
                {
                    outcome.Ok = false;
                    Log($"jump to {p.x:F1},{p.z:F1}: FAIL; the owner's client refused it: {clientWhy}");
                    yield break;
                }
                bool hasClient = TryLocalGhost(out World clientWorld, out Entity clientGhost, out _);
                ushort clientSeq0 = hasClient ? clientWorld.EntityManager.GetComponentData<ClientMotionPlan>(clientGhost).Plan.PlanSeq : (ushort)0;
                int pressFrame = Time.frameCount, predictedFrame = -1, clipFrame = -1;
                BodyClicks.Jump(p);
                ScreenWatch.ResetWorst();
                RenderWatch.Reset();
                float pressed = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - pressed < 2.4f && planner.Refused == refused0)
                {
                    yield return new WaitForEndOfFrame();
                    Vector3 b = own.position;
                    // prediction: the owner's own plan for this click (made from the press, not the server's); the clip on the drawn body
                    if (predictedFrame < 0 && hasClient && clientWorld.IsCreated && clientWorld.EntityManager.Exists(clientGhost))
                    {
                        MotionPlan mine = clientWorld.EntityManager.GetComponentData<ClientMotionPlan>(clientGhost).Plan;
                        if (mine.Kind != MotionKind.None && mine.PlanSeq != clientSeq0) predictedFrame = Time.frameCount;
                    }
                    if (clipFrame < 0 && motor != null && motor.WarpedJumpLine != "no jump clip") clipFrame = Time.frameCount;
                    if (lift < 0f && b.y > bodyStart.y + 0.02f) lift = Time.realtimeSinceStartup - pressed;
                    bodyTop = Mathf.Max(bodyTop, b.y);
                    serverTop = Mathf.Max(serverTop, ServerPosition(ghost).y);
                    if (state != null && state.HasPose) worstRoot = Mathf.Max(worstRoot, Vector3.Distance(b, state.PosePosition));
                    if (state != null && state.InJump) worstArc = Mathf.Max(worstArc, state.JumpArcErrorM);
                    if (motor != null && clipLines++ < 30)
                        Log($"jump frame {Time.frameCount} +{Time.realtimeSinceStartup - pressed:F2} s: animation {motor.CurrentState}, {motor.WarpedJumpLine}; body y {b.y:F2}");
                }
                var plan = serverWorld.EntityManager.GetComponentData<MotionPlan>(ghost);
                Vector3 to = new Vector3(plan.ToXMm, plan.ToYMm, plan.ToZMm) * 0.001f;
                Vector3 server = ServerPosition(ghost);
                bool taken = planner.Planned == planned0 + 1;
                float landError = Vector3.Distance(server, to), clickError = Flat(server - p);
                bool liftOk = lift >= 0f && lift <= 0.3f;
                // NGO started the clip on the press frame (BeginOwnJump); the owner predicts it (its own plan) instead of waiting for the server
                bool predictedOk = predictedFrame >= 0 && predictedFrame - pressFrame <= 0;
                bool clipOk = clipFrame >= 0 && clipFrame - pressFrame <= 0;
                // one start tick on both sides, so the server's plan never restarts the owner's arc (run predict-2: 113 vs 117)
                MotionPlan ownPlan = hasClient && clientWorld.IsCreated ? clientWorld.EntityManager.GetComponentData<ClientMotionPlan>(clientGhost).Plan : default;
                bool sameStart = ownPlan.StartTick.IsValid && plan.StartTick == ownPlan.StartTick;
                bool smooth = RenderWatch.WorstOffsetChangeMm <= MaxOffsetChangeMm;
                if (!smooth) m_JudderFailures++;
                // the owner's bar (31 Oct): the drawn body on the exact arc within 1 cm at any frame rate; the landing within 2 cm of the plan
                outcome.Ok = taken && landError < 0.02f && clickError < 0.5f && pay.Paid == paid0 + 1 && worstRoot < 0.02f && worstArc < 0.01f && liftOk
                    && predictedOk && clipOk && sameStart;
                Log($"jump to {p.x:F1},{p.z:F1}: {(outcome.Ok ? "PASS" : "FAIL")}; {(taken ? $"planned, started {planner.LastStartTicksBack} ticks before the server's tick" : "NOT planned: " + planner.LastRefusal)}; " +
                    $"start tick server {(plan.StartTick.IsValid ? plan.StartTick.TickIndexForValidTick : 0)}, owner {(ownPlan.StartTick.IsValid ? ownPlan.StartTick.TickIndexForValidTick : 0)} " +
                    $"({(sameStart ? "the same" : "DIFFERENT")}); " +
                    $"owner predicted {(predictedFrame < 0 ? "NEVER" : (predictedFrame - pressFrame) + " frames after the press")}, jump clip " +
                    $"{(clipFrame < 0 ? "NEVER" : (clipFrame - pressFrame) + " frames after the press")} (limit 0 = the press frame); " +
                    $"press to lift {lift:F3} s (limit 0.30); apex server {serverTop - from.y:F2} m (plan {(plan.ApexYMm * 0.001f) - from.y:F2}), body {bodyTop - bodyStart.y:F2} m; " +
                    $"landed server {server.x:F2},{server.y:F2},{server.z:F2}: {landError * 100f:F1} cm from the plan, {clickError:F2} m from the click; " +
                    $"Flux {flux:F0} -> {Flux(body):F0} (paid {pay.Paid - paid0}x); root off the follow's pose worst {worstRoot * 100f:F1} cm; drawn body off the exact arc worst {worstArc * 100f:F2} cm (limit 1); fps {1f / Mathf.Max(1e-4f, Time.smoothDeltaTime):F0}; " +
                    $"camera offset from the body worst change {RenderWatch.WorstOffsetChangeMm:F0} mm at render time (limit {MaxOffsetChangeMm:F0}) -> {(smooth ? "smooth" : "JUDDER")}");
                yield return new WaitForSeconds(0.6f);   // the landing cooldown (0.25 s) and the recovery clip
            }

            static void Log(string text) => Debug.Log("[KnPlayHere] " + text);

            Vector3 m_YawFrom;
            bool m_PlaceBisectStarted;
            int m_YawFailures, m_JudderFailures;
            public const float MaxScreenStepPx = 3f;
            /// <summary>At the camera's ~8 m, 10 mm of camera-to-body change in one frame is about 2 px on a 1080p screen.</summary>
            public const float MaxOffsetChangeMm = 10f;

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

            internal static bool TryServer(out GameObject body, out Entity ghost)
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

    /// <summary>
    /// The clean stop of a scripted proof run (owner, 31 Oct: "shouldn't it just close down properly?"; a force-killed Editor leaves
    /// scene backups and stops the next start on "Scene Backup Detected"). The run's wrapper writes <see cref="StopFile"/>; this sees it
    /// on the next Editor update, leaves Play mode and exits with code 3. The wrapper kills the process only if it has not ended in time.
    /// </summary>
    [InitializeOnLoad]
    static class KnStopRequest
    {
        public const string StopFile = "Temp/kn-stop-request";
        static bool s_Stopping;

        static KnStopRequest() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (s_Stopping || !System.IO.File.Exists(StopFile)) return;
            string why = "";
            try { why = System.IO.File.ReadAllText(StopFile).Trim(); System.IO.File.Delete(StopFile); } catch (Exception) { }
            s_Stopping = true;
            Debug.Log("[KnStop] stop requested (" + why + "): leaving Play mode, then exiting");
            if (EditorApplication.isPlaying)
            {
                EditorApplication.playModeStateChanged += ExitWhenStopped;
                EditorApplication.isPlaying = false;
            }
            else EditorApplication.Exit(3);
        }

        static void ExitWhenStopped(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(3);
        }
    }
}
#endif
