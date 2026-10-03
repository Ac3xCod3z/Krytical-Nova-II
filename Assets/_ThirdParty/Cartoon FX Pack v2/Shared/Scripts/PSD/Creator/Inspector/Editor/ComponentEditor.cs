using PSD.Creator.Tool;
using UnityEditor;
using UnityEngine;

namespace PSD.Creator.Editor.Inspector.Editors
{
    [CustomEditor(typeof(Component<,,>/*thats cool*/), true)]
    [CanEditMultipleObjects]
    public class ComponentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("The Particle System is controlled by a Cartoon FX component. Any changes made here will be overridden by the component's settings.", MessageType.Warning);
            GUI.enabled = false;
            base.OnInspectorGUI();
            GUI.enabled = true;
        }
    }
}