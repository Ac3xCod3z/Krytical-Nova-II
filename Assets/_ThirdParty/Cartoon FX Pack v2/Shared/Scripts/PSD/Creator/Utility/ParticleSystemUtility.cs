using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Utility
{
    public static class ParticleSystemUtils
    {
#if UNITY_EDITOR
        public static void CopySystem(this ParticleSystem to, ParticleSystem from)
        {
            Copy(from, to);
            var fromRenderer = from.GetComponent<ParticleSystemRenderer>();
            var toRenderer = to.GetComponent<ParticleSystemRenderer>();
            Copy(fromRenderer, toRenderer);
        }

        private static void Copy<T>(T from, T to) where T : Object
        {
            SerializedObject soFrom = new SerializedObject(from);
            SerializedObject soTo = new SerializedObject(to);

            soFrom.Update();
            soTo.Update();

            EditorUtility.CopySerializedIfDifferent(from, to);

            soTo.ApplyModifiedPropertiesWithoutUndo();
        }
#endif
    }
}