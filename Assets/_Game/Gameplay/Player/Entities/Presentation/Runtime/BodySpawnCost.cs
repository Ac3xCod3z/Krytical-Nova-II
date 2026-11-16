using System;
using System.Text;
using KryticalNova.Diagnostics;
using UnityEngine;

namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>
    /// What one frame of client body spawns put on the managed heap, step by step (R3-5 B join spike, 16 Nov 2026: about 2.7 MB and
    /// 30 ms per body measured at 150 bots, appearance merge ruled out). <see cref="PlayerBodySpawnSystem"/> opens a body with
    /// <see cref="Begin"/> and closes it with <see cref="Step.Register"/>; the body factory and <c>PlayerBodyAssembler</c> mark the
    /// steps between. Each mark counts what the heap grew since the one before (a step a collection fell in is left out, its growth
    /// cannot be read). Only while the performance log records; otherwise every call returns at once. A server body (no
    /// <see cref="Begin"/>) is never counted. Main thread only. Start() runs a frame later and is not in any step.
    /// </summary>
    public static class BodySpawnCost
    {
        public enum Step { Instantiate, Strip, Wake, Hooks, Register }
        const int Steps = 5;
        static readonly string[] Names = { "instantiate", "strip", "wake", "hooks", "register" };
        static readonly long[] s_Bytes = new long[Steps];
        static long s_Mark;
        static int s_Gcs;
        static bool s_Open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Array.Clear(s_Bytes, 0, Steps);
            s_Open = false;
        }

        /// <summary>A client body starts (the spawn system, before it asks the factory).</summary>
        public static void Begin()
        {
            s_Open = PerformanceLog.IsRecording;
            if (!s_Open) return;
            s_Mark = GC.GetTotalMemory(false);
            s_Gcs = GC.CollectionCount(0);
        }

        /// <summary>The step since the last mark is over; <see cref="Step.Register"/> closes the body.</summary>
        public static void Mark(Step step)
        {
            if (!s_Open) return;
            long now = GC.GetTotalMemory(false);
            int gcs = GC.CollectionCount(0);
            if (gcs == s_Gcs && now >= s_Mark) s_Bytes[(int)step] += now - s_Mark;
            s_Mark = now;
            s_Gcs = gcs;
            if (step == Step.Register) s_Open = false;
        }

        /// <summary>The frame's steps as "instantiate N, strip N, ..." in KB (and their sum), then zero for the next frame.</summary>
        public static string Take(out long totalBytes)
        {
            totalBytes = 0;
            var text = new StringBuilder(80);
            for (int i = 0; i < Steps; i++)
            {
                totalBytes += s_Bytes[i];
                if (i > 0) text.Append(", ");
                text.Append(Names[i]).Append(' ').Append(s_Bytes[i] / 1024);
                s_Bytes[i] = 0;
            }
            s_Open = false;
            return text.ToString();
        }
    }
}
