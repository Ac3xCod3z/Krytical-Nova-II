using UnityEngine;
using System;
using PSD.Creator.Inspector.Utility;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Data.Attributes
{
    public abstract class RequiredAttribute : PropertyAttribute
    {
#if UNITY_EDITOR
        public abstract bool Validate(SerializedProperty property, out string message, out MessageType type);
#endif
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class RequiredNotNullAttribute : RequiredAttribute
    {
        public readonly string[] Fields;

        public RequiredNotNullAttribute(params string[] fields) => Fields = fields;

#if UNITY_EDITOR
        public override bool Validate(SerializedProperty property, out string message, out MessageType type)
        {
            foreach (var field in Fields)
            {
                var p = property.FindRelativeProperty(field);

                if (p == null || p.objectReferenceValue == null)
                {
                    message = $"{field} is required.";
                    type = MessageType.Error;
                    return false;
                }
            }

            message = null;
            type = MessageType.None;
            return true;
        }
#endif
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class RequiredValueAttribute : RequiredAttribute
    {
        public enum Operation
        {
            Add,
            Subtract,
            Multiply,
            Divide
        }

        public enum Compare
        {
            Over,
            Under
        }

        public readonly string Text;
        public readonly string LimitField;
        public readonly string[] Fields;
        public readonly Operation Op;
        public readonly Compare Cmp;

        public RequiredValueAttribute(
            string text,
            string limitField,
            Operation op,
            Compare cmp,
            params string[] fields)
        {
            Text = text;
            LimitField = limitField;
            Fields = fields;
            Op = op;
            Cmp = cmp;
        }

#if UNITY_EDITOR
        public override bool Validate(SerializedProperty property, out string message, out MessageType type)
        {
            var limitProp = property.FindRelativeProperty(LimitField);

            if (limitProp == null)
            {
                message = $"Limit field '{LimitField}' not found.";
                type = MessageType.Error;
                return false;
            }

            float limit = GetFloat(limitProp);
            float result = 0f;

            bool first = true;

            foreach (var field in Fields)
            {
                var p = property.FindRelativeProperty(field);
                if (p == null)
                {
                    message = $"Field '{field}' not found.";
                    type = MessageType.Error;
                    return false;
                }

                float v = GetFloat(p);

                if (first)
                {
                    result = v;
                    first = false;
                    continue;
                }

                switch (Op)
                {
                    case Operation.Add:
                        result += v;
                        break;
                    case Operation.Subtract:
                        result -= v;
                        break;
                    case Operation.Multiply:
                        result *= v;
                        break;
                    case Operation.Divide:
                        result = v == 0 ? 0 : result / v;
                        break;
                }
            }

            bool ok = Cmp switch
            {
                Compare.Over => result > limit,
                Compare.Under => result < limit,
                _ => true
            };

            if (!ok)
            {
                message = Text;
                type = MessageType.Error;
                return false;
            }

            message = null;
            type = MessageType.None;
            return true;
        }

        private static float GetFloat(SerializedProperty p)
        {
            return p.propertyType switch
            {
                SerializedPropertyType.Float => p.floatValue,
                SerializedPropertyType.Integer => p.intValue,
                SerializedPropertyType.Vector2 => p.vector2Value.x,
                SerializedPropertyType.Vector3 => p.vector3Value.x,
                SerializedPropertyType.Vector2Int => p.vector2IntValue.x,
                SerializedPropertyType.Vector3Int => p.vector3IntValue.x,
                _ => 0f
            };
        }
#endif
    }
}