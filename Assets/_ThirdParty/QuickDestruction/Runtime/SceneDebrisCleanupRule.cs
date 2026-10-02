namespace RipVertices.QuickDestruction
{
    /// <summary>When the scene retires loose debris, independent of its visual transition.</summary>
    public enum SceneDebrisCleanupRule
    {
        SceneLimit = 0,
        KeepAll = 1,
        AfterDelay = 2,
        SmallAfterDelay = 3,
        DynamicAfterDelay = 4,
        SmallAndRuntimeAfterDelay = 5
    }
}
