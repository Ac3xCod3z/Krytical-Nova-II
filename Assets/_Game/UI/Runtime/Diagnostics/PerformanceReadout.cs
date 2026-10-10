using System;
using System.Text;
using Unity.Netcode;
using Unity.Profiling;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// The performance readout: a short list of how the game is running on this machine (frame rate and where the
    /// frame's time goes, what is being drawn, memory, the connection), in a corner of the screen while
    /// <see cref="GraphicsPreferences.PerformanceReadout"/> is ticked on the Graphics page of Settings: on the left
    /// edge, half way down, the one stretch of the screen the HUD keeps clear. Lives on its
    /// own object for the whole session, so it shows in the menus as in the world. It measures nothing while it is off.
    /// Drawn with IMGUI on purpose: it must work in every scene, over every panel, and depend on none of them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceReadout : MonoBehaviour
    {
        private const float RefreshSeconds = 0.5f;
        private const int FrameWindow = 300;        // frames the 1 % low is taken over
        private const float Margin = 12f, Padding = 8f;
        private const float Megabyte = 1024f * 1024f;

        private static PerformanceReadout instance;

        private readonly float[] frameTimes = new float[FrameWindow];
        private readonly float[] sorted = new float[FrameWindow];
        private int frameAt, frameCount;
        private float windowTime;
        private int windowFrames;
        private float nextRefresh;

        private ProfilerRecorder drawCalls, batches, setPass, triangles, vertices;
        private ProfilerRecorder usedMemory, managedMemory, allocatedInFrame;
        private readonly FrameTiming[] timings = new FrameTiming[1];
        private readonly StringBuilder text = new StringBuilder(512);
        private string shown = string.Empty;
        private GUIStyle style;
        private Texture2D plate;
        private bool measuring;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance != null || Application.isBatchMode) return;
            var host = new GameObject("Performance Readout");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<PerformanceReadout>();
        }

        private void OnEnable()
        {
            GraphicsPreferences.Changed += Apply;
            Apply();
        }

        private void OnDisable()
        {
            GraphicsPreferences.Changed -= Apply;
            Measure(false);
        }

        private void OnDestroy()
        {
            if (plate != null) Destroy(plate);
            if (instance == this) instance = null;
        }

        private void Apply() => Measure(GraphicsPreferences.PerformanceReadout);

        /// <summary>Starts or stops the counters: none of them costs anything while the readout is off.</summary>
        private void Measure(bool on)
        {
            if (measuring == on) return;
            measuring = on;
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
            Line("Resolution", Screen.width + " x " + Screen.height + "  (scale " + GraphicsPreferences.RenderScale.ToString("0.00") + ")");
            Line("Quality", GraphicsPreferences.Preset.ToString());

            text.Append("\nMEMORY\n");
            Line("In use", Megabytes(usedMemory));
            Line("Scripts", Megabytes(managedMemory));
            if (allocatedInFrame.Valid && Debug.isDebugBuild)
                Line("Allocated this frame", (allocatedInFrame.LastValue / 1024f).ToString("0.0") + " KB");

            text.Append("\nNETWORK\n");
            WriteNetwork();
            shown = text.ToString();
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
            if (network.NetworkTickSystem != null) Line("Tick rate", network.NetworkTickSystem.TickRate + " a second");
            if (network.SpawnManager != null) Line("Networked objects", network.SpawnManager.SpawnedObjectsList.Count.ToString());
        }

        private void Line(string label, string value) => text.Append(label.PadRight(22)).Append(value).Append('\n');

        private static string Count(ProfilerRecorder recorder) => recorder.Valid ? recorder.LastValue.ToString("N0") : "-";

        private static string Megabytes(ProfilerRecorder recorder) => recorder.Valid ? (recorder.LastValue / Megabyte).ToString("N0") + " MB" : "-";

        private void OnGUI()
        {
            if (!measuring || shown.Length == 0 || Event.current.type != EventType.Repaint) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New", "Menlo", "DejaVu Sans Mono" }, 13),
                    fontSize = 13, richText = false, wordWrap = false, alignment = TextAnchor.UpperLeft,
                };
                style.normal.textColor = new Color(0.86f, 0.95f, 0.9f);
                plate = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                plate.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
                plate.Apply();
            }
            // Sized for a 1080-line screen and scaled with it, so it reads the same on a larger one.
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            Matrix4x4 before = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            Vector2 size = style.CalcSize(new GUIContent(shown));
            float height = size.y + Padding * 2f;
            var box = new Rect(Margin, Mathf.Max(Margin, (Screen.height / scale - height) * 0.5f), size.x + Padding * 2f, height);
            GUI.DrawTexture(box, plate);
            GUI.Label(new Rect(box.x + Padding, box.y + Padding, size.x, size.y), shown, style);
            GUI.matrix = before;
        }
    }
}
