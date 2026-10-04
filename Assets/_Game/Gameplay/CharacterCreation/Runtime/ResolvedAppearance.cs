using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;

namespace KryticalNova.CharacterCreation
{
    /// <summary>A recipe turned into loaded parts, ready for <see cref="CharacterAppearance.ApplyBase"/>. Reusable.</summary>
    public sealed class ResolvedAppearance
    {
        /// <summary>Always worn: head, hair, face.</summary>
        public readonly List<SidekickPartRef> fixedParts = new List<SidekickPartRef>(16);
        /// <summary>The character's own body and outfit, shown wherever no gear is equipped.</summary>
        public readonly List<SidekickPartRef> defaultParts = new List<SidekickPartRef>(32);
        public BodyShapeWeights shape;
        public SidekickPartRef wrap;
        /// <summary>Recipe parts that were unknown or failed to load.</summary>
        public int missing;

        public void Clear()
        {
            fixedParts.Clear();
            defaultParts.Clear();
            shape = default;
            wrap = default;
            missing = 0;
        }
    }
}
