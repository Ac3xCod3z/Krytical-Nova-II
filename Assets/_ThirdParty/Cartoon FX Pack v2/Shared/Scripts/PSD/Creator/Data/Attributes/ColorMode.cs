using UnityEngine;
using System;

namespace PSD.Creator.Data.Attributes
{
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class ColorModeAttribute : PropertyAttribute
    {
        public readonly string ModeProperty;
        public readonly string ColorProperty;
        public ColorModeAttribute(string modeProperty, string colorProperty)
        {
            ModeProperty = modeProperty;
            ColorProperty = colorProperty;
        }
    }
}