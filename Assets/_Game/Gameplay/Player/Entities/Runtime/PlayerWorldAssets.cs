using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Scenes;
using UnityEngine;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// What a netcode world needs to let players in, found the same way in every scene (M3a-2b R8): the subscene that carries the
    /// player ghost prefab (loaded into each server and client world the moment the world is made, so a client and the server
    /// always know the same ghost) and the player body prefab (Player.prefab), which a dedicated server of the entity netcode has
    /// no NetworkManager to hand it. One asset at <c>Resources/PlayerWorldAssets</c>, written by <c>PlayerGhostAssetsBuilder</c>.
    /// </summary>
    public sealed class PlayerWorldAssets : ScriptableObject
    {
        public const string ResourcePath = "PlayerWorldAssets";

        [Tooltip("The subscene that holds the player ghost prefab (PlayerGhostPrefabAuthoring).")]
        public EntitySceneReference ghostScene;

        [Tooltip("The player body built around each ghost (Assets/_Game/Gameplay/Player/Prefabs/Player.prefab).")]
        public GameObject playerPrefab;

        static PlayerWorldAssets s_Loaded;
        static bool s_Looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Loaded = null;
            s_Looked = false;
        }

        /// <summary>The asset, or null when the build has none (one error is logged).</summary>
        public static PlayerWorldAssets Instance
        {
            get
            {
                if (!s_Looked)
                {
                    s_Looked = true;
                    s_Loaded = Resources.Load<PlayerWorldAssets>(ResourcePath);
                    if (s_Loaded == null)
                        Debug.LogError("[Kn] No Resources/" + ResourcePath + " asset: players cannot enter (run Build Player Ghost Assets).");
                }
                return s_Loaded;
            }
        }

        /// <summary>
        /// Starts loading the ghost subscene into <paramref name="world"/> (it streams in during the world's next update). False when
        /// there is no asset or no valid scene reference; the admission then refuses everyone with "not ready".
        /// </summary>
        public static bool LoadGhostScene(World world)
        {
            if (world == null || !world.IsCreated) return false;
            PlayerWorldAssets assets = Instance;
            if (assets == null) return false;
            if (!assets.ghostScene.IsReferenceValid)
            {
                Debug.LogError("[Kn] PlayerWorldAssets has no ghost subscene: players cannot enter (run Build Player Ghost Assets).");
                return false;
            }
            SceneSystem.LoadSceneAsync(world.Unmanaged, assets.ghostScene,
                new SceneSystem.LoadParameters { Flags = SceneLoadFlags.BlockOnStreamIn });
            return true;
        }
    }
}
