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
            return null;
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
