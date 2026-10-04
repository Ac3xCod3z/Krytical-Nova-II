using System.Collections;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using UnityEngine;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Renders small busts of characters into textures, for the tiles that pick a lineage and body type in the
    /// creator. One display doll is restyled for each bust on a stage far below the showroom, photographed by its own
    /// camera, then removed: the pictures always match the real parts, with nothing baked into the project.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortraitStudio : MonoBehaviour
    {
        [SerializeField] private CharacterCreationDatabase database;
        [Tooltip("The player's Sidekick model prefab (Player_01).")]
        [SerializeField] private GameObject modelPrefab;
        [SerializeField] private AnimationClip idleClip;
        [Tooltip("Renders only on request. Kept disabled.")]
        [SerializeField] private Camera portraitCamera;
        [Tooltip("Where the sitter stands, out of sight of the showroom camera.")]
        [SerializeField] private Transform stage;
        [SerializeField, Min(64)] private int size = 320;
        [Tooltip("The sitter is turned this far from facing the camera.")]
        [SerializeField] private float turn = 18f;

        private readonly List<RenderTexture> textures = new List<RenderTexture>();

        /// <summary>Photographs each recipe into the image at the same index. Images are hidden until their picture is ready.</summary>
        public void Render(IReadOnlyList<CharacterRecipe> recipes, IReadOnlyList<RawImage> targets, Color backdrop)
        {
            if (portraitCamera == null || stage == null || modelPrefab == null) return;
            foreach (var target in targets)
                if (target != null) target.enabled = false;
            portraitCamera.backgroundColor = backdrop;
            StartCoroutine(RenderRoutine(recipes, targets));
        }

        private IEnumerator RenderRoutine(IReadOnlyList<CharacterRecipe> recipes, IReadOnlyList<RawImage> targets)
        {
            var doll = CharacterDoll.Create(modelPrefab, stage, idleClip);
            if (doll == null) yield break;
            doll.transform.localRotation = Quaternion.Euler(0f, 180f + turn, 0f); // the stage's -Z faces the camera

            int count = Mathf.Min(recipes.Count, targets.Count);
            for (int i = 0; i < count; i++)
            {
                if (targets[i] == null || !doll.Apply(recipes[i], database)) continue;
                // A freshly merged mesh is not drawn in the frame it was built.
                yield return null;
                yield return new WaitForEndOfFrame();

                var texture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { name = $"Portrait {i}" };
                portraitCamera.targetTexture = texture;
                portraitCamera.Render();
                portraitCamera.targetTexture = null;
                textures.Add(texture);
                targets[i].texture = texture;
                targets[i].enabled = true;
            }
            Destroy(doll.gameObject);
        }

        private void OnDestroy()
        {
            foreach (var texture in textures)
            {
                if (texture == null) continue;
                texture.Release();
                Destroy(texture);
            }
            textures.Clear();
        }
    }
}
