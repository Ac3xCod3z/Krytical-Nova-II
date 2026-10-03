using UnityEngine;
using System;

namespace PSD.Creator.Data.Attributes
{
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class MinMaxSliderAttribute : PropertyAttribute
    {
        public readonly Vector2 Range;
        public MinMaxSliderAttribute(float min, float max) => Range = new Vector2(min, max);
    }
}