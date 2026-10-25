using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// The paint of a painted gear part (a Tripo model fitted in Blender): its own texture, drawn as it was painted,
    /// instead of the Sidekick colour map every other part reads (owner, 24 Oct 2026: flattening the paint into the
    /// colour grid "broke the gear"). The dye regions say which areas a colourway recolours: R main cloth, G second
    /// cloth, B plate and metal, A leather and trim; a dye keeps the paint's light and dark and swaps only the colour
    /// (<see cref="GearPaint"/>). Made by Krytical Nova ▸ Items ▸ Import Painted Gear; sits in
    /// Resources/Meshes/PartPaint as the part's name plus "_Paint", where the part library finds it.
    /// </summary>
    public sealed class GearPaintLook : ScriptableObject
    {
        [Tooltip("The material the part is drawn in, the paint as its base map. Shared by the parts that share a paint.")]
        public Material material;
        [Tooltip("Where a dye lands: R main cloth, G second cloth, B plate and metal, A leather and trim.")]
        public Texture2D regions;
        [Tooltip("The paint's mean brightness in each region (R, G, B, A as above), so a dye keeps the shading around it.")]
        public Vector4 regionBrightness = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);

        public Texture Paint => material != null ? material.mainTexture : null;
    }
}
