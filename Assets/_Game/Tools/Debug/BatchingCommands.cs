using KryticalNova.Diagnostics;
using KryticalNova.Rendering;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>Quantum Console commands for what draws the world in batches (local, presentation only).</summary>
    public static class BatchingCommands
    {
        [Command("gm.batching", "What falls out of the GPU Resident Drawer and why, the shader variants in view and the shadow casters " +
                                "by kind: a summary here, the whole report in a batching_ file beside the performance logs.")]
        private static string Report()
        {
            BatchingReport.Result result = BatchingReport.Build();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string folder = PerformanceLog.IsRecording && !string.IsNullOrEmpty(PerformanceLog.FilePath)
                ? System.IO.Path.GetDirectoryName(PerformanceLog.FilePath) : PerformanceLogRecorder.Folder();
            string path = System.IO.Path.Combine(folder, "batching_" + System.DateTime.Now.ToString("yyyy-MM-dd_HHmmss", inv) + ".txt");
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                System.IO.File.WriteAllText(path, result.Text);
            }
            catch (System.Exception e) { return result.Summary + "\nThe report could not be written: " + e.Message; }
            return result.Summary + "\nWritten to " + path;
        }

        [Command("gm.batching", "Switch one of the batching pass's changes, to measure with and without (gm.bench): " +
                                "shadows (small world meshes cast no shadow on Low and Medium) or occlusion (the GPU Resident Drawer's occlusion culling).")]
        private static string Switch(string change, bool on)
        {
            switch (change.Trim().ToLowerInvariant())
            {
                case "shadows":
                    ShadowBands.Enabled = on;
                    Note("shadows", on);
                    return (on ? "Shadow bands on: " + ShadowBands.Lifted + " small world meshes cast no shadow at this preset."
                               : "Shadow bands off: every world mesh casts its shadow.");
                case "occlusion":
                    ResidentDrawerOcclusion.On = on;
                    Note("occlusion", on);
                    return (on ? "GPU Resident Drawer occlusion culling on." : "GPU Resident Drawer occlusion culling off.")
                           + (PipelineRebuilds.Waiting ? " It changes the pipeline, so it takes effect at the next menu (a map's light probes do not survive it)." : string.Empty);
                default:
                    return "Unknown change '" + change + "': shadows or occlusion.";
            }
        }

        // Numbers before and after a switch are not to be compared: the log says what is in force.
        private static void Note(string change, bool on)
        {
            if (PerformanceLog.IsRecording) PerformanceLog.Change("batching", change + "=" + (on ? "on" : "off"));
        }
    }
}
