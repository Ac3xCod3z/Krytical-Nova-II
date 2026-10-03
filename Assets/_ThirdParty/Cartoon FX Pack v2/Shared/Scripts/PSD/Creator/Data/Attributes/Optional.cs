using UnityEngine;
using System;

namespace PSD.Creator.Data.Attributes
{
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class OptionalAttribute : PropertyAttribute
    {
        public readonly string EnabledProperty;
        public OptionalAttribute(string enabledProperty) => EnabledProperty = enabledProperty;
    }
}