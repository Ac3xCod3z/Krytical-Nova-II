using System;
using System.Collections.Generic;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Bodies;
using Unity.Netcode;
using UnityEngine;
using World = Unity.Entities.World;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// Wires the player into the netcode worlds <see cref="KnBootstrap"/> makes (M3a-2b): on a server the admission (body first, ghost
    /// last), the server planner and the leave handler; on a client the body factory, the clicks and the character to send. Registered
    /// through <see cref="KnWorldHooks"/> after their reset; nothing happens in a probe run (<c>-knprobe</c>) or while no Krytical Nova
    /// world is made, so the Netcode for GameObjects game is untouched. No statics beyond the registration.
    /// </summary>
    public static class PlayerWorldsSetup
    {
        /// <summary>The body prefab when the scene's NetworkManager does not hold it (tests, a scene without one).</summary>
        public static GameObject PrefabOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PrefabOverride = null;

        // After SubsystemRegistration (where KnWorldHooks resets) and before the worlds are made (BeforeSceneLoad bootstrap).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            if (IsProbeRun()) return;
            KnWorldHooks.ServerWorldMade += world => InstallServer(world);
        }

        private static bool IsProbeRun()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (string.Equals(arg, "-knprobe", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// The real Player.prefab: the override, else the player prefab of the scene's NetworkManager (the same one the Netcode for
        /// GameObjects game spawns; <c>ServerRoster</c> reads it the same way). Asked at each admission: the scene may load after the world.
        /// </summary>
        public static GameObject PlayerPrefab()
        {
            if (PrefabOverride != null) return PrefabOverride;
            NetworkManager network = NetworkManager.Singleton;
            return network != null && network.NetworkConfig != null ? network.NetworkConfig.PlayerPrefab : null;
        }

        /// <summary>Server world: planner, admission, spawn points. Returns the admission (tests read its counters).</summary>
        public static ServerBodyAdmission InstallServer(World server, Func<GameObject> prefab = null)
        {
            var planner = new NavMeshPathPlanner();
            PlayerEntitiesSetup.Install(server, planner);
            var rules = new PlayerAdmission { Planner = planner };
            var admission = new ServerBodyAdmission(server, prefab ?? (Func<GameObject>)PlayerPrefab, rules)
            {
                Picker = (IReadOnlyList<Vector3> occupied, out Vector3 position, out Quaternion rotation) =>
                    KryticalNova.Networking.PlayerSpawnService.PickPoint(occupied, planner.BaseOffsetM, out position, out rotation),
            };
            var admissionSystem = server.GetExistingSystemManaged<KnAdmissionSystem>();
            if (admissionSystem != null) admissionSystem.Admission = admission;
            else Debug.LogError("[KnServer] the server world has no KnAdmissionSystem: nobody can enter.");
            // The scene's avatar must not hold an account on a server (ServerSave.TryBind would hand it over).
            KryticalNova.Networking.OfflinePlayerHandoff.ReleaseOfflineAvatar();
            return admission;
        }
    }
}
