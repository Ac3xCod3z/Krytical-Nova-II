using System;
using System.Collections.Generic;
using KryticalNova.Items.Visuals;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// Applies a <see cref="CharacterRecipe"/> to a Sidekick model: the parts and body shape go to
    /// <see cref="CharacterAppearance"/>, the colours into a per-character colour map on a material instance, and the
    /// attachment joints are moved to suit the body shape. Used by the networked player, the offline avatar and the
    /// menu showroom, so a character looks the same everywhere. The colour map also carries the set colours of the
    /// gear the character wears, and follows what it puts on and takes off.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterAppearance))]
    public sealed class CharacterLook : MonoBehaviour, IPreviewDollPart
    {
        private static readonly int ColorMapId = Shader.PropertyToID("_ColorMap");
        private static readonly int CutsAmountId = Shader.PropertyToID("_CutsAmount");
        private static readonly int EyelinerAmountId = Shader.PropertyToID("_EyelinerAmount");
        private static readonly int DirtAmountId = Shader.PropertyToID("_DirtAmount");

        private readonly ResolvedAppearance resolved = new ResolvedAppearance();
        private readonly Dictionary<string, Transform> joints = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private CharacterAppearance appearance;
        private Material material;
        private Texture2D colorMap;
        private Color32[] pixels;
        private bool jointsIndexed;
        private CharacterCreationDatabase database;
        private CharacterLook mirrored;
        private bool gearColorsStale;

        public CharacterRecipe Current { get; private set; }
        /// <summary>False until a recipe has been applied: the prefab's authored look is showing.</summary>
        public bool HasLook { get; private set; }
        /// <summary>Raised after a recipe is applied (the merged mesh follows in the same frame's LateUpdate).</summary>
        public event Action Applied;

        public CharacterAppearance Appearance
        {
            get
            {
                if (appearance == null) appearance = GetComponent<CharacterAppearance>();
                return appearance;
            }
        }

        /// <summary>Returns false (and changes nothing) when the recipe cannot be resolved against the database.</summary>
        public bool Apply(in CharacterRecipe recipe, CharacterCreationDatabase database)
        {
            if (database == null || Appearance == null) return false;
            if (!RecipeResolver.Resolve(recipe, database, resolved)) return false;

            appearance.ApplyBase(resolved.fixedParts, resolved.defaultParts, resolved.shape, resolved.wrap);
            this.database = database;
            ApplyColors(recipe, database);
            ApplyJoints(recipe, database);

            Current = recipe;
            HasLook = true;
            Applied?.Invoke();
            return true;
        }

        private void OnEnable() => Appearance.WornChanged += OnWornChanged;

        private void OnDisable()
        {
            if (appearance != null) appearance.WornChanged -= OnWornChanged;
        }

        // A loadout changes several slots in one frame: the map is redrawn once, after all of them.
        private void OnWornChanged() => gearColorsStale = true;

        private void LateUpdate()
        {
            if (!gearColorsStale) return;
            gearColorsStale = false;
            if (HasLook && database != null) ApplyColors(Current, database);
        }

        /// <summary>
        /// On the equipment preview doll: wear the original's colours on a colour map of this copy's own, so a piece
        /// that is only tried on shows in its set colours without recolouring the character in the world.
        /// </summary>
        void IPreviewDollPart.BindToOriginal(GameObject original)
        {
            if (mirrored != null) mirrored.Applied -= MirrorOriginal;
            mirrored = original != null ? original.GetComponent<CharacterLook>() : null;
            if (mirrored == null || mirrored == this) { mirrored = null; return; }
            mirrored.Applied += MirrorOriginal;
            MirrorOriginal();
        }

        private void MirrorOriginal()
        {
            if (mirrored == null || !mirrored.HasLook || mirrored.database == null || Appearance == null) return;
            Current = mirrored.Current;
            database = mirrored.database;
            HasLook = true;
            // The parts are not applied here: the doll shares the original's merged mesh until something is tried on.
            ApplyColors(Current, database);
            ApplyJoints(Current, database);
        }

        private void ApplyColors(in CharacterRecipe recipe, CharacterCreationDatabase database)
        {
            var smr = appearance.Renderer;
            if (smr == null || smr.sharedMaterial == null) return;

            if (material == null)
            {
                // One material per character: the colour map is what makes two players look different.
                material = new Material(smr.sharedMaterial) { name = smr.sharedMaterial.name + " (Character)" };
                smr.sharedMaterial = material;
            }
            if (colorMap == null)
            {
                colorMap = CharacterColorMap.CreateTexture(name + "_ColorMap");
                pixels = new Color32[CharacterColorMap.PixelCount];
            }

            CharacterColorMap.Fill(recipe, database, pixels);
            foreach (var slot in CharacterAppearance.ColorOrder) CharacterColorMap.OverlayGear(appearance.GetWorn(slot), pixels);
            colorMap.SetPixels32(pixels);
            colorMap.Apply(false, false);
            material.SetTexture(ColorMapId, colorMap);

            if (material.HasProperty(CutsAmountId)) material.SetFloat(CutsAmountId, recipe.scars * 0.01f);
            if (material.HasProperty(EyelinerAmountId)) material.SetFloat(EyelinerAmountId, recipe.eyeliner * 0.01f);
            if (material.HasProperty(DirtAmountId)) material.SetFloat(DirtAmountId, recipe.grime * 0.01f);
        }

        /// <summary>
        /// Shoulder, back, hip, elbow and knee attachments hang off dedicated joints. Sidekick moves those joints as
        /// the body gets heavier, slimmer, more muscular or feminine, so the attachments stay seated on the surface.
        /// </summary>
        private void ApplyJoints(in CharacterRecipe recipe, CharacterCreationDatabase database)
        {
            var rests = database.JointRests;
            if (rests.Count == 0) return;
            if (!jointsIndexed)
            {
                jointsIndexed = true;
                foreach (var t in GetComponentsInChildren<Transform>(true)) joints.TryAdd(t.name, t);
            }

            float feminine = recipe.bodyType == BodyType.Feminine ? 1f : 0f;
            float heavy = recipe.build > 0 ? recipe.build * 0.01f : 0f;
            float skinny = recipe.build < 0 ? -recipe.build * 0.01f : 0f;
            float bulk = (recipe.muscle + 100f) * 0.005f;

            var offsets = database.JointOffsets;
            foreach (var rest in rests)
            {
                if (!joints.TryGetValue(rest.joint, out var joint) || joint == null) continue;
                Vector3 position = rest.localPosition;
                Quaternion rotation = rest.localRotation;
                foreach (var offset in offsets)
                {
                    if (!string.Equals(offset.joint, rest.joint, StringComparison.Ordinal)) continue;
                    float weight;
                    switch (offset.blend)
                    {
                        case CharacterCreationDatabase.BodyBlend.Feminine: weight = feminine; break;
                        case CharacterCreationDatabase.BodyBlend.Heavy: weight = heavy; break;
                        case CharacterCreationDatabase.BodyBlend.Skinny: weight = skinny; break;
                        default: weight = bulk; break;
                    }
                    if (weight <= 0f) continue;
                    position += Vector3.Lerp(Vector3.zero, offset.maxOffset, weight);
                    rotation *= Quaternion.Lerp(Quaternion.identity, Quaternion.Euler(offset.maxRotationEuler), weight);
                }
                joint.SetLocalPositionAndRotation(position, rotation);
            }
        }

        private void OnDestroy()
        {
            if (mirrored != null) mirrored.Applied -= MirrorOriginal;
            if (material != null) Destroy(material);
            if (colorMap != null) Destroy(colorMap);
        }
    }
}
