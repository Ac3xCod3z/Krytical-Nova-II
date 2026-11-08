using KryticalNova.Items;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// A Tripo set's look templates are made without an icon (the ladder items wear the line's painted icon), which
    /// leaves the template itself blank. This gives every blank look template the icon of the line that wears it.
    /// </summary>
    public static class GearLookIconFiller
    {
        [MenuItem("Krytical Nova/Items/Blender Sets/Fill Set Look Icons")]
        public static int Fill()
        {
            var dictionary = AssetDatabase.LoadAssetAtPath<GearDictionary>(SidekickGearGenerator.DictionaryPath);
            if (dictionary == null) return 0;
            int filled = 0;
            foreach (var line in dictionary.Lines)
            {
                if (!line.IsValid || line.look == null || line.look.icon != null || line.icon == null) continue;
                line.look.icon = line.icon;
                EditorUtility.SetDirty(line.look);
                filled++;
            }
            if (filled > 0) AssetDatabase.SaveAssets();
            Debug.Log($"[Items] {filled} look templates took their line's icon.");
            return filled;
        }
    }
}
