using System;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Items;
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
    /// server, or offline); on the owner's machine it only keeps what the select screen shows of the character
    /// current: its level and the gear its doll wears.
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
        private PlayerEquipment equipment;
        private ISaveParticipant[] participants = Array.Empty<ISaveParticipant>();
        // Owner only: what is worn changed and the select screen's copy is to be brought up to date.
        private const float WornDelay = 1f;
        private readonly System.Collections.Generic.List<WornPiece> wornScratch = new System.Collections.Generic.List<WornPiece>(EquipSlots.Count);
        private float wornDue = -1f;

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
            equipment = GetComponent<PlayerEquipment>();
            participants = GetComponents<ISaveParticipant>();
        }

        private void OnEnable()
        {
            if (identity != null) identity.Changed += OnIdentityChanged;
            if (progression != null) progression.Changed += OnProgressionChanged;
            if (equipment != null)
            {
                equipment.SlotChanged += OnWornChanged;
                equipment.TemperChanged += OnWornTemperChanged;
                equipment.LookChanged += OnWornTemperChanged;
            }
            // The offline avatar comes back after a session: pick up what the networked player saved.
            if (!IsSpawned) TryLoad();
        }

        private void OnDisable()
        {
            if (identity != null) identity.Changed -= OnIdentityChanged;
            if (progression != null) progression.Changed -= OnProgressionChanged;
            if (equipment != null)
            {
                equipment.SlotChanged -= OnWornChanged;
                equipment.TemperChanged -= OnWornTemperChanged;
                equipment.LookChanged -= OnWornTemperChanged;
            }
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
            // Leaving the world: the select screen is next, and shows what was worn at this moment.
            if (wornDue >= 0f) RememberWorn();
            if (IsServer) SaveAndRelease();
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (character != null && Time.unscaledTime >= nextSave) Save();
            if (wornDue >= 0f && Time.unscaledTime >= wornDue) RememberWorn();
        }

        private void OnIdentityChanged() => TryLoad();

        private void OnProgressionChanged()
        {
            if (IsLocalPlayer && progression != null) CharacterSession.RememberLevel(progression.CurrentLevel);
        }

        private void OnWornChanged(EquipSlot slot, ushort previous, ushort next) => OnWornTemperChanged(slot);

        // Several slots change at once when a character is loaded: one write a moment later covers them all.
        private void OnWornTemperChanged(EquipSlot slot)
        {
            if (wornDue < 0f) wornDue = Time.unscaledTime + WornDelay;
        }

        /// <summary>
        /// The owner's machine: hands the select screen what this character wears now, so its doll there matches.
        /// A player's own game is told its loadout and each piece's Temper whoever hosts, so this needs no save file.
        /// </summary>
        private void RememberWorn()
        {
            wornDue = -1f;
            if (!IsLocalPlayer || equipment == null || identity == null || !identity.HasIdentity) return;
            // Only for the character the roster says is being played (a test avatar has none behind it).
            if (!CharacterSession.TryGetActive(out CharacterProfile active) || active.id != identity.CharacterId) return;
            wornScratch.Clear();
            for (int i = 1; i < EquipSlots.Count; i++)
            {
                var slot = (EquipSlot)i;
                ushort itemId = equipment.Get(slot);
                if (itemId != 0)
                    wornScratch.Add(new WornPiece(slot, itemId, equipment.GetTemper(slot),
                        equipment.Looks.GetWeave(slot), equipment.Looks.GetDye(slot)));
            }
            CharacterSession.RememberWorn(wornScratch);
        }

        // ------------------------------------------------------------------ load

        private void TryLoad()
        {
            if (character != null || !HasAuthority || !isActiveAndEnabled || identity == null || !identity.HasIdentity) return;
            string wantedAccount = identity.AccountId;
            string wantedCharacter = identity.CharacterId;
            if (!SaveKeys.IsId(wantedAccount) || !SaveKeys.IsId(wantedCharacter)) return;

            // Online phase 2: the characters this game played before Unity sign-in move to its proven account, once.
            if (IsSpawned && OwnerClientId != NetworkManager.ServerClientId
                && KryticalNova.Networking.ServerAccounts.TryGet(OwnerClientId, out var proven) && proven.Account == wantedAccount)
                ServerSave.CarryOverOnce(proven.OldAccount, wantedAccount);

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
            // All of it happens on this frame: every system's state gathered, turned to text and written.
            using var timed = KryticalNova.Diagnostics.PerformanceLog.Time("save.player");

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
            // Where the character is (online phase 4): the map and the spot, kept for when there is more than one map.
            // Only a real move counts as a change, so standing still writes nothing.
            Vector3 at = transform.position;
            string map = gameObject.scene.name;
            if (character.map != map || (new Vector3(character.x, character.y, character.z) - at).sqrMagnitude > 1f)
            {
                character.map = map;
                character.x = Mathf.Round(at.x * 10f) / 10f;
                character.y = Mathf.Round(at.y * 10f) / 10f;
                character.z = Mathf.Round(at.z * 10f) / 10f;
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
