using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace KryticalNova.World
{
    /// <summary>
    /// The one way a map is loaded (owner, 20 Oct 2026: "the newer maps use Addressables"). Each map scene is an
    /// Addressable of its own (a group per map, <c>MapAddressables</c> in the Editor), addressed by its scene name, and
    /// the game loads it under the loading page, the map before it going with it: a map loads alone (Single), and its
    /// bundle is let go as soon as it unloads. A dedicated server keeps its maps in its build list instead (it runs one map
    /// and never changes it), so a scene in the build list is loaded from there. The game joins a map's server only once
    /// the map is loaded, because Netcode's scene syncing is off (one map per server makes it unneeded).
    /// </summary>
    public static class MapScenes
    {
        /// <summary>A map being loaded: its progress, whether it waits to be shown, and showing it.</summary>
        public sealed class Load
        {
            internal AsyncOperation Built;
            internal AsyncOperationHandle<SceneInstance> Addressable;

            public string SceneName { get; internal set; }
            /// <summary>Why it cannot load, or null.</summary>
            public string Error { get; internal set; }

            /// <summary>0 to 1 while the map loads; 1 once it is ready to be shown.</summary>
            public float Progress
            {
                get
                {
                    if (Built != null) return Mathf.Clamp01(Built.progress / 0.9f);
                    return Addressable.IsValid() ? (Addressable.IsDone ? 1f : Mathf.Clamp01(Addressable.PercentComplete)) : 0f;
                }
            }

            /// <summary>Loaded and waiting to be shown (or failed: see <see cref="Error"/>).</summary>
            public bool Ready
            {
                get
                {
                    if (Error != null) return true;
                    if (Built != null) return Built.progress >= 0.9f || Built.isDone;
                    if (!Addressable.IsValid() || !Addressable.IsDone) return false;
                    if (Addressable.Status != AsyncOperationStatus.Succeeded)
                        Error = $"{SceneName} could not be loaded: {Addressable.OperationException?.Message ?? "unknown"}";
                    return true;
                }
            }

            /// <summary>Shows the loaded map; the one before it unloads.</summary>
            public void Show()
            {
                if (Error != null) return;
                if (Built != null) Built.allowSceneActivation = true;
                else if (Addressable.IsValid() && Addressable.Status == AsyncOperationStatus.Succeeded) Addressable.Result.ActivateAsync();
            }
        }

        /// <summary>True when <paramref name="sceneName"/> is a map this game can load (from its build list or as an Addressable).</summary>
        public static bool Exists(string sceneName) =>
            !string.IsNullOrEmpty(sceneName) && (Application.CanStreamedLevelBeLoaded(sceneName) || Locate(sceneName) != null);

        /// <summary>
        /// Starts loading a map, alone. With <paramref name="show"/> false it waits, loaded, for <see cref="Load.Show"/>
        /// (the loading page shows it when its own work is done).
        /// </summary>
        public static Load Begin(string sceneName, bool show = true)
        {
            var load = new Load { SceneName = sceneName };
            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                load.Built = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
                if (load.Built == null) load.Error = $"{sceneName} could not be loaded.";
                else load.Built.allowSceneActivation = show;
                return load;
            }
            IResourceLocation location = Locate(sceneName);
            if (location == null)
            {
                load.Error = $"There is no map called {sceneName} in this game (not in the build list, and not an Addressable map: Krytical Nova ▸ World ▸ Make Maps Addressable).";
                return load;
            }
            Debug.Log($"[Maps] Loading {sceneName} from its Addressable ({location.InternalId}).");
            // The handle lets go of the map's bundle by itself when the map unloads (the next map loads alone).
            load.Addressable = Addressables.LoadSceneAsync(location, LoadSceneMode.Single, SceneReleaseMode.ReleaseSceneWhenSceneUnloaded, show);
            return load;
        }

        // The map's place among the Addressables, or null. The catalog is read once, the first time (a moment, from disk).
        private static IResourceLocation Locate(string sceneName)
        {
            try
            {
                Addressables.InitializeAsync().WaitForCompletion();
                foreach (var locator in Addressables.ResourceLocators)
                    if (locator.Locate(sceneName, typeof(SceneInstance), out var found) && found.Count > 0)
                        return found[0];
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Maps] The Addressables catalog could not be read: {e.Message}");
            }
            return null;
        }
    }
}
