using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.LowLevel;

namespace KryticalNova.Diagnostics
{
    /// <summary>
    /// Where in a frame the time goes, measured by the game itself (a built game has no profiler markers): a clock
    /// read at the start and at the end of each top-level phase of Unity's player loop (time and the wait for the
    /// screen, initialization, early update, fixed update and physics, pre-update, the scripts' Update, animation
    /// and LateUpdate, and the last phase, in which the frame is drawn). The start of the first phase is the
    /// boundary between two frames: the frame that just ended is handed to <see cref="FrameEnded"/> there.
    /// </summary>
    internal static class FramePhaseProbes
    {
        /// <summary>The phases, in the order they run. Empty until installed.</summary>
        public static string[] Names { get; private set; } = Array.Empty<string>();
        /// <summary>Clock ticks spent in each phase of the frame that just ended. Valid during <see cref="FrameEnded"/> only.</summary>
        public static long[] Ticks { get; private set; } = Array.Empty<long>();
        /// <summary>Called at the boundary with the length of the frame that ended, in clock ticks.</summary>
        public static Action<long> FrameEnded;

        private static long[] started = Array.Empty<long>();
        private static long frameStarted;

        public static void Install()
        {
            Remove();
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] phases = loop.subSystemList;
            if (phases == null || phases.Length == 0) return;

            Names = new string[phases.Length];
            Ticks = new long[phases.Length];
            started = new long[phases.Length];
            frameStarted = 0;
            for (int i = 0; i < phases.Length; i++)
            {
                int phase = i;
                Names[i] = phases[i].type != null ? phases[i].type.Name : "phase" + i;
                var systems = new List<PlayerLoopSystem>(phases[i].subSystemList ?? Array.Empty<PlayerLoopSystem>());
                PlayerLoopSystem.UpdateFunction begin = phase == 0 ? (PlayerLoopSystem.UpdateFunction)BeginFrame : () => started[phase] = Stopwatch.GetTimestamp();
                systems.Insert(0, new PlayerLoopSystem { type = typeof(FramePhaseProbes), updateDelegate = begin });
                systems.Add(new PlayerLoopSystem { type = typeof(FramePhaseProbes), updateDelegate = () => Ticks[phase] += Stopwatch.GetTimestamp() - started[phase] });
                phases[i].subSystemList = systems.ToArray();
            }
            loop.subSystemList = phases;
            PlayerLoop.SetPlayerLoop(loop);
        }

        // The first thing a frame does: the one before it is over.
        private static void BeginFrame()
        {
            long now = Stopwatch.GetTimestamp();
            if (frameStarted != 0)
            {
                try { FrameEnded?.Invoke(now - frameStarted); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
            for (int i = 0; i < Ticks.Length; i++) Ticks[i] = 0;
            frameStarted = now;
            started[0] = Stopwatch.GetTimestamp(); // what the hand-over above cost is not the first phase's
        }

        /// <summary>Takes the probes out of the player loop again. Safe to call when there are none.</summary>
        public static void Remove()
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] phases = loop.subSystemList;
            if (phases == null) return;
            bool changed = false;
            for (int i = 0; i < phases.Length; i++)
            {
                PlayerLoopSystem[] systems = phases[i].subSystemList;
                if (systems == null) continue;
                var kept = new List<PlayerLoopSystem>(systems.Length);
                foreach (PlayerLoopSystem system in systems)
                    if (system.type != typeof(FramePhaseProbes)) kept.Add(system);
                if (kept.Count == systems.Length) continue;
                phases[i].subSystemList = kept.ToArray();
                changed = true;
            }
            if (changed)
            {
                loop.subSystemList = phases;
                PlayerLoop.SetPlayerLoop(loop);
            }
            frameStarted = 0;
        }
    }
}
