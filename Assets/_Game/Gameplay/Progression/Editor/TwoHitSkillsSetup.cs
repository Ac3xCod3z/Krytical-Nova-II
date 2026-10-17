using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The two-hit attacks (owner, 16 Oct 2026) in one go: the Monoblade's four, every class's copy, their Skill
    /// Chips and drops, an icon for every new skill, and the Hotbar's fifth letter slot (T) in every faction's HUD.
    /// Each step is its own menu too; this runs them in the order they need. Safe to run again.
    /// </summary>
    public static class TwoHitSkillsSetup
    {
        [MenuItem("Krytical Nova/Progression/Build Two-Hit Skills (all steps)")]
        public static void Run()
        {
            BladeSkillBuilder.Build();
            ClassSkillBuilder.Build();
            UtilityItemGenerator.Generate();
            CraftingBuilder.Build();

            IconLibrary library = IconBaker.Library();
            int given = IconPacks.GiveToAllWithout(library, IconBaker.Settings(), IconTargets.Skills());
            // The chips share the attack chip's picture: generating the items again hands it to the new ones.
            UtilityItemGenerator.Generate();
            IconPacks.CheckEverything();

            // The HUD builder is in the presentation editor assembly, which this one cannot name.
            if (!EditorApplication.ExecuteMenuItem("Krytical Nova/UI/Build Faction HUD"))
                Debug.LogWarning("[TwoHit] Run Krytical Nova ▸ UI ▸ Build Faction HUD for the Hotbar's T slot.");
            AssetDatabase.SaveAssets();
            Debug.Log($"[TwoHit] Two-hit attacks built; {given} skill icon(s) given from the tags (look them over in Icons ▸ Icon Library).");
        }
    }
}
