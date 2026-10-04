using UnityEditor;

namespace KryticalNova.EditorTools.Vendors
{
    /// <summary>
    /// Cartoon FX Pack v2 shows its welcome screen once per Editor session until its own "don't show again" box is
    /// ticked. That choice is stored per machine, so this keeps it ticked for everyone who opens the project. The
    /// vendor code is untouched, and the screen still opens from Tools ▸ CTFX2 ▸ Welcome Screen.
    /// </summary>
    [InitializeOnLoad]
    internal static class CartoonFxWelcomeScreen
    {
        // WatermarkWindowConfig.DontShowAgainKey in the pack's CTFX2WelcomeScreen.cs.
        private const string DontShowAgainKey = "CTFX2_Watermark_DontShow";

        static CartoonFxWelcomeScreen()
        {
            // The pack reads this from a delayed call after scripts load, so setting it here is always in time.
            if (!EditorPrefs.GetBool(DontShowAgainKey, false)) EditorPrefs.SetBool(DontShowAgainKey, true);
        }
    }
}
