using System;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Persistence
{
    /// <summary>
    /// Loads a player's saved character when it enters the world and writes it back: every so often, when the player
    /// leaves, and when the server stops. It knows nothing about what is saved: every component on the player that
    /// implements <see cref="ISaveParticipant"/> saves and restores itself. Works where the game is decided (the
    /// server, or offline); on the owner's machine it only keeps the select screen's level current.
    /// A player without a saved character behind it (a test avatar) is simply never saved.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSaveAgent : NetworkBehaviour
    {
        [Tooltip("Seconds between automatic saves of this player.")]
        [SerializeField, Min(10f)] private float autosaveSeconds = 60f;

        private PlayerIdentity identity;
        private PlayerResources resources;
        private PlayerCharacterProgression progression;
        private ISaveParticipant[] participants = Array.Empty<ISaveParticipant>();

        private AccountSave account;
        private CharacterSave character;
        private string accountId = string.Empty;
        private float nextSave;

        /// <summary>Raised where the game is decided once this player's saved state is in place (also for a new character).</summary>
        public event Action Loaded;

        /// <summary>True while this player's character is loaded from, and saved to, the store.</summary>
        public bool IsLoaded => character != null;
        public string AccountId => accountId;
        public string CharacterId => character != null ? character.characterId : string.Empty;
        /// <summary>The account document while loaded (server / offline). Account-wide systems read and change it here.</summary>
        public AccountSave Account => account;

        private bool HasAuthority => !IsSpawned || IsServer;
        private bool IsLocalPlayer => IsSpawned ? IsOwner : NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;

        private void Awake()
        {
            identity = GetComponent<PlayerIdentity>();
            resources = GetComponent<PlayerResources>();
            progression = GetComponent<PlayerCharacterProgression>();
            participants = GetComponents<ISaveParticipant>();
        }

        private void OnEnable()
        {
            if (identity != null) identity.Changed += OnIdentityChanged;
            if (progression != null) progression.Changed += OnProgressionChanged;
            // The offline avatar comes back after a session: pick up what the networked player saved.
            if (!IsSpawned) TryLoad();
        }

        private void OnDisable()
        {
            if (identity != null) identity.Changed -= OnIdentityChanged;
            if (progression != null) progression.Changed -= OnProgressionChanged;
            // The offline avatar is switched off while a session runs: its character belongs to the networked player now.
            if (!IsSpawned) SaveAndRelease();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            TryLoad();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) SaveAndRelease();
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (character != null && Time.unscaledTime >= nextSave) Save();
        }

        private void OnIdentityChanged() => TryLoad();

        private void OnProgressionChanged()
        {
            if (IsLocalPlayer && progression != null) CharacterSession.RememberLevel(progression.CurrentLevel);
        }

        // ------------------------------------------------------------------ load

        private void TryLoad()
        {
            if (character != null || !HasAuthority || !isActiveAndEnabled || identity == null || !identity.HasIdentity) return;
            string wantedAccount = identity.AccountId;
            string wantedCharacter = identity.CharacterId;
            if (!SaveKeys.IsId(wantedAccount) || !SaveKeys.IsId(wantedCharacter)) return;

            if (!ServerSave.TryBind(this, wantedAccount, out string refusal))
            {
                Debug.LogWarning($"[Save] {identity.CharacterName}: {refusal}", this);
                if (IsSpawned && OwnerClientId != NetworkManager.ServerClientId) NetworkManager.DisconnectClient(OwnerClientId, refusal);
                return;
            }

            accountId = wantedAccount;
            account = ServerSave.LoadAccount(accountId);
            character = account.Find(wantedCharacter);
            bool isNew = character == null;
            if (isNew)
            {
                character = new CharacterSave { characterId = wantedCharacter };
                account.characters.Add(character);
            }

            foreach (ISaveParticipant participant in participants)
            {
                string json = participant.SaveScope == SaveScope.Account ? account.Get(participant.SaveKey) : character.Get(participant.SaveKey);
                try
                {
                    participant.RestoreSave(json);
                }
                catch (Exception e)
                {
                    // One system's bad document must not cost the player the rest of the character.
                    Debug.LogError($"[Save] {identity.CharacterName}: '{participant.SaveKey}' could not be restored: {e}", this);
                }
            }
            // Max HP and Flux come from the level and gear that were just put back.
            if (!isNew && resources != null && resources.IsAlive) resources.RestoreAll();

            Save();
            Loaded?.Invoke();
        }

        // ------------------------------------------------------------------ save

        /// <summary>Server (or offline): writes this player now, if anything changed. Call after anything that must not be lost.</summary>
        public void Save()
        {
            if (character == null || !HasAuthority) return;
            nextSave = Time.unscaledTime + autosaveSeconds;

            bool changed = false;
            foreach (ISaveParticipant participant in participants)
            {
                string json;
                try
                {
                    json = participant.CaptureSave();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Save] {identity.CharacterName}: '{participant.SaveKey}' could not be captured; its last save is kept: {e}", this);
                    continue;
                }
                changed |= participant.SaveScope == SaveScope.Account ? account.Set(participant.SaveKey, json) : character.Set(participant.SaveKey, json);
            }

            string characterName = identity.CharacterName;
            int faction = (int)identity.Faction;
            int level = progression != null ? progression.CurrentLevel : 1;
            if (character.name != characterName || character.faction != faction || character.level != level)
            {
                character.name = characterName;
                character.faction = faction;
                character.level = level;
                changed = true;
            }
            if (!changed) return;

            character.savedUtc = DateTime.UtcNow.Ticks;
            ServerSave.WriteAccount(account);
        }

        /// <summary>
        /// Server (or offline): saves within a moment instead of at the next autosave. For changes that must not be
        /// lost but come in bursts (a dozen items moved into the Vault): one write covers them all.
        /// </summary>
        public void SaveSoon(float withinSeconds = 2f)
        {
            if (character == null || !HasAuthority) return;
            nextSave = Mathf.Min(nextSave, Time.unscaledTime + Mathf.Max(0f, withinSeconds));
        }

        /// <summary>Saves and lets go of the character (the player left, or the offline avatar gave way to a session).</summary>
        internal void SaveAndRelease()
        {
            if (character == null) return;
            Save();
            ReleaseWithoutSaving();
        }

        internal void ReleaseWithoutSaving()
        {
            ServerSave.Release(this, accountId);
            account = null;
            character = null;
            accountId = string.Empty;
        }
    }
}
