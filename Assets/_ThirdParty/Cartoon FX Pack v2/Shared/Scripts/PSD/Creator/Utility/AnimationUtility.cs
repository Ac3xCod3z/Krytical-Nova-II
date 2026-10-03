using UnityEngine;

namespace PSD.Creator.Utility
{
    public static class AnimationUtility
    {
        public static AnimationCurve DefaultConstantZero => AnimationCurve.Constant(0, 1, 0);
        public static AnimationCurve DefaultConstantOne => AnimationCurve.Constant(0, 1, 1);
        public static AnimationCurve DefaultLinear => AnimationCurve.Linear(0, 0, 1, 1);

        public static AnimationCurve DefaultEasing => new AnimationCurve(
            new Keyframe(0, 0),
            new Keyframe(0.125f, 1),
            new Keyframe(0.25f, 0.75f),
            new Keyframe(0.375f, 0.5f),
            new Keyframe(0.5f, 0.25f),
            new Keyframe(0.625f, 0.125f),
            new Keyframe(0.75f, 0.0625f),
            new Keyframe(0.875f, 0.03125f),
            new Keyframe(1, 0)
        );
    }
}