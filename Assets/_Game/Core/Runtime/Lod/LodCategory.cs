namespace KryticalNova.Rendering
{
    /// <summary>What kind of thing a mesh in the world is, for how its detail falls away with distance.</summary>
    public enum LodCategory : byte
    {
        Tree,
        Bush,
        Rock,
        Building,
        Prop,
        SmallProp,
    }
}
