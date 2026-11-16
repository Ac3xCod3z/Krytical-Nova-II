namespace KryticalNova.Rendering
{
    /// <summary>
    /// What a character's level of detail (<see cref="CharacterLod"/>) reads from the world's LOD settings. CharacterLod lives in
    /// Core so the server can keep it as an empty shell (the player prefab carries it); WorldLodSettings, in Rendering, fills this.
    /// </summary>
    public interface ICharacterLodSettings
    {
        float CharacterMidDistance { get; }
        float CharacterFarDistance { get; }
        float CharacterHiddenDistance { get; }
        float CharacterRefresh { get; }
        /// <summary>Brings these settings in (the preset's tier is applied).</summary>
        void Use();
    }
}
