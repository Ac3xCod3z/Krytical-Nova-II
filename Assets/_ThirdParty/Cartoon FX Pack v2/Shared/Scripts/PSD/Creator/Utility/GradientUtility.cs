using UnityEngine;

namespace PSD.Creator.Utility
{
    public static class GradientUtility
    {
        public static Gradient DefaultGradient => new Gradient()
        {
            colorKeys = new GradientColorKey[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
        };
    }
}