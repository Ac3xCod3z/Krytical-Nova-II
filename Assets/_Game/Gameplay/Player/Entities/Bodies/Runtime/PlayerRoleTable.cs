using System;
using System.Collections.Generic;
using KryticalNova.Core;

namespace KryticalNova.Player.Entities.Bodies
{
    /// <summary>What happens to one component of the player prefab when a body is built for a role.</summary>
    public enum PartAction : byte
    {
        Keep = 0,
        /// <summary>Destroyed before the body wakes up.</summary>
        Strip = 1,
        /// <summary>Kept (others need it, or it is called into) but switched off.</summary>
        Disable = 2,
    }

    /// <summary>The three shapes a real player prefab takes in the entity netcode (M3a-2b).</summary>
    public enum BodyShape : byte
    {
        /// <summary>Server: the rules run, nothing is shown, the ghost moves it.</summary>
        Server = 0,
        /// <summary>The player this machine controls: presentation, camera and input, rules as local stubs.</summary>
        ViewLocal = 1,
        /// <summary>Another player seen from here: presentation only.</summary>
        ViewRemote = 2,
    }

    /// <summary>
    /// Which components of the player prefab each body shape keeps (plan section 2.3; design step 2). The lists hold type names,
    /// not types, because Player cannot see Combat, Economy, Social and the rest, and because a third-party type
    /// (Animancer, Netcode) should not leak into this assembly. A component type the table does not know is kept, and the
    /// lint test <c>PlayerRoleTableTests</c> fails when the prefab holds one, so a new <c>PlayerSystemsSetup</c> row forces
    /// a decision here.
    /// </summary>
    public static class PlayerRoleTable
    {
        /// <summary>Everything that draws, animates or follows the ground visually.</summary>
        public static readonly string[] Presentation =
        {
            "AnimancerComponent", "Animator", "MotionWarping", "CharacterLook", "CharacterAppearance", "FaceBlinker", "OffHandGripIK",
            "WeaponVisualMount", "EquipmentVisualBinder", "CharacterLod", "PetFollower", "FoliageInteractor", "PlayerAnimancerMotor",
            "PlayerJumpPresentation", "FootGroundingIK", "MeshRenderer", "SkinnedMeshRenderer", "MeshFilter",
        };

        /// <summary>Movement and input: on a body of the entity netcode the ghost moves it, never the agent.</summary>
        public static readonly string[] Movement =
        {
            "PointAndClickPlayer", "PlayerInput", "NavMeshAgent", "PlayerMoveAnticipation", "NetworkTransform",
            "PlayerJumpMotor", "PlayerVaultMotor", "PlayerWallRunMotor", "PlayerWaterstrideMotor", "PlayerDeathMotor",
        };

        /// <summary>Client automation: steers through the movement API, back in M3c.</summary>
        public static readonly string[] Automation =
        {
            "PlayerAutopilot", "MagnetController", "AutopilotController", "AutobuffCaster",
        };

        /// <summary>Ported systems (they implement <see cref="IRoleAwareBody"/>).</summary>
        public static readonly string[] RoleAware =
        {
            "PlayerSaveAgent", "PlayerIdentity",
        };

        /// <summary>Rule behaviours that still read the raw network object: they run as offline-style stubs (decision D2).</summary>
        public static readonly string[] Rules =
        {
            "PlayerCharacterProgression", "PlayerMovementSkills", "PlayerSkillHotbar", "PlayerResources", "CharacterCombatStats",
            "CombatEntity", "CharacterBuffManager", "PlayerCombatSkills", "PlayerMeleeCombat", "PlayerEquipment", "PlayerInventory",
            "PlayerSkillAttacks", "PlayerGmState", "PlayerChat", "PlayerDuel", "PlayerParty", "PlayerWeaponClass", "PlayerHotbarMemory",
            "PlayerPet", "PlayerWallet", "PlayerLoot", "PlayerVault", "PlayerDeepVault", "PlayerTrade", "PlayerKiosk", "PlayerContacts",
            "PlayerCrew", "PlayerGearWork", "PlayerNova", "PlayerEmotes", "PlayerFollow", "PlayerSerums", "FactionExtension",
            "PlayerCombatState", "PlayerSkillQueue", "PlayerShopping", "PlayerItemSources", "PlayerWeaveLibrary", "PlayerWeaves",
            "PlayerCrafting", "PlayerRestyle", "PlayerSponsor", "PlayerExperienceBoosts", "PlayerCrate", "PlayerHousing", "PlayerRecall",
            "PlayerFlask", "PlayerGm", "PlayerGmSight", "PlayerCadence", "PlayerLoadouts", "PlayerMounts", "MountFeeding", "MountRider",
            "PlayerAutobuff",   // M8 8.3.11: the licence, the cells, the Pass use and the save run on the server body
            "PlayerRename", "KNEmeraldPlayerBridge", "TargetPositionModifier", "WeaponHitbox", "PlayerNightLight",
        };

        /// <summary>Rule behaviours a remote body keeps because nameplates and targeting read them (the level too: M8 8.3.1, it takes the
        /// server's through <c>PlayerLevelTag</c>), and its equipment: what the player wears is drawn from it (M8 8.1.2b; it takes the server's
        /// gear through the look text, <c>PlayerEquipment.ApplyObserved</c>). M4.5h (owner pick A, 5 Nov 2026: copy NGO, whose clients had them): the
        /// combat parts that play what others see, the casts, charges, swings, impacts and buffs (PlayerCombatSkills, PlayerSkillAttacks,
        /// PlayerMeleeCombat) and what they require (PlayerResources, CharacterBuffManager, CharacterCombatStats; the NavMeshAgent stays, off).
        /// On such a body they decide nothing: <c>BodyRoleTag.IsRemoteClientView</c> guards every path that would.</summary>
        public static readonly string[] RemoteKeepsRules = { "CombatEntity", "FactionExtension", "PlayerEquipment", "PlayerCharacterProgression", "PlayerNova", "PlayerParty", "PlayerCrew", "PlayerEmotes", "PlayerInventory", "PlayerPet", "PlayerMounts", "MountRider",
            "PlayerCombatSkills", "PlayerSkillAttacks", "PlayerMeleeCombat", "PlayerResources", "CharacterBuffManager", "CharacterCombatStats",
            "PlayerDuel" };   // M4 8b: a Callout's opponent, read by every view's hostility (namecards, targets)

        /// <summary>Things every shape keeps.</summary>
        public static readonly string[] Infrastructure =
        {
            "Transform", "RectTransform", "NetworkObject", "Rigidbody", "CapsuleCollider", "BoxCollider", "SphereCollider", "MeshCollider",
            "NavMeshModifier",
        };

        private static readonly HashSet<string> PresentationSet = new HashSet<string>(Presentation);
        private static readonly HashSet<string> MovementSet = new HashSet<string>(Movement);
        private static readonly HashSet<string> AutomationSet = new HashSet<string>(Automation);
        private static readonly HashSet<string> RoleAwareSet = new HashSet<string>(RoleAware);
        private static readonly HashSet<string> RulesSet = new HashSet<string>(Rules);
        private static readonly HashSet<string> RemoteKeepsSet = new HashSet<string>(RemoteKeepsRules);
        private static readonly HashSet<string> InfraSet = new HashSet<string>(Infrastructure);

        /// <summary>
        /// The death motor stays on, on every view (M4 6b): it plays the death the ghost shows (the fall, the de-rez, lying, getting up) on
        /// its own clock, and on a view it never moves the root or decides anything. The server body keeps it switched off (M4 6a).
        /// </summary>
        public const string ViewShowsDeath = "PlayerDeathMotor";

        /// <summary>
        /// The traversal motors stay on another player's view too, switched off as on the own view (owner's 2-player test, 6 Nov 2026,
        /// item 2: a vault went through the obstacle on the other screen): the ghost's vault, Wallstride phase and Waterstride play
        /// through them (GhostJumpView reads their clips and styles); switched off, they never move the body or decide anything.
        /// </summary>
        public static readonly string[] ViewShowsTraversal = { "PlayerVaultMotor", "PlayerWallRunMotor", "PlayerWaterstrideMotor" };
        private static readonly HashSet<string> ViewShowsTraversalSet = new HashSet<string>(ViewShowsTraversal);

        /// <summary>The shape of a body for a role (Offline has none: it is the scene avatar, never built here).</summary>
        public static BodyShape ShapeOf(NetRoleKind role, bool isLocal)
        {
            if (role == NetRoleKind.ServerBody) return BodyShape.Server;
            if (role == NetRoleKind.ClientView) return isLocal ? BodyShape.ViewLocal : BodyShape.ViewRemote;
            throw new ArgumentException("A body is built for ServerBody or ClientView, not " + role, nameof(role));
        }

        /// <summary>True when the table has an opinion on this component type.</summary>
        public static bool IsClassified(string typeName) =>
            PresentationSet.Contains(typeName) || MovementSet.Contains(typeName) || AutomationSet.Contains(typeName) ||
            RoleAwareSet.Contains(typeName) || RulesSet.Contains(typeName) || InfraSet.Contains(typeName);

        /// <summary>What to do with a component of this type (the type's simple name) on a body of this shape.</summary>
        public static PartAction ActionFor(BodyShape shape, string typeName)
        {
            if (InfraSet.Contains(typeName)) return PartAction.Keep;
            switch (shape)
            {
                case BodyShape.Server:
                    if (PresentationSet.Contains(typeName) || AutomationSet.Contains(typeName)) return PartAction.Strip;
                    if (MovementSet.Contains(typeName))
                        return typeName == "PlayerInput" || typeName == "NetworkTransform" || typeName == "PlayerMoveAnticipation"
                            ? PartAction.Strip : PartAction.Disable;
                    return PartAction.Keep;
                case BodyShape.ViewLocal:
                    if (typeName == ViewShowsDeath) return PartAction.Keep;
                    if (AutomationSet.Contains(typeName)) return PartAction.Disable;
                    if (MovementSet.Contains(typeName))
                    {
                        if (typeName == "PointAndClickPlayer" || typeName == "PlayerInput") return PartAction.Keep;
                        return typeName == "NetworkTransform" || typeName == "PlayerMoveAnticipation" ? PartAction.Strip : PartAction.Disable;
                    }
                    return PartAction.Keep;
                default: // ViewRemote
                    if (typeName == ViewShowsDeath) return PartAction.Keep;
                    if (ViewShowsTraversalSet.Contains(typeName)) return PartAction.Disable;
                    if (MovementSet.Contains(typeName) || AutomationSet.Contains(typeName)) return PartAction.Strip;
                    if (typeName == "PlayerSaveAgent") return PartAction.Strip;
                    if (RulesSet.Contains(typeName)) return RemoteKeepsSet.Contains(typeName) ? PartAction.Keep : PartAction.Strip;
                    return PartAction.Keep;
            }
        }
    }
}
