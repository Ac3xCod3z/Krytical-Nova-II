using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Keeps Synty's package helper quiet in a Multiplayer Play Mode clone. The helper decides a package is missing
    /// by reading Packages/manifest.json from the folder the Editor runs in. A clone runs from Library/VP/&lt;id&gt;, which
    /// has no manifest, so it asks to install com.unity.shadergraph (already in the project) once per Synty pack, on
    /// every project change, and the install fails because a clone has no Package Manager. The dialog also blocks
    /// every command sent to the clone. In a clone this unhooks the helper's check; the main Editor is left alone.
    /// The helper is found by name, so nothing breaks when Synty renames or removes it, and Synty's files are not
    /// edited, so an update of theirs does not undo this.
    /// </summary>
    [InitializeOnLoad]
    internal static class SyntyPackageHelperCloneGuard
    {
        private const string CloneFolder = "/Library/VP/";
        private const string HelperTypeName = "SyntyPackageHelper";
        private const string HandlerName = "OnProjectChanged";

        static SyntyPackageHelperCloneGuard()
        {
            if (Application.dataPath.Replace('\\', '/').IndexOf(CloneFolder, StringComparison.OrdinalIgnoreCase) < 0) return;
            Unhook();                              // the helper may have subscribed already
            EditorApplication.delayCall += Unhook; // or may subscribe after this runs
        }

        private static void Unhook()
        {
            foreach (Type type in TypeCache.GetTypesWithAttribute<InitializeOnLoadAttribute>())
            {
                if (type.Name != HelperTypeName) continue;
                MethodInfo handler = type.GetMethod(HandlerName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (handler == null) continue;
                // Removing a handler that is not subscribed does nothing, so this is safe to run twice.
                EditorApplication.projectChanged -= (Action)Delegate.CreateDelegate(typeof(Action), handler);
            }
        }
    }
}
