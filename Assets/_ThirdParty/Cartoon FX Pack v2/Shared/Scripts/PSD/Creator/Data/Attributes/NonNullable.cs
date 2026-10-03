using UnityEngine;
using System;
using PSD.Creator.Inspector.Utility;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Data.Attributes
{
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class NonNullableCheckAttribute : PropertyAttribute
    {
        public readonly string[] Fields;
        public NonNullableCheckAttribute(params string[] fields) => Fields = fields;

#if UNITY_EDITOR
        public bool Show(SerializedProperty property)
        {
            foreach (var field in Fields)
            {
                var conditionProperty = property.FindRelativeProperty(field);
                if (conditionProperty != null && conditionProperty.objectReferenceValue == null) return false;
            }
            return true;
        }
#endif
    }
}