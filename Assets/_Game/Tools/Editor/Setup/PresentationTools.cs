using System;
using System.Reflection;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Reaches the builders of the HUD, menus and game feel (assembly KryticalNova.Editor.Presentation) from the
    /// tools in KryticalNova.Editor, which cannot reference them: that assembly needs the client-only UI and GameFeel
    /// code and is left out while the Dedicated Server target is active. Without it every call here does nothing.
    /// </summary>
    public static class PresentationTools
    {
        public const string AssemblyName = "KryticalNova.Editor.Presentation";

        /// <summary>True while the presentation builders are compiled (not under the Dedicated Server target).</summary>
        public static bool Available => Type("HudSetup") != null;

        /// <summary>Calls a public static method of a presentation builder, e.g. <c>Call("HudSetup", "SetUpOpenScene")</c>.</summary>
        public static object Call(string typeName, string method, params object[] args)
        {
            MethodInfo m = Type(typeName)?.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            if (m == null)
            {
                Debug.LogWarning($"[Setup] {typeName}.{method} is not here: the presentation builders are left out under the Dedicated Server target.");
                return null;
            }
            return m.Invoke(null, args);
        }

        /// <summary>Reads a public static property of a presentation builder.</summary>
        public static object Get(string typeName, string property) =>
            Type(typeName)?.GetProperty(property, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

        private static Type Type(string typeName) =>
            System.Type.GetType("KryticalNova.EditorTools." + typeName + ", " + AssemblyName);
    }
}
