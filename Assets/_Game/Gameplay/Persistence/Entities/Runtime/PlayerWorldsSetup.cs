using System;
using System.Collections.Generic;
using KryticalNova.Core;
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

        /// <summary>
        /// The camera takes the local body in the frame it is made (on in the game). A switch only so a proof run can measure one camera
        /// change at a time (Play DOTS here, 31 Oct).
        /// </summary>
        public static bool TakeCameraWhenBodyMade = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PrefabOverride = null;
            TakeCameraWhenBodyMade = true;
        }

        // After SubsystemRegistration (where KnWorldHooks resets) and before the worlds are made (BeforeSceneLoad bootstrap).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            if (IsProbeRun()) return;
            KnWorldHooks.ServerWorldMade += world => InstallServer(world);
            KnWorldHooks.ClientWorldMade += world => InstallClient(world);
            KnGoInGame.Source = new CharacterSessionGoInGame();
        }

        private static bool IsProbeRun()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (string.Equals(arg, "-knprobe", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// The real Player.prefab: the override, else the one in <see cref="PlayerWorldAssets"/> (a dedicated server of the entity netcode
        /// has no NetworkManager), else the player prefab of the scene's NetworkManager (the same one the Netcode for GameObjects game
        /// spawns; <c>ServerRoster</c> reads it the same way). Asked at each admission: the scene may load after the world.
        /// </summary>
        public static GameObject PlayerPrefab()
        {
            if (PrefabOverride != null) return PrefabOverride;
            PlayerWorldAssets assets = PlayerWorldAssets.Instance;
            if (assets != null && assets.playerPrefab != null) return assets.playerPrefab;
            NetworkManager network = NetworkManager.Singleton;
            return network != null && network.NetworkConfig != null ? network.NetworkConfig.PlayerPrefab : null;
        }

        /// <summary>
        /// Server world: the ghost subscene (R8), planner, admission, spawn points. Returns the admission (tests read its counters).
        /// <paramref name="loadGhostScene"/> false = the caller loads a ghost subscene of its own (a test world).
        /// </summary>
        public static ServerBodyAdmission InstallServer(World server, Func<GameObject> prefab = null, bool loadGhostScene = true)
        {
            if (loadGhostScene) PlayerWorldAssets.LoadGhostScene(server);
#if KN_TEST_LANE && (KN_DEV_BOTS || UNITY_EDITOR)
            TestLaneBotCharacters.IsBot = KnDevBots.IsBotAccount; // the 2-client gate's bots get characters (plan 43.1)
#endif
#if KN_TEST_LANE
            PlayerClickLog.Sink = KnTrace.Write; // the click-start pairs of the 2-client gate (plan 43)
#endif
            // The roster checks new looks against the player's character rules; without a NetworkManager it finds them here.
            if (KryticalNova.Persistence.ServerRoster.Database == null)
            {
                GameObject player = PlayerPrefab();
                var identity = player != null ? player.GetComponent<KryticalNova.Player.PlayerIdentity>() : null;
                if (identity != null) KryticalNova.Persistence.ServerRoster.Database = identity.Database;
            }
            var planner = new NavMeshPathPlanner();
            // from the prefab now: the first spawn spot is picked before any body exists (owner's feel test 30 Oct: with base
            // offset 0 the first player stood half in the ground until it walked)
            ConfigurePlanner(planner, (prefab ?? PlayerPrefab)());
            // what a server body says to its own player goes to that player's connection (M7/M8 system 0)
            KryticalNova.Core.OwnerMessages.Current = new EntityOwnerMessages();
            KryticalNova.Items.ChatOwnerLink.Current = new EntityChatOwnerLink();   // chat lines, announcements, System notices (M8 8.4.1)
            KryticalNova.Items.ContactsOwnerLink.Current = new EntityContactsOwnerLink();   // the Contacts window and its prompts (M8 8.4.2)
            KryticalNova.Items.PartyOwnerLink.Current = new EntityPartyOwnerLink();   // the Squad roster and invitations (M8 8.4.3)
            KryticalNova.Items.DuelOwnerLink.Current = new EntityDuelOwnerLink();     // the Callout's prompt, countdown and result (M4 step 8)
            KryticalNova.Items.FollowOwnerLink.Current = new EntityFollowOwnerLink();   // whom the owner follows (M8 8.4.3b)
            KryticalNova.Items.SponsorOwnerLink.Current = new EntitySponsorOwnerLink();   // the Sponsor window and its prompt (M8 8.4.4)
            KryticalNova.Items.CrewOwnerLink.Current = new EntityCrewOwnerLink();   // the Crew window and its invitation (M8 8.4.5)
            KryticalNova.Items.PetShowLink.Current = new EntityPetShowLink();   // pet tricks and evolutions to the other machines (M8 8.4.7)
            KryticalNova.Core.MountOwnerLink.Current = new EntityMountOwnerLink();   // the Mount window's boxes and evolutions (M8 8.4.8)
            KryticalNova.Items.RecallShowLink.Current = new EntityRecallShowLink();   // the Recall aura and arrival to every machine (M8 8.4.10)
            KryticalNova.Items.TradeOwnerLink.Current = new EntityTradeOwnerLink();   // the trade view, notice and prompt (M8 8.2.3)
            KryticalNova.Items.KioskOwnerLink.Current = new EntityKioskOwnerLink();   // the owner's own Kiosk (M7.6)
            KryticalNova.Items.WorkshopOwnerLink.Current = new EntityWorkshopOwnerLink();   // Forge and Fabricator results (M8 8.3.6-7)
            KryticalNova.Progression.NovaLink.Current = new EntityNovaLink();   // the Herald's results and moments (M8 8.3.5)
            KryticalNova.CharacterCreation.RestyleOwnerLink.Current = new EntityRestyleOwnerLink();   // a finished restyle (M8 8.3.8)
#if UNITY_SERVER || UNITY_EDITOR
            KnPageRules.Commands = new KnPageGmTools();   // the page's command box and GM list (M7.4)
            KnServerStatus.NameOf = KnPageServerDesk.CharacterOf;   // the page's player list: name, level and faction of the server body (8 Nov 2026)
            KnServerStatus.FactsOf = KnPageServerDesk.FactsOf;
            KnPageDesk.Desk = new KnPageServerDesk();     // the rest of NGO's control page: save, announce, bans, log, flags (8 Nov 2026)
            KnPageBrowser.Host = new KnPageBrowserHost(); // the browser page and its password login (8 Nov 2026)
#endif
#if UNITY_SERVER
            KnServerRoutine.Begin();                      // reach notes and timed backups, as NGO's ServerLifetime (8 Nov 2026)
#endif
            var serverVaults = new BodyVaultPlanner(server);
            PlayerEntitiesSetup.Install(server, planner, runCost: new BodyRunCost(server), clickBudget: new BudgetClickLimit(server),
                jumpPlanner: new BodyJumpPlanner(server), jumpCost: new BodyJumpCost(server), vaultPlanner: serverVaults,
                waterPlanner: new BodyWaterPlanner(server, planner, vaults: serverVaults), waterFlux: new BodyWaterFlux(server, (prefab ?? PlayerPrefab)()),
                wallPlanner: new BodyWallPlanner(server), wallGrip: new BodyWallGrip(server, (prefab ?? PlayerPrefab)()),
                clickTaken: new BodyClickTaken(server));
            // walk numbers from the settings asset and the jump's curves from its clip, the same on server and client (prediction must agree)
            PlayerMoveFromSkills.InstallSettings(server, PlayerMoveFromSkills.SettingsOf((prefab ?? PlayerPrefab)()));
            PlayerJumpFromSkills.InstallCurves(server, (prefab ?? PlayerPrefab)());
            PlayerJumpFromSkills.InstallKnockCurves(server, (prefab ?? PlayerPrefab)());   // the death knock's travel (M4 6b-2)
            var rules = new PlayerAdmission
            {
                Planner = planner,
                // Entry and every later change of a player's look: observers fetch the text by its new version (step 8).
                LookChanged = (body, ghost, text) =>
                {
                    if (!server.IsCreated) return;
                    server.GetExistingSystemManaged<PlayerLookServerSystem>()?.SetLook(ghost, text);
                },
            };
            var admission = new ServerBodyAdmission(server, prefab ?? (Func<GameObject>)PlayerPrefab, rules)
            {
                Picker = (IReadOnlyList<Vector3> occupied, out Vector3 position, out Quaternion rotation) =>
                    KryticalNova.Networking.PlayerSpawnService.PickPoint(occupied, planner.BaseOffsetM, out position, out rotation),
                // the speeds of the loaded character (Run rank, mount, gear, GM); PlayerMoveStatsRefreshSystem keeps them current
                MoveStatsOf = body =>
                {
                    // the server's body: trees near it are climbable, placed trees marked (NGO's tree proxies follow its players; M3c part 3)
                    if (body != null && body.GetComponent<KryticalNova.Player.TreeProxyAnchor>() == null) body.AddComponent<KryticalNova.Player.TreeProxyAnchor>();
                    PlayerMoveFromSkills.InstallSettings(server, PlayerMoveFromSkills.SettingsOf(body));
                    return PlayerMoveFromSkills.StatsOf(body);
                },
            };
            var admissionSystem = server.GetExistingSystemManaged<KnAdmissionSystem>();
            if (admissionSystem != null) admissionSystem.Admission = admission;
            else Debug.LogError("[KnServer] the server world has no KnAdmissionSystem: nobody can enter.");
            InstallLeave(server);
            // The scene's avatar must not hold an account on a server (ServerSave.TryBind would hand it over).
            KryticalNova.Networking.OfflinePlayerHandoff.ReleaseOfflineAvatar();
            return admission;
        }

        /// <summary>
        /// Client world: the body factory (real Player.prefab as ClientView), the clicks of the local body's view mode, and the client's
        /// planner (configured from the local body's NavMeshAgent when it is made). The ghost subscene is loaded first: a client that
        /// goes in game without the server's ghost prefab is disconnected by netcode. Returns the factory (tests read its counters).
        /// </summary>
        public static ClientBodyFactory InstallClient(World client, Func<GameObject> prefab = null, bool loadGhostScene = true)
        {
            if (loadGhostScene) PlayerWorldAssets.LoadGhostScene(client);
            var planner = new NavMeshPathPlanner();
            BodyClicks.Clear();
            var factory = new ClientBodyFactory(prefab ?? (Func<GameObject>)PlayerPrefab)
            {
                BeforeLocalBody = KryticalNova.Networking.OfflinePlayerHandoff.ReleaseOfflineAvatar,
                BodyMade = (body, request) =>
                {
                    // every view body shows the ghosts' jumps with its own clip (M3b)
                    if (body != null && body.GetComponent<GhostJumpView>() == null) body.AddComponent<GhostJumpView>();
                    // another player's view too picks walk or run from its ghost's walk speed: its run selection is the owner's only, so the
                    // walk clip played at a run's speed (owner's hand test 7 Nov 2026, x3: the other player slid instead of running)
                    if (!request.IsLocal) { DriveAnimationFromGhost(body); return; }
                    // items and buffs are used by the server on its body; this body asks (feel test item 5)
                    if (body.GetComponent<KryticalNova.Core.BodyRoleTag>() is KryticalNova.Core.BodyRoleTag tag) tag.Requests = new EntityBodyRequests(client);
                    // the owner's own body plans its climbs too: the trees near it (NGO WantsTreeProxies: the server and the owner)
                    if (body.GetComponent<KryticalNova.Player.TreeProxyAnchor>() == null) body.AddComponent<KryticalNova.Player.TreeProxyAnchor>();
                    PlayerMoveFromSkills.InstallSettings(client, PlayerMoveFromSkills.SettingsOf(body));
                    PlayerJumpFromSkills.InstallCurves(client, body);
                    PlayerJumpFromSkills.InstallKnockCurves(client, body);
                    // the camera looks at the body from the first frame it is drawn (no frame off it, then a snap)
                    if (TakeCameraWhenBodyMade) body.GetComponent<KryticalNova.Player.PointAndClickPlayer>()?.TakeCameraNow();
                    ConfigurePlanner(planner, body);
                    DriveAnimationFromGhost(body);
                },
            };
            var clientVaults = new BodyVaultPlanner(client);
            PlayerEntitiesSetup.Install(client, planner, jumpPlanner: new BodyJumpPlanner(client, checkCost: true), vaultPlanner: clientVaults,
                waterPlanner: new BodyWaterPlanner(client, planner, tellOwner: true, vaults: clientVaults), wallPlanner: new BodyWallPlanner(client, tellOwner: true));
            PlayerMoveFromSkills.InstallSettings(client, PlayerMoveFromSkills.SettingsOf((prefab ?? PlayerPrefab)()));
            PlayerJumpFromSkills.InstallCurves(client, (prefab ?? PlayerPrefab)());
            PlayerJumpFromSkills.InstallKnockCurves(client, (prefab ?? PlayerPrefab)());
            KryticalNova.Player.Entities.Presentation.PlayerPresentationSetup.Install(client, factory, new BodyClickSource());
            var looks = client.GetExistingSystemManaged<PlayerLookClientSystem>();
            if (looks != null) looks.Applier = new IdentityLookApplier();
            // the join's door (the loading screen) stays shut until the world may be shown: the body made, the camera on it, steady
            // frames, or the cap (WorldEntryDoor); a world that is gone never holds it
            var doorSystem = client.GetExistingSystemManaged<WorldEntryDoorSystem>();
            if (doorSystem != null)
            {
                var door = new WorldEntryDoor();
                doorSystem.Door = door;
                KryticalNova.Networking.JoinFlow.HoldUntilWorldReady(() => door.IsOpen || !client.IsCreated);
            }
            return factory;
        }

        /// <summary>
        /// The player's own body animates at its predicted ghost's speed (<see cref="KryticalNova.Player.Entities.Presentation.PlayerPresentationState"/>,
        /// written every frame by the follow), as the NGO game's own walk read its agent's velocity: no measuring lag at a start or a stop.
        /// </summary>
        public static void DriveAnimationFromGhost(GameObject body)
        {
            var motor = body != null ? body.GetComponent<KryticalNova.Player.PlayerAnimancerMotor>() : null;
            if (motor == null) return;
            KryticalNova.Player.Entities.Presentation.PlayerPresentationState state = null;   // added by the spawn system after the body is made
            motor.ExternalSpeed = () =>
            {
                if (state == null && body != null) state = body.GetComponent<KryticalNova.Player.Entities.Presentation.PlayerPresentationState>();
                return state != null ? state.State.Speed : 0f;
            };
        }

        /// <summary>The planner walks the way the player's NavMeshAgent would (agent type, areas, base offset); nothing without one.</summary>
        public static void ConfigurePlanner(NavMeshPathPlanner planner, GameObject player)
        {
            var agent = player != null ? player.GetComponent<UnityEngine.AI.NavMeshAgent>() : null;
            if (planner == null || agent == null) return;
            planner.AgentTypeId = agent.agentTypeID;
            planner.AreaMask = agent.areaMask;
            planner.BaseOffsetM = agent.baseOffset;
        }

        /// <summary>
        /// Server world: the leave handler in the closed-connection system (ghost invalidated, save and release, body, ghost, budgets).
        /// The access record (<c>ServerRecords.Left</c>) is keyed by the Netcode for GameObjects client id and keeps nothing without its
        /// NetworkManager, so it is not called here (M4 ports it).
        /// </summary>
        public static PlayerLeaveHandler InstallLeave(World server)
        {
            var closed = server.GetExistingSystemManaged<ConnectionClosedSystem>();
            if (closed == null)
            {
                Debug.LogError("[KnServer] the server world has no ConnectionClosedSystem: leaving players would not be saved.");
                return null;
            }
            foreach (IKnConnectionClosedHandler existing in closed.Handlers)
                if (existing is PlayerLeaveHandler already) return already;
            var leave = new PlayerLeaveHandler(server)
            {
                Forget = key =>
                {
                    KryticalNova.Networking.RpcBudget.Forget(key);
                    KryticalNova.Networking.NetworkHealth.Forget(key);
                },
            };
            closed.Handlers.Add(leave);
            return leave;
        }
    }
}
