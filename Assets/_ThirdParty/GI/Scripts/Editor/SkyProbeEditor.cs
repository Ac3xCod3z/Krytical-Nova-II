using UnityEditor;
using UnityEngine;

namespace GI
{
    [CustomEditor(typeof(SkyProbe))]
    [CanEditMultipleObjects]
    public class SkyProbeEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("Copy From Reflection Probe"))
            {
                foreach (var obj in Selection.gameObjects)
                {
                    var sky = obj.GetComponent<SkyProbe>();

                    if (sky == null)
                        continue;

                    var probe = obj.GetComponent<ReflectionProbe>();

                    Undo.RecordObject(sky, "Copy");
                    sky.Texture = probe.texture;
                }
            }
        }
    }
}