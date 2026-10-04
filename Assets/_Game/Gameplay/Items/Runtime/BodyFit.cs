using System;

namespace KryticalNova.Items
{
    /// <summary>
    /// The body shapes gear is modelled for. Humans and elves share one; a robot chassis and a skeleton each need
    /// their own, because parts made for one do not line up on another.
    /// </summary>
    [Flags]
    public enum BodyFit : byte
    {
        None = 0,
        Humanoid = 1 << 0,
        Robot = 1 << 1,
        Skeleton = 1 << 2,
        All = Humanoid | Robot | Skeleton
    }
}
