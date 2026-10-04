using System;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// Who this player is: the character's name, faction, lineage and look. Server-written and readable by everyone,
    /// so every peer shows the same character. The owner sends the character it chose in the menus when it spawns;
    /// the server checks the name and the recipe before accepting it. Offline (the unspawned scene avatar) the local
    /// copy is authoritative. Until an identity arrives the prefab's own look is shown.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerIdentity : NetworkBehaviour, IFactionMember, IGearWearer
    {
        [SerializeField] private CharacterCreationDatabase database;
        [Tooltip("Shown until a character identity arrives.")]
        [SerializeField] private string defaultName = "Player";

        private readonly NetworkVariable<CharacterNetState> netState = new NetworkVariable<CharacterNetState>(
            writePerm: NetworkVariableWritePermission.Server);

        private CharacterNetState current;
        private LineageDefinition lineage;
        private CharacterLook look;
        private CombatEntity combatEntity;
        private PlayerInventory inventory;

        /// <summary>Raised on every peer when the identity is applied (name, faction and look are then current).</summary>
        public event Action Changed;

        public CharacterCreationDatabase Database => database;
        public bool HasIdentity => !current.IsEmpty;
        public string CharacterName => HasIdentity ? current.name.ToString() : defaultName;
        public FactionId Faction => current.faction;
        public byte LineageId => current.lineage;
        public LineageDefinition Lineage => lineage;
        public BodyFit Body => lineage != null ? lineage.body : BodyFit.None;
        public string GearPack => lineage != null ? lineage.ownPack : null;
        public CharacterNetState State => current;

        private void Awake()
        {
            look = GetComponentInChildren<CharacterLook>(true);
            combatEntity = GetComponent<CombatEntity>();
            inventory = GetComponent<PlayerInventory>();
            if (database != null && database.Factions != null) FactionRules.CrossFactionPvp = database.Factions.crossFactionPvp;
        }

        private void Start()
        {
            // Offline avatar: nobody else will assign an identity.
            if (!IsSpawned && CharacterSession.TryGetActive(out var profile))
                ServerAssign(CharacterNetState.From(profile.name, profile.recipe));
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netState.OnValueChanged += OnNetStateChanged;
            if (!IsServer && !netState.Value.IsEmpty) Apply(netState.Value);
            if (IsOwner && CharacterSession.TryGetActive(out var profile))
                SubmitIdentityRpc(CharacterNetState.From(profile.name, profile.recipe));
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;
            base.OnNetworkDespawn();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitIdentityRpc(CharacterNetState state) => ServerAssign(state);

        /// <summary>Server (or offline) only: validate and commit an identity. A character is assigned once per spawn.</summary>
        private void ServerAssign(CharacterNetState state)
        {
            if (IsSpawned && !IsServer) return;
            if (HasIdentity || state.IsEmpty) return;

            if (!CharacterNameRules.Validate(state.name.ToString(), out string cleanName, out string nameReason))
            {
                Debug.LogWarning($"[PlayerIdentity] Rejected a character name ({nameReason}); keeping the default look.", this);
                return;
            }
            var recipe = state.ToRecipe();
            if (!CharacterRecipeValidator.Validate(recipe, database, out string recipeReason))
            {
                Debug.LogWarning($"[PlayerIdentity] Rejected character '{cleanName}' ({recipeReason}); keeping the default look.", this);
                return;
            }

            state.name.CopyFromTruncated(cleanName);
            Apply(state);
            if (IsSpawned) netState.Value = state;
        }

        private void OnNetStateChanged(CharacterNetState previous, CharacterNetState next)
        {
            if (IsServer) return; // already applied locally in ServerAssign
            Apply(next);
        }

        private void Apply(CharacterNetState state)
        {
            current = state;
            lineage = database != null ? database.GetLineage(state.lineage) : null;
            if (state.IsEmpty) return;

            if (look != null && !look.Apply(state.ToRecipe(), database))
                Debug.LogWarning($"[PlayerIdentity] Could not build the look of '{CharacterName}'.", this);
            if (combatEntity != null) combatEntity.SetEntityName(CharacterName);
            // The gear a new character can use comes from its own pack (until gear is earned in the world).
            if (inventory != null && (!IsSpawned || IsServer)) inventory.ServerGrantPackGear(GearPack);
            Changed?.Invoke();
        }
    }
}
