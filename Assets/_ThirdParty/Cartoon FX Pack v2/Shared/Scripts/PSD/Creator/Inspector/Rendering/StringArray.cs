using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Rendering
{
#if UNITY_EDITOR
    internal class StringArrayRenderer : FieldRenderer
    {
        public override bool Handles(SerializedProperty p) => p.propertyType == SerializedPropertyType.Generic && p.isArray && p.arrayElementType == "string";

        public override void Render(SerializedProperty p, SerializedProperty owner, GUIContent label, bool enabled)
        {
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < p.arraySize; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var element = p.GetArrayElementAtIndex(i);
                element.stringValue = EditorGUILayout.DelayedTextField(element.stringValue);

                if (GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Minus"), EditorStyles.miniButton, GUILayout.Width(24)))
                {
                    p.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Plus"), EditorStyles.miniButton, GUILayout.Width(24)))
            {
                int idx = p.arraySize;
                p.InsertArrayElementAtIndex(idx);
                p.GetArrayElementAtIndex(idx).stringValue = "";
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
        }
    }
#endif
}