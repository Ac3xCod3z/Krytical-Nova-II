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
    }
}
