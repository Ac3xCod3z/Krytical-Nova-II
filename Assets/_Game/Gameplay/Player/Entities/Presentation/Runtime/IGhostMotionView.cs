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
    }
}
