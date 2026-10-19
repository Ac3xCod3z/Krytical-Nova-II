using System.IO;
using KryticalNova.Rendering;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Krytical Nova ▸ Rendering ▸ Batching Report (Open Scene): what falls out of the GPU Resident Drawer and why, the
    /// shader variants in the scene and the shadow casters by kind (<see cref="BatchingReport"/>). Writes
    /// Library/KryticalNova.Batching.txt and logs a one-line summary; a cancelable progress bar, never a dialog.
    /// </summary>
    public static class BatchingReportMenu
    {
        public const string OutputPath = "Library/KryticalNova.Batching.txt";

        [MenuItem("Krytical Nova/Rendering/Batching Report (Open Scene)")]
        public static void Run() => Write();

        /// <summary>Builds and writes the report; returns its summary line. Callable from an eval.</summary>
        public static string Write()
        {
            BatchingReport.Result result;
            try
            {
                result = BatchingReport.Build(share => EditorUtility.DisplayCancelableProgressBar("Batching Report", "Sorting the scene's renderers", share));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            File.WriteAllText(OutputPath, result.Text);
            Debug.Log("[Batching] report: " + result.Summary + " Written to " + OutputPath);
            return result.Summary;
        }
    }
}
