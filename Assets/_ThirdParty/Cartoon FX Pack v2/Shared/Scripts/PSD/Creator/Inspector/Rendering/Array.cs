using UnityEngine;
using System.Collections.Generic;
using PSD.Creator.Inspector.Utility;
using PSD.Preferences.Theming;
using System;
using System.Runtime.Serialization;
using System.Reflection;
using System.Linq;






#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Rendering
{
#if UNITY_EDITOR
    internal class ArrayRenderer : FieldRenderer
    {
        private static readonly Dictionary<string, bool> Foldouts = new();

        public override bool Handles(SerializedProperty p) => p.propertyType == SerializedPropertyType.Generic && p.isArray;

        public override void Render(SerializedProperty p, SerializedProperty owner, GUIContent label, bool enabled)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel++;

            string elementLabel = label.text.EndsWith("s") ? label.text[..^1] : label.text;

            for (int i = 0; i < p.arraySize; i++)
            {
                var element = p.GetArrayElementAtIndex(i);
                string key = $"{p.propertyPath}[{i}]";
                if (!Foldouts.ContainsKey(key)) Foldouts[key] = true;
                bool expanded = Foldouts[key];

                EditorGUILayout.Space(3);
                bool clickedButton = DrawElementHeader(p, i, elementLabel, expanded, out bool newExpanded);
                if (clickedButton) break;
                Foldouts[key] = newExpanded;

                if (newExpanded)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        InspectorUtility.DrawProperty(element, owner, element.displayName);
                }
            }

            GUI.enabled = true;
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Plus"), EditorStyles.miniButton, GUILayout.Width(24)))
            {
                int idx = p.arraySize;
                p.InsertArrayElementAtIndex(idx);
                var newEl = p.GetArrayElementAtIndex(idx);
                if (newEl.propertyType == SerializedPropertyType.String)
                    newEl.stringValue = "";

                ApplyDefaults(newEl);

                Foldouts[$"{p.propertyPath}[{idx}]"] = true;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
            EditorGUILayout.BeginHorizontal();
        }

        private static void ApplyDefaults(SerializedProperty element)
        {
            Type elementType = GetTypeFromSerializedProperty(element);
            if (elementType == null) return;

            object defaultInstance = CreateDefaultInstance(elementType);
            if (defaultInstance == null) return;

            var child = element.Copy();
            var end = element.GetEndProperty();
            if (!child.NextVisible(true)) return;

            while (!SerializedProperty.EqualContents(child, end))
            {
                FieldInfo field = GetFieldInHierarchy(elementType, child.name);
                if (field != null)
                {
                    object defaultValue = field.GetValue(defaultInstance);
                    ApplyValue(child, defaultValue);
                }
                child.NextVisible(false);
            }
        }

        private static void ApplyValue(SerializedProperty prop, object value)
        {
            if (value == null) return;
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Float when value is float f: prop.floatValue = f; break;
                case SerializedPropertyType.Integer when value is int i: prop.intValue = i; break;
                case SerializedPropertyType.Boolean when value is bool b: prop.boolValue = b; break;
                case SerializedPropertyType.String when value is string s: prop.stringValue = s; break;
                case SerializedPropertyType.Color when value is Color c: prop.colorValue = c; break;
                case SerializedPropertyType.Vector2 when value is Vector2 v2: prop.vector2Value = v2; break;
                case SerializedPropertyType.Vector3 when value is Vector3 v3: prop.vector3Value = v3; break;
                case SerializedPropertyType.Vector2Int when value is Vector2Int v2i: prop.vector2IntValue = v2i; break;
                case SerializedPropertyType.Enum when value is Enum e: prop.enumValueIndex = Convert.ToInt32(e); break;
            }
        }

        private static Type GetTypeFromSerializedProperty(SerializedProperty prop)
        {
            string typeName = prop.type;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = assembly.GetTypes().FirstOrDefault(x =>
                    x.FullName == typeName || x.Name == typeName);
                if (t != null) return t;
            }
            return null;
        }

        private static object CreateDefaultInstance(Type type)
        {
            try { return Activator.CreateInstance(type); }
            catch { return FormatterServices.GetUninitializedObject(type); }
        }

        private static FieldInfo GetFieldInHierarchy(Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
                type = type.BaseType;
            }
            return null;
        }

        private static bool DrawElementHeader(SerializedProperty array, int i, string elementLabel, bool expanded, out bool newExpanded)
        {
            newExpanded = expanded;

            Rect headerRect = EditorGUILayout.GetControlRect(false, 24);

            int prevIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            EditorGUI.DrawRect(headerRect, Theme.ContentBackground);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 3, headerRect.height), expanded ? Theme.Accent : Theme.AccentSubtle);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 1, headerRect.width, 1), Theme.AccentBorderSubtle);

            var element = array.GetArrayElementAtIndex(i);
            var enabledProp = element.FindPropertyRelative("enabled");

            float x = headerRect.x + 8;

            if (enabledProp != null)
            {
                Rect toggleRect = new Rect(x, headerRect.y + (headerRect.height - 16) / 2f, 16, 16);
                enabledProp.boolValue = EditorGUI.Toggle(toggleRect, enabledProp.boolValue);
                x += 20;
            }

            float btnY = headerRect.y + (headerRect.height - 18) / 2f;
            float btnRight = headerRect.xMax - 4;
            Rect minusRect = new Rect(btnRight - 24, btnY, 24, 18);
            Rect downRect = new Rect(minusRect.x - 24, btnY, 24, 18);
            Rect upRect = new Rect(downRect.x - 24, btnY, 24, 18);

            var foldoutStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = enabledProp != null && !enabledProp.boolValue ? Theme.SubtleText : Theme.ContentText }
            };
            EditorGUI.LabelField(new Rect(x, headerRect.y, upRect.x - x, headerRect.height), $"{(expanded ? "▾" : "▸")}  {elementLabel} {i + 1}", foldoutStyle);

            bool prevEnabled = GUI.enabled;

            GUI.enabled = true;
            if (GUI.Button(minusRect, EditorGUIUtility.IconContent("Toolbar Minus"), EditorStyles.miniButtonRight))
            {
                array.DeleteArrayElementAtIndex(i);
                GUI.enabled = prevEnabled;
                EditorGUI.indentLevel = prevIndent;
                return true;
            }

            GUI.enabled = i < array.arraySize - 1;
            if (GUI.Button(downRect, "▼", EditorStyles.miniButtonMid))
            {
                array.MoveArrayElement(i, i + 1);
                GUI.enabled = prevEnabled;
                EditorGUI.indentLevel = prevIndent;
                return true;
            }

            GUI.enabled = i > 0;
            if (GUI.Button(upRect, "▲", EditorStyles.miniButtonLeft))
            {
                array.MoveArrayElement(i, i - 1);
                GUI.enabled = prevEnabled;
                EditorGUI.indentLevel = prevIndent;
                return true;
            }

            GUI.enabled = prevEnabled;

            x -= 3;
            Rect clickZone = new Rect(x, headerRect.y, upRect.x - x, headerRect.height);
            if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
            {
                if (clickZone.Contains(Event.current.mousePosition))
                {
                    newExpanded = !expanded;
                    Event.current.Use();
                }
            }

            EditorGUI.indentLevel = prevIndent;

            if (!enabledProp?.boolValue ?? false) newExpanded = false;

            return false;
        }
    }
#endif
}