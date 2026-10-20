using System;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Networking;
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
    /// The look is saved with the character: the first time a character enters, the look it was made with is
    /// taken and saved; from then on the saved look is the one shown, whatever the player's own game sends, and
    /// only the server changes it (<see cref="ServerRestyle"/>: the Stylist, the Reshaper, the Coach).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerIdentity : NetworkBehaviour, IFactionMember, IGearWearer, ISaveParticipant
    {
        public const string LookSaveKey = "look";

        [SerializeField] private CharacterCreationDatabase database;
        [Tooltip("Shown until a character identity arrives.")]
        [SerializeField] private string defaultName = "Player";

        private readonly NetworkVariable<CharacterNetState> netState = new NetworkVariable<CharacterNetState>(
            writePerm: NetworkVariableWritePermission.Server);

        // PvP: the game-wide cross-faction switch. One value for the whole server, mirrored on every player object
        // so a late joiner receives it with the first player it sees.
        private readonly NetworkVariable<bool> netCrossFactionPvp = new NetworkVariable<bool>(
            writePerm: NetworkVariableWritePermission.Server);

        private CharacterNetState current;
        // The saved look arrives while the submitted one is being applied (the save loads on Changed): it is kept
        // here and applied when that is done.
        private bool assigning;
        private bool hasSavedLook;
        private CharacterNetState savedLook;
        // Which saved character this is. Known where the game is decided and to the owner; never replicated to others.
        private string accountId = string.Empty;
        private string characterId = string.Empty;
        private IDuelState duel;
        private LineageDefinition lineage;
        private CharacterLook look;
        private CombatEntity combatEntity;
        private PlayerInventory inventory;

        /// <summary>Raised on every peer when the identity is applied (name, faction and look are then current).</summary>
        public event Action Changed;
        /// <summary><see cref="IGearWearer"/>: faction, body, pack or Discipline changed. Raised with <see cref="Changed"/>.</summary>
        public event Action WearerChanged;

        public CharacterCreationDatabase Database => database;
        public bool HasIdentity => !current.IsEmpty;
        public string CharacterName => HasIdentity ? ShownName() : defaultName;

        // The name as text is made once for each name: the namecards and the lists ask for it every frame, and
        // turning the replicated name into a string made a new one each time.
        private string shownName;
        private Unity.Collections.FixedString32Bytes shownNameOf;

        private string ShownName()
        {
            if (shownName == null || !shownNameOf.Equals(current.name))
            {
                shownNameOf = current.name;
                shownName = current.name.ToString();
            }
            return shownName;
        }
        public FactionId Faction => current.faction;
        public byte LineageId => current.lineage;
        public LineageDefinition Lineage => lineage;
        public BodyFit Body => lineage != null ? lineage.body : BodyFit.None;
        public string GearPack => lineage != null ? lineage.ownPack : null;
        /// <summary>The class chosen in the creator; characters saved before classes existed get their faction's offense class.</summary>
        public WeaponClassId Discipline => WeaponClassRules.Normalize(current.weaponClass, current.faction);
        public CharacterNetState State => current;
        /// <summary>The account this character belongs to, or empty (no saved character). Server, offline and owner only.</summary>
        public string AccountId => accountId;
        /// <summary>The saved character's id (<see cref="CharacterProfile.id"/>), or empty. Server, offline and owner only.</summary>
        public string CharacterId => characterId;

        /// <summary>The duel exception of the faction rule: true while this player and <paramref name="other"/> duel each other.</summary>
        public bool IsDuelling(IFactionMember other) =>
            duel != null && other is PlayerIdentity otherPlayer && duel.IsDuellingWith(otherPlayer.NetworkObject);

        private void Awake()
        {
            look = GetComponentInChildren<CharacterLook>(true);
            combatEntity = GetComponent<CombatEntity>();
            inventory = GetComponent<PlayerInventory>();
            duel = GetComponent<IDuelState>();
            if (database != null && database.Factions != null) FactionRules.Configure(database.Factions.crossFactionPvp);
        }

        private void Start()
        {
            // Offline avatar: nobody else will assign an identity.
            if (!IsSpawned && CharacterSession.TryGetActive(out var profile))
                ServerAssign(CharacterNetState.From(profile.name, profile.recipe), CharacterSession.AccountId, profile.id);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netState.OnValueChanged += OnNetStateChanged;
            netCrossFactionPvp.OnValueChanged += OnNetCrossFactionPvpChanged;
            if (IsServer) netCrossFactionPvp.Value = FactionRules.CrossFactionPvp;
            else FactionRules.CrossFactionPvp = netCrossFactionPvp.Value;
            if (!IsServer && !netState.Value.IsEmpty) Apply(netState.Value);
            if (IsOwner && CharacterSession.TryGetActive(out var profile))
            {
                if (!IsServer)
                {
                    accountId = CharacterSession.AccountId;
                    characterId = profile.id ?? string.Empty;
                }
                SubmitIdentityRpc(CharacterNetState.From(profile.name, profile.recipe), CharacterSession.AccountId, profile.id ?? string.Empty);
            }
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;
            netCrossFactionPvp.OnValueChanged -= OnNetCrossFactionPvpChanged;
            base.OnNetworkDespawn();
        }

        // ---------------- PvP (GM tools) ----------------

        /// <summary>Server (or offline) only: switch PvP between factions on or off for everyone.</summary>
        public static void SetCrossFactionPvp(bool on)
        {
            var network = NetworkManager.Singleton;
            if (network != null && network.IsListening && !network.IsServer) return;
            FactionRules.CrossFactionPvp = on;
            foreach (var identity in FindObjectsByType<PlayerIdentity>(FindObjectsInactive.Exclude))
                if (identity.IsSpawned) identity.netCrossFactionPvp.Value = on;
        }

        private void OnNetCrossFactionPvpChanged(bool previous, bool next)
        {
            if (!IsServer) FactionRules.CrossFactionPvp = next;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitIdentityRpc(CharacterNetState state, FixedString64Bytes account, FixedString64Bytes character)
        {
            if (!RpcBudget.Allow(this, CallKind.Account, nameof(SubmitIdentityRpc))) return;
            ServerAssign(state, ProvenAccount(account.ToString()), character.ToString());
        }

        // Online phase 2: the account is the one Unity's sign-in proved when this player joined, never the one the game
        // names. A server that requires proof keeps nothing for a player without it; the Editor hosting a test takes the
        // game's own id, as before.
        private string ProvenAccount(string claimed)
        {
            if (OwnerClientId == NetworkManager.ServerClientId) return claimed; // the host's own player
            if (ServerAccounts.TryGet(OwnerClientId, out ServerAccounts.Entry proven)) return proven.Account;
            return KryticalNova.Networking.NetworkConnectionManager.RequireProof ? string.Empty : claimed;
        }

        /// <summary>
        /// Server (or offline) only: validate and commit an identity. A character is assigned once per spawn. The
        /// two ids say which saved character this is; they are taken on trust until real sign-in exists, and a
        /// malformed one simply means nothing is saved for this player.
        /// </summary>
        private void ServerAssign(CharacterNetState state, string account, string character)
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
            bool known = SaveKeys.IsId(account) && SaveKeys.IsId(character);
            accountId = known ? account : string.Empty;
            characterId = known ? character : string.Empty;
            assigning = true;
            hasSavedLook = false;
            Apply(state);
            assigning = false;
            if (hasSavedLook)
            {
                hasSavedLook = false;
                state = savedLook;
                Apply(state);
            }
            if (IsSpawned) netState.Value = state;
        }

        /// <summary>
        /// Server (or offline) only: gives the character another look and shows it to everyone. The recipe is
        /// checked like a new character's; what may differ from the old look is the caller's business.
        /// </summary>
        public bool ServerRestyle(in CharacterRecipe recipe)
        {
            if ((IsSpawned && !IsServer) || !HasIdentity) return false;
            if (!CharacterRecipeValidator.Validate(recipe, database, out _)) return false;
            CharacterNetState state = CharacterNetState.From(CharacterName, recipe);
            if (state.Equals(current)) return true;
            Apply(state);
            if (IsSpawned) netState.Value = state;
            return true;
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
            // A new character starts in the level-1 outfit of its own pack, holding its Discipline's weapon.
            if (inventory != null && (!IsSpawned || IsServer)) inventory.ServerGrantStarterKit(GearPack, Faction, Body, Discipline);
            // The player's own game keeps its copy of the look in step, so the select screen shows the same character.
            if ((!IsSpawned || IsOwner) && CharacterSession.TryGetActive(out CharacterProfile mine) && mine.id == characterId)
                CharacterSession.RememberRecipe(state.ToRecipe());
            WearerChanged?.Invoke();
            Changed?.Invoke();
        }

        // ---------------- saving ----------------

        [Serializable]
        private sealed class LookSave
        {
            public CharacterRecipe recipe;
        }

        /// <summary>The saved look's text for <paramref name="recipe"/>: what a new character made on the server starts with.</summary>
        public static string LookJson(in CharacterRecipe recipe) => JsonUtility.ToJson(new LookSave { recipe = recipe });

        /// <summary>The look in a saved look's text; false when there is none.</summary>
        public static bool TryReadLook(string json, out CharacterRecipe recipe)
        {
            recipe = default;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                LookSave save = JsonUtility.FromJson<LookSave>(json);
                if (save == null || save.recipe.IsEmpty) return false;
                recipe = save.recipe;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public string SaveKey => LookSaveKey;
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave() => HasIdentity ? JsonUtility.ToJson(new LookSave { recipe = current.ToRecipe() }) : null;

        /// <summary>
        /// The saved look wins over the one the player's game sent. Nothing saved (a new character, or one from
        /// before looks were saved) keeps the submitted look, which is saved from here on. A saved look the game
        /// can no longer build (a part that was removed) is dropped the same way.
        /// </summary>
        public void RestoreSave(string json)
        {
            if ((IsSpawned && !IsServer) || !HasIdentity || string.IsNullOrEmpty(json)) return;
            LookSave save = JsonUtility.FromJson<LookSave>(json);
            if (save == null || save.recipe.IsEmpty) return;
            if (!CharacterRecipeValidator.Validate(save.recipe, database, out string reason))
            {
                Debug.LogWarning($"[PlayerIdentity] The saved look of '{CharacterName}' is no longer valid ({reason}); keeping the one it came in with.", this);
                return;
            }
            CharacterNetState state = CharacterNetState.From(CharacterName, save.recipe);
            if (state.Equals(current)) return;
            if (assigning)
            {
                savedLook = state;
                hasSavedLook = true;
                return;
            }
            Apply(state);
            if (IsSpawned) netState.Value = state;
        }
    }
}
