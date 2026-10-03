using UnityEngine;
using System;

namespace PSD.Creator.Data.Attributes
{
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class LabelAttribute : PropertyAttribute
    {
        public readonly string Label;
        public LabelAttribute(string label) => Label = label;
    }
}