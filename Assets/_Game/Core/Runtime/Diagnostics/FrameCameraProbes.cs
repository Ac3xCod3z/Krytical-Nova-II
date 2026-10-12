using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.Diagnostics
{
    /// <summary>
    /// How long the main thread spent on each camera of the frame: finding what it sees and laying out its
    /// passes (the drawing itself is the render thread's and the GPU's, and has its own columns). The step that
    /// draws the frame is one step to <see cref="FramePhaseProbes"/>; this says which camera in it took the time:
    /// the world's, a preview's, a portrait's.
    /// </summary>
    internal static class FrameCameraProbes
    {
        private const int MostCameras = 8;
        private const int MostNames = 64;

        private static readonly Camera[] cameras = new Camera[MostCameras];
        private static readonly long[] ticks = new long[MostCameras];
        // A camera's name is a new string every time it is asked for: asked once for each camera.
        private static readonly Dictionary<Camera, string> names = new Dictionary<Camera, string>(16);
        private static int count, current = -1;
        private static long started;
        private static bool installed;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
        }

        public static void Remove()
        {
            if (!installed) return;
            installed = false;
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.endCameraRendering -= End;
            NewFrame();
            names.Clear();
        }

        /// <summary>The frame is over: what was gathered for it is let go.</summary>
        public static void NewFrame()
        {
            for (int i = 0; i < count; i++)
            {
                cameras[i] = null;
                ticks[i] = 0;
            }
            count = 0;
            current = -1;
        }

        private static void Begin(ScriptableRenderContext context, Camera camera)
        {
            current = -1;
            for (int i = 0; i < count; i++)
                if (ReferenceEquals(cameras[i], camera)) { current = i; break; }
            if (current < 0 && count < MostCameras)
            {
                current = count++;
                cameras[current] = camera;
            }
            started = Stopwatch.GetTimestamp();
        }

        private static void End(ScriptableRenderContext context, Camera camera)
        {
            if (current >= 0) ticks[current] += Stopwatch.GetTimestamp() - started;
            current = -1;
        }

        /// <summary>
        /// The cameras of the frame that just ended, as "camera:Name=ms", longest first; null when none took
        /// <paramref name="atLeastMs"/>. Makes strings: for a slow frame's line only.
        /// </summary>
        public static string Describe(double atLeastMs)
        {
            StringBuilder text = null;
            for (int pass = 0; pass < count; pass++)
            {
                int longest = -1;
                for (int i = 0; i < count; i++)
                    if (ticks[i] > 0 && (longest < 0 || ticks[i] > ticks[longest])) longest = i;
                if (longest < 0) break;
                double ms = ticks[longest] * 1000.0 / Stopwatch.Frequency;
                ticks[longest] = -ticks[longest]; // said; put back below
                if (ms < atLeastMs) break;
                text ??= new StringBuilder(96);
                if (text.Length > 0) text.Append(' ');
                text.Append("camera:").Append(NameOf(cameras[longest])).Append('=').Append(ms.ToString("0.0", CultureInfo.InvariantCulture));
            }
            for (int i = 0; i < count; i++)
                if (ticks[i] < 0) ticks[i] = -ticks[i];
            return text?.ToString();
        }

        private static string NameOf(Camera camera)
        {
            if (camera == null) return "(gone)";
            if (names.TryGetValue(camera, out string name)) return name;
            if (names.Count >= MostNames) names.Clear();
            name = camera.name.Replace(' ', '_');
            names[camera] = name;
            return name;
        }
    }
}
