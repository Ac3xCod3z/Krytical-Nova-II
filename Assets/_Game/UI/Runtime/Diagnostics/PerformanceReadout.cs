using System;
using System.Text;
using KryticalNova.Core;
using Unity.Netcode;
using Unity.Profiling;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// The performance readout: a short list of how the game is running on this machine (frame rate and where the
    /// frame's time goes, the slowest frame of the last minute, what is being drawn, memory, the connection, and
    /// which machine this is, so a reading sent from another PC can be judged), in a corner of the screen while
    /// <see cref="GraphicsPreferences.PerformanceReadout"/> is ticked on the Graphics page of Settings: on the left
    /// edge, half way down, the one stretch of the screen the HUD keeps clear. Lives on its
    /// own object for the whole session, so it shows in the menus as in the world. It measures nothing while it is off.
    /// Drawn on a canvas of its own, made here in code and sorted above every other: it works in every scene, over
    /// every panel, and depends on none of them. (It was IMGUI until 11 Oct 2026: that handed out some 4 KB of
    /// memory every frame to draw the same text again, more than the rest of the game together, so the readout
    /// itself was what brought on most of the collections it was there to show.)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceReadout : MonoBehaviour
    {
        private const float RefreshSeconds = 0.5f;
        private const int FrameWindow = 300;        // frames the 1 % low is taken over
        private const int WorstSeconds = 60;        // the slowest frame is kept this long
        private const float TooSlow = 1f / 50f;     // the game's floor: no frame may take longer (50 a second)
        private const float Margin = 12f, Padding = 8f;
        private const float Megabyte = 1024f * 1024f;

        private static PerformanceReadout instance;

        private readonly float[] frameTimes = new float[FrameWindow];
        private readonly float[] sorted = new float[FrameWindow];
        private int frameAt, frameCount;
        private float windowTime;
        private int windowFrames;
        private float nextRefresh;
        // The slowest frame of each of the last sixty seconds, and how many of its frames were under the floor.
        private readonly float[] worstBySecond = new float[WorstSeconds];
        private readonly int[] slowBySecond = new int[WorstSeconds];
        private int secondAt;
        private float secondLeft;
        private string machine;
        private const int CanvasOrder = 32000;      // over the menus' pages (30000) and the HUD's panel (10)
        private const int FontSize = 13;
        private Canvas canvas;
        private RectTransform plate;
        private UnityEngine.UI.Text label;

        private ProfilerRecorder drawCalls, batches, setPass, triangles, vertices;
        private ProfilerRecorder usedMemory, managedMemory, allocatedInFrame;
        private readonly FrameTiming[] timings = new FrameTiming[1];
        private readonly StringBuilder text = new StringBuilder(512);
        private string shown = string.Empty;
        private bool measuring;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance != null || Application.isBatchMode) return;
            var host = new GameObject("Performance Readout");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<PerformanceReadout>();
        }

        private void Awake()
        {
            GraphicsPreferences.Changed += Apply;
            Apply();
        }

        private void OnDisable() => Measure(false);

        private void OnDestroy()
        {
            GraphicsPreferences.Changed -= Apply;
            if (instance == this) instance = null;
        }

        // The component is on only while the readout is: switched off, it has no Update and its canvas is not drawn.
        private void Apply()
        {
            bool on = GraphicsPreferences.PerformanceReadout;
            if (enabled != on) enabled = on;
            Measure(on && isActiveAndEnabled);
        }

        /// <summary>Starts or stops the counters: none of them costs anything while the readout is off.</summary>
        private void Measure(bool on)
        {
            if (measuring == on) return;
            measuring = on;
            if (on && canvas == null) BuildView();
            if (canvas != null) canvas.gameObject.SetActive(on);
            if (on)
            {
                drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
                vertices = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
                usedMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
                managedMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
                allocatedInFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                frameAt = frameCount = windowFrames = 0;
                Array.Clear(worstBySecond, 0, WorstSeconds);
                Array.Clear(slowBySecond, 0, WorstSeconds);
                secondAt = 0;
                secondLeft = 1f;
                windowTime = 0f;
                nextRefresh = 0f;
                shown = string.Empty;
            }
            else
            {
                drawCalls.Dispose(); batches.Dispose(); setPass.Dispose(); triangles.Dispose(); vertices.Dispose();
                usedMemory.Dispose(); managedMemory.Dispose(); allocatedInFrame.Dispose();
            }
        }

        private void Update()
        {
            if (!measuring) return;
            float frame = Time.unscaledDeltaTime;
            frameTimes[frameAt] = frame;
            frameAt = (frameAt + 1) % FrameWindow;
            if (frameCount < FrameWindow) frameCount++;
            windowTime += frame;
            windowFrames++;
            secondLeft -= frame;
            if (secondLeft <= 0f)
            {
                secondLeft = 1f;
                secondAt = (secondAt + 1) % WorstSeconds;
                worstBySecond[secondAt] = 0f;
                slowBySecond[secondAt] = 0;
            }
            if (frame > worstBySecond[secondAt]) worstBySecond[secondAt] = frame;
            if (frame > TooSlow) slowBySecond[secondAt]++;
            FrameTimingManager.CaptureFrameTimings();

            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            Write();
            windowTime = 0f;
            windowFrames = 0;
        }

        /// <summary>The frame time only one frame in a hundred is slower than, over the last frames: what a stutter feels like.</summary>
        private float SlowFrame()
        {
            if (frameCount == 0) return 0f;
            Array.Copy(frameTimes, sorted, frameCount);
            Array.Sort(sorted, 0, frameCount);
            return sorted[Mathf.Clamp(Mathf.CeilToInt(frameCount * 0.99f) - 1, 0, frameCount - 1)];
        }

        private void Write()
        {
            text.Clear();
            float average = windowFrames > 0 ? windowTime / windowFrames : 0f;
            float slow = SlowFrame();
            text.Append("FRAME\n");
            Line("Frames a second", average > 0f ? (1f / average).ToString("0") : "-");
            Line("1 % low", slow > 0f ? (1f / slow).ToString("0") : "-");
            Line("Frame time", (average * 1000f).ToString("0.0") + " ms");
            float worst = 0f;
            int tooSlow = 0;
            for (int i = 0; i < WorstSeconds; i++)
            {
                if (worstBySecond[i] > worst) worst = worstBySecond[i];
                tooSlow += slowBySecond[i];
            }
            Line("Slowest, last minute", worst > 0f ? (worst * 1000f).ToString("0.0") + " ms  (" + (1f / worst).ToString("0") + " a second)" : "-");
            Line("Frames under 50", tooSlow + " in the last minute");
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                FrameTiming t = timings[0];
                Line("CPU main thread", t.cpuMainThreadFrameTime.ToString("0.0") + " ms");
                Line("CPU render thread", t.cpuRenderThreadFrameTime.ToString("0.0") + " ms");
                Line("GPU", t.gpuFrameTime > 0.0 ? t.gpuFrameTime.ToString("0.0") + " ms" : "not reported");
            }
            else Line("CPU / GPU split", "not reported");

            text.Append("\nDRAWN\n");
            // With the GPU Resident Drawer on, these two read nothing: a row of zeros would only mislead.
            if (drawCalls.Valid && drawCalls.LastValue > 0) Line("Draw calls", Count(drawCalls));
            if (batches.Valid && batches.LastValue > 0) Line("Batches", Count(batches));
            Line("SetPass calls", Count(setPass));
            Line("Triangles", Count(triangles));
            Line("Vertices", Count(vertices));
            UpscalerChoice upscaler = GraphicsSettingsApplier.UpscalerInForce;
            Line("Resolution", Screen.width + " x " + Screen.height + (upscaler == UpscalerChoice.Off
                ? "  (scale " + GraphicsPreferences.RenderScale.ToString("0.00") + ")"
                : "  (" + (upscaler == UpscalerChoice.Dlss ? "DLSS" : "FSR 2") + ", " + GraphicsPreferences.UpscalerQuality + ")"));
            Line("Quality", GraphicsPreferences.Preset.ToString());

            text.Append("\nMEMORY\n");
            Line("In use", Megabytes(usedMemory));
            Line("Scripts", Megabytes(managedMemory));
            if (allocatedInFrame.Valid && Debug.isDebugBuild)
                Line("Allocated this frame", (allocatedInFrame.LastValue / 1024f).ToString("0.0") + " KB");

            text.Append("\nNETWORK\n");
            WriteNetwork();

            // Which machine the numbers are from: a reading means nothing without it.
            machine ??= "\nMACHINE\n" + SystemInfo.graphicsDeviceName + "\n" + SystemInfo.processorType.Trim() + "\n"
                + (SystemInfo.systemMemorySize / 1024f).ToString("0") + " GB memory, " + SystemInfo.graphicsDeviceType + "\n";
            text.Append(machine);
            // The performance log this run is writing: what to send when the game stuttered.
            if (KryticalNova.Diagnostics.PerformanceLog.IsRecording)
                text.Append("\nLOG\nPerformanceLogs / ").Append(System.IO.Path.GetFileName(KryticalNova.Diagnostics.PerformanceLog.FilePath)).Append('\n');
            shown = text.ToString();
            if (label == null) return;
            // Sized for a 1080-line screen and scaled with it, so it reads the same on a larger one.
            canvas.scaleFactor = Mathf.Max(1f, Screen.height / 1080f);
            label.text = shown;
            plate.sizeDelta = new Vector2(label.preferredWidth + Padding * 2f, label.preferredHeight + Padding * 2f);
        }

        // A plate at the left edge, half way down, and the text on it. Nothing here can be clicked.
        private void BuildView()
        {
            var host = new GameObject("Readout Canvas", typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
            host.transform.SetParent(transform, false);
            canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasOrder;

            var plateObject = new GameObject("Plate", typeof(RectTransform)) { layer = host.layer };
            plate = (RectTransform)plateObject.transform;
            plate.SetParent(host.transform, false);
            plate.anchorMin = plate.anchorMax = new Vector2(0f, 0.5f);
            plate.pivot = new Vector2(0f, 0.5f);
            plate.anchoredPosition = new Vector2(Margin, 0f);
            var backing = plateObject.AddComponent<UnityEngine.UI.Image>();
            backing.color = new Color(0f, 0f, 0f, 0.72f);
            backing.raycastTarget = false;

            var textObject = new GameObject("Text", typeof(RectTransform)) { layer = host.layer };
            var area = (RectTransform)textObject.transform;
            area.SetParent(plate, false);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(Padding, Padding);
            area.offsetMax = new Vector2(-Padding, -Padding);
            label = textObject.AddComponent<UnityEngine.UI.Text>();
            // Every letter as wide as the next, so the two columns line up.
            label.font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New", "Menlo", "DejaVu Sans Mono" }, FontSize);
            label.fontSize = FontSize;
            label.color = new Color(0.86f, 0.95f, 0.9f);
            label.alignment = TextAnchor.UpperLeft;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void WriteNetwork()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsListening)
            {
                Line("Session", "none");
                return;
            }
            Line("Session", network.IsHost ? "host" : network.IsServer ? "server" : "client");
            if (network.IsClient && !network.IsServer)
            {
                ulong ping = network.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
                Line("Ping", ping + " ms");
            }
            if (network.IsServer) Line("Players", network.ConnectedClientsIds.Count.ToString());
            if (NetClock.TickRate > 0) Line("Tick rate", NetClock.TickRate + " a second");
            if (network.SpawnManager != null) Line("Networked objects", network.SpawnManager.SpawnedObjectsList.Count.ToString());
        }

        private void Line(string label, string value) => text.Append(label.PadRight(22)).Append(value).Append('\n');

        private static string Count(ProfilerRecorder recorder) => recorder.Valid ? recorder.LastValue.ToString("N0") : "-";

        private static string Megabytes(ProfilerRecorder recorder) => recorder.Valid ? (recorder.LastValue / Megabyte).ToString("N0") + " MB" : "-";
    }
}
