using KryticalNova.Factions;
using KryticalNova.Items;
using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>
    /// A whole look of a Hall, changed at one stroke by one item: a Facade (the building that stands on the Lot)
    /// or a Finish (every wall, the floor or the ceiling of the room inside). A Hall wears one of each; what it
    /// does not wear is kept in the Furnishing Crate like any furnishing, and is its faction's own in the same
    /// way. Each faction has a standard look for each of the four, which every Hall has and no Crate holds.
    /// Made by the looks builder from vendor prefabs, which are only read.
    /// </summary>
    public sealed class HallLookDefinition : ScriptableObject
    {
        [Tooltip("The item this look is carried, stored and traded as.")]
        public ItemDefinition item;
        public ushort itemId;
        public FactionId faction;
        [Tooltip("Which of a Hall's four looks it is.")]
        public HomeLook slot;
        [Tooltip("The look a new Hall of this faction wears. Every Hall has it; it is never in a Crate.")]
        public bool standard;

        [Header("Model")]
        [Tooltip("A Facade: the building. A Finish: one panel of wall, one tile of floor or ceiling, repeated to fill the room.")]
        public GameObject prefab;
        [Tooltip("What the vendor's model is multiplied by to hold it to the world's measure.")]
        [Min(0.01f)] public float modelScale = 1f;
        [Tooltip("The turn the model is given. A Facade: so its door faces the gate. A wall panel: so it runs along x with its face to +z. A floor or ceiling tile: so its face is up or down.")]
        public Vector3 modelEuler;
        [Tooltip("The model's box after the scale and the turn above, measured by the builder.")]
        public Vector3 boundsCenter;
        public Vector3 boundsSize = Vector3.one;

        [Tooltip("A Finish: worn by the panel instead of its own material (the packs' plain walls and floors take any of their tiling surfaces).")]
        public Material material;

        [Header("Facade")]
        [Tooltip("Moves the building on its pad, in metres (x across, z towards the back): so that its door stands at the Lot's door.")]
        public Vector2 padOffset;

        public string DisplayName => item != null ? item.displayName : name;
        public bool IsFinish => slot != HomeLook.Facade;
    }
}
