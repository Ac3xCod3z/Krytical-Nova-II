using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Keeps the vendor packs' TextMesh Pro fonts out of every save. Their fonts draw letters on demand: showing a
    /// word they have not drawn before (the game's name on a title screen, in the Editor or in Play) adds its glyphs
    /// to the font asset, and the next save would write them into the vendor's file. Vendor files are never changed
    /// (CLAUDE.md), and the glyphs are drawn again whenever they are needed, so nothing is lost by leaving them out.
    /// </summary>
    public sealed class VendorFontGuard : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            List<string> kept = null;
            for (int i = 0; i < paths.Length; i++)
            {
                if (!IsVendorFont(paths[i]))
                {
                    kept?.Add(paths[i]);
                    continue;
                }
                if (kept == null)
                {
                    kept = new List<string>(paths.Length);
                    for (int j = 0; j < i; j++) kept.Add(paths[j]);
                }
                // Forgotten, not only skipped: or it would be offered again with every save.
                foreach (Object part in AssetDatabase.LoadAllAssetsAtPath(paths[i]))
                    if (part != null) EditorUtility.ClearDirty(part);
            }
            return kept != null ? kept.ToArray() : paths;
        }

        private static bool IsVendorFont(string path)
        {
            if (!path.EndsWith(".asset", System.StringComparison.Ordinal)) return false;
            if (!path.StartsWith("Assets/Synty/", System.StringComparison.Ordinal) && !path.StartsWith("Assets/_ThirdParty/", System.StringComparison.Ordinal)) return false;
            return AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(TMP_FontAsset);
        }
    }
}
