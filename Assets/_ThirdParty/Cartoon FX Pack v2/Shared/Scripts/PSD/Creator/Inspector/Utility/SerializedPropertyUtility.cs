using System;
using System.Linq;
using System.Reflection;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Utility
{
    public static class SerializedPropertyExtensions
    {
#if UNITY_EDITOR
        public static bool GetAttribute<T>(this SerializedProperty property, out T attribute) where T : Attribute
        {
            var field = property.GetFieldInfo();
            attribute = field?.GetCustomAttribute<T>();
            return attribute != null;
        }
        public static bool GetAttributes<T>(this SerializedProperty property, out T[] attributes) where T : Attribute
        {
            var field = property.GetFieldInfo();
            attributes = field?.GetCustomAttributes<T>().ToArray();
            return attributes?.Length > 0;
        }

        public static SerializedProperty FindRelativeProperty(this SerializedProperty property, string name)
        {
            if (property == null) return null;

            string path = property.propertyPath;
            while (true)
            {
                int lastDot = path.LastIndexOf('.');
                string candidate = lastDot >= 0 ? path[..(lastDot + 1)] + name : name;

                var found = property.serializedObject.FindProperty(candidate);
                if (found != null) return found;

                if (lastDot < 0) break;
                path = path[..lastDot];
            }
            return null;
        }

        public static FieldInfo GetFieldInfo(this SerializedProperty property)
        {
            if (property == null) return null;

            Type type = property.serializedObject.targetObject.GetType();
            FieldInfo field = null;

            foreach (string part in property.propertyPath.Split('.'))
            {
                if (part == "Array") continue;
                if (part.StartsWith("data["))
                {
                    type = type.GetElementType() ?? type.GetGenericArguments()[0];
                    continue;
                }

                field = type.GetField(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) return null;
                type = field.FieldType;
            }

            return field;
        }
#endif
    }
}