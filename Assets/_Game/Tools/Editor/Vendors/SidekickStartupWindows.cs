using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;

namespace KryticalNova.EditorTools.Vendors
{
    /// <summary>
    /// Synty's Sidekick package opens its Character Tool once per Editor session, and creates its Tool Downloader
    /// window on every script reload to check for updates (popping it up when there is one). Neither is wanted
    /// here, and in a Multiplayer Play Mode virtual player the blocked windows log an error on every reload.
    /// This takes the package's two startup callbacks off the Editor's update loop before they run. The vendor
    /// code is untouched, and both windows still open from the Synty menu.
    /// </summary>
    [InitializeOnLoad]
    internal static class SidekickStartupWindows
    {
        static SidekickStartupWindows()
        {
            Cancel("Synty.SidekickCharacters.UI.MenuBootstrapController", "OpenWindowOnStartup");
            Cancel("Synty.SidekickCharacters.DownloaderBackgroundService", "CreateToolInstance");
        }

        private static void Cancel(string typeName, string methodName)
        {
            Type type = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, false);
                if (type != null) break;
            }

            MethodInfo method = type?.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null) return; // the package is gone or has changed: nothing to cancel

            // Its static constructor is what queues the callback; make sure that has happened before removing it.
            RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            EditorApplication.update -= (EditorApplication.CallbackFunction)Delegate.CreateDelegate(
                typeof(EditorApplication.CallbackFunction), method);
        }
    }
}
