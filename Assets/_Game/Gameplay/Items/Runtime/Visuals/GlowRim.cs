using System.Collections.Generic;
using KryticalNova.Rendering;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// The glow of one glowing thing (a tempered weapon, an evolved pet): a halo of light round its outline that
    /// breathes, drawn on the screen by <see cref="GlowHaloFeature"/> so it follows any model's shape and never
    /// tints the model itself. The stronger looks add sparks that twinkle, lightning and flame, all in the light's
    /// own colour and only where the model glows. The owner sets the look once and adds the models that wear it (a
    /// pair of weapons shares one glow). Nothing is added to the models.
    /// </summary>
    public sealed class GlowRim
    {
        private readonly GlowHalo halo = new GlowHalo();
        private readonly List<GameObject> models = new List<GameObject>(2);

        public void Set(in GlowHaloLook look) => halo.Set(look);

        /// <summary>
        /// Puts the glow on a model, as well as on any it is already on. A model that says which part of it glows
        /// (<see cref="WeaponGlowZone"/>) is lit there only; any other glows all over.
        /// </summary>
        public void Attach(GameObject model)
        {
            if (model == null) return;
            if (!models.Contains(model)) models.Add(model);
            GlowZone zone = default;
            string[] dark = null;
            if (model.TryGetComponent(out WeaponGlowZone part))
            {
                zone = part.zone;
                dark = part.exclude;
            }
            halo.Add(model, zone, dark);
        }

        /// <summary>Takes the glow off everything that wears it.</summary>
        public void Detach()
        {
            halo.Clear();
            models.Clear();
        }
    }
}
