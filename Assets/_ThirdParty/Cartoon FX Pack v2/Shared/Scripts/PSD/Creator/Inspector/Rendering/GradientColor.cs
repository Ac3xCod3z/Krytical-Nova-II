using UnityEngine;
using PSD.Creator.Inspector.Utility;
using PSD.Creator.Data.Attributes;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Rendering
{
#if UNITY_EDITOR
    internal class GradientColorRenderer : FieldRenderer
    {
        private const string PathColor = "PSD/Editor/Icons/Color Icon";
        private const string PathGradient = "PSD/Editor/Icons/Gradient Icon";
        private const string PathCopy = "PSD/Editor/Icons/Copy Icon";
        private const string PathPaste = "PSD/Editor/Icons/Paste Icon";

        public override bool Handles(SerializedProperty p) => p.propertyType == SerializedPropertyType.Gradient && p.GetAttribute(out ColorModeAttribute _);

        public override void Render(SerializedProperty p, SerializedProperty owner, GUIContent label, bool enabled)
        {
            p.GetAttribute(out ColorModeAttribute attr);
            var modeProp = p.FindRelativeProperty(attr.ModeProperty);
            var colorProp = p.FindRelativeProperty(attr.ColorProperty);
            bool isGradient = modeProp == null || modeProp.boolValue;

            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel, GUILayout.Width(LabelWidth));

            if (isGradient)
            {
                EditorGUILayout.PropertyField(p, GUIContent.none);

                if (GUILayout.Button(IconCache.Content(PathCopy, "Copy"), EditorStyles.miniButtonLeft, GUILayout.Width(22), GUILayout.Height(18)))
                    GradientClipboard.Copy(p.gradientValue);
                if (GUI.enabled) EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);

                bool canPaste = GradientClipboard.CanPaste();
                GUI.enabled = canPaste && enabled;
                if (GUILayout.Button(IconCache.Content(PathPaste, "Paste"), EditorStyles.miniButtonMid, GUILayout.Width(22), GUILayout.Height(18)))
                {
                    if (GradientClipboard.TryPaste(out Gradient g))
                    {
                        p.gradientValue = g;
                        Apply(p, false);
                    }
                }
                if (GUI.enabled) EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
                GUI.enabled = enabled;
            }
            else if (colorProp != null)
            {
                EditorGUI.BeginChangeCheck();
                Color newColor = EditorGUILayout.ColorField(GUIContent.none, colorProp.colorValue, true, true, true);
                if (EditorGUI.EndChangeCheck())
                {
                    colorProp.colorValue = newColor;
                    Apply(p, false);
                }
            }

            if (modeProp != null)
            {
                if (GUILayout.Button(IconCache.Content(isGradient ? PathGradient : PathColor), EditorStyles.miniButtonRight, GUILayout.Width(22), GUILayout.Height(18)))
                {
                    modeProp.boolValue = !modeProp.boolValue;
                    Apply(p, false);
                }
                if (GUI.enabled) EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            }
        }
    }
#endif
}