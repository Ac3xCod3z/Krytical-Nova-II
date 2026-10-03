using UnityEngine;
using System;
using UnityEditor;
using PSD.Creator.Inspector;
using PSD.Creator.Inspector.Utility;

namespace PSD.Creator.Utility.Inspector.Editors
{
    public abstract partial class ModuleDrawer : PropertyDrawer
    {
        protected abstract Layout GetLayout();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var layout = GetLayout();
            if (layout == null) return;

            foreach (var element in layout.Elements)
            {
                switch (element)
                {
                    case HeaderElement h:
                        InspectorUtility.DrawHeader(h.Title, h.Subtitle);
                        break;
                    case SubHeaderElement sh:
                        InspectorUtility.DrawSubHeader(sh.Title);
                        break;
                    case PropertyElement pe:
                        var prop = property.FindPropertyRelative(pe.Property);
                        if (prop == null) break;

                        InspectorUtility.DrawProperty(prop, property, prop.displayName, pe.Override);

                        if (pe.ValidationRule != null)
                        {
                            var (isValid, message, type) = pe.ValidationRule(prop);
                            if (!isValid)
                            {
                                EditorGUILayout.HelpBox(message, type);
                                EditorGUILayout.Space(2);
                            }
                        }
                        break;
                    case BeginBoxElement b:
                        InspectorUtility.BeginBox(b.Label);
                        break;
                    case EndBoxElement:
                        InspectorUtility.EndBox();
                        break;
                    case SeparatorElement:
                        InspectorUtility.Separate();
                        break;
                }
            }
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => 0;
    }

    public abstract partial class ModuleDrawer
    {
        protected abstract class Validation
        {
            public readonly string Message;
            public readonly MessageType Type;
            public Validation(string message, MessageType type) { Message = message; Type = type; }

            protected abstract bool Validate(SerializedProperty property);

            public static implicit operator Func<SerializedProperty, (bool isValid, string message, MessageType type)>(Validation validation)
                => p => (validation.Validate(p), validation.Message, validation.Type);
        }

        protected class NonNullValidation : Validation
        {
            public NonNullValidation(string message, MessageType type = MessageType.Error) : base(message, type) { }
            protected override bool Validate(SerializedProperty property) => property.objectReferenceValue != null;
        }
    }
}