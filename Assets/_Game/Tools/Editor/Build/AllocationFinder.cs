using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Finds what makes garbage while the game plays. Every piece of script memory handed out has to be collected
    /// later, and a collection is a hitch; the performance log counts the collections but cannot say whose memory
    /// it was. This records Play with the Profiler, with the call stack of every allocation, and writes a list of
    /// who allocated how much, per frame, largest first: the game's own code named by method.
    /// <para><b>Krytical Nova ▸ Build ▸ Allocations</b>: Start Recording while in Play, play the part to be looked
    /// at (the Profiler keeps the last 300 frames unless its preferences say more), then Write Report.
    /// Numbers are the Editor's: some allocations (empty GetComponent, UI Toolkit's Editor checks) do not happen
    /// in a built game, so read the ranking, not the totals.</para>
    /// </summary>
    public static class AllocationFinder
    {
        public const string ReportPath = "Library/KryticalNova.Allocations.txt";
        private const string StartPath = "Krytical Nova/Build/Allocations/Start Recording";
        private const string ReportMenuPath = "Krytical Nova/Build/Allocations/Write Report";
        private const string GameNamespace = "KryticalNova.";
        private const int Rows = 60;
        private const int StackFrames = 3;

        private struct Tally
        {
            public long Bytes;
            public int Frames, Allocations, LastFrame;
        }

        public static bool Recording { get; private set; }

        [MenuItem(StartPath, priority = 70)]
        public static void Begin()
        {
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.memoryRecordMode = ProfilerMemoryRecordMode.GCAlloc;
            Profiler.enabled = true;
            ProfilerDriver.enabled = true;
            Recording = true;
            Debug.Log("[Allocations] Recording Play with allocation call stacks (the Profiler keeps the last frames it has room for, 300 unless set higher).");
        }

        [MenuItem(StartPath, true)]
        private static bool CanBegin() => EditorApplication.isPlaying && !Recording;

        [MenuItem(ReportMenuPath, priority = 71)]
        private static void ReportMenu() => Debug.Log("[Allocations] " + End());

        [MenuItem(ReportMenuPath, true)]
        private static bool CanReport() => Recording;

        /// <summary>Stops recording, writes the report to <see cref="ReportPath"/> and returns its first lines.</summary>
        public static string End()
        {
            ProfilerDriver.enabled = false;
            Profiler.enabled = false;
            ProfilerDriver.memoryRecordMode = ProfilerMemoryRecordMode.None;
            Recording = false;

            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            if (first < 0 || last < first) return "No frames were recorded.";

            var byCaller = new Dictionary<string, Tally>(512);
            var byScript = new Dictionary<string, Tally>(256);
            var names = new List<string>(32);
            var left = new List<int>(32);
            var stack = new List<ulong>(64);
            var methods = new Dictionary<ulong, string>(4096);
            long total = 0;
            int frames = 0;
            for (int frame = first; frame <= last; frame++)
            {
                using RawFrameDataView view = ProfilerDriver.GetRawFrameDataView(frame, 0);
                if (view == null || !view.valid) continue;
                frames++;
                int allocation = view.GetMarkerId("GC.Alloc");
                names.Clear();
                left.Clear();
                // The samples come in the order they ran, each followed by its children: the open ones are the path.
                for (int sample = 0; sample < view.sampleCount; sample++)
                {
                    int children = view.GetSampleChildrenCount(sample);
                    if (view.GetSampleMarkerId(sample) == allocation)
                    {
                        long bytes = view.GetSampleMetadataCount(sample) > 0 ? view.GetSampleMetadataAsLong(sample, 0) : 0;
                        string script = names.Count > 0 ? names[names.Count - 1] : "(top)";
                        stack.Clear();
                        view.GetSampleCallstack(sample, stack);
                        total += bytes;
                        Add(byScript, script, bytes, frame, 1);
                        Add(byCaller, Caller(view, stack, methods, script), bytes, frame, 1);
                    }
                    if (children > 0)
                    {
                        names.Add(view.GetSampleName(sample));
                        left.Add(children);
                        continue;
                    }
                    // A sample without children is finished, and so is every open one it was the last child of.
                    while (left.Count > 0)
                    {
                        int top = left.Count - 1;
                        if (--left[top] > 0) break;
                        left.RemoveAt(top);
                        names.RemoveAt(top);
                    }
                }
            }
            if (frames == 0) return "No frames could be read.";

            var text = new StringBuilder(8192);
            text.Append("Allocations over ").Append(frames).Append(" frames of Play in the Editor: ")
                .Append(Kb(total / (double)frames)).Append(" a frame, ").Append(Kb(total)).Append(" in all\n\n");
            Table(text, "BY THE GAME'S OWN CODE (the first of our methods on each allocation's call stack, and who called it)", byCaller, frames);
            Table(text, "BY WHAT THE ENGINE WAS RUNNING (the marker the allocation happened under)", byScript, frames);
            File.WriteAllText(ReportPath, text.ToString());

            using var reader = new StringReader(text.ToString());
            var head = new StringBuilder();
            for (int i = 0; i < 14; i++)
            {
                string line = reader.ReadLine();
                if (line == null) break;
                head.Append(line).Append('\n');
            }
            return "written to " + ReportPath + "\n" + head;
        }

        // The game's own methods nearest the allocation, innermost first; the engine's marker when there are none.
        private static string Caller(RawFrameDataView view, List<ulong> stack, Dictionary<ulong, string> methods, string script)
        {
            if (stack.Count == 0) return "(no call stack) under " + script;
            string found = null, innermost = null;
            int ours = 0;
            foreach (ulong address in stack)
            {
                if (!methods.TryGetValue(address, out string method))
                {
                    var info = view.ResolveMethodInfo(address);
                    method = Managed(info.methodName);
                    // Where one of ours starts, so it can be found.
                    if (method != null && method.IndexOf(GameNamespace, StringComparison.Ordinal) >= 0 && info.sourceFileLine > 0) method += ":" + info.sourceFileLine;
                    methods[address] = method;
                }
                if (method == null) continue;
                innermost ??= method;
                if (method.IndexOf(GameNamespace, StringComparison.Ordinal) < 0) continue;
                // What allocated, when it is not ours: string.Concat, List.Add, a closure.
                if (ours == 0 && innermost != method) found = "[" + Short(innermost) + "] ";
                found = (found ?? string.Empty) + (ours > 0 ? " < " : string.Empty) + Short(method);
                if (++ours >= StackFrames) break;
            }
            return found ?? "(not ours) " + Short(innermost ?? "?") + " under " + script;
        }

        // The Profiler names a method "Assembly.dll!Namespace::Type.Method()"; a frame of the engine's own native code
        // has no name at all.
        private static string Managed(string method)
        {
            if (string.IsNullOrEmpty(method)) return null;
            int module = method.IndexOf('!');
            if (module >= 0) method = method.Substring(module + 1);
            method = method.Replace("::", ".").Replace("()", string.Empty).Trim();
            return method.Length == 0 ? null : method;
        }

        private static string Short(string method)
        {
            method = method.Replace(GameNamespace, string.Empty);
            return method.Length > 90 ? method.Substring(0, 90) : method;
        }

        private static void Add(Dictionary<string, Tally> tallies, string key, long bytes, int frame, int allocations)
        {
            tallies.TryGetValue(key, out Tally tally);
            tally.Bytes += bytes;
            tally.Allocations += allocations;
            if (tally.LastFrame != frame + 1)
            {
                tally.LastFrame = frame + 1;
                tally.Frames++;
            }
            tallies[key] = tally;
        }

        private static void Table(StringBuilder text, string title, Dictionary<string, Tally> tallies, int frames)
        {
            text.Append(title).Append('\n');
            text.Append("   a frame      in all   in % of frames   allocations   who\n");
            foreach (KeyValuePair<string, Tally> row in tallies.OrderByDescending(t => t.Value.Bytes).Take(Rows))
                text.Append(Kb(row.Value.Bytes / (double)frames).PadLeft(10)).Append(Kb(row.Value.Bytes).PadLeft(12))
                    .Append((row.Value.Frames * 100.0 / frames).ToString("0", CultureInfo.InvariantCulture).PadLeft(15)).Append(" %")
                    .Append(row.Value.Allocations.ToString(CultureInfo.InvariantCulture).PadLeft(14)).Append("   ").Append(row.Key).Append('\n');
            text.Append('\n');
        }

        private static string Kb(double bytes) => bytes >= 1024 * 1024
            ? (bytes / (1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " MB"
            : bytes >= 1024 ? (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB" : bytes.ToString("0", CultureInfo.InvariantCulture) + " B";
    }
}
