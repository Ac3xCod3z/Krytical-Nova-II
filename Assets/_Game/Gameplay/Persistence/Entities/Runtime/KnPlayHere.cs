#if UNITY_EDITOR
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using KryticalNova.Player;
using KryticalNova.Player.Entities;
using UnityEditor;
using UnityEngine;
using World = Unity.Entities.World;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// "Play DOTS here" (M3a owner feel checkpoint, plan section 50): Play in this Editor runs an entity-netcode server world and one
    /// client world, the owner walks and runs in the open map with real clicks. Safe by construction:
    /// - saves go to memory only for the whole session (<see cref="MemorySaveStore"/>, set before the scene loads): nothing reaches the
    ///   save database, the save service or the local profile on disk;
    /// - the server listens on loopback only, on the test lane's <see cref="Port"/>; a real-server port is refused;
    /// - the player enters unsaved, as "<see cref="TesterName"/>" in the first lineage's default look (no account, no character id).
    /// Editor only; nothing of it is in a build.
    /// </summary>
    public static class KnPlayHere
    {
        public const ushort Port = 7797;
        public const string TesterName = "Tester";
        public const string MenuPath = "Krytical Nova/Online/Entities/Play DOTS here";

        // Set by the menu, read on entering Play. Not reset at SubsystemRegistration on purpose: it must outlive the Play start.
        static bool s_Requested;

        /// <summary>True while this Play session is a "Play DOTS here" one.</summary>
        public static bool Active { get; private set; }

        [MenuItem(MenuPath)]
        static void PlayHere()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[KnPlayHere] Stop Play first.");
                return;
            }
            s_Requested = true;
            EditorApplication.isPlaying = true;
        }

        [MenuItem(MenuPath, true)]
        static bool PlayHereValid() => !EditorApplication.isPlayingOrWillChangePlaymode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeScene()
        {
            Active = s_Requested;
            s_Requested = false;
            if (!Active) return;
            // before any scene object loads or saves (ServerSave resets its store at SubsystemRegistration, which ran already)
            ServerSave.Store = new MemorySaveStore();
            Debug.Log("[KnPlayHere] Saves go to memory only this session: nothing is written to disk.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterScene()
        {
            if (!Active) return;
            if (!TryTesterLook(out string recipeText, out string problem))
            {
                Debug.LogError("[KnPlayHere] Not started: " + problem);
                return;
            }
            KnGoInGame.Source = new TesterGoInGame(new GoInGamePayload { CharacterId = string.Empty, Name = TesterName, RecipeText = recipeText });
            string join = JoinRules.PayloadText(GameBuild.Version, true);
            if (KnBootstrap.StartPlayHere(Port, join))
                Debug.Log($"[KnPlayHere] Entity-netcode server and client started on loopback {Port}. Click the ground to walk.");
        }

        /// <summary>The database's first lineage in its default look (the same way the test-lane bots enter).</summary>
        public static bool TryTesterLook(out string recipeText, out string problem)
        {
            recipeText = null;
            problem = null;
            GameObject prefab = PlayerWorldsSetup.PlayerPrefab();
            PlayerIdentity identity = prefab != null ? prefab.GetComponent<PlayerIdentity>() : null;
            CharacterCreationDatabase database = identity != null ? identity.Database : null;
            if (database == null || database.Lineages.Count == 0) { problem = "no player prefab with a character database (Build Player Ghost Assets)."; return false; }
            recipeText = RecipeText.Encode(RecipeEdit.Default(database.Lineages[0], default(BodyType)));
            return true;
        }

        sealed class TesterGoInGame : IKnGoInGameSource
        {
            readonly GoInGamePayload m_Payload;
            public TesterGoInGame(GoInGamePayload payload) => m_Payload = payload;
            public bool TryGet(World clientWorld, out GoInGamePayload payload) { payload = m_Payload; return true; }
        }
    }
}
#endif
