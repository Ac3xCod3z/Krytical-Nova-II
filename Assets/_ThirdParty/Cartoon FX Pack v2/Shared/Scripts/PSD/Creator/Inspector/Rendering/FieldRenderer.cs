using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Rendering
{
#if UNITY_EDITOR
    public abstract class FieldRenderer
    {
        protected static float LabelWidth => EditorGUIUtility.labelWidth / 2;

        public abstract bool Handles(SerializedProperty property);

        public abstract void Render(SerializedProperty property, SerializedProperty owner, GUIContent label, bool enabled);

        protected void Apply(SerializedProperty p, bool update = true)
        {
            p.serializedObject.ApplyModifiedProperties();
            if (update) p.serializedObject.Update();
        }
    }

    public static partial class FieldRendererRegistry
    {
        public static FieldRenderer For(SerializedProperty property)
        {
            foreach (var r in Renderers)
                if (r.Handles(property)) return r;
            return new DefaultRenderer();
        }
    }
#endif
}