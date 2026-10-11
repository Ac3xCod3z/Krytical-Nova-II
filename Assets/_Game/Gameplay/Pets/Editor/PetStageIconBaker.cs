using System.Collections.Generic;
using KryticalNova.Pets;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes an icon for each evolution of a pet that has a model of its own (menu Krytical Nova ▸ Items ▸ Bake Pet
    /// Stage Icons). A stage whose model is empty wears the pet item's own model, and so the item's own icon: only
    /// stages with a model get a picture here. The pictures are drawn as every item icon is
    /// (<see cref="ItemIconCapture"/>: the preview renderer's comic look, from the front-left), into
    /// <see cref="IconFolder"/> as Pet_&lt;Pet&gt;_S&lt;stage&gt;.png, and written onto the stage
    /// (<see cref="PetStageLook.icon"/>), where the interface finds the one for the pet as it is now. Run it again
    /// after a stage's model changes, or when another pet gets its evolutions' models.
    /// </summary>
    public static class PetStageIconBaker
    {
        public const string IconFolder = "Assets/_Game/Gameplay/Pets/Data/Assets/Icons";

        [MenuItem("Krytical Nova/Items/Bake Pet Stage Icons")]
        public static void Bake()
        {
            var subjects = new List<(GameObject model, string path)>();
            var owners = new List<(PetDefinition pet, int stage)>();
            foreach (string guid in AssetDatabase.FindAssets("t:PetDefinition", new[] { "Assets/_Game/Gameplay/Pets/Data/Assets" }))
            {
                var pet = AssetDatabase.LoadAssetAtPath<PetDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (pet == null || pet.stages == null) continue;
                for (int stage = 0; stage < pet.stages.Length; stage++)
                {
                    if (pet.stages[stage].model == null) continue;
                    subjects.Add((pet.stages[stage].model, $"{IconFolder}/{pet.name.Replace("PetDef_", "Pet_")}_S{stage + 1}.png"));
                    owners.Add((pet, stage));
                }
            }
            if (subjects.Count == 0)
            {
                Debug.Log("[Pets] No pet has a model of its own for any stage: nothing to bake.");
                return;
            }

            WeatherVfxBuilder.EnsureFolder(IconFolder);
            List<Sprite> icons = ItemIconCapture.CaptureModels(subjects);
            int given = 0;
            for (int i = 0; i < owners.Count && i < icons.Count; i++)
            {
                if (icons[i] == null) continue;
                (PetDefinition pet, int stage) = owners[i];
                PetStageLook look = pet.stages[stage];
                look.icon = icons[i];
                pet.stages[stage] = look;
                EditorUtility.SetDirty(pet);
                given++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Pets] {given} stage icon(s) baked into {IconFolder}.");
        }
    }
}
