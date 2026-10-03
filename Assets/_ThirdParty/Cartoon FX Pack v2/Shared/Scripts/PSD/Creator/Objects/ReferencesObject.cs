using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Objects
{
    [CreateAssetMenu(fileName = "References", menuName = "CTFX2/References", order = 1)]
    public class ReferencesObject : ScriptableObject
    {
        [Serializable]
        public class DefaultSystem
        {
            public ParticleSystem system;
        }

        [Serializable]
        public class System
        {
            public ParticleSystem system;
            public Material material;
        }

        public DefaultSystem defaultSystem;

        [Space, Category("Text")]
        public System text;
        public System background;
        public System halftone;

        [Space, Category("FXs")]
        public System effect;
        public System impact;

        [Space, Category("Particles")]
        public System debris;

        [Space, Category("Smoke")]
        public System smoke;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class CategoryAttribute : PropertyAttribute
    {
        public readonly string Name;
        public CategoryAttribute(string name) => Name = name;
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(CategoryAttribute))]
    public class CategoryDecoratorDrawer : DecoratorDrawer
    {
        CategoryAttribute Attr => (CategoryAttribute)attribute;

        static readonly Color Background = new Color(0.06f, 0.08f, 0.10f, 0.92f);
        static readonly Color Shadow = new Color(0f, 0f, 0f, 0.25f);
        static readonly Color Highlight = new Color(0.2f, 0.9f, 1f, 0.18f);
        static readonly Color Border = new Color(0.2f, 0.9f, 1f, 0.18f);

        const float SHADOW_SIZE = 4;
        const float BORDER_SIZE = 1;
        const float ACCENT_SIZE = 4;

        public override void OnGUI(Rect position)
        {
            float h = GetHeight();
            Rect r = new Rect(position.x, position.y + 3, position.width, h - 6);

            EditorGUI.DrawRect(r, Background);

            float x = r.x + ACCENT_SIZE;

            EditorGUI.DrawRect(new Rect(x, r.y, r.width, BORDER_SIZE), Highlight);
            EditorGUI.DrawRect(new Rect(x, r.yMax - SHADOW_SIZE, r.width, SHADOW_SIZE), Shadow);

            EditorGUI.DrawRect(new Rect(r.xMax - SHADOW_SIZE, r.y, SHADOW_SIZE, r.height), Shadow);

            EditorGUI.DrawRect(new Rect(r.x, r.y, ACCENT_SIZE, r.height), new Color(0.2f, 0.9f, 1f, 0.35f));

            EditorGUI.DrawRect(new Rect(x, r.yMax - BORDER_SIZE, r.width, BORDER_SIZE), Border);

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.85f, 0.95f, 1f, 0.85f) }
            };

            Rect label = new Rect(r.x + 10, r.y, r.width - 10, r.height);
            GUI.Label(label, Attr.Name.ToUpperInvariant(), style);
        }

        public override float GetHeight() => EditorGUIUtility.singleLineHeight + 10;
    }

    [CustomPropertyDrawer(typeof(ReferencesObject.System), true)]
    public class ReferencesSystemDrawer : PropertyDrawer
    {
        readonly float spacing = EditorGUIUtility.standardVerticalSpacing;
        readonly float padding = EditorGUIUtility.standardVerticalSpacing * 2;
        const float ACCENT_WIDTH = 4f;

        static readonly Color headerAccent = new Color(0.2f, 0.8f, 1f, 0.025f);
        static readonly Color headerBorder = new Color(0.2f, 0.8f, 1f, 0.55f);

        static readonly Color rowA = new Color(0, 0, 0, 0.10f);
        static readonly Color rowB = new Color(0, 0, 0, 0.18f);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float line = EditorGUIUtility.singleLineHeight;
            float labelWidth = EditorGUIUtility.labelWidth / 2f;

            Color systemColor = GetColorFromProperty(property.name);

            GUI.Box(position, GUIContent.none, EditorStyles.helpBox);

            Rect accentRect = new Rect(position.x + spacing, position.y + spacing * 2, ACCENT_WIDTH, position.height - spacing * 3);
            EditorGUI.DrawRect(accentRect, systemColor);

            EditorGUI.DrawRect(new Rect(position.x, position.y, position.width, 2), headerBorder);

            Rect headerBg = new Rect(position.x, position.y, position.width, line + padding * 2);
            EditorGUI.DrawRect(headerBg, headerAccent);

            Rect rect = new Rect(
                accentRect.xMax + spacing * 3,
                position.y + padding,
                position.width - padding * 2 - ACCENT_WIDTH,
                line
            );

            string title = ObjectNames.NicifyVariableName(property.name);

            Rect tagRect = new Rect(rect.x - 2, rect.y, EditorStyles.boldLabel.CalcSize(new GUIContent(title)).x + 10, line);
            EditorGUI.DrawRect(tagRect, new Color(1, 1, 1, 0.05f));

            GUI.Label(rect, title, EditorStyles.boldLabel);

            rect.y += line + spacing * 2;

            EditorGUI.indentLevel++;

            var iterator = property.Copy();
            var end = iterator.GetEndProperty();

            bool enterChildren = true;
            int row = 0;

            bool hasMissing = false;

            while (iterator.NextVisible(enterChildren) &&
                   !SerializedProperty.EqualContents(iterator, end))
            {
                if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceValue == null)
                    hasMissing = true;

                float h = EditorGUI.GetPropertyHeight(iterator, true);

                Rect rowRect = new Rect(rect.x, rect.y, rect.width, h);
                EditorGUI.DrawRect(rowRect, row % 2 == 0 ? rowA : rowB);

                Rect labelRect = new Rect(rect.x + spacing * 2, rect.y, labelWidth - 4, line);
                Rect fieldRect = new Rect(rect.x + labelWidth, rect.y, rect.width - labelWidth, h);

                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                EditorGUI.LabelField(labelRect, ObjectNames.NicifyVariableName(iterator.name), EditorStyles.miniBoldLabel);
                GUI.color = prev;

                EditorGUI.PropertyField(fieldRect, iterator, GUIContent.none, true);

                rect.y += h + spacing;
                row++;
                enterChildren = false;
            }

            if (hasMissing)
                EditorGUI.HelpBox(new Rect(rect.x, rect.y, rect.width - padding, line), "Missing required references", MessageType.Warning);

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float height = line + padding * 2 + spacing * 2;

            var iterator = property.Copy();
            var end = iterator.GetEndProperty();

            bool enterChildren = true;

            bool hasMissing = false;

            while (iterator.NextVisible(enterChildren) &&
                   !SerializedProperty.EqualContents(iterator, end))
            {
                if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceValue == null)
                    hasMissing = true;

                height += EditorGUI.GetPropertyHeight(iterator, true) + spacing;
                enterChildren = false;
            }

            if (hasMissing)
                height += EditorGUIUtility.singleLineHeight;

            return height;
        }

        private Color GetColorFromProperty(string propertyName)
        {
            float hash = Mathf.Abs(propertyName.GetHashCode() % 100) / 100f;
            Color c = Color.HSVToRGB(hash, 0.7f, 0.9f);
            c.a = 0.8f;
            return c;
        }
    }

    [CustomPropertyDrawer(typeof(ReferencesObject.DefaultSystem), true)]
    public class ReferencesDefaultSystemDrawer : PropertyDrawer
    {
        static readonly Color Background = new Color(0.05f, 0.06f, 0.08f, 0.92f);
        static readonly Color Shadow = new Color(0f, 0f, 0f, 0.25f);
        static readonly Color Highlight = new Color(0.2f, 0.9f, 1f, 0.18f);

        const float ACCENT = 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float line = EditorGUIUtility.singleLineHeight;

            Rect r = new Rect(position.x, position.y, position.width, position.height);

            EditorGUI.DrawRect(new Rect(r.x + 2, r.y + 2, r.width, r.height), Shadow);

            EditorGUI.DrawRect(r, Background);

            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), Highlight);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), Shadow);

            EditorGUI.DrawRect(new Rect(r.x, r.y, ACCENT, r.height), new Color(0.2f, 0.9f, 1f, 0.35f));

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.85f, 0.95f, 1f, 0.85f) }
            };

            Rect header = new Rect(r.x + ACCENT + 8, r.y + 4, r.width, line);
            GUI.Label(header, label.text.ToUpperInvariant(), titleStyle);

            Rect content = new Rect(
                r.x + ACCENT + 6,
                r.y + line + 8,
                r.width - ACCENT - 10,
                r.height
            );

            var it = property.Copy();
            var end = it.GetEndProperty();

            bool enter = true;

            float y = content.y;

            bool hasMissing = false;

            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null)
                    hasMissing = true;

                float h = EditorGUI.GetPropertyHeight(it, true);

                Rect fieldRect = new Rect(content.x, y, content.width, h);

                EditorGUI.PropertyField(fieldRect, it, GUIContent.none, true);

                y += h + 4;
                enter = false;
            }

            if (hasMissing)
                EditorGUI.HelpBox(new Rect(content.x, y, content.width - EditorGUIUtility.standardVerticalSpacing * 2, line), "Missing required references", MessageType.Warning);

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float h = EditorGUIUtility.singleLineHeight + 16;

            var it = property.Copy();
            var end = it.GetEndProperty();

            bool enter = true;

            bool hasMissing = false;

            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null)
                    hasMissing = true;

                h += EditorGUI.GetPropertyHeight(it, true) + 4;
                enter = false;
            }

            if (hasMissing)
                h += EditorGUIUtility.singleLineHeight;

            return h;
        }
    }
#endif
}
