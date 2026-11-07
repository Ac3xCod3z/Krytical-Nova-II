using System;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Bodies;
using Unity.Entities;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The game's rules for a new ServerBody (M3a-2b step 5): the character the client named, with the account the server proved,
    /// goes through <see cref="PlayerIdentity.ServerBegin"/>; that raises <c>Changed</c>, and <see cref="PlayerSaveAgent"/> loads the
    /// save at once (account lock, roster, migrations; only <c>look</c> and the progression header are real, decision D3). A refusal
    /// anywhere is the text the player reads. A character with no save behind it plays unsaved only where the server does not insist
    /// on proof (an Editor test server).
    /// </summary>
    public sealed class PlayerAdmission : IServerBodyRules
    {
        public const string BadCharacterText = "The server could not read your character. Go back to character select and choose again.";
        public const string NeedsSignInText = "This server only lets in players signed in to their account.";
        public const string NotLoadedText = "Your character could not be loaded. Try again in a moment.";
        public const string BrokenBodyText = "The server could not make your character. Try again in a moment.";

        /// <summary>The server world's planner; set from the first body's NavMeshAgent (agent type, base offset).</summary>
        public NavMeshPathPlanner Planner;

        /// <summary>The look of a player changed (entry, a restyle): text for observers (<see cref="RecipeText.EncodeLook"/>).</summary>
        public Action<GameObject, Entity, string> LookChanged;

        /// <summary>
        /// The relog check (saved map, this map, saved root, base offset -> the spot, or null for the spawn point); the NavMesh and physics
        /// one of <see cref="KryticalNova.Networking.PlayerSpawnService.TryReturnSpot(string, string, Vector3, float, out Vector3)"/> unless a
        /// test sets another.
        /// </summary>
        public Func<string, string, Vector3, float, Vector3?> ReturnSpot = (map, here, at, offset) =>
            KryticalNova.Networking.PlayerSpawnService.TryReturnSpot(map, here, at, offset, out Vector3 spot) ? spot : (Vector3?)null;

        public string Begin(GameObject body, in ServerBodyContext context)
        {
            var identity = body.GetComponent<PlayerIdentity>();
            if (identity == null)
            {
                Debug.LogError("[KnServer] the player body has no PlayerIdentity.");
                return BrokenBodyText;
            }
            ConfigurePlanner(body);

            if (!RecipeText.TryDecode(context.RecipeText, out CharacterRecipe recipe)) return BadCharacterText;
            bool requireProof = ServerPolicy.RequireProof(false);
            if (requireProof && !SaveKeys.IsId(context.Account)) return NeedsSignInText;
            // A bot of the 2-client gate gets its own character on a test-lane server (plan 43.1); everyone else keeps the id sent.
            string characterId = TestLaneBotCharacters.Ensure(context.Account, context.CharacterId, context.Name, recipe, out string botRefusal);
            if (botRefusal != null) return botRefusal;
            bool saved = SaveKeys.IsId(context.Account) && SaveKeys.IsId(characterId);
            if (requireProof && !saved) return BadCharacterText;

            // Changed -> PlayerSaveAgent.TryLoad runs inside this call (it may queue its own refusal through NetRole.DisconnectOwner;
            // KnPlayerRefusals keeps the first reason for a key, so the agent's words win over the generic one returned below).
            string refusal = identity.ServerBegin(CharacterNetState.From(context.Name, recipe), context.Account, characterId);
            if (refusal != null) return refusal;
            var agent = body.GetComponent<PlayerSaveAgent>();
            if (saved && (agent == null || !agent.IsLoaded)) return NotLoadedText;
            ReturnToSavedSpot(body, agent);
            return null;
        }

        /// <summary>
        /// Owner, 6 Nov 2026 (both games): a relog puts the player back where they stood. The body was built on a spawn point (step 1 of
        /// <see cref="ServerBodyAdmission"/>); it moves to the saved spot when <see cref="ReturnSpot"/> allows, before its ghost is made on
        /// the body's spot, so every view starts there too. Otherwise it stays on the spawn point.
        /// </summary>
        void ReturnToSavedSpot(GameObject body, PlayerSaveAgent agent)
        {
            if (agent == null || ReturnSpot == null || !agent.TryGetSavedSpot(out string map, out Vector3 at)) return;
            var navAgent = body.GetComponent<NavMeshAgent>();
            float baseOffset = navAgent != null ? navAgent.baseOffset : Planner != null ? Planner.BaseOffsetM : 0f;
            Vector3? spot = ReturnSpot(map, body.scene.name, at, baseOffset);
            if (!spot.HasValue) return;
            if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh) navAgent.Warp(spot.Value);
            body.transform.position = spot.Value;
            // the body's kinematic Rigidbody keeps its own position until physics syncs: moved with it, so nothing snaps it back
            if (body.TryGetComponent(out Rigidbody rb)) rb.position = spot.Value;
            Physics.SyncTransforms();
        }

        public void Admitted(GameObject body, Entity ghost, in ServerBodyContext context)
        {
            var identity = body.GetComponent<PlayerIdentity>();
            if (identity == null || LookChanged == null) return;
            // The saved look may have won over the one sent; observers get what the body wears now, and every later change.
            var equipment = body.GetComponent<PlayerEquipment>();
            LookChanged(body, ghost, LookOf(identity, equipment));
            Action changed = () => { if (body != null) LookChanged?.Invoke(body, ghost, LookOf(identity, equipment)); };
            identity.Changed += changed;
            if (equipment == null) return;
            // what they wear, as NGO's equipment variables told observers (M8 8.1.2b): the text changes, observers fetch it once
            equipment.SlotChanged += (_, __, ___) => changed();
            equipment.LookChanged += _ => changed();
            equipment.TemperChanged += _ => changed();
        }

        public static string LookOf(PlayerIdentity identity) => LookOf(identity, null);

        /// <summary>The name, the recipe and (with <paramref name="equipment"/>) what the player wears.</summary>
        public static string LookOf(PlayerIdentity identity, PlayerEquipment equipment) =>
            identity != null && identity.HasIdentity
                ? RecipeText.EncodeLook(identity.CharacterName, identity.State.ToRecipe(),
                    equipment != null ? GearText.Encode(equipment.Loadout, equipment.Looks, equipment.Temper) : null)
                : string.Empty;

        void ConfigurePlanner(GameObject body)
        {
            if (Planner == null) return;
            var agent = body.GetComponent<NavMeshAgent>();
            if (agent == null) return;
            Planner.AgentTypeId = agent.agentTypeID;
            Planner.AreaMask = agent.areaMask;
            Planner.BaseOffsetM = agent.baseOffset;
        }
    }
}
