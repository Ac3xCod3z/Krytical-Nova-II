using KryticalNova.UI;
using UnityEditor;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Regenerates the in-game UI's skin sheets when a <see cref="UiSkin"/> asset changes, so a value changed in a
    /// skin shows on every screen that uses it without a menu command: as it is edited in the Inspector, and when the
    /// asset is saved by anything else. The sheets it writes are not skin assets, so it cannot set itself off.
    /// </summary>
    [InitializeOnLoad]
    public sealed class UiSkinWatcher : AssetModificationProcessor
    {
        private static bool queued;

        static UiSkinWatcher()
        {
            UiSkin.Edited -= Queue;
            UiSkin.Edited += Queue;
        }

        private static string[] OnWillSaveAssets(string[] paths)
        {
            foreach (string path in paths)
                if (path.StartsWith(FactionSkinBuilder.SkinFolder + "/", System.StringComparison.Ordinal)
                    && AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(UiSkin))
                {
                    Queue();
                    break;
                }
            return paths;
        }

        // Once, after whatever is running now (an edit in the Inspector raises this for every field it touches).
        private static void Queue()
        {
            if (queued) return;
            queued = true;
            // On the Editor's next tick (delayCall waits for the Editor to be in front, and this must not).
            EditorApplication.update += Rebuild;
        }

        private static void Rebuild()
        {
            EditorApplication.update -= Rebuild;
            queued = false;
            FactionSkinBuilder.Build();
        }
    }
}
