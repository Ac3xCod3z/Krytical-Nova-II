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
            || Environment.GetEnvironmentVariable("KN_PLAYHERE_WATER") == "1";                  // the scripted Waterstride (M3c)
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
                if (!string.IsNullOrEmpty(Walk) || !string.IsNullOrEmpty(Jump) || VaultCount > 0 || WaterRun)
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
                if (string.IsNullOrEmpty(Walk) && string.IsNullOrEmpty(Jump) && VaultCount <= 0) yield break; // by hand: the owner clicks

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
                Log($"done: {arrived} of {tries} arrived, {landed} of {jumpPoints.Count} jumps passed, low-rank refusals {(lowRank.Ok ? "match" : "DIFFER")}, " +
                    $"{vaults.Passed} of {vaults.Tried} vaults passed, {waters.Passed} of {waters.Tried} Waterstride tries passed, " +
                    $"{m_YawFailures} facing the wrong way, {m_JudderFailures} juddering, {m_Errors} error lines");
                Finish(arrived == tries && landed == jumpPoints.Count && lowRank.Ok && vaults.Passed == vaults.Tried && waters.Passed == waters.Tried
                    && m_YawFailures == 0 && m_JudderFailures == 0 && m_Errors == 0);
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
                    yield return new WaitForEndOfFrame();
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
