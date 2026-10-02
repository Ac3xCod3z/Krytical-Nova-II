namespace RipVertices.QuickDestruction
{
    /// <summary>How excess scene debris disappears after its physics is disabled.</summary>
    public enum DebrisDespawnMode
    {
        SmoothShrink = 0,
        ShrinkAndSink = 1,
        Instant = 2
    }
}
