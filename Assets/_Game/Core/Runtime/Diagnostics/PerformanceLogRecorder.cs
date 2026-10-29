using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using KryticalNova.Core;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace KryticalNova.Diagnostics
{
    /// <summary>
    /// Writes the performance log: one text file for each run of the game, in a <c>PerformanceLogs</c> folder beside
    /// the game's exe, to be read afterwards (by a person, or by <c>Tools/Editor/Build/ReadPerformanceLog.py</c>).
    /// Once a second a line of how the game ran; for every slow frame a line saying how long it took, where in the
    /// frame the time went (<see cref="FramePhaseProbes"/>), whether memory was being collected, and what the game
    /// had just done (<see cref="PerformanceLog"/>). A built game always records. Play in the Editor records only
    /// when <b>Krytical Nova ▸ Build ▸ Record Performance Log In Play</b> is ticked: the Editor's own slow frames
    /// would drown the game's. Nothing is sent anywhere.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceLogRecorder : MonoBehaviour
    {
        private const string FolderName = "PerformanceLogs";
        private const int KeepFiles = 20;
        private const double SlowMs = 20.0;             // the game's floor: 50 frames a second
        private const double SlowAgainstUsual = 3.0;    // or this many times the usual frame of the second before
        private const double NeverSlowBelowMs = 8.0;
        private const int MaxPending = 24;
        private const int SplitKnownAfterFrames = 6;    // FrameTimingManager reports a frame a few frames late
        private const double LoadingSeconds = 3.0;      // frames this soon after a scene or a session arrives are loading, not play
        private const double StartSeconds = 5.0;        // and this long after the game's first frame
        private const double HistogramEverySeconds = 60.0;
        private const double CameraWorthNamingMs = 0.3;
        private const float Megabyte = 1024f * 1024f;
        // Mono compiles a method the first time it runs (a hitch each time); IL2CPP has it compiled beforehand.
#if ENABLE_IL2CPP
        private const string ScriptingBackend = "IL2CPP";
#else
        private const string ScriptingBackend = "Mono";
#endif

        private static PerformanceLogRecorder instance;

        private StreamWriter writer;
        private readonly StringBuilder text = new StringBuilder(2048);
        private long startedAt;
        private long frameNumber;
        private long markersWritten;

        // The second being gathered.
        private int frames, overTwenty, timingFrames, gpuFrames;
        private double sumMs, slowestMs, mainMs, renderMs, gpuMs;
        private double secondLeft = 1.0;
        private double usualMs;
        private long logTicks;
        private int collections;
        private double loadingUntil;
        private string scene = "-";
        private string session = "none";
        private Camera view;

        private struct Pending
        {
            public PerformanceLogText.SlowFrame Frame;
            public float[] Phases, Steps;
            public string Cameras;
            public long KnownAtFrame;
            public bool Used;
        }

        private readonly Pending[] pending = new Pending[MaxPending];

        // How long every frame of play took, in bins, for the section being played.
        private readonly int[] bins = new int[PerformanceLogText.Bins];
        private int sectionSerial;
        private string sectionName = PerformanceLog.DefaultSection;
        private long sectionFrames, sectionOverTwenty, sectionLoadingFrames;
        private double sectionSeconds, sectionSlowestMs, histogramLeft = HistogramEverySeconds;
        private readonly FrameTiming[] oneTiming = new FrameTiming[1];
        private readonly FrameTiming[] lateTimings = new FrameTiming[SplitKnownAfterFrames + 4];
        private ProfilerRecorder setPass, triangles, usedMemory, scriptMemory, batches, drawCalls;

        // The probes can be thrown out by anything that rebuilds the player loop: Update notices and puts them back.
        private long framesSeenByUpdate;
        private int updatesWithoutFrames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            FramePhaseProbes.FrameEnded = null;
            FramePhaseProbes.Remove();
            FrameCameraProbes.Remove();
            PerformanceLog.Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (instance != null || Application.isBatchMode || !ShouldRecord()) return;
            var host = new GameObject("Performance Log");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<PerformanceLogRecorder>();
        }

        private static bool ShouldRecord()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetBool(PerformanceLog.EditorPrefKey, false);
#else
            return true;
#endif
        }

        /// <summary>Where this run's log goes: beside the exe in a built game, under Library in the Editor.</summary>
        public static string Folder()
        {
            string beside = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Application.isEditor ? Path.Combine(beside, "Library", FolderName) : Path.Combine(beside, FolderName);
        }

        private void Awake()
        {
            if (!Open(Folder()) && !Open(Path.Combine(Application.persistentDataPath, FolderName)))
            {
                Debug.LogWarning("[PerformanceLog] No folder could be written: nothing is recorded this run.");
                enabled = false;
                return;
            }

            startedAt = Stopwatch.GetTimestamp();
            collections = GC.CollectionCount(0);
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            usedMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
            scriptMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
            for (int i = 0; i < MaxPending; i++) pending[i].Phases = pending[i].Steps = Array.Empty<float>();
            sectionSerial = PerformanceLog.SectionSerial;
            sectionName = PerformanceLog.SectionName;

            SceneManager.sceneLoaded += OnSceneLoaded;
            FramePhaseProbes.FrameEnded = OnFrame;
            FramePhaseProbes.Install();
            FrameCameraProbes.Install();
            PerformanceLog.IsRecording = true;
            WriteHeader();
            Debug.Log($"[PerformanceLog] Recording to {PerformanceLog.FilePath}");
        }

        private bool Open(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                PerformanceLogText.Prune(folder, KeepFiles - 1);
                string path = Path.Combine(folder, PerformanceLogText.FileName(DateTime.Now));
                // Readable while the game runs: the file can be looked at during a session.
                var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                writer = new StreamWriter(stream, new UTF8Encoding(false), 16 * 1024);
                PerformanceLog.FilePath = path;
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
            {
                writer = null;
                return false;
            }
        }

        private void WriteHeader()
        {
            string built = "-";
            try { built = Directory.GetLastWriteTime(Application.dataPath).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }
            catch (Exception) { }
            Resolution display = Screen.currentResolution;
            text.Clear();
            text.Append("# Krytical Nova II performance log\n");
            text.Append("# started\t").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .Append("\tversion ").Append(Application.version).Append("\tUnity ").Append(Application.unityVersion)
                .Append(Application.isEditor ? "\tEDITOR (its own slow frames are in here)" : "\tbuilt " + built)
                .Append("\tscripts ").Append(ScriptingBackend).Append('\n');
            text.Append("# machine\t").Append(SystemInfo.graphicsDeviceName).Append("\t").Append(SystemInfo.processorType.Trim())
                .Append("\t").Append(SystemInfo.processorCount).Append(" threads\t").Append((SystemInfo.systemMemorySize / 1024f).ToString("0", CultureInfo.InvariantCulture))
                .Append(" GB\t").Append(SystemInfo.graphicsDeviceType).Append(" (").Append(SystemInfo.renderingThreadingMode).Append(")\t")
                .Append(SystemInfo.operatingSystem).Append('\n');
            text.Append("# screen\t").Append(Screen.width).Append(" x ").Append(Screen.height).Append("\tdisplay ").Append(display.width).Append(" x ").Append(display.height)
                .Append(" at ").Append(display.refreshRateRatio.value.ToString("0", CultureInfo.InvariantCulture)).Append(" Hz\t").Append(Screen.fullScreenMode).Append('\n');
            text.Append("# a slow frame is one over ").Append(SlowMs.ToString("0", CultureInfo.InvariantCulture)).Append(" ms, or over ")
                .Append(SlowAgainstUsual.ToString("0", CultureInfo.InvariantCulture)).Append(" times the usual frame of the second before; ~ before a number: matched to its frame by length, not for certain\n");
            text.Append("# phases\t").Append(string.Join(" ", FramePhaseProbes.Names)).Append('\n');
            text.Append(PerformanceLogText.SecondColumns).Append('\n').Append(PerformanceLogText.SlowFrameColumns).Append('\n')
                .Append(PerformanceLogText.MarkerColumns).Append('\n').Append(PerformanceLogText.ChangeColumns).Append('\n')
                .Append(PerformanceLogText.HistogramColumns).Append('\n');
            writer.Write(text.ToString());
            writer.Flush();
            text.Clear();
        }

        // A scene arriving: the frames just after it are loading, not play.
        private void OnSceneLoaded(Scene loaded, LoadSceneMode mode) => NoticeScene();

        // Also asked once a second: Play in the Editor can start in a scene that is already open, with no event for it.
        private void NoticeScene()
        {
            string now = SceneManager.GetActiveScene().name;
            if (now == scene) return;
            scene = now;
            view = null;
            StillLoading(LoadingSeconds);
            PerformanceLog.Change("scene", now);
        }

        private void StillLoading(double seconds) => loadingUntil = Math.Max(loadingUntil, Seconds(Stopwatch.GetTimestamp()) + seconds);

        private double Seconds(long timestamp) => (timestamp - startedAt) / (double)Stopwatch.Frequency;

        // Called at the start of every frame, with the length of the one that just ended.
        private void OnFrame(long frameTicks)
        {
            if (writer == null) return;
            long began = Stopwatch.GetTimestamp();
            double ms = frameTicks * 1000.0 / Stopwatch.Frequency;
            if (frameNumber == 0) StillLoading(StartSeconds); // the game's first frames are it starting up
            frameNumber++;
            frames++;
            sumMs += ms;
            if (ms > slowestMs) slowestMs = ms;
            if (ms > SlowMs) overTwenty++;

            int collectionsNow = GC.CollectionCount(0);
            bool collected = collectionsNow != collections;
            collections = collectionsNow;

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, oneTiming) > 0)
            {
                mainMs += oneTiming[0].cpuMainThreadFrameTime;
                renderMs += oneTiming[0].cpuRenderThreadFrameTime;
                timingFrames++;
                // The GPU's time is not reported for every frame, and now and then it comes back as nonsense.
                if (Believable(oneTiming[0].gpuFrameTime))
                {
                    gpuMs += oneTiming[0].gpuFrameTime;
                    gpuFrames++;
                }
            }

            if (ms > SlowMs || (usualMs > 0 && ms > Math.Max(NeverSlowBelowMs, usualMs * SlowAgainstUsual))) Keep(ms, collected, began);
            Resolve(false);
            Count(ms, began);
            FrameCameraProbes.NewFrame();

            secondLeft -= ms / 1000.0;
            if (secondLeft <= 0) WriteSecond(began);
            logTicks += Stopwatch.GetTimestamp() - began;
        }

        // Every frame of play is counted by its length, for the section it fell in. A new section closes the old one's count.
        private void Count(double ms, long endedAt)
        {
            if (PerformanceLog.SectionSerial != sectionSerial)
            {
                WriteHistogram(endedAt);
                Array.Clear(bins, 0, bins.Length);
                sectionFrames = sectionOverTwenty = sectionLoadingFrames = 0;
                sectionSeconds = sectionSlowestMs = 0;
                sectionSerial = PerformanceLog.SectionSerial;
                sectionName = PerformanceLog.SectionName;
                histogramLeft = HistogramEverySeconds;
            }
            if (Seconds(endedAt) < loadingUntil)
            {
                sectionLoadingFrames++;
                return;
            }
            bins[PerformanceLogText.BinOf(ms)]++;
            sectionFrames++;
            sectionSeconds += ms / 1000.0;
            if (ms > sectionSlowestMs) sectionSlowestMs = ms;
            if (ms > SlowMs) sectionOverTwenty++;
            // Now and then as it stands, so a game that is killed leaves its count behind.
            histogramLeft -= ms / 1000.0;
            if (histogramLeft > 0) return;
            histogramLeft = HistogramEverySeconds;
            WriteHistogram(endedAt);
        }

        private void WriteHistogram(long now)
        {
            if (sectionFrames == 0 && sectionLoadingFrames == 0) return;
            PerformanceLogText.AppendHistogram(text, Seconds(now), sectionSerial, sectionName, sectionFrames, sectionSeconds, sectionSlowestMs,
                sectionOverTwenty, sectionLoadingFrames, bins);
        }

        private void Keep(double ms, bool collected, long endedAt)
        {
            for (int i = 0; i < MaxPending; i++)
            {
                if (pending[i].Used) continue;
                ref Pending slot = ref pending[i];
                long[] ticks = FramePhaseProbes.Ticks;
                if (slot.Phases.Length != ticks.Length) slot.Phases = new float[ticks.Length];
                for (int p = 0; p < ticks.Length; p++) slot.Phases[p] = (float)(ticks[p] * 1000.0 / Stopwatch.Frequency);
                long[] steps = FramePhaseProbes.StepTicks;
                if (slot.Steps.Length != steps.Length) slot.Steps = new float[steps.Length];
                for (int s = 0; s < steps.Length; s++) slot.Steps[s] = (float)(steps[s] * 1000.0 / Stopwatch.Frequency);
                slot.Cameras = FrameCameraProbes.Describe(CameraWorthNamingMs);
                double time = Seconds(endedAt);
                slot.Frame = new PerformanceLogText.SlowFrame
                {
                    Time = time, Frame = frameNumber, Ms = ms, Collection = collected, Loading = time < loadingUntil,
                    MainMs = -1, PresentWaitMs = -1, RenderMs = -1, GpuMs = -1, EndedAt = endedAt,
                };
                slot.KnownAtFrame = frameNumber + SplitKnownAfterFrames;
                slot.Used = true;
                return;
            }
            // More slow frames at once than are kept: the second's own line still counts them.
        }

        // Gives each kept slow frame its main thread / render thread / GPU split once the engine reports it, and writes it.
        private void Resolve(bool all)
        {
            int got = -1;
            for (int i = 0; i < MaxPending; i++)
            {
                if (!pending[i].Used || (!all && frameNumber < pending[i].KnownAtFrame)) continue;
                ref Pending slot = ref pending[i];
                if (got < 0) got = (int)FrameTimingManager.GetLatestTimings((uint)lateTimings.Length, lateTimings);

                // The reported frame whose length is nearest this one's.
                int best = -1;
                double bestDifference = double.MaxValue;
                for (int t = 0; t < got; t++)
                {
                    double difference = Math.Abs(lateTimings[t].cpuFrameTime - slot.Frame.Ms);
                    if (difference < bestDifference) { bestDifference = difference; best = t; }
                }
                if (best >= 0 && bestDifference <= Math.Max(3.0, slot.Frame.Ms * 0.3))
                {
                    slot.Frame.MainMs = lateTimings[best].cpuMainThreadFrameTime;
                    slot.Frame.PresentWaitMs = lateTimings[best].cpuMainThreadPresentWaitTime;
                    slot.Frame.RenderMs = lateTimings[best].cpuRenderThreadFrameTime;
                    slot.Frame.GpuMs = Believable(lateTimings[best].gpuFrameTime) ? lateTimings[best].gpuFrameTime : -1;
                    // Certain only when no other slow frame is near enough to be taken for it.
                    slot.Frame.SplitCertain = true;
                    for (int other = 0; other < MaxPending; other++)
                        if (other != i && pending[other].Used && Math.Abs(pending[other].Frame.Frame - slot.Frame.Frame) <= SplitKnownAfterFrames + 2) slot.Frame.SplitCertain = false;
                }
                else slot.Frame.SplitCertain = true; // nothing reported matches: the split is unknown, not doubtful
                PerformanceLogText.Append(text, slot.Frame, FramePhaseProbes.Names, slot.Phases, PerformanceLog.Markers, Stopwatch.Frequency,
                    FramePhaseProbes.StepNames, slot.Steps, slot.Cameras);
                slot.Cameras = null;
                slot.Used = false;
            }
        }

        private void WriteSecond(long now)
        {
            NoticeScene();
            WriteSession();
            WriteMarkers();

            if (view == null) view = Camera.main;
            Vector3 at = view != null ? view.transform.position : Vector3.zero;
            var second = new PerformanceLogText.Second
            {
                Time = Seconds(now), Scene = scene, Frames = frames, OverTwentyMs = overTwenty,
                MeanMs = frames > 0 ? sumMs / frames : 0, SlowestMs = slowestMs,
                MainMs = timingFrames > 0 ? mainMs / timingFrames : -1, RenderMs = timingFrames > 0 ? renderMs / timingFrames : -1,
                GpuMs = gpuFrames > 0 ? gpuMs / gpuFrames : -1,
                SetPass = Last(setPass), Triangles = Last(triangles), Batches = Last(batches), DrawCalls = Last(drawCalls),
                MemoryMb = (long)(Last(usedMemory) / Megabyte), ScriptMemoryMb = (long)(Last(scriptMemory) / Megabyte),
                Collections = collections,
                Players = NetSession.PlayerCount,
                NetworkedObjects = Math.Max(0, NetSession.NetworkedObjectCount),
                CameraX = at.x, CameraZ = at.z,
                LogMicrosecondsAFrame = frames > 0 ? logTicks * 1000000.0 / Stopwatch.Frequency / frames : 0,
            };
            PerformanceLogText.Append(text, second);
            Flush();

            usualMs = second.MeanMs;
            frames = overTwenty = timingFrames = gpuFrames = 0;
            sumMs = slowestMs = mainMs = renderMs = gpuMs = 0;
            logTicks = 0;
            secondLeft = 1.0;
        }

        // Hosting, joining and leaving change what the PC has to do: said as a change, not left to be guessed.
        private void WriteSession()
        {
            string now = NetSession.ModeWord;
            if (now == session) return;
            session = now;
            StillLoading(LoadingSeconds);
            PerformanceLog.Change("session", now);
        }

        private void WriteMarkers()
        {
            MarkerRing markers = PerformanceLog.Markers;
            long made = markers.Made;
            if (markersWritten < markers.Oldest) markersWritten = markers.Oldest; // more were made in a second than the ring holds
            for (; markersWritten < made; markersWritten++)
                if (markers.TryGet(markersWritten, out MarkerRing.Marker marker)) PerformanceLogText.Append(text, Seconds(marker.Timestamp), marker);
        }

        private static long Last(ProfilerRecorder recorder) => recorder.Valid ? recorder.LastValue : 0;

        private static bool Believable(double gpuMilliseconds) => gpuMilliseconds > 0.0 && gpuMilliseconds < 2000.0;

        private void Flush()
        {
            if (writer == null || text.Length == 0) return;
            try
            {
                writer.Write(text.ToString());
                writer.Flush();
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[PerformanceLog] Writing stopped: {e.Message}");
                Close();
            }
            text.Clear();
        }

        private void Update()
        {
            if (writer == null) return;
            if (frameNumber != framesSeenByUpdate)
            {
                framesSeenByUpdate = frameNumber;
                updatesWithoutFrames = 0;
                return;
            }
            if (++updatesWithoutFrames < 120) return;
            updatesWithoutFrames = 0;
            FramePhaseProbes.Install();
            PerformanceLog.Mark("log.probes", "the player loop was rebuilt; the frame probes were put back");
        }

        private void OnApplicationQuit() => Close();

        private void OnDestroy()
        {
            Close();
            if (instance == this) instance = null;
        }

        private void Close()
        {
            if (writer == null) return;
            PerformanceLog.IsRecording = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            FramePhaseProbes.FrameEnded = null;
            FramePhaseProbes.Remove();
            FrameCameraProbes.Remove();
            try
            {
                Resolve(true);
                WriteMarkers();
                WriteHistogram(Stopwatch.GetTimestamp());
                text.Append("# ended\t").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\n');
                writer.Write(text.ToString());
                writer.Flush();
                writer.Dispose();
            }
            catch (Exception e) when (e is IOException || e is ObjectDisposedException) { }
            text.Clear();
            writer = null;
            setPass.Dispose(); triangles.Dispose(); batches.Dispose(); drawCalls.Dispose(); usedMemory.Dispose(); scriptMemory.Dispose();
            PerformanceLog.FilePath = null;
        }
    }
}
