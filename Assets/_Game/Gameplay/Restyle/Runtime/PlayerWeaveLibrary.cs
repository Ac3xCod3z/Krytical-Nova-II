using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.Persistence;
using KryticalNova.Pets;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Restyle
{
    /// <summary>
    /// The Weave Library: every gear look an account has collected, kept after the gear itself is gone. One for the
    /// whole account, so a look found on one character is there for all of them. A look is collected when its piece
    /// is earned (bought from an NPC, taken from a Hostile's drop, granted) or worn; a piece that only passed
    /// through a trade, a Kiosk or another player's hands counts once it is worn. Looks are kept by gear line
    /// (<see cref="WeaveRules.KeyOf"/>). The server decides; only the owner is told what it holds.
    /// It also holds the account's Morphs, the looks of pets (<see cref="MorphRules"/>): one for each stage a pet
    /// evolved into while out with one of the account's characters, or was put out at.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory), typeof(PlayerEquipment))]
    public sealed class PlayerWeaveLibrary : NetworkBehaviour, ISaveParticipant
    {
        public const string LibrarySaveKey = "weaves";

        private NetworkList<ushort> netLines;
        private readonly HashSet<ushort> lines = new HashSet<ushort>();
        private NetworkList<uint> netMorphs;
        private readonly HashSet<uint> morphs = new HashSet<uint>();
        private PlayerInventory inventory;
        private PlayerEquipment equipment;
        private PlayerSaveAgent saveAgent;
        private PlayerPet pet;
        private bool pullPending;
        // A character's worn gear is collected as it loads: nothing is announced until the save is in place.
        private bool settled;

        /// <summary>The library of the player this machine controls.</summary>
        public static PlayerWeaveLibrary Local => LocalPlayer.Get<PlayerWeaveLibrary>();

        /// <summary>Raised where the library is known (server, offline, owner) when a look is added or it is replaced.</summary>
        public event Action Changed;

        public int Count => lines.Count;
        /// <summary>True when the look of a gear line has been collected.</summary>
        public bool Has(ushort lineId) => lineId != 0 && lines.Contains(lineId);
        /// <summary>True when the look this piece wears has been collected.</summary>
        public bool HasLookOf(ItemDefinition item) => Has(WeaveRules.KeyOf(item));
        /// <summary>The collected gear lines. Do not modify.</summary>
        public IReadOnlyCollection<ushort> Lines => lines;

        public int MorphCount => morphs.Count;
        /// <summary>True when the look of a pet at a growth stage has been collected.</summary>
        public bool HasMorph(ushort petItemId, int stage)
        {
            uint key = MorphRules.Key(petItemId, stage);
            return key != 0 && morphs.Contains(key);
        }
        /// <summary>The collected Morphs, as <see cref="MorphRules.Key"/>. Do not modify.</summary>
        public IReadOnlyCollection<uint> Morphs => morphs;

        private bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            equipment = GetComponent<PlayerEquipment>();
            saveAgent = GetComponent<PlayerSaveAgent>();
            pet = GetComponent<PlayerPet>();
            netLines = new NetworkList<ushort>(readPerm: NetworkVariableReadPermission.Owner,
                writePerm: NetworkVariableWritePermission.Server);
            netMorphs = new NetworkList<uint>(readPerm: NetworkVariableReadPermission.Owner,
                writePerm: NetworkVariableWritePermission.Server);
        }

        private void OnEnable()
        {
            inventory.Earned += OnEarned;
            equipment.SlotChanged += OnSlotChanged;
            if (pet != null) pet.StageReached += OnPetStageReached;
            if (saveAgent != null) saveAgent.Loaded += OnLoaded;
        }

        private void OnDisable()
        {
            inventory.Earned -= OnEarned;
            equipment.SlotChanged -= OnSlotChanged;
            if (pet != null) pet.StageReached -= OnPetStageReached;
            if (saveAgent != null) saveAgent.Loaded -= OnLoaded;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netLines.OnListChanged += OnNetLinesChanged;
            netMorphs.OnListChanged += OnNetMorphsChanged;
            if (IsServer) WriteNetLines();
            else if (IsOwner) PullNetLines();
        }

        public override void OnNetworkDespawn()
        {
            netLines.OnListChanged -= OnNetLinesChanged;
            netMorphs.OnListChanged -= OnNetMorphsChanged;
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            netLines?.Dispose(); // guarded; covers the never-spawned offline avatar
            netMorphs?.Dispose();
        }

        // ------------------------------------------------------------------ server

        private void OnLoaded() => settled = true;

        private void OnEarned(ushort itemId) => ServerCollect(itemId);

        private void OnSlotChanged(EquipSlot slot, ushort previous, ushort next)
        {
            if (next != 0) ServerCollect(next);
        }

        /// <summary>Server (or offline): adds the look of an item, if it has one that is not collected yet.</summary>
        public bool ServerCollect(ushort itemId)
        {
            if (!HasAuthority || inventory.Catalog == null) return false;
            ItemDefinition item = inventory.Catalog.Get(itemId);
            if (!ServerCollectLine(WeaveRules.KeyOf(item))) return false;
            if (settled) Tell($"New Weave: {item.displayName}.");
            return true;
        }

        /// <summary>Server (or offline): adds a gear line's look. False when it was there already.</summary>
        public bool ServerCollectLine(ushort lineId)
        {
            if (!HasAuthority || lineId == 0 || !lines.Add(lineId)) return false;
            if (IsSpawned) netLines.Add(lineId);
            Changed?.Invoke();
            if (saveAgent != null) saveAgent.SaveSoon();
            return true;
        }

        /// <summary>
        /// Server (or offline): collects the look of everything a character has with it and has stored. Run once
        /// per character, for the gear it owned before looks were collected.
        /// </summary>
        public void ServerCollectOwned()
        {
            if (!HasAuthority || inventory.Catalog == null) return;
            foreach (EquipSlot slot in WeaveRules.WeaveSlots) CollectQuietly(equipment.Get(slot));
            for (int cell = 0; cell < PlayerInventory.Capacity; cell++) CollectQuietly(inventory.Get(cell).itemId);
            foreach (PlayerStorage storage in GetComponents<PlayerStorage>())
                for (int cell = 0; cell < StorageRules.Capacity; cell++) CollectQuietly(storage.Get(cell).itemId);
        }

        private void CollectQuietly(ushort itemId)
        {
            if (itemId != 0) ServerCollectLine(WeaveRules.KeyOf(inventory.Catalog.Get(itemId)));
        }

        private void OnPetStageReached(PetDefinition reached, int stage) => ServerCollectMorph(reached, stage);

        /// <summary>Server (or offline): adds the look of a pet at a growth stage. False when it was there already.</summary>
        public bool ServerCollectMorph(PetDefinition of, int stage)
        {
            if (of == null || !ServerCollectMorph(MorphRules.Key(of.itemId, stage))) return false;
            if (settled) Tell($"New Morph: {MorphRules.NameOf(of, stage)}.");
            return true;
        }

        /// <summary>Server (or offline): adds a Morph by its key, saying nothing. False when it was there already.</summary>
        public bool ServerCollectMorph(uint key)
        {
            if (!HasAuthority || key == 0 || !morphs.Add(key)) return false;
            if (IsSpawned) netMorphs.Add(key);
            Changed?.Invoke();
            if (saveAgent != null) saveAgent.SaveSoon();
            return true;
        }

        /// <summary>GM: empties the library, Morphs and all. Server (or offline) only.</summary>
        public void ServerClear()
        {
            if (!HasAuthority || (lines.Count == 0 && morphs.Count == 0)) return;
            lines.Clear();
            morphs.Clear();
            if (IsSpawned) netMorphs.Clear();
            if (IsSpawned) netLines.Clear();
            Changed?.Invoke();
            if (saveAgent != null) saveAgent.SaveSoon();
        }

        // ------------------------------------------------------------------ telling the owner

        private void Tell(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (IsSpawned && !IsOwnedByServer) TellRpc(text);
            else if (!IsSpawned || IsOwner) ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void TellRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        // ------------------------------------------------------------------ replication

        private void WriteNetLines()
        {
            netLines.Clear();
            foreach (ushort line in lines) netLines.Add(line);
            netMorphs.Clear();
            foreach (uint morph in morphs) netMorphs.Add(morph);
        }

        private void PullNetLines()
        {
            lines.Clear();
            for (int i = 0; i < netLines.Count; i++) lines.Add(netLines[i]);
            morphs.Clear();
            for (int i = 0; i < netMorphs.Count; i++) morphs.Add(netMorphs[i]);
            Changed?.Invoke();
        }

        private void OnNetMorphsChanged(NetworkListEvent<uint> change)
        {
            if (!IsServer && IsOwner) pullPending = true;
        }

        private void OnNetLinesChanged(NetworkListEvent<ushort> change)
        {
            // The first sync arrives as one event per look; coalesce into a single pull per frame.
            if (!IsServer && IsOwner) pullPending = true;
        }

        private void LateUpdate()
        {
            if (!pullPending) return;
            pullPending = false;
            PullNetLines();
        }

        // ------------------------------------------------------------------ saving

        [Serializable]
        private sealed class LibrarySave
        {
            public List<ushort> lines = new List<ushort>();
            public List<uint> morphs = new List<uint>();
        }

        public string SaveKey => LibrarySaveKey;
        public SaveScope SaveScope => SaveScope.Account;

        public string CaptureSave()
        {
            var save = new LibrarySave();
            save.lines.AddRange(lines);
            save.lines.Sort(); // the same library always writes the same text
            save.morphs.AddRange(morphs);
            save.morphs.Sort();
            return JsonUtility.ToJson(save);
        }

        /// <summary>
        /// Adds what the account had collected to what this character collected while it loaded (the order systems
        /// are restored in is not fixed, and worn gear is collected as it is put back).
        /// </summary>
        public void RestoreSave(string json)
        {
            if (!HasAuthority || string.IsNullOrEmpty(json)) return;
            var save = JsonUtility.FromJson<LibrarySave>(json);
            if (save == null) return;
            bool added = false;
            if (save.lines != null)
                foreach (ushort line in save.lines)
                    if (line != 0) added |= lines.Add(line);
            if (save.morphs != null)
                foreach (uint morph in save.morphs)
                    if (morph != 0) added |= morphs.Add(morph);
            if (!added) return;
            if (IsSpawned && IsServer) WriteNetLines();
            Changed?.Invoke();
        }
    }
}
