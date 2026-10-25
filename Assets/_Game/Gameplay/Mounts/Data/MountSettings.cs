using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>
    /// Every number of the mount system in one place (owner, 25 Oct 2026: feed, EXP and evolution like the pets, as a
    /// system of its own). Growth and hunger work as the pets' do, with three steps instead of five; change the
    /// numbers here, never in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Mounts/Mount Settings")]
    public sealed class MountSettings : ScriptableObject
    {
        [Header("Growth (like a pet's)")]
        [Tooltip("Mount EXP that takes the growth meter from 0 % to 100 %.")]
        [Min(1f)] public float xpToFullGrowth = 100_000f;
        [Tooltip("Mount EXP for each Hostile its owner kills while it is the selected mount, ridden or not.")]
        [Min(0f)] public float xpPerHostileKill = 0.04f;

        [Header("Hunger (only while ridden)")]
        [Tooltip("Percent of the hunger meter lost per minute while the mount is ridden.")]
        [Min(0f)] public float hungerDrainPercentPerMinute = 0.05f;
        [Tooltip("The owner is warned once when hunger falls to this percent.")]
        [Range(0f, 100f)] public float hungerWarningPercent = 20f;
        [Tooltip("Seconds between the server writing the ridden mount's hunger into the character's mounts.")]
        [Min(1f)] public float hungerWriteSeconds = 10f;

        [Header("Riding speed (grows with growth; owner, 25 Oct 2026: top speed 34 m/s)")]
        [Tooltip("Walking on the mount, in metres a second: at 0 % growth (x) and at 100 % (y). A starving mount goes at x.")]
        public Vector2 walkSpeed = new Vector2(6f, 9f);
        [Tooltip("Running on the mount (the Run selection, as on foot), in metres a second: at 0 % growth (x) and at 100 % (y). " +
                 "The Run skill on foot tops out at 28. The Move Speed stat does not add to it.")]
        public Vector2 runSpeed = new Vector2(20f, 34f);

        [Header("The mount's own clips")]
        [Tooltip("The ground speed, in metres a second, at which the walk clip (x) and the run clip (y) look right played at 1x. Faster or slower travel plays them faster or slower.")]
        public Vector2 clipMetresPerSecond = new Vector2(4f, 14f);
        [Tooltip("The slowest (x) and fastest (y) the walk and run clips are ever played.")]
        public Vector2 clipPlaybackRange = new Vector2(0.7f, 2.4f);

        [Header("The rider at a gallop (owner, 25 Oct 2026: lean forward and bob a little)")]
        [Tooltip("Degrees the rider leans forward from the hips at a gallop, on top of the riding clip.")]
        [Range(0f, 40f)] public float gallopLean = 16f;
        [Tooltip("How far the rider rises and drops with each of the mount's strides at a gallop, in metres. Keep it very subtle (owner).")]
        [Range(0f, 0.2f)] public float gallopBob = 0.02f;
        [Tooltip("The same at a walk: a slight lean and bob.")]
        [Range(0f, 20f)] public float walkLean = 3f;
        [Range(0f, 0.1f)] public float walkBob = 0.006f;
        [Tooltip("Strides the mount's walk and run clips each hold (the Brambleback's hold three).")]
        [Min(1)] public int stridesPerClip = 3;
        [Tooltip("How quickly the lean comes and goes, in seconds.")]
        [Min(0.01f)] public float leanEase = 0.25f;

        [Header("Idling")]
        [Tooltip("At rest the mount and its rider play one of their other idles (a sniff, a look round, a pat on the neck) every this many seconds. 0 = never.")]
        [Min(0f)] public float idleVarietySeconds = 9f;

        public MountTuning Tuning => new MountTuning(xpToFullGrowth, xpPerHostileKill, hungerDrainPercentPerMinute);
    }
}
