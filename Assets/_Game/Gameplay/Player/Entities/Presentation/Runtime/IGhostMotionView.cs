namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>
    /// The body's side of a ghost's jump (M3b): the clip and its feel, played by the layer that knows the body's animation (this
    /// assembly cannot see it). <see cref="PlayerPresentationFollowSystem"/> calls it once per phase of each jump, by the plan's
    /// number, so a rollback or a late snapshot never restarts a jump; the follow itself places the root on the ghost's arc.
    /// </summary>
    public interface IGhostMotionView
    {
        /// <summary>A jump starts; <paramref name="clipSeconds"/> of its clip are already over (a late snapshot fast-forwards).</summary>
        void BeginJump(float clipSeconds);

        /// <summary>The feet leave the ground.</summary>
        void JumpTookOff();

        /// <summary>The feet touch down; the landing recovery plays out in place.</summary>
        void JumpLanded();

        /// <summary>The jump was taken back before it landed (refused, called off at take-off, a teleport): back to standing.</summary>
        void JumpCancelled();

        /// <summary>
        /// A vault starts (M3c): the body's own clip for the obstacle's height class, <paramref name="elapsedSeconds"/> of the
        /// <paramref name="airSeconds"/> already over (a late frame or snapshot fast-forwards), with the take-off feel.
        /// </summary>
        void BeginVault(byte heightClass, float elapsedSeconds, float airSeconds);

        /// <summary>The vault lands: back to the walk at <paramref name="speedMps"/>, with the landing feel.</summary>
        void EndVault(float speedMps);
    }
}
